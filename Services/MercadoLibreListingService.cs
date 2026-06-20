using System.Diagnostics;
using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class MercadoLibreListingService : IMercadoLibreListingService
    {
        // Tope defensivo para evitar corridas infinitas si el scan no corta.
        private const int MaxItemsPorImportacion = 10_000;

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IProductoService _productoService;
        private readonly IMapper _mapper;
        private readonly ILogger<MercadoLibreListingService> _logger;

        public MercadoLibreListingService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreAuthService authService,
            IMercadoLibreApiClient apiClient,
            IProductoService productoService,
            IMapper mapper,
            ILogger<MercadoLibreListingService> logger)
        {
            _contextFactory = contextFactory;
            _authService = authService;
            _apiClient = apiClient;
            _productoService = productoService;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<MercadoLibreImportResultViewModel> ImportarPublicacionesAsync(int accountId, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var resultado = new MercadoLibreImportResultViewModel();

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(ct);

                var cuenta = await context.MercadoLibreAccounts
                    .FirstOrDefaultAsync(a => a.Id == accountId && a.Activa, ct)
                    ?? throw new MercadoLibreApiException($"Cuenta de Mercado Libre {accountId} inexistente o inactiva.");

                var token = await _authService.GetValidAccessTokenAsync(accountId, ct);

                // 1. Recorrer todo el inventario con search_type=scan
                var itemIds = new List<string>();
                string? scrollId = null;

                while (itemIds.Count < MaxItemsPorImportacion)
                {
                    var pagina = await _apiClient.SearchItemIdsAsync(token, cuenta.MeliUserId, scrollId, 100, ct);

                    if (pagina.Results.Count == 0)
                        break;

                    itemIds.AddRange(pagina.Results);
                    scrollId = pagina.ScrollId;

                    if (string.IsNullOrEmpty(scrollId))
                        break;
                }

                resultado.TotalEncontradas = itemIds.Count;

                if (itemIds.Count == 0)
                {
                    resultado.Mensajes.Add("El vendedor no tiene publicaciones.");
                }
                else
                {
                    // 2. Detalle vía multiget
                    var items = await _apiClient.GetItemsAsync(token, itemIds, ct);
                    resultado.Errores = itemIds.Count - items.Count;

                    // 3. Upsert idempotente por ItemId
                    var existentes = await context.MercadoLibreListings
                        .IgnoreQueryFilters()
                        .Include(l => l.Variaciones)
                        .Where(l => l.AccountId == accountId)
                        .ToDictionaryAsync(l => l.ItemId, ct);

                    foreach (var item in items)
                    {
                        if (existentes.TryGetValue(item.Id, out var listing))
                        {
                            ActualizarListing(listing, item);
                            resultado.Actualizadas++;
                        }
                        else
                        {
                            listing = new MercadoLibreListing { AccountId = accountId, ItemId = item.Id };
                            ActualizarListing(listing, item);
                            context.MercadoLibreListings.Add(listing);
                            resultado.Creadas++;
                        }

                        if (listing.TieneVariaciones)
                            resultado.ConVariaciones++;
                    }
                }

                cuenta.UltimaImportacionListingsUtc = DateTime.UtcNow;

                sw.Stop();
                resultado.DuracionMs = sw.ElapsedMilliseconds;

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = accountId,
                    Operacion = "ImportListings",
                    Exito = true,
                    Detalle = $"Encontradas {resultado.TotalEncontradas}, creadas {resultado.Creadas}, " +
                              $"actualizadas {resultado.Actualizadas}, con variaciones {resultado.ConVariaciones}, " +
                              $"errores {resultado.Errores}.",
                    DuracionMs = resultado.DuracionMs
                });

                await context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Importación de publicaciones ML completada para cuenta {AccountId}: {Detalle}",
                    accountId, resultado.Mensajes.FirstOrDefault() ?? $"{resultado.TotalEncontradas} publicaciones");

                return resultado;
            }
            catch (Exception ex)
            {
                sw.Stop();
                await RegistrarErrorAsync(accountId, "ImportListings", ex, sw.ElapsedMilliseconds, ct);
                throw;
            }
        }

        public async Task<List<MercadoLibreListingViewModel>> GetListingsAsync(string? filtroVinculo = null, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var query = context.MercadoLibreListings
                .AsNoTracking()
                .Include(l => l.Producto)
                .Include(l => l.Variaciones)
                .AsQueryable();

            if (string.Equals(filtroVinculo, "vinculadas", StringComparison.OrdinalIgnoreCase))
                query = query.Where(l => l.ProductoId != null);
            else if (string.Equals(filtroVinculo, "sin-vincular", StringComparison.OrdinalIgnoreCase))
                query = query.Where(l => l.ProductoId == null);
            else if (string.Equals(filtroVinculo, "con-variaciones", StringComparison.OrdinalIgnoreCase))
                query = query.Where(l => l.TieneVariaciones);
            else if (string.Equals(filtroVinculo, "con-error", StringComparison.OrdinalIgnoreCase))
            {
                var idsConError = await context.MercadoLibreSyncLogs
                    .AsNoTracking()
                    .Where(l => !l.Exito && l.ListingId.HasValue)
                    .Select(l => l.ListingId!.Value)
                    .Distinct()
                    .ToListAsync(ct);
                query = query.Where(l => idsConError.Contains(l.Id));
            }

            var listings = await query
                .OrderBy(l => l.Titulo)
                .ToListAsync(ct);

            var viewModels = _mapper.Map<List<MercadoLibreListingViewModel>>(listings);

            // Sugerencia de vinculación: SellerSku == Producto.Codigo (solo para no vinculadas)
            var skusSinVincular = viewModels
                .Where(vm => !vm.Vinculada && !string.IsNullOrWhiteSpace(vm.SellerSku))
                .Select(vm => vm.SellerSku!)
                .Distinct()
                .ToList();

            if (skusSinVincular.Count > 0)
            {
                var sugerencias = await context.Productos
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted && skusSinVincular.Contains(p.Codigo))
                    .Select(p => new { p.Id, p.Codigo, p.Nombre })
                    .ToListAsync(ct);

                var porCodigo = sugerencias
                    .GroupBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var vm in viewModels.Where(v => !v.Vinculada && !string.IsNullOrWhiteSpace(v.SellerSku)))
                {
                    if (porCodigo.TryGetValue(vm.SellerSku!, out var producto))
                    {
                        vm.ProductoSugeridoId = producto.Id;
                        vm.ProductoSugeridoNombre = $"{producto.Codigo} - {producto.Nombre}";
                    }
                }
            }

            return viewModels;
        }

        public async Task VincularProductoAsync(int listingId, int productoId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .FirstOrDefaultAsync(l => l.Id == listingId, ct)
                ?? throw new InvalidOperationException($"Publicación {listingId} inexistente.");

            var productoExiste = await context.Productos
                .AnyAsync(p => p.Id == productoId && !p.IsDeleted, ct);

            if (!productoExiste)
                throw new InvalidOperationException($"Producto {productoId} inexistente o eliminado.");

            // Solo se setea la FK en la publicación: el Producto interno no se toca.
            listing.ProductoId = productoId;

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = listing.AccountId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Operacion = "LinkProducto",
                Exito = true,
                Detalle = $"Publicación {listing.ItemId} vinculada al producto {productoId}."
            });

            await context.SaveChangesAsync(ct);
        }

        public async Task DesvincularProductoAsync(int listingId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .FirstOrDefaultAsync(l => l.Id == listingId, ct)
                ?? throw new InvalidOperationException($"Publicación {listingId} inexistente.");

            var productoAnterior = listing.ProductoId;
            listing.ProductoId = null;

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = listing.AccountId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Operacion = "UnlinkProducto",
                Exito = true,
                Detalle = $"Publicación {listing.ItemId} desvinculada del producto {productoAnterior}."
            });

            await context.SaveChangesAsync(ct);
        }

        public async Task<MercadoLibreCrearProductoViewModel?> GetCrearProductoViewModelAsync(
            int listingId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == listingId, ct);

            if (listing is null)
                return null;

            if (listing.ProductoId is not null)
                throw new InvalidOperationException("La publicación ya está vinculada a un producto.");

            return new MercadoLibreCrearProductoViewModel
            {
                ListingId = listing.Id,
                Codigo = Truncar(listing.SellerSku, 50) ?? listing.ItemId,
                Nombre = Truncar(listing.Titulo, 200) ?? string.Empty,
                PrecioVenta = listing.Precio,
                StockInicial = listing.AvailableQuantity,
                PorcentajeIVA = 21m,
                ListingItemId = listing.ItemId,
                ListingTitulo = listing.Titulo,
                ListingPrecio = listing.Precio,
                ListingStock = listing.AvailableQuantity,
                ListingSku = listing.SellerSku,
                ListingCondicion = listing.Condition,
                ListingPermalink = listing.Permalink
            };
        }

        public async Task<int> CrearProductoDesdeListingAsync(
            MercadoLibreCrearProductoViewModel viewModel, string usuario, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .FirstOrDefaultAsync(l => l.Id == viewModel.ListingId, ct)
                ?? throw new InvalidOperationException($"Publicación {viewModel.ListingId} inexistente.");

            if (listing.ProductoId is not null)
                throw new InvalidOperationException(
                    "La publicación ya está vinculada a un producto. Desvinculá primero si querés crear otro.");

            var producto = new Producto
            {
                Codigo = viewModel.Codigo.Trim(),
                Nombre = viewModel.Nombre.Trim(),
                Descripcion = string.IsNullOrWhiteSpace(viewModel.Descripcion) ? null : viewModel.Descripcion.Trim(),
                CategoriaId = viewModel.CategoriaId!.Value,
                MarcaId = viewModel.MarcaId!.Value,
                PrecioCompra = viewModel.PrecioCompra,
                PrecioVenta = viewModel.PrecioVenta,
                PorcentajeIVA = viewModel.PorcentajeIVA,
                StockActual = viewModel.StockInicial,
                RequiereNumeroSerie = viewModel.RequiereNumeroSerie,
                CreatedBy = usuario
            };

            // Alta canónica: validaciones del ERP + movimiento de stock inicial.
            var creado = await _productoService.CreateAsync(producto);

            // Vinculación automática (la regla del Checkpoint 3/2: ERP→ML nace vinculado).
            listing.ProductoId = creado.Id;

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = listing.AccountId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Operacion = "CrearProductoDesdeListing",
                Exito = true,
                Detalle = $"Producto {creado.Codigo} (id {creado.Id}) creado desde la publicación {listing.ItemId} " +
                          $"con stock inicial {viewModel.StockInicial:0.##} y vinculado automáticamente. " +
                          "No se modificó nada en Mercado Libre.",
                CreatedBy = usuario
            });

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Producto {ProductoId} creado desde listing ML {ItemId} por {Usuario}",
                creado.Id, listing.ItemId, usuario);

            return creado.Id;
        }

        public async Task ConfigurarOrigenStockAsync(
            int listingId, MercadoLibreOrigenStock? origen, int? productoUnidadId, string usuario, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .FirstOrDefaultAsync(l => l.Id == listingId, ct)
                ?? throw new InvalidOperationException($"Publicación {listingId} inexistente.");

            if (origen == MercadoLibreOrigenStock.DepositoSucursal)
                throw new InvalidOperationException(
                    "El origen 'depósito/sucursal' no está disponible: el ERP no maneja stock por sucursal.");

            if (origen == MercadoLibreOrigenStock.UnidadFisicaEspecifica)
            {
                if (listing.ProductoId is null)
                    throw new InvalidOperationException(
                        "Vinculá la publicación a un producto antes de asignarle una unidad física.");

                if (productoUnidadId is null)
                    throw new InvalidOperationException("Elegí la unidad física que representa esta publicación.");

                var unidad = await context.ProductoUnidades
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == productoUnidadId.Value && !u.IsDeleted, ct)
                    ?? throw new InvalidOperationException("La unidad física seleccionada no existe.");

                if (unidad.ProductoId != listing.ProductoId.Value)
                    throw new InvalidOperationException(
                        "La unidad física seleccionada no pertenece al producto vinculado.");
            }
            else
            {
                productoUnidadId = null;
            }

            listing.OrigenStockOverride = origen;
            listing.ProductoUnidadId = productoUnidadId;

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = listing.AccountId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Operacion = "OrigenStock",
                Exito = true,
                Detalle = origen is null
                    ? $"Origen de stock de {listing.ItemId} vuelto al global por {usuario}."
                    : $"Origen de stock de {listing.ItemId} configurado a {origen} " +
                      (productoUnidadId is null ? "" : $"(unidad {productoUnidadId}) ") + $"por {usuario}.",
                CreatedBy = usuario
            });

            await context.SaveChangesAsync(ct);
        }

        public async Task ConfigurarVariacionAsync(
            int listingId,
            long variationId,
            int? productoId,
            MercadoLibreOrigenStock? origen,
            int? productoUnidadId,
            string usuario,
            CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .Include(l => l.Variaciones)
                .FirstOrDefaultAsync(l => l.Id == listingId, ct)
                ?? throw new InvalidOperationException($"Publicación {listingId} inexistente.");

            var variacion = listing.Variaciones
                .FirstOrDefault(v => v.VariationId == variationId && !v.IsDeleted)
                ?? throw new InvalidOperationException($"Variación {variationId} inexistente en la publicación {listing.ItemId}.");

            if (origen == MercadoLibreOrigenStock.DepositoSucursal)
                throw new InvalidOperationException(
                    "El origen 'depósito/sucursal' no está disponible: el ERP no maneja stock por sucursal.");

            if (productoId.HasValue)
            {
                var productoExiste = await context.Productos
                    .AnyAsync(p => p.Id == productoId.Value && !p.IsDeleted, ct);

                if (!productoExiste)
                    throw new InvalidOperationException($"Producto {productoId.Value} inexistente o eliminado.");
            }

            var productoEfectivoId = productoId ?? listing.ProductoId;

            if (origen == MercadoLibreOrigenStock.UnidadFisicaEspecifica)
            {
                if (productoEfectivoId is null)
                    throw new InvalidOperationException(
                        "Vinculá la variación a un producto antes de asignarle una unidad física.");

                if (productoUnidadId is null)
                    throw new InvalidOperationException("Elegí la unidad física que representa esta variación.");

                var unidad = await context.ProductoUnidades
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == productoUnidadId.Value && !u.IsDeleted, ct)
                    ?? throw new InvalidOperationException("La unidad física seleccionada no existe.");

                if (unidad.ProductoId != productoEfectivoId.Value)
                    throw new InvalidOperationException(
                        "La unidad física seleccionada no pertenece al producto efectivo de la variación.");
            }
            else
            {
                productoUnidadId = null;
            }

            variacion.ProductoId = productoId;
            variacion.OrigenStockOverride = origen;
            variacion.ProductoUnidadId = productoUnidadId;

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = listing.AccountId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Operacion = "VariationLink",
                Exito = true,
                Detalle = $"Variación {variationId} configurada por {usuario}: " +
                          $"producto {(productoId?.ToString() ?? "fallback")}, " +
                          $"origen {(origen?.ToString() ?? "fallback")}" +
                          (productoUnidadId is null ? "." : $", unidad {productoUnidadId}."),
                CreatedBy = usuario
            });

            await context.SaveChangesAsync(ct);
        }

        private static void ActualizarListing(MercadoLibreListing listing, MeliItemDto item)
        {
            listing.Titulo = Truncar(item.Title, 300) ?? string.Empty;
            listing.Precio = item.Price ?? 0m;
            listing.CurrencyId = item.CurrencyId ?? "ARS";
            listing.AvailableQuantity = item.AvailableQuantity ?? 0;
            listing.SoldQuantity = item.SoldQuantity ?? 0;
            listing.Status = item.Status ?? string.Empty;
            listing.SubStatus = item.SubStatus.Count > 0 ? Truncar(string.Join(',', item.SubStatus), 200) : null;
            listing.Permalink = Truncar(item.Permalink, 500);
            listing.CategoryId = Truncar(item.CategoryId, 30);
            listing.ListingTypeId = Truncar(item.ListingTypeId, 30);
            listing.Condition = Truncar(item.Condition, 20);
            listing.SellerSku = Truncar(item.ResolverSellerSku(), 100);
            listing.TieneVariaciones = item.Variations.Count > 0;
            listing.LastSyncUtc = DateTime.UtcNow;
            listing.RawJson = JsonSerializer.Serialize(item);
            listing.IsDeleted = false;

            ActualizarVariaciones(listing, item);
        }

        private static void ActualizarVariaciones(MercadoLibreListing listing, MeliItemDto item)
        {
            var recibidas = item.Variations.ToDictionary(v => v.Id);

            // Variaciones que ya no existen en ML: soft delete (nunca borrado físico).
            foreach (var existente in listing.Variaciones)
            {
                if (!recibidas.ContainsKey(existente.VariationId))
                    existente.IsDeleted = true;
            }

            foreach (var dto in item.Variations)
            {
                var variacion = listing.Variaciones.FirstOrDefault(v => v.VariationId == dto.Id);

                if (variacion is null)
                {
                    variacion = new MercadoLibreListingVariation { VariationId = dto.Id };
                    listing.Variaciones.Add(variacion);
                }

                variacion.Precio = dto.Price ?? 0m;
                variacion.AvailableQuantity = dto.AvailableQuantity ?? 0;
                variacion.SoldQuantity = dto.SoldQuantity ?? 0;
                variacion.SellerSku = Truncar(dto.ResolverSellerSku(), 100);
                variacion.AttributesJson = dto.AttributeCombinations.HasValue
                    ? dto.AttributeCombinations.Value.GetRawText()
                    : null;
                variacion.IsDeleted = false;
            }
        }

        private static string? Truncar(string? valor, int max) =>
            valor is null ? null : (valor.Length <= max ? valor : valor[..max]);

        private async Task RegistrarErrorAsync(int accountId, string operacion, Exception ex, long duracionMs, CancellationToken ct)
        {
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(ct);

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = accountId,
                    Operacion = operacion,
                    Exito = false,
                    Detalle = Truncar(ex.Message, 2000),
                    DuracionMs = duracionMs
                });

                await context.SaveChangesAsync(ct);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "No se pudo registrar el error de sincronización ML para cuenta {AccountId}", accountId);
            }
        }
    }
}
