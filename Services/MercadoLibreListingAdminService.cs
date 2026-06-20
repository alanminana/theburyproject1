using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class MercadoLibreListingAdminService : IMercadoLibreListingAdminService
    {
        private static readonly Dictionary<string, (string Status, string Verbo)> AccionesEstado = new()
        {
            ["pausar"] = ("paused", "pausada"),
            ["reactivar"] = ("active", "reactivada"),
            ["finalizar"] = ("closed", "finalizada")
        };

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibrePricingService _pricingService;
        private readonly ILogger<MercadoLibreListingAdminService> _logger;

        public MercadoLibreListingAdminService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreApiClient apiClient,
            IMercadoLibreAuthService authService,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibrePricingService pricingService,
            ILogger<MercadoLibreListingAdminService> logger)
        {
            _contextFactory = contextFactory;
            _apiClient = apiClient;
            _authService = authService;
            _configuracionService = configuracionService;
            _pricingService = pricingService;
            _logger = logger;
        }

        public async Task<MercadoLibreListingDetalleViewModel?> GetDetalleAsync(
            int listingId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .AsNoTracking()
                .Include(l => l.Producto)
                .Include(l => l.Variaciones)
                    .ThenInclude(v => v.Producto)
                .Include(l => l.Variaciones)
                    .ThenInclude(v => v.ProductoUnidad)
                .FirstOrDefaultAsync(l => l.Id == listingId, ct);

            if (listing is null)
                return null;

            var config = await _configuracionService.GetAsync(ct);

            var variacionesActivas = listing.Variaciones
                .Where(v => !v.IsDeleted)
                .OrderBy(v => v.VariationId)
                .ToList();

            var productoIdsVariaciones = variacionesActivas
                .Select(v => v.ProductoId ?? (variacionesActivas.Count == 1 ? listing.ProductoId : null))
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var unidadIdsSeleccionadas = variacionesActivas
                .Where(v => v.ProductoUnidadId.HasValue)
                .Select(v => v.ProductoUnidadId!.Value)
                .Distinct()
                .ToList();

            var unidadesPorProducto = productoIdsVariaciones.Count == 0
                ? new Dictionary<int, List<MercadoLibreUnidadOptionViewModel>>()
                : await context.ProductoUnidades
                    .AsNoTracking()
                    .Where(u => productoIdsVariaciones.Contains(u.ProductoId) && !u.IsDeleted &&
                                (u.Estado == TheBuryProject.Models.Enums.EstadoUnidad.EnStock ||
                                 unidadIdsSeleccionadas.Contains(u.Id)))
                    .OrderBy(u => u.FechaIngreso).ThenBy(u => u.Id)
                    .GroupBy(u => u.ProductoId)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Select(u => new MercadoLibreUnidadOptionViewModel
                        {
                            Id = u.Id,
                            Codigo = u.CodigoInternoUnidad,
                            NumeroSerie = u.NumeroSerie
                        }).ToList(),
                        ct);

            var vm = new MercadoLibreListingDetalleViewModel
            {
                Id = listing.Id,
                ItemId = listing.ItemId,
                Titulo = listing.Titulo,
                Precio = listing.Precio,
                CurrencyId = listing.CurrencyId,
                AvailableQuantity = listing.AvailableQuantity,
                SoldQuantity = listing.SoldQuantity,
                Status = listing.Status,
                SubStatus = listing.SubStatus,
                Permalink = listing.Permalink,
                CategoryId = listing.CategoryId,
                ListingTypeId = listing.ListingTypeId,
                Condition = listing.Condition,
                SellerSku = listing.SellerSku,
                TieneVariaciones = listing.TieneVariaciones,
                LastSyncUtc = listing.LastSyncUtc,
                ProductoId = listing.ProductoId,
                ProductoCodigo = listing.Producto?.Codigo,
                ProductoNombre = listing.Producto?.Nombre,
                ProductoStockActual = listing.Producto?.StockActual,
                ProductoPrecioVenta = listing.Producto?.PrecioVenta,
                ModoSimulacion = config.ModoSimulacion,
            };

            foreach (var variacion in variacionesActivas)
            {
                var permitirFallbackProductoListing = variacionesActivas.Count == 1;
                var productoEfectivo = variacion.Producto ?? (permitirFallbackProductoListing ? listing.Producto : null);
                var disponible = await MercadoLibreStockResolver
                    .ResolverStockDisponibleParaVariacionAsync(
                        context, listing, variacion, config, permitirFallbackProductoListing, ct);

                vm.Variaciones.Add(new MercadoLibreVariacionViewModel
                {
                    VariationId = variacion.VariationId,
                    Precio = variacion.Precio,
                    AvailableQuantity = variacion.AvailableQuantity,
                    SoldQuantity = variacion.SoldQuantity,
                    SellerSku = variacion.SellerSku,
                    Atributos = ResumirAtributos(variacion.AttributesJson),
                    ProductoId = variacion.ProductoId,
                    ProductoCodigo = productoEfectivo?.Codigo,
                    ProductoNombre = productoEfectivo?.Nombre,
                    OrigenStockOverride = variacion.OrigenStockOverride,
                    OrigenStockEfectivo = disponible.Origen,
                    ProductoUnidadId = variacion.ProductoUnidadId,
                    StockDisponibleSegunOrigen = disponible.Stock,
                    AdvertenciaStock = disponible.Advertencia,
                    UsaProductoDePublicacion = variacion.ProductoId is null && productoEfectivo is not null,
                    RequiereVinculoParaStock = disponible.BloqueaSync,
                    UnidadesDisponibles = productoEfectivo is null
                        ? new List<MercadoLibreUnidadOptionViewModel>()
                        : unidadesPorProducto.GetValueOrDefault(productoEfectivo.Id) ?? new List<MercadoLibreUnidadOptionViewModel>()
                });
            }

            // Datos on-demand desde ML (no se persisten: ML es la fuente): descripción,
            // imágenes actuales y estado live (refleja transiciones under_review → active).
            try
            {
                var token = await _authService.GetValidAccessTokenAsync(listing.AccountId, ct);

                try
                {
                    vm.Descripcion = await _apiClient.GetItemDescriptionAsync(token, listing.ItemId, ct);
                    vm.DescripcionConsultada = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo consultar la descripción de {ItemId}", listing.ItemId);
                    vm.DescripcionConsultada = false;
                }

                try
                {
                    var items = await _apiClient.GetItemsAsync(token, new[] { listing.ItemId }, ct);
                    var item = items.FirstOrDefault();
                    if (item is not null)
                    {
                        vm.ImagenesActuales = item.Pictures
                            .Select(p => p.UrlEfectiva)
                            .Where(u => !string.IsNullOrWhiteSpace(u))
                            .Select(u => u!)
                            .ToList();
                        vm.ImagenesConsultadas = true;

                        if (!string.IsNullOrWhiteSpace(item.Status))
                            vm.Status = item.Status!;
                        if (item.SubStatus.Count > 0)
                            vm.SubStatus = string.Join(", ", item.SubStatus);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudieron consultar imágenes/estado de {ItemId}", listing.ItemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo obtener token para el detalle de {ItemId}", listing.ItemId);
                vm.DescripcionConsultada = false;
            }

            // Origen de stock (Checkpoint 4): global, override y stock publicable.
            vm.OrigenStockGlobal = config.OrigenStock;
            vm.OrigenStockOverride = listing.OrigenStockOverride;
            vm.ProductoUnidadId = listing.ProductoUnidadId;

            if (listing.Producto is not null)
            {
                var disponible = await MercadoLibreStockResolver
                    .ResolverStockDisponibleAsync(context, listing, config, ct);

                vm.StockDisponibleSegunOrigen = disponible.Stock;
                vm.AdvertenciaStock = disponible.Advertencia;

                vm.UnidadesDisponibles = await context.ProductoUnidades
                    .AsNoTracking()
                    .Where(u => u.ProductoId == listing.ProductoId!.Value && !u.IsDeleted &&
                                (u.Estado == TheBuryProject.Models.Enums.EstadoUnidad.EnStock || u.Id == listing.ProductoUnidadId))
                    .OrderBy(u => u.FechaIngreso).ThenBy(u => u.Id)
                    .Select(u => new MercadoLibreUnidadOptionViewModel
                    {
                        Id = u.Id,
                        Codigo = u.CodigoInternoUnidad,
                        NumeroSerie = u.NumeroSerie
                    })
                    .ToListAsync(ct);
            }

            // Calculadora (Fase G) — solo con producto vinculado.
            if (listing.ProductoId.HasValue)
                vm.Desglose = await _pricingService.CalcularDesgloseAsync(listing.ProductoId.Value, ct);

            vm.UltimosLogs = await context.MercadoLibreSyncLogs
                .AsNoTracking()
                .Where(l => l.ItemId == listing.ItemId || l.ListingId == listing.Id)
                .OrderByDescending(l => l.CreatedAt)
                .Take(20)
                .Select(l => new MercadoLibreSyncLogViewModel
                {
                    Fecha = l.CreatedAt,
                    Operacion = l.Operacion,
                    Exito = l.Exito,
                    Detalle = l.Detalle
                })
                .ToListAsync(ct);

            return vm;
        }

        public async Task<(bool Ok, string Mensaje)> CambiarEstadoAsync(
            int listingId, string accion, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            if (!AccionesEstado.TryGetValue(accion?.ToLowerInvariant() ?? "", out var destino))
                return (false, $"Acción de estado inválida: '{accion}'.");

            return await EjecutarAccionAsync(
                listingId,
                "CambiarEstado",
                confirmarReal,
                usuario,
                descripcionCambio: $"status → {destino.Status}",
                payload: new Dictionary<string, object> { ["status"] = destino.Status },
                aplicarLocal: (listing, item) => listing.Status = item.Status ?? destino.Status,
                mensajeExito: $"Publicación {destino.Verbo}.",
                ct);
        }

        public async Task<(bool Ok, string Mensaje)> EditarImagenesAsync(
            int listingId, IReadOnlyList<string> imagenesUrls, bool confirmarReal, string usuario,
            CancellationToken ct = default)
        {
            var urls = (imagenesUrls ?? Array.Empty<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (urls.Count == 0)
                return (false, "Agregá al menos una URL de imagen.");

            // Mercado Libre admite hasta 12 imágenes por publicación.
            if (urls.Count > 12)
                return (false, "Mercado Libre permite hasta 12 imágenes por publicación.");

            if (urls.Any(u => !EsUrlImagenPublica(u!)))
                return (false, "Hay URLs inválidas o no públicas. Usá direcciones http/https absolutas (no localhost): ML descarga la imagen desde la URL.");

            var pictures = urls
                .Select(u => (object)new Dictionary<string, object> { ["source"] = u! })
                .ToList();

            return await EjecutarAccionAsync(
                listingId,
                "EditarImagenes",
                confirmarReal,
                usuario,
                descripcionCambio: $"imágenes → {urls.Count} foto(s)",
                payload: new Dictionary<string, object> { ["pictures"] = pictures },
                // ML rehospeda las imágenes en su CDN; no hay nada local que persistir.
                aplicarLocal: (_, _) => { },
                mensajeExito: $"Imágenes actualizadas ({urls.Count}).",
                ct);
        }

        /// <summary>URL absoluta http/https que ML pueda descargar (no localhost/loopback).</summary>
        private static bool EsUrlImagenPublica(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            var host = uri.Host.ToLowerInvariant();
            return host != "localhost" && host != "127.0.0.1" && host != "::1" && host != "[::1]";
        }

        public async Task<(bool Ok, string Mensaje)> EditarAsync(
            int listingId, string? titulo, decimal? precio, int? stock, string? sellerSku,
            bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            var payload = new Dictionary<string, object>();
            var cambios = new List<string>();

            await using (var context = await _contextFactory.CreateDbContextAsync(ct))
            {
                var listing = await context.MercadoLibreListings
                    .AsNoTracking()
                    .Include(l => l.Variaciones)
                    .FirstOrDefaultAsync(l => l.Id == listingId, ct);

                if (listing is null)
                    return (false, "Publicación no encontrada.");

                var variaciones = listing.Variaciones.Where(v => !v.IsDeleted).ToList();

                if (!string.IsNullOrWhiteSpace(titulo) && titulo.Trim() != listing.Titulo)
                {
                    payload["title"] = titulo.Trim();
                    cambios.Add($"título → \"{titulo.Trim()}\"");
                }

                if (precio.HasValue && precio.Value > 0 && precio.Value != listing.Precio)
                {
                    if (variaciones.Count > 0)
                    {
                        payload["variations"] = variaciones
                            .Select(v => new Dictionary<string, object> { ["id"] = v.VariationId, ["price"] = precio.Value })
                            .ToList();
                    }
                    else
                    {
                        payload["price"] = precio.Value;
                    }

                    cambios.Add($"precio {listing.Precio:0.##} → {precio.Value:0.##}");
                }

                if (stock.HasValue && stock.Value >= 0 && stock.Value != listing.AvailableQuantity)
                {
                    if (variaciones.Count > 1)
                        return (false, "Stock con varias variaciones: el ERP no maneja stock por variación (editar en ML).");

                    if (variaciones.Count == 1)
                    {
                        // merge con price si ya está
                        if (payload.TryGetValue("variations", out var existente) &&
                            existente is List<Dictionary<string, object>> lista && lista.Count == 1)
                        {
                            lista[0]["available_quantity"] = stock.Value;
                        }
                        else
                        {
                            payload["variations"] = new List<Dictionary<string, object>>
                            {
                                new() { ["id"] = variaciones[0].VariationId, ["available_quantity"] = stock.Value }
                            };
                        }
                    }
                    else
                    {
                        payload["available_quantity"] = stock.Value;
                    }

                    cambios.Add($"stock {listing.AvailableQuantity} → {stock.Value}");
                }

                if (!string.IsNullOrWhiteSpace(sellerSku) && sellerSku.Trim() != listing.SellerSku)
                {
                    payload["attributes"] = new[]
                    {
                        new Dictionary<string, object> { ["id"] = "SELLER_SKU", ["value_name"] = sellerSku.Trim() }
                    };
                    cambios.Add($"SKU → {sellerSku.Trim()}");
                }
            }

            if (payload.Count == 0)
                return (false, "No hay cambios para aplicar.");

            return await EjecutarAccionAsync(
                listingId,
                "EditarPublicacion",
                confirmarReal,
                usuario,
                descripcionCambio: string.Join(", ", cambios),
                payload,
                aplicarLocal: (listing, item) =>
                {
                    if (!string.IsNullOrEmpty(item.Title)) listing.Titulo = item.Title;
                    if (item.Price.HasValue) listing.Precio = item.Price.Value;
                    if (item.AvailableQuantity.HasValue) listing.AvailableQuantity = item.AvailableQuantity.Value;
                    listing.SellerSku = item.ResolverSellerSku() ?? listing.SellerSku;

                    foreach (var variacionApi in item.Variations)
                    {
                        var local = listing.Variaciones.FirstOrDefault(v => v.VariationId == variacionApi.Id && !v.IsDeleted);
                        if (local is not null)
                        {
                            if (variacionApi.Price.HasValue) local.Precio = variacionApi.Price.Value;
                            if (variacionApi.AvailableQuantity.HasValue) local.AvailableQuantity = variacionApi.AvailableQuantity.Value;
                        }
                    }
                },
                mensajeExito: $"Publicación actualizada: {string.Join(", ", cambios)}.",
                ct);
        }

        public async Task<(bool Ok, string Mensaje)> EditarDescripcionAsync(
            int listingId, string plainText, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .FirstOrDefaultAsync(l => l.Id == listingId, ct);

            if (listing is null)
                return (false, "Publicación no encontrada.");

            var config = await _configuracionService.GetAsync(ct);
            var simulado = config.ModoSimulacion || !confirmarReal;

            if (simulado)
            {
                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = listing.AccountId,
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Operacion = "EditarDescripcion",
                    Exito = true,
                    Detalle = $"SIMULADO por {usuario}: nueva descripción ({plainText.Length} caracteres).",
                    CreatedBy = usuario
                });
                await context.SaveChangesAsync(ct);

                return (true, "SIMULACIÓN: la descripción no se envió a Mercado Libre.");
            }

            var sw = Stopwatch.StartNew();

            try
            {
                var token = await _authService.GetValidAccessTokenAsync(listing.AccountId, ct);
                await _apiClient.UpdateItemDescriptionAsync(token, listing.ItemId, plainText, ct);

                sw.Stop();

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = listing.AccountId,
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Operacion = "EditarDescripcion",
                    Exito = true,
                    Detalle = $"Descripción actualizada por {usuario} ({plainText.Length} caracteres).",
                    DuracionMs = sw.ElapsedMilliseconds,
                    CreatedBy = usuario
                });
                await context.SaveChangesAsync(ct);

                return (true, "Descripción actualizada en Mercado Libre.");
            }
            catch (Exception ex)
            {
                sw.Stop();

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = listing.AccountId,
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Operacion = "EditarDescripcion",
                    Exito = false,
                    Detalle = Truncar($"Error: {ex.Message}", 2000),
                    DuracionMs = sw.ElapsedMilliseconds,
                    CreatedBy = usuario
                });
                await context.SaveChangesAsync(ct);

                return (false, $"Error actualizando la descripción: {ex.Message}");
            }
        }

        public async Task<(bool Ok, string Mensaje)> EditarCategoriaAsync(
            int listingId, string? categoryId, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            var nueva = (categoryId ?? string.Empty).Trim();
            if (nueva.Length == 0)
                return (false, "Elegí una categoría.");

            string? actual;
            await using (var context = await _contextFactory.CreateDbContextAsync(ct))
            {
                var listing = await context.MercadoLibreListings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.Id == listingId, ct);

                if (listing is null)
                    return (false, "Publicación no encontrada.");

                actual = listing.CategoryId;
            }

            if (string.Equals(nueva, actual, StringComparison.OrdinalIgnoreCase))
                return (false, "La categoría es la misma: no hay cambios para aplicar.");

            return await EjecutarAccionAsync(
                listingId,
                "EditarCategoria",
                confirmarReal,
                usuario,
                descripcionCambio: $"categoría {actual ?? "—"} → {nueva}",
                payload: new Dictionary<string, object> { ["category_id"] = nueva },
                aplicarLocal: (listing, item) => listing.CategoryId = item.CategoryId ?? nueva,
                mensajeExito: $"Categoría actualizada a {nueva}.",
                ct);
        }

        // ------------------------------------------------------------------
        // Núcleo común simulación/real + log
        // ------------------------------------------------------------------

        private async Task<(bool Ok, string Mensaje)> EjecutarAccionAsync(
            int listingId,
            string operacion,
            bool confirmarReal,
            string usuario,
            string descripcionCambio,
            Dictionary<string, object> payload,
            Action<MercadoLibreListing, TheBuryProject.Models.DTOs.MeliItemDto> aplicarLocal,
            string mensajeExito,
            CancellationToken ct)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listing = await context.MercadoLibreListings
                .Include(l => l.Variaciones)
                .FirstOrDefaultAsync(l => l.Id == listingId, ct);

            if (listing is null)
                return (false, "Publicación no encontrada.");

            var config = await _configuracionService.GetAsync(ct);
            var simulado = config.ModoSimulacion || !confirmarReal;

            if (simulado)
            {
                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = listing.AccountId,
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Operacion = operacion,
                    Exito = true,
                    Detalle = $"SIMULADO por {usuario}: {descripcionCambio}. Payload: {JsonSerializer.Serialize(payload)}",
                    CreatedBy = usuario
                });
                await context.SaveChangesAsync(ct);

                return (true, $"SIMULACIÓN: {descripcionCambio} (no se envió a Mercado Libre).");
            }

            var sw = Stopwatch.StartNew();

            try
            {
                var token = await _authService.GetValidAccessTokenAsync(listing.AccountId, ct);
                var (payloadItem, payloadsVariaciones) = SepararPayloadVariaciones(payload);

                TheBuryProject.Models.DTOs.MeliItemDto? actualizado = null;

                if (payloadItem.Count > 0)
                    actualizado = await _apiClient.UpdateItemAsync(token, listing.ItemId, payloadItem, ct);

                if (payloadsVariaciones.Count > 0)
                {
                    actualizado ??= new TheBuryProject.Models.DTOs.MeliItemDto
                    {
                        Id = listing.ItemId,
                        Title = listing.Titulo,
                        Price = listing.Precio,
                        AvailableQuantity = listing.AvailableQuantity,
                        Status = listing.Status
                    };

                    foreach (var (variationId, variationPayload) in payloadsVariaciones)
                    {
                        var variacionActualizada = await _apiClient.UpdateItemVariationAsync(
                            token, listing.ItemId, variationId, variationPayload, ct);

                        actualizado.Variations.Add(variacionActualizada);
                    }
                }

                aplicarLocal(listing, actualizado ?? new TheBuryProject.Models.DTOs.MeliItemDto { Id = listing.ItemId });
                listing.LastSyncUtc = DateTime.UtcNow;

                sw.Stop();

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = listing.AccountId,
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Operacion = operacion,
                    Exito = true,
                    Detalle = $"Aplicado por {usuario}: {descripcionCambio}.",
                    DuracionMs = sw.ElapsedMilliseconds,
                    CreatedBy = usuario
                });

                await context.SaveChangesAsync(ct);

                return (true, mensajeExito);
            }
            catch (Exception ex)
            {
                sw.Stop();

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = listing.AccountId,
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Operacion = operacion,
                    Exito = false,
                    Detalle = Truncar($"Error en {descripcionCambio}: {ex.Message}", 2000),
                    DuracionMs = sw.ElapsedMilliseconds,
                    CreatedBy = usuario
                });
                await context.SaveChangesAsync(ct);

                _logger.LogError(ex, "Acción {Operacion} falló para {ItemId}", operacion, listing.ItemId);

                return (false, $"Mercado Libre rechazó el cambio: {ex.Message}");
            }
        }

        private static (Dictionary<string, object> PayloadItem, List<(long VariationId, Dictionary<string, object> Payload)> Variaciones)
            SepararPayloadVariaciones(Dictionary<string, object> payload)
        {
            var payloadItem = payload
                .Where(kvp => !string.Equals(kvp.Key, "variations", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            var variaciones = new List<(long VariationId, Dictionary<string, object> Payload)>();

            if (!payload.TryGetValue("variations", out var rawVariations))
                return (payloadItem, variaciones);

            if (rawVariations is not IEnumerable<Dictionary<string, object>> entries)
                return (payloadItem, variaciones);

            foreach (var entry in entries)
            {
                if (!entry.TryGetValue("id", out var rawId))
                    continue;

                var variationId = Convert.ToInt64(rawId);
                var variationPayload = entry
                    .Where(kvp => !string.Equals(kvp.Key, "id", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                if (variationPayload.Count > 0)
                    variaciones.Add((variationId, variationPayload));
            }

            return (payloadItem, variaciones);
        }

        private static string? ResumirAtributos(string? attributesJson)
        {
            if (string.IsNullOrWhiteSpace(attributesJson))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(attributesJson);

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;

                var partes = new List<string>();

                foreach (var attr in doc.RootElement.EnumerateArray())
                {
                    var nombre = attr.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var valor = attr.TryGetProperty("value_name", out var v) ? v.GetString() : null;

                    if (nombre is not null && valor is not null)
                        partes.Add($"{nombre}: {valor}");
                }

                return partes.Count > 0 ? string.Join(" · ", partes) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Truncar(string valor, int max)
            => valor.Length <= max ? valor : valor[..max];
    }
}
