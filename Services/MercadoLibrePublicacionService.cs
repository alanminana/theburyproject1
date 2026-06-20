using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class MercadoLibrePublicacionService : IMercadoLibrePublicacionService
    {
        private static readonly string[] CondicionesValidas = { "new", "used" };

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibrePricingService _pricingService;
        private readonly IMercadoLibreCategoryCatalogService _catalogService;
        private readonly ILogger<MercadoLibrePublicacionService> _logger;

        public MercadoLibrePublicacionService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreApiClient apiClient,
            IMercadoLibreAuthService authService,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibrePricingService pricingService,
            IMercadoLibreCategoryCatalogService catalogService,
            ILogger<MercadoLibrePublicacionService> logger)
        {
            _contextFactory = contextFactory;
            _apiClient = apiClient;
            _authService = authService;
            _configuracionService = configuracionService;
            _pricingService = pricingService;
            _catalogService = catalogService;
            _logger = logger;
        }

        public async Task<MercadoLibreBorradorCrearResultado> CrearBorradorAsync(
            int productoId, string usuario, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var producto = await context.Productos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productoId && !p.IsDeleted, ct)
                ?? throw new InvalidOperationException($"Producto {productoId} inexistente o eliminado.");

            var borradorExistente = await context.MercadoLibrePublicacionBorradores
                .AsNoTracking()
                .Where(b => b.ProductoId == productoId
                    && (b.Estado == MercadoLibreBorradorEstado.Borrador
                        || b.Estado == MercadoLibreBorradorEstado.Validado))
                .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (borradorExistente is not null)
            {
                return new MercadoLibreBorradorCrearResultado(
                    borradorExistente.Id,
                    null,
                    true,
                    "El producto ya tiene un borrador activo. Continuá desde el borrador existente.");
            }

            var listingExistente = await context.MercadoLibreListings
                .AsNoTracking()
                .Where(l => l.ProductoId == productoId)
                .Where(l => l.Status == null
                    || (l.Status.ToLower() != "closed"
                        && l.Status.ToLower() != "deleted"))
                .OrderByDescending(l => l.LastSyncUtc ?? l.UpdatedAt ?? l.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (listingExistente is not null)
            {
                return new MercadoLibreBorradorCrearResultado(
                    null,
                    listingExistente.Id,
                    true,
                    $"El producto ya tiene una publicación vinculada ({listingExistente.ItemId}). Revisala antes de crear un borrador nuevo.");
            }

            // Precio sugerido: precio de canal (lista + ajuste + redondeo); si no
            // se puede calcular, cae al precio de venta del producto.
            decimal precioSugerido = producto.PrecioVenta;
            var precios = await _pricingService.CalcularPrecioCanalAsync(new[] { productoId }, ct);
            if (precios.TryGetValue(productoId, out var canal) && canal.PrecioCanal > 0)
                precioSugerido = canal.PrecioCanal;

            // Stock sugerido según el origen global configurado.
            var disponible = await MercadoLibreStockResolver.ResolverParaProductoAsync(
                context, producto, config.OrigenStock, null, ct);

            var borrador = new MercadoLibrePublicacionBorrador
            {
                ProductoId = producto.Id,
                Titulo = Truncar(producto.Nombre, 60) ?? string.Empty,
                Descripcion = producto.Descripcion,
                Precio = precioSugerido,
                Stock = disponible.Stock,
                Condicion = "new",
                CreatedBy = usuario
            };

            context.MercadoLibrePublicacionBorradores.Add(borrador);

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = config.AccountId,
                Operacion = "BorradorCrear",
                Exito = true,
                Detalle = $"Borrador creado desde producto {producto.Codigo} por {usuario} " +
                          $"(precio sugerido {precioSugerido:N2}, stock sugerido {disponible.Stock}).",
                CreatedBy = usuario
            });

            await context.SaveChangesAsync(ct);

            return new MercadoLibreBorradorCrearResultado(
                borrador.Id,
                null,
                false,
                "Borrador creado. Completá la categoría ML y validalo antes de publicar.");
        }

        public async Task ActualizarBorradorAsync(
            MercadoLibreBorradorEditViewModel viewModel, string usuario, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var borrador = await context.MercadoLibrePublicacionBorradores
                .FirstOrDefaultAsync(b => b.Id == viewModel.Id, ct)
                ?? throw new InvalidOperationException($"Borrador {viewModel.Id} inexistente.");

            if (borrador.Estado is not (MercadoLibreBorradorEstado.Borrador or MercadoLibreBorradorEstado.Validado))
                throw new InvalidOperationException($"El borrador está {borrador.Estado}: no se puede editar.");

            var condicion = (viewModel.Condicion ?? "new").Trim().ToLowerInvariant();
            if (!CondicionesValidas.Contains(condicion))
                throw new InvalidOperationException($"Condición inválida: '{viewModel.Condicion}' (new | used).");

            borrador.Titulo = Truncar(viewModel.Titulo.Trim(), 60) ?? string.Empty;
            borrador.Descripcion = string.IsNullOrWhiteSpace(viewModel.Descripcion) ? null : viewModel.Descripcion.Trim();
            borrador.Precio = viewModel.Precio;
            borrador.Stock = viewModel.Stock;
            borrador.CategoryIdMl = Truncar(viewModel.CategoryIdMl?.Trim(), 30);
            // Snapshot de la categoría resuelto server-side por el picker.
            if (string.IsNullOrWhiteSpace(borrador.CategoryIdMl))
            {
                borrador.CategoryNombre = null;
                borrador.CategoryPathFromRoot = null;
                borrador.CategoryEsHoja = null;
            }
            else
            {
                borrador.CategoryNombre = Truncar(viewModel.CategoryNombre?.Trim(), 200);
                borrador.CategoryPathFromRoot = Truncar(viewModel.CategoryPathFromRoot?.Trim(), 500);
                borrador.CategoryEsHoja = viewModel.CategoryEsHoja;
            }
            borrador.Condicion = condicion;
            borrador.ListingTypeId = Truncar(string.IsNullOrWhiteSpace(viewModel.ListingTypeId)
                ? "gold_special" : viewModel.ListingTypeId.Trim(), 30)!;
            borrador.Garantia = Truncar(viewModel.Garantia?.Trim(), 200);

            // Imágenes: una URL por línea → lista normalizada (trim + dedupe) → JSON.
            var imagenes = ParseImagenesDesdeTexto(viewModel.ImagenesUrls);
            borrador.ImagenesJson = imagenes.Count == 0 ? null : JsonSerializer.Serialize(imagenes);

            // Atributos específicos de categoría ML: se guardan solo los que traen valor.
            // Si la categoría se borró/cambió, los atributos previos pierden sentido.
            borrador.AtributosCompletadosJson = SerializarAtributos(
                string.IsNullOrWhiteSpace(borrador.CategoryIdMl) ? null : viewModel.Atributos);

            // Toda edición invalida la validación anterior.
            borrador.Estado = MercadoLibreBorradorEstado.Borrador;
            borrador.ErroresValidacion = null;
            borrador.FechaValidacionUtc = null;
            borrador.PublicadoEnSimulacion = false;
            borrador.FechaSimulacionUtc = null;
            borrador.PayloadSimuladoJson = null;
            borrador.UpdatedAt = DateTime.UtcNow;
            borrador.UpdatedBy = usuario;

            await context.SaveChangesAsync(ct);
        }

        public async Task<(bool Ok, List<string> Errores, List<string> Advertencias)> ValidarAsync(
            int borradorId, string usuario, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var borrador = await context.MercadoLibrePublicacionBorradores
                .Include(b => b.Producto)
                .FirstOrDefaultAsync(b => b.Id == borradorId, ct)
                ?? throw new InvalidOperationException($"Borrador {borradorId} inexistente.");

            if (borrador.Estado is not (MercadoLibreBorradorEstado.Borrador or MercadoLibreBorradorEstado.Validado))
                throw new InvalidOperationException($"El borrador está {borrador.Estado}: no se puede validar.");

            var errores = new List<string>();
            var advertencias = new List<string>();

            if (string.IsNullOrWhiteSpace(borrador.Titulo))
                errores.Add("El título es obligatorio.");
            else if (borrador.Titulo.Length > 60)
                errores.Add("El título supera los 60 caracteres que permite Mercado Libre.");

            if (borrador.Precio <= 0)
                errores.Add("El precio debe ser mayor a 0.");

            if (string.IsNullOrWhiteSpace(borrador.CategoryIdMl))
                errores.Add("Falta la categoría de Mercado Libre. Elegila con el buscador de categorías del borrador.");
            else if (borrador.CategoryEsHoja == false)
                errores.Add("La categoría elegida no es una categoría hoja [leaf]: ML solo permite publicar en hojas. Elegí una más específica.");

            // Validación contra el catálogo local de categorías (si está importado):
            // hoja + listing_allowed y atributos obligatorios completos (Checkpoint 12).
            var camposAtributosError = new List<string>();
            if (!string.IsNullOrWhiteSpace(borrador.CategoryIdMl))
            {
                var catalogoImportado = await _catalogService.HayCatalogoAsync(ct: ct);
                if (!catalogoImportado)
                {
                    advertencias.Add(
                        "Catálogo de categorías ML no importado: no se pueden validar los atributos obligatorios localmente. " +
                        "Importalo desde Configuración o revisá los atributos en Mercado Libre antes de publicar.");
                }
                else
                {
                    var (existe, esHoja, listingAllowed) =
                        await _catalogService.IsLeafListingAllowedAsync(borrador.CategoryIdMl, ct: ct);

                    if (!existe)
                    {
                        advertencias.Add(
                            $"La categoría {borrador.CategoryIdMl} no está en el catálogo local importado: " +
                            "no se pueden validar sus atributos obligatorios. Reimportá el catálogo si es reciente.");
                    }
                    else
                    {
                        if (!esHoja)
                            errores.Add("La categoría no es hoja según el catálogo local: ML solo permite publicar en hojas.");
                        if (!listingAllowed)
                            errores.Add("La categoría no permite publicar (listing_allowed = false) según el catálogo local.");

                        var requeridos = await _catalogService.GetRequiredAttributesAsync(
                            borrador.CategoryIdMl, borrador.Condicion, borrador.ListingTypeId, ct: ct);
                        var completados = DeserializarAtributos(borrador.AtributosCompletadosJson);

                        foreach (var attr in requeridos)
                        {
                            var valor = completados.FirstOrDefault(c =>
                                string.Equals(c.Id, attr.AttributeId, StringComparison.OrdinalIgnoreCase));
                            var completo = valor is not null && valor.TieneValor;

                            if (!completo && attr.EsBloqueante)
                            {
                                errores.Add($"Falta el atributo obligatorio de Mercado Libre: {attr.Name} [{attr.AttributeId}].");
                                camposAtributosError.Add(attr.AttributeId);
                            }
                            else if (!completo && attr.EsRecomendado)
                            {
                                advertencias.Add($"Atributo recomendado sin completar: {attr.Name} [{attr.AttributeId}].");
                            }
                        }
                    }
                }
            }

            if (!CondicionesValidas.Contains(borrador.Condicion))
                errores.Add($"Condición inválida: '{borrador.Condicion}' (new | used).");

            if (borrador.Producto is null || borrador.Producto.IsDeleted)
                errores.Add("El producto de origen no existe o fue eliminado.");
            else
            {
                if (!borrador.Producto.Activo)
                    advertencias.Add("El producto está inactivo en el ERP.");

                if (borrador.Stock < 1)
                    errores.Add("El stock a publicar debe ser al menos 1.");
                else
                {
                    // No publicar más stock que el disponible según el origen configurado.
                    var disponible = await MercadoLibreStockResolver.ResolverParaProductoAsync(
                        context, borrador.Producto, config.OrigenStock, null, ct);

                    if (borrador.Stock > disponible.Stock)
                        errores.Add(
                            $"El stock a publicar ({borrador.Stock}) supera el disponible según el origen " +
                            $"'{disponible.Origen}' ({disponible.Stock}).");

                    if (disponible.Advertencia is not null)
                        advertencias.Add(disponible.Advertencia);
                }

                if (config.MargenMinimoPorcentaje.HasValue && borrador.Producto.PrecioCompra > 0)
                {
                    var margen = (borrador.Precio - borrador.Producto.PrecioCompra) / borrador.Producto.PrecioCompra * 100m;
                    if (margen < config.MargenMinimoPorcentaje.Value)
                        advertencias.Add(
                            $"Margen resultante {margen:0.##}% por debajo del mínimo configurado ({config.MargenMinimoPorcentaje:0.##}%).");
                }
            }

            if (string.IsNullOrWhiteSpace(borrador.Descripcion))
                advertencias.Add("Sin descripción: ML lo permite pero baja la calidad de la publicación.");

            if (string.IsNullOrWhiteSpace(borrador.Garantia))
                advertencias.Add("Sin garantía declarada (campo informativo en el MVP; cargala en ML si aplica).");

            // Imágenes: las URLs inválidas son error (rompen el POST); la falta de
            // imágenes es advertencia (la simulación se permite) salvo que el tipo
            // de publicación las exija al publicar REAL (bloqueo en PublicarAsync).
            var imagenes = LeerImagenes(borrador);
            foreach (var url in imagenes.Where(u => !EsUrlImagenValida(u)))
                errores.Add($"Imagen con URL inválida: '{Truncar(url, 80)}'. Usá direcciones http o https completas.");

            if (imagenes.Count == 0)
            {
                if (EsListingTypeQueExigeImagen(borrador.ListingTypeId))
                    advertencias.Add(
                        "Mercado Libre exige imágenes para publicación gratuita [free]. Podés simular igual, " +
                        "pero para publicar REAL agregá al menos una imagen o cambiá el tipo de publicación.");
                else
                    advertencias.Add(
                        "Sin imágenes: la publicación puede quedar incompleta en Mercado Libre hasta que cargues al menos una.");
            }

            var ok = errores.Count == 0;

            borrador.Estado = ok ? MercadoLibreBorradorEstado.Validado : MercadoLibreBorradorEstado.Borrador;
            borrador.FechaValidacionUtc = DateTime.UtcNow;

            var lineas = errores.Select(e => $"ERROR: {e}")
                .Concat(advertencias.Select(a => $"ADVERTENCIA: {a}"))
                .ToList();
            if (camposAtributosError.Count > 0)
                lineas.Add("ATRIBUTOS_ERROR: " + string.Join(",", camposAtributosError));

            borrador.ErroresValidacion = Truncar(string.Join("\n", lineas), 2000);
            borrador.UpdatedBy = usuario;

            await context.SaveChangesAsync(ct);

            return (ok, errores, advertencias);
        }

        public async Task<(bool Ok, string Mensaje)> PublicarAsync(
            int borradorId, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var borrador = await context.MercadoLibrePublicacionBorradores
                .Include(b => b.Producto)
                .FirstOrDefaultAsync(b => b.Id == borradorId, ct)
                ?? throw new InvalidOperationException($"Borrador {borradorId} inexistente.");

            if (borrador.Estado == MercadoLibreBorradorEstado.Publicado)
                return (false, $"El borrador ya fue publicado como {borrador.PublicadoItemId}.");

            if (borrador.Estado != MercadoLibreBorradorEstado.Validado)
                return (false, "Validá el borrador (sin errores) antes de publicar.");

            var payload = ConstruirPayload(borrador);
            var payloadJson = JsonSerializer.Serialize(payload);

            // Nuevo modelo de decisión (Checkpoint 2): la simulación es el comportamiento
            // por defecto y se decide por el checkbox "Publicación REAL" del borrador
            // (confirmarReal), NO por el ModoSimulacion global —que sigue gobernando
            // sync/precio/mensajes pero ya no compite con esta decisión—.
            //   confirmarReal == false → SIMULA (sin permiso ni cuenta).
            //   confirmarReal == true  → publica REAL, exige PermitirPublicacionDesdeErp + cuenta.
            if (!confirmarReal)
            {
                borrador.PublicadoEnSimulacion = true;
                borrador.FechaSimulacionUtc = DateTime.UtcNow;
                borrador.PayloadSimuladoJson = payloadJson;
                borrador.UpdatedBy = usuario;

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = config.AccountId,
                    Operacion = "PublicarItem",
                    Exito = true,
                    Detalle = $"SIMULADO por {usuario}: borrador {borrador.Id} ('{borrador.Titulo}'). " +
                              $"Payload que se enviaría a POST /items: {JsonSerializer.Serialize(payload)}",
                    CreatedBy = usuario
                });

                await context.SaveChangesAsync(ct);

                return (true, "SIMULACIÓN: el payload quedó registrado en el log. No se publicó nada en Mercado Libre.");
            }

            // Publicación REAL: permiso maestro + cuenta conectada son obligatorios.
            if (!config.PermitirPublicacionDesdeErp)
                return (false, "La publicación real está deshabilitada en Configuración.");

            if (!config.AccountId.HasValue)
                return (false, "No hay cuenta de Mercado Libre configurada para publicar.");

            // Guardas locales (Checkpoint 4): no llamar a POST /items cuando sabemos
            // que ML va a rechazar por imágenes. Se marcan los campos involucrados.
            var imagenes = LeerImagenes(borrador);

            if (imagenes.Any(u => !EsUrlImagenValida(u)))
                return await BloquearPublicacionLocalAsync(
                    context, borrador, config.AccountId,
                    "Hay imágenes con URL inválida. Usá direcciones http o https completas antes de publicar.",
                    new[] { "pictures" }, usuario, ct);

            if (EsListingTypeQueExigeImagen(borrador.ListingTypeId) && imagenes.Count == 0)
                return await BloquearPublicacionLocalAsync(
                    context, borrador, config.AccountId,
                    "Mercado Libre exige imágenes para publicación gratuita. Agregá al menos una imagen o cambiá el tipo de publicación.",
                    new[] { "listing_type_id", "pictures" }, usuario, ct);

            try
            {
                var accountId = config.AccountId.GetValueOrDefault();
                var token = await _authService.GetValidAccessTokenAsync(accountId, ct);
                var item = await _apiClient.CreateItemAsync(token, payload, ct);

                // La publicación nueva nace VINCULADA al producto de origen.
                var listing = new MercadoLibreListing
                {
                    AccountId = accountId,
                    ItemId = item.Id,
                    Titulo = Truncar(item.Title, 300) ?? borrador.Titulo,
                    Precio = item.Price ?? borrador.Precio,
                    CurrencyId = item.CurrencyId ?? borrador.CurrencyId,
                    AvailableQuantity = item.AvailableQuantity ?? borrador.Stock,
                    Status = item.Status ?? "active",
                    Permalink = Truncar(item.Permalink, 500),
                    CategoryId = Truncar(item.CategoryId ?? borrador.CategoryIdMl, 30),
                    ListingTypeId = Truncar(item.ListingTypeId ?? borrador.ListingTypeId, 30),
                    Condition = Truncar(item.Condition ?? borrador.Condicion, 20),
                    SellerSku = Truncar(borrador.Producto.Codigo, 100),
                    ProductoId = borrador.ProductoId,
                    LastSyncUtc = DateTime.UtcNow,
                    RawJson = JsonSerializer.Serialize(item),
                    CreatedBy = usuario
                };

                context.MercadoLibreListings.Add(listing);

                borrador.Estado = MercadoLibreBorradorEstado.Publicado;
                borrador.PublicadoItemId = item.Id;
                borrador.FechaPublicadoUtc = DateTime.UtcNow;
                borrador.PublicadoEnSimulacion = false;
                borrador.FechaSimulacionUtc = null;
                borrador.PayloadSimuladoJson = null;
                borrador.UpdatedBy = usuario;

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = config.AccountId,
                    ItemId = item.Id,
                    Operacion = "PublicarItem",
                    Exito = true,
                    Detalle = $"Borrador {borrador.Id} publicado por {usuario} como {item.Id} " +
                              $"('{borrador.Titulo}', precio {borrador.Precio:N2}, stock {borrador.Stock}). " +
                              "Publicación vinculada automáticamente al producto.",
                    CreatedBy = usuario
                });

                await context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Borrador {BorradorId} publicado en ML como {ItemId} por {Usuario}",
                    borrador.Id, item.Id, usuario);

                return (true, $"Publicado en Mercado Libre como {item.Id}. Recordá cargar imágenes desde ML.");
            }
            catch (Exception ex)
            {
                // Si ML rechaza con cause[]/references, traducimos a errores por campo
                // y los persistimos en el borrador para marcarlos en la UI (Checkpoint 7).
                var (mensajeUsuario, erroresPersistidos) = InterpretarRechazoMl(ex);

                if (erroresPersistidos is not null)
                {
                    borrador.ErroresValidacion = Truncar(erroresPersistidos, 2000);
                    borrador.UpdatedBy = usuario;
                }

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = config.AccountId,
                    Operacion = "PublicarItem",
                    Exito = false,
                    Detalle = $"Borrador {borrador.Id}: {Truncar(ex.Message, 1800)}",
                    CreatedBy = usuario
                });

                await context.SaveChangesAsync(ct);

                _logger.LogError(ex, "Error publicando borrador {BorradorId}", borrador.Id);

                return (false, mensajeUsuario);
            }
        }

        public async Task DescartarAsync(int borradorId, string usuario, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var borrador = await context.MercadoLibrePublicacionBorradores
                .FirstOrDefaultAsync(b => b.Id == borradorId, ct)
                ?? throw new InvalidOperationException($"Borrador {borradorId} inexistente.");

            if (borrador.Estado == MercadoLibreBorradorEstado.Publicado)
                throw new InvalidOperationException("No se descarta un borrador ya publicado.");

            borrador.Estado = MercadoLibreBorradorEstado.Descartado;
            borrador.UpdatedBy = usuario;

            await context.SaveChangesAsync(ct);
        }

        public async Task<List<MercadoLibreBorradorListViewModel>> GetBorradoresAsync(CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            return await context.MercadoLibrePublicacionBorradores
                .AsNoTracking()
                .Where(b => b.Estado != MercadoLibreBorradorEstado.Descartado)
                .OrderByDescending(b => b.CreatedAt)
                .Take(200)
                .Select(b => new MercadoLibreBorradorListViewModel
                {
                    Id = b.Id,
                    ProductoCodigo = b.Producto.Codigo,
                    ProductoNombre = b.Producto.Nombre,
                    Titulo = b.Titulo,
                    Precio = b.Precio,
                    Stock = b.Stock,
                    CategoryIdMl = b.CategoryIdMl,
                    CategoryNombre = b.CategoryNombre,
                    Estado = b.Estado,
                    FechaValidacionUtc = b.FechaValidacionUtc,
                    PublicadoEnSimulacion = b.PublicadoEnSimulacion,
                    FechaSimulacionUtc = b.FechaSimulacionUtc,
                    PublicadoItemId = b.PublicadoItemId,
                    CreatedAt = b.CreatedAt
                })
                .ToListAsync(ct);
        }

        public async Task<MercadoLibreBorradorEditViewModel?> GetBorradorAsync(
            int borradorId, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var borrador = await context.MercadoLibrePublicacionBorradores
                .AsNoTracking()
                .Include(b => b.Producto)
                .FirstOrDefaultAsync(b => b.Id == borradorId, ct);

            if (borrador is null)
                return null;

            var imagenes = LeerImagenes(borrador);

            // Catálogo local de categorías: atributos requeridos + valores guardados.
            var catalogoImportado = await _catalogService.HayCatalogoAsync(ct: ct);
            var guardados = DeserializarAtributos(borrador.AtributosCompletadosJson);

            IReadOnlyList<ViewModels.CatalogoAtributoVm> requeridos = Array.Empty<ViewModels.CatalogoAtributoVm>();
            if (catalogoImportado && !string.IsNullOrWhiteSpace(borrador.CategoryIdMl))
                requeridos = await _catalogService.GetRequiredAttributesAsync(
                    borrador.CategoryIdMl, borrador.Condicion, borrador.ListingTypeId, ct: ct);

            // Lista de binding alineada a los requeridos, pre-cargada con lo guardado;
            // se conservan valores guardados que ya no figuren entre los requeridos.
            var atributos = requeridos.Select(r =>
            {
                var g = guardados.FirstOrDefault(x =>
                    string.Equals(x.Id, r.AttributeId, StringComparison.OrdinalIgnoreCase));
                return new ViewModels.AtributoCompletadoVm
                {
                    Id = r.AttributeId,
                    ValueId = g?.ValueId,
                    ValueName = g?.ValueName
                };
            }).ToList();
            foreach (var g in guardados)
                if (!atributos.Any(a => string.Equals(a.Id, g.Id, StringComparison.OrdinalIgnoreCase)))
                    atributos.Add(g);

            return new MercadoLibreBorradorEditViewModel
            {
                Id = borrador.Id,
                ProductoId = borrador.ProductoId,
                Titulo = borrador.Titulo,
                Descripcion = borrador.Descripcion,
                Precio = borrador.Precio,
                Stock = borrador.Stock,
                CategoryIdMl = borrador.CategoryIdMl,
                CategoryNombre = borrador.CategoryNombre,
                CategoryPathFromRoot = borrador.CategoryPathFromRoot,
                CategoryEsHoja = borrador.CategoryEsHoja,
                Condicion = borrador.Condicion,
                ListingTypeId = borrador.ListingTypeId,
                Garantia = borrador.Garantia,
                ImagenesUrls = imagenes.Count == 0 ? null : string.Join("\n", imagenes),
                Imagenes = imagenes,
                Atributos = atributos,
                AtributosRequeridos = requeridos,
                CatalogoImportado = catalogoImportado,
                Estado = borrador.Estado,
                ErroresValidacion = borrador.ErroresValidacion,
                FechaValidacionUtc = borrador.FechaValidacionUtc,
                PublicadoItemId = borrador.PublicadoItemId,
                FechaPublicadoUtc = borrador.FechaPublicadoUtc,
                PublicadoEnSimulacion = borrador.PublicadoEnSimulacion,
                FechaSimulacionUtc = borrador.FechaSimulacionUtc,
                PayloadSimuladoJson = borrador.PayloadSimuladoJson,
                ProductoCodigo = borrador.Producto.Codigo,
                ProductoNombre = borrador.Producto.Nombre,
                ProductoPrecioVenta = borrador.Producto.PrecioVenta,
                ProductoStockActual = borrador.Producto.StockActual,
                ProductoRequiereNumeroSerie = borrador.Producto.RequiereNumeroSerie,
                PermitirPublicacionDesdeErp = config.PermitirPublicacionDesdeErp,
                CuentaConectada = config.AccountId.HasValue
            };
        }

        private static Dictionary<string, object?> ConstruirPayload(MercadoLibrePublicacionBorrador borrador)
        {
            var payload = new Dictionary<string, object?>
            {
                ["title"] = borrador.Titulo,
                ["category_id"] = borrador.CategoryIdMl,
                ["price"] = borrador.Precio,
                ["currency_id"] = borrador.CurrencyId,
                ["available_quantity"] = borrador.Stock,
                ["condition"] = borrador.Condicion,
                ["listing_type_id"] = borrador.ListingTypeId,
                ["description"] = new Dictionary<string, object?> { ["plain_text"] = borrador.Descripcion ?? string.Empty },
                ["attributes"] = ConstruirAtributos(borrador)
            };

            // pictures solo si hay URLs válidas: ML rechaza un array vacío.
            var imagenes = LeerImagenes(borrador).Where(EsUrlImagenValida).ToList();
            if (imagenes.Count > 0)
                payload["pictures"] = imagenes
                    .Select(u => new Dictionary<string, object?> { ["source"] = u })
                    .ToArray();

            return payload;
        }

        /// <summary>
        /// Arma el array attributes del payload: SELLER_SKU + atributos de categoría
        /// completados. Omite vacíos y duplicados; SELLER_SKU nunca se duplica. Si hay
        /// value_id se envía {id, value_id, value_name}; si no, {id, value_name}.
        /// </summary>
        private static List<Dictionary<string, object?>> ConstruirAtributos(MercadoLibrePublicacionBorrador borrador)
        {
            var atributos = new List<Dictionary<string, object?>>();
            var idsUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // SELLER_SKU primero (siempre presente, una sola vez).
            atributos.Add(new Dictionary<string, object?>
            {
                ["id"] = "SELLER_SKU",
                ["value_name"] = borrador.Producto.Codigo
            });
            idsUsados.Add("SELLER_SKU");

            foreach (var attr in DeserializarAtributos(borrador.AtributosCompletadosJson))
            {
                var id = attr.Id?.Trim();
                if (string.IsNullOrWhiteSpace(id) || !attr.TieneValor) continue;
                if (!idsUsados.Add(id)) continue; // sin duplicados

                var entrada = new Dictionary<string, object?> { ["id"] = id };
                if (!string.IsNullOrWhiteSpace(attr.ValueId))
                {
                    entrada["value_id"] = attr.ValueId.Trim();
                    if (!string.IsNullOrWhiteSpace(attr.ValueName))
                        entrada["value_name"] = attr.ValueName.Trim();
                }
                else
                {
                    entrada["value_name"] = attr.ValueName!.Trim();
                }
                atributos.Add(entrada);
            }

            return atributos;
        }

        // Tokens de campo que ML referencia en cause[].code / cause[].references,
        // mapeados al rótulo del borrador (clave estable usada también por la vista
        // para marcar el control con error).
        private static readonly (string Token, string Campo)[] CamposMl =
        {
            ("available_quantity", "Stock a publicar"),
            ("category_id", "Categoría"),
            ("condition", "Condición"),
            ("listing_type_id", "Tipo de publicación"),
            ("pictures", "Imágenes"),
            ("title", "Título"),
            ("price", "Precio"),
            ("currency_id", "Moneda"),
            ("description", "Descripción"),
        };

        /// <summary>
        /// Traduce un error de publicación a (mensaje para el operador, texto a persistir
        /// en ErroresValidacion). Si el body de ML trae cause[]/references, marca los
        /// campos involucrados con una línea machine-readable "CAMPOS_ERROR:" que la vista usa.
        /// Es defensivo: ante cualquier problema de parseo cae al mensaje genérico.
        /// </summary>
        private static (string Mensaje, string? ErroresPersistidos) InterpretarRechazoMl(Exception ex)
        {
            var excerpt = (ex as Exceptions.MercadoLibreApiException)?.ResponseExcerpt;
            if (string.IsNullOrWhiteSpace(excerpt))
                return ($"Mercado Libre rechazó la publicación: {ex.Message}", null);

            try
            {
                using var doc = JsonDocument.Parse(excerpt);
                var root = doc.RootElement;

                var lineas = new List<string>();
                var camposTokens = new List<string>();
                var atributosFaltantes = new List<string>();

                if (root.TryGetProperty("cause", out var causes) && causes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var causa in causes.EnumerateArray())
                    {
                        var code = causa.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                        var msg = causa.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

                        var refs = new List<string>();
                        if (causa.TryGetProperty("references", out var r) && r.ValueKind == JsonValueKind.Array)
                            refs.AddRange(r.EnumerateArray().Select(e => e.GetString() ?? ""));

                        // Atributos obligatorios faltantes (Checkpoint 13): ML los lista
                        // entre corchetes en el message. Se marca cada uno para la UI.
                        if (code.Contains("missing_required", StringComparison.OrdinalIgnoreCase)
                            || code.Contains("attributes", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var attrId in ExtraerAtributosFaltantes(msg))
                                if (!atributosFaltantes.Contains(attrId, StringComparer.OrdinalIgnoreCase))
                                    atributosFaltantes.Add(attrId);
                        }

                        // Una causa puede referenciar varios campos (ej:
                        // item.listing_type_id.requiresPictures involucra tanto el tipo
                        // de publicación como las imágenes): marcamos TODOS los que matcheen.
                        var matches = CamposMl.Where(cm =>
                            code.Contains(cm.Token, StringComparison.OrdinalIgnoreCase)
                            || refs.Any(rf => rf.Contains(cm.Token, StringComparison.OrdinalIgnoreCase)))
                            .ToList();

                        foreach (var campo in matches)
                            if (!camposTokens.Contains(campo.Token))
                                camposTokens.Add(campo.Token);

                        var rotulo = matches.Count > 0
                            ? string.Join(" / ", matches.Select(cm => cm.Campo))
                            : (atributosFaltantes.Count > 0 ? "Atributos obligatorios" : "General");
                        var detalle = string.IsNullOrWhiteSpace(msg) ? code : msg;
                        lineas.Add($"ERROR: {rotulo} — {detalle}");
                    }
                }

                if (lineas.Count == 0)
                    return ($"Mercado Libre rechazó la publicación: {ex.Message}", null);

                var persistido = string.Join("\n", lineas);
                if (camposTokens.Count > 0)
                    persistido += "\nCAMPOS_ERROR: " + string.Join(",", camposTokens);
                if (atributosFaltantes.Count > 0)
                    persistido += "\nATRIBUTOS_ERROR: " + string.Join(",", atributosFaltantes);

                var resumenCampos = camposTokens.Count > 0
                    ? " Revisá: " + string.Join(", ", camposTokens.Select(t => CamposMl.First(cm => cm.Token == t).Campo)) + "."
                    : string.Empty;

                if (atributosFaltantes.Count > 0)
                    return (
                        $"Mercado Libre exige atributos obligatorios para esta categoría: {string.Join(", ", atributosFaltantes)}.",
                        persistido);

                return ($"Mercado Libre rechazó la publicación.{resumenCampos}", persistido);
            }
            catch
            {
                return ($"Mercado Libre rechazó la publicación: {ex.Message}", null);
            }
        }

        private static string? Truncar(string? valor, int max)
            => valor is null ? null : (valor.Length <= max ? valor : valor[..max]);

        // ── Atributos completados (persistencia JSON) ──────────────────────────
        /// <summary>Serializa los atributos con valor a JSON; null si no hay ninguno (o lista null).</summary>
        private static string? SerializarAtributos(List<ViewModels.AtributoCompletadoVm>? atributos)
        {
            if (atributos is null) return null;

            var limpios = atributos
                .Where(a => !string.IsNullOrWhiteSpace(a.Id) && a.TieneValor)
                .Select(a => new ViewModels.AtributoCompletadoVm
                {
                    Id = a.Id.Trim(),
                    ValueId = string.IsNullOrWhiteSpace(a.ValueId) ? null : a.ValueId.Trim(),
                    ValueName = string.IsNullOrWhiteSpace(a.ValueName) ? null : a.ValueName.Trim()
                })
                .ToList();

            return limpios.Count == 0 ? null : JsonSerializer.Serialize(limpios);
        }

        private static List<ViewModels.AtributoCompletadoVm> DeserializarAtributos(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try
            {
                return JsonSerializer.Deserialize<List<ViewModels.AtributoCompletadoVm>>(json) ?? new();
            }
            catch
            {
                return new();
            }
        }

        /// <summary>Extrae los ids de atributos de un mensaje "The attributes [A, B, C] are required...".</summary>
        private static List<string> ExtraerAtributosFaltantes(string mensaje)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(mensaje)) return ids;

            var inicio = mensaje.IndexOf('[');
            var fin = mensaje.IndexOf(']');
            if (inicio < 0 || fin <= inicio) return ids;

            var contenido = mensaje.Substring(inicio + 1, fin - inicio - 1);
            foreach (var parte in contenido.Split(','))
            {
                var id = parte.Trim();
                if (id.Length > 0 && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                    ids.Add(id);
            }
            return ids;
        }

        // ── Imágenes ───────────────────────────────────────────────────────
        // Listing types donde ML exige al menos una imagen para publicar.
        private static readonly string[] ListingTypesQueExigenImagen = { "free" };

        private static bool EsListingTypeQueExigeImagen(string? listingTypeId)
            => listingTypeId is not null
               && ListingTypesQueExigenImagen.Contains(listingTypeId.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Texto del textarea (una URL por línea) → lista normalizada (trim + dedupe, sin vacíos).</summary>
        private static List<string> ParseImagenesDesdeTexto(string? raw)
        {
            var resultado = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
                return resultado;

            foreach (var linea in raw.Split('\n', '\r'))
            {
                var url = linea.Trim();
                if (url.Length > 0 && !resultado.Contains(url, StringComparer.OrdinalIgnoreCase))
                    resultado.Add(url);
            }

            return resultado;
        }

        /// <summary>Lee las URLs persistidas (JSON). Defensivo ante JSON inválido/legacy.</summary>
        private static List<string> LeerImagenes(MercadoLibrePublicacionBorrador borrador)
        {
            if (string.IsNullOrWhiteSpace(borrador.ImagenesJson))
                return new List<string>();

            try
            {
                var urls = JsonSerializer.Deserialize<List<string>>(borrador.ImagenesJson);
                return urls?.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList()
                       ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static bool EsUrlImagenValida(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// Bloqueo local previo a POST /items: persiste el error por campo (línea
        /// machine-readable CAMPOS_ERROR que la vista usa para marcar controles),
        /// registra el intento fallido y NO llama a Mercado Libre.
        /// </summary>
        private static async Task<(bool Ok, string Mensaje)> BloquearPublicacionLocalAsync(
            AppDbContext context,
            MercadoLibrePublicacionBorrador borrador,
            int? accountId,
            string mensaje,
            string[] camposTokens,
            string usuario,
            CancellationToken ct)
        {
            var persistido = $"ERROR: {mensaje}";
            if (camposTokens.Length > 0)
                persistido += "\nCAMPOS_ERROR: " + string.Join(",", camposTokens);

            borrador.ErroresValidacion = Truncar(persistido, 2000);
            borrador.UpdatedBy = usuario;

            context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = accountId,
                Operacion = "PublicarItem",
                Exito = false,
                Detalle = $"Borrador {borrador.Id}: bloqueo local antes de POST /items — {Truncar(mensaje, 1800)}",
                CreatedBy = usuario
            });

            await context.SaveChangesAsync(ct);

            return (false, mensaje);
        }
    }
}
