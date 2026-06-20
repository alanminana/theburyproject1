using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class MercadoLibrePriceBatchService : IMercadoLibrePriceBatchService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibrePricingService _pricingService;
        private readonly ILogger<MercadoLibrePriceBatchService> _logger;

        public MercadoLibrePriceBatchService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreApiClient apiClient,
            IMercadoLibreAuthService authService,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibrePricingService pricingService,
            ILogger<MercadoLibrePriceBatchService> logger)
        {
            _contextFactory = contextFactory;
            _apiClient = apiClient;
            _authService = authService;
            _configuracionService = configuracionService;
            _pricingService = pricingService;
            _logger = logger;
        }

        // ------------------------------------------------------------------
        // Simulación (snapshot obligatorio)
        // ------------------------------------------------------------------

        public async Task<int> SimularAsync(
            MercadoLibrePriceBatchRequest request, string usuario, CancellationToken ct = default)
        {
            if (request.Origen == MercadoLibrePriceBatchOrigen.DesdePrecioErp)
                request.SoloVinculadas = true; // el precio ERP exige producto vinculado

            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var query = context.MercadoLibreListings
                .AsNoTracking()
                .Include(l => l.Producto)
                .Include(l => l.Variaciones)
                .Where(l => l.Status != "closed");

            if (request.SoloVinculadas)
                query = query.Where(l => l.ProductoId != null);

            if (!string.IsNullOrEmpty(request.Estado))
                query = query.Where(l => l.Status == request.Estado);

            if (request.CategoriaId.HasValue)
                query = query.Where(l => l.Producto != null &&
                    (l.Producto.CategoriaId == request.CategoriaId.Value || l.Producto.SubcategoriaId == request.CategoriaId.Value));

            if (request.MarcaId.HasValue)
                query = query.Where(l => l.Producto != null &&
                    (l.Producto.MarcaId == request.MarcaId.Value || l.Producto.SubmarcaId == request.MarcaId.Value));

            if (request.PrecioDesde.HasValue)
                query = query.Where(l => l.Precio >= request.PrecioDesde.Value);

            if (request.PrecioHasta.HasValue)
                query = query.Where(l => l.Precio <= request.PrecioHasta.Value);

            if (request.SoloConStock)
                query = query.Where(l => l.AvailableQuantity > 0);

            var listings = await query.OrderBy(l => l.ItemId).ToListAsync(ct);

            // Precio de canal para origen DesdePrecioErp
            var preciosCanal = request.Origen == MercadoLibrePriceBatchOrigen.DesdePrecioErp
                ? await _pricingService.CalcularPrecioCanalAsync(
                    listings.Where(l => l.ProductoId.HasValue).Select(l => l.ProductoId!.Value).Distinct().ToList(), ct)
                : new Dictionary<int, MercadoLibrePrecioCanal>();

            var batch = new MercadoLibrePriceBatch
            {
                Nombre = request.Nombre,
                Origen = request.Origen,
                ValorAjustePorcentaje = request.ValorAjustePorcentaje,
                FiltrosJson = JsonSerializer.Serialize(request),
                SolicitadoPor = usuario,
                FechaSolicitud = DateTime.UtcNow,
                CreatedBy = usuario
            };

            foreach (var listing in listings)
            {
                var variaciones = listing.Variaciones.Where(v => !v.IsDeleted).ToList();

                decimal? CalcularNuevo(decimal precioActual)
                {
                    switch (request.Origen)
                    {
                        case MercadoLibrePriceBatchOrigen.PorcentajeSobrePrecioMl:
                            return _pricingService.Redondear(
                                precioActual * (1 + request.ValorAjustePorcentaje / 100m), config.ReglaRedondeo);

                        case MercadoLibrePriceBatchOrigen.DesdePrecioErp:
                            if (!listing.ProductoId.HasValue ||
                                !preciosCanal.TryGetValue(listing.ProductoId.Value, out var canal))
                                return null;

                            return _pricingService.Redondear(
                                canal.PrecioCanal * (1 + request.ValorAjustePorcentaje / 100m), config.ReglaRedondeo);

                        default:
                            return null;
                    }
                }

                void AgregarItem(long? variationId, decimal precioAnterior)
                {
                    var precioNuevo = CalcularNuevo(precioAnterior);

                    if (precioNuevo is null or <= 0 || precioNuevo == precioAnterior)
                        return;

                    var item = new MercadoLibrePriceBatchItem
                    {
                        ListingId = listing.Id,
                        ItemId = listing.ItemId,
                        VariationId = variationId,
                        Titulo = listing.Titulo,
                        PrecioAnterior = precioAnterior,
                        PrecioNuevo = precioNuevo.Value,
                        DiferenciaPorcentaje = precioAnterior > 0
                            ? Math.Round((precioNuevo.Value - precioAnterior) / precioAnterior * 100m, 2)
                            : 0m,
                        CreatedBy = usuario
                    };

                    if (!listing.ProductoId.HasValue)
                    {
                        item.TieneAdvertencia = true;
                        item.MensajeAdvertencia =
                            "Sin producto vinculado: se omitirá en el envío real. Vinculá esta publicación a un producto interno antes de operar.";
                    }

                    // Advertencia de margen (solo medible con producto vinculado).
                    if (listing.ProductoId.HasValue &&
                        config.MargenMinimoPorcentaje.HasValue &&
                        listing.Producto is not null &&
                        listing.Producto.PrecioCompra > 0)
                    {
                        var margen = (precioNuevo.Value - listing.Producto.PrecioCompra) / listing.Producto.PrecioCompra * 100m;

                        if (margen < config.MargenMinimoPorcentaje.Value)
                        {
                            item.TieneAdvertencia = true;
                            item.MensajeAdvertencia =
                                $"Margen resultante {margen:0.##}% por debajo del mínimo ({config.MargenMinimoPorcentaje:0.##}%).";
                        }
                    }

                    batch.Items.Add(item);
                }

                if (variaciones.Count == 0)
                {
                    AgregarItem(null, listing.Precio);
                }
                else
                {
                    foreach (var variacion in variaciones)
                        AgregarItem(variacion.VariationId, variacion.Precio);
                }
            }

            batch.CantidadPublicaciones = batch.Items.Select(i => i.ListingId).Distinct().Count();
            batch.SimulacionJson = JsonSerializer.Serialize(new
            {
                totalItems = batch.Items.Count,
                publicaciones = batch.CantidadPublicaciones,
                advertencias = batch.Items.Count(i => i.TieneAdvertencia),
                aumentoPromedio = batch.Items.Count > 0
                    ? Math.Round(batch.Items.Average(i => i.DiferenciaPorcentaje), 2)
                    : 0m
            });

            context.MercadoLibrePriceBatches.Add(batch);
            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Lote ML {BatchId} simulado por {Usuario}: {Items} items en {Publicaciones} publicaciones",
                batch.Id, usuario, batch.Items.Count, batch.CantidadPublicaciones);

            return batch.Id;
        }

        // ------------------------------------------------------------------
        // Aplicación
        // ------------------------------------------------------------------

        public async Task<(bool Ok, string Mensaje)> AplicarAsync(
            int batchId, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var batch = await context.MercadoLibrePriceBatches
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == batchId, ct);

            if (batch is null)
                return (false, "Lote no encontrado.");

            if (batch.Estado != MercadoLibrePriceBatchEstado.Simulado)
                return (false, $"El lote está en estado {batch.Estado}; solo se aplican lotes simulados.");

            if (config.ModoSimulacion)
            {
                foreach (var item in batch.Items.Where(i => !i.IsDeleted))
                {
                    item.Aplicado = true;
                    item.PayloadAplicacionJson = JsonSerializer.Serialize(CrearPayloadPrecio(item.PrecioNuevo));
                }

                batch.Estado = MercadoLibrePriceBatchEstado.Aplicado;
                batch.AplicadoEnSimulacion = true;
                batch.AplicadoPor = usuario;
                batch.FechaAplicacion = DateTime.UtcNow;

                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    Operacion = "PriceBatchAplicar",
                    Exito = true,
                    Detalle = $"SIMULADO por {usuario}: lote '{batch.Nombre}' ({batch.Items.Count} items). No se llamó a ML.",
                    CreatedBy = usuario
                });

                await context.SaveChangesAsync(ct);
                return (true, $"SIMULACIÓN: lote aplicado en simulación ({batch.Items.Count} items, sin llamadas a ML).");
            }

            if (!confirmarReal)
                return (false, "Aplicar cambios reales requiere confirmacion explicita y modo simulacion desactivado.");

            var (exitosos, fallidos) = await EmpujarPreciosAsync(
                context, batch, usarPrecioNuevo: true, usuario, ct);

            batch.Estado = fallidos == 0
                ? MercadoLibrePriceBatchEstado.Aplicado
                : MercadoLibrePriceBatchEstado.AplicadoParcial;
            batch.AplicadoEnSimulacion = false;
            batch.AplicadoPor = usuario;
            batch.FechaAplicacion = DateTime.UtcNow;

            await context.SaveChangesAsync(ct);

            return (fallidos == 0,
                $"Lote aplicado: {exitosos} items OK, {fallidos} con error." +
                (fallidos > 0 ? " Ver detalle en el lote." : ""));
        }

        // ------------------------------------------------------------------
        // Rollback
        // ------------------------------------------------------------------

        public async Task<(bool Ok, string Mensaje)> RevertirAsync(
            int batchId, string? motivo, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var batch = await context.MercadoLibrePriceBatches
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.Id == batchId, ct);

            if (batch is null)
                return (false, "Lote no encontrado.");

            if (batch.Estado is not (MercadoLibrePriceBatchEstado.Aplicado or MercadoLibrePriceBatchEstado.AplicadoParcial))
                return (false, $"El lote está en estado {batch.Estado}; solo se revierten lotes aplicados.");

            if (batch.AplicadoEnSimulacion)
            {
                context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    Operacion = "PriceBatchRevertirSimulado",
                    Exito = true,
                    Detalle = $"No requiere rollback: lote '{batch.Nombre}' fue simulacion sin cambios reales en Mercado Libre.",
                    CreatedBy = usuario
                });

                await context.SaveChangesAsync(ct);
                return (true, "No requiere rollback: fue simulacion sin cambios reales.");
            }

            if (config.ModoSimulacion || !confirmarReal)
                return (false, "Revertir requiere confirmación explícita y modo simulación desactivado.");

            var (exitosos, fallidos) = await EmpujarPreciosAsync(
                context, batch, usarPrecioNuevo: false, usuario, ct);

            batch.Estado = MercadoLibrePriceBatchEstado.Revertido;
            batch.RevertidoPor = usuario;
            batch.FechaReversion = DateTime.UtcNow;
            batch.MotivoReversion = motivo;

            await context.SaveChangesAsync(ct);

            return (fallidos == 0,
                $"Rollback completado: {exitosos} items restaurados, {fallidos} con error.");
        }

        public async Task CancelarAsync(int batchId, string usuario, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var batch = await context.MercadoLibrePriceBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
                ?? throw new InvalidOperationException("Lote no encontrado.");

            if (batch.Estado != MercadoLibrePriceBatchEstado.Simulado)
                throw new InvalidOperationException("Solo se cancelan lotes simulados.");

            batch.Estado = MercadoLibrePriceBatchEstado.Cancelado;
            batch.UpdatedBy = usuario;

            await context.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Empuja precios a ML agrupando los items por publicación (un PUT por
        /// publicación). usarPrecioNuevo=false re-publica el snapshot anterior (rollback).
        /// </summary>
        private async Task<(int Exitosos, int Fallidos)> EmpujarPreciosAsync(
            AppDbContext context,
            MercadoLibrePriceBatch batch,
            bool usarPrecioNuevo,
            string usuario,
            CancellationToken ct)
        {
            var exitosos = 0;
            var fallidos = 0;
            string? accessToken = null;

            var itemsPorListing = batch.Items
                .Where(i => !i.IsDeleted && (usarPrecioNuevo ? !i.Aplicado : i.Aplicado && !i.Revertido))
                .GroupBy(i => i.ListingId);

            foreach (var grupo in itemsPorListing)
            {
                var items = grupo.ToList();

                var listing = await context.MercadoLibreListings
                    .Include(l => l.Variaciones)
                    .FirstOrDefaultAsync(l => l.Id == grupo.Key, ct);

                if (listing is null)
                {
                    foreach (var item in items)
                        item.Error = "Publicación inexistente en el ERP.";
                    fallidos += items.Count;
                    continue;
                }

                // Regla de vinculación: publicaciones sin Producto interno no
                // reciben cambios de precio REALES (ni aplicar ni revertir).
                if (listing.ProductoId is null)
                {
                    foreach (var item in items)
                        item.Error = "Sin producto vinculado. Vinculá esta publicación a un producto interno antes de operar.";
                    fallidos += items.Count;

                    context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                    {
                        AccountId = listing.AccountId,
                        ListingId = listing.Id,
                        ItemId = listing.ItemId,
                        Operacion = usarPrecioNuevo ? "PriceBatchAplicar" : "PriceBatchRevertir",
                        Exito = false,
                        Detalle = $"Lote '{batch.Nombre}': omitida por no tener producto vinculado.",
                        CreatedBy = usuario
                    });
                    continue;
                }

                var variacionesActivas = listing.Variaciones
                    .Where(v => !v.IsDeleted)
                    .ToDictionary(v => v.VariationId);

                var itemsValidos = new List<MercadoLibrePriceBatchItem>();

                foreach (var item in items)
                {
                    if (item.VariationId.HasValue)
                    {
                        if (!variacionesActivas.ContainsKey(item.VariationId.Value))
                        {
                            item.Error = $"La variacion {item.VariationId.Value} no existe o no esta activa en la publicacion local.";
                            fallidos++;
                            continue;
                        }
                    }
                    else if (variacionesActivas.Count > 0)
                    {
                        item.Error = "La publicacion tiene variaciones; el cambio real debe operar por VariationId.";
                        fallidos++;
                        continue;
                    }

                    itemsValidos.Add(item);
                }

                if (itemsValidos.Count == 0)
                {
                    context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                    {
                        AccountId = listing.AccountId,
                        ListingId = listing.Id,
                        ItemId = listing.ItemId,
                        Operacion = usarPrecioNuevo ? "PriceBatchAplicar" : "PriceBatchRevertir",
                        Exito = false,
                        Detalle = $"Lote '{batch.Nombre}': omitida por no tener precio/variacion resoluble.",
                        CreatedBy = usuario
                    });
                    continue;
                }

                items = itemsValidos;

                try
                {
                    accessToken ??= await _authService.GetValidAccessTokenAsync(listing.AccountId, ct);

                    if (items.Count == 1 && items[0].VariationId is null)
                    {
                        var payload = CrearPayloadPrecio(ResolverPrecioItem(items[0], usarPrecioNuevo));

                        if (usarPrecioNuevo)
                            items[0].PayloadAplicacionJson = JsonSerializer.Serialize(payload);

                        var actualizado = await _apiClient.UpdateItemAsync(accessToken, listing.ItemId, payload, ct);

                        if (actualizado.Price.HasValue)
                            listing.Precio = actualizado.Price.Value;
                        else
                            listing.Precio = usarPrecioNuevo ? items[0].PrecioNuevo : items[0].PrecioAnterior;
                    }
                    else
                    {
                        foreach (var item in items.Where(i => i.VariationId.HasValue))
                        {
                            var payload = CrearPayloadPrecio(ResolverPrecioItem(item, usarPrecioNuevo));

                            if (usarPrecioNuevo)
                                item.PayloadAplicacionJson = JsonSerializer.Serialize(payload);

                            await _apiClient.UpdateItemVariationAsync(
                                accessToken, listing.ItemId, item.VariationId!.Value, payload, ct);
                        }
                    }

                    foreach (var item in items.Where(i => i.VariationId.HasValue))
                    {
                        var variacion = listing.Variaciones.FirstOrDefault(
                            v => v.VariationId == item.VariationId!.Value && !v.IsDeleted);

                        if (variacion is not null)
                            variacion.Precio = usarPrecioNuevo ? item.PrecioNuevo : item.PrecioAnterior;
                    }

                    listing.LastSyncUtc = DateTime.UtcNow;

                    foreach (var item in items)
                    {
                        if (usarPrecioNuevo)
                            item.Aplicado = true;
                        else
                            item.Revertido = true;

                        item.Error = null;
                    }

                    exitosos += items.Count;

                    context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                    {
                        AccountId = listing.AccountId,
                        ListingId = listing.Id,
                        ItemId = listing.ItemId,
                        Operacion = usarPrecioNuevo ? "PriceBatchAplicar" : "PriceBatchRevertir",
                        Exito = true,
                        Detalle = $"Lote '{batch.Nombre}' por {usuario}: {items.Count} precio(s) " +
                                  (usarPrecioNuevo ? "aplicado(s)." : "restaurado(s) al snapshot anterior."),
                        CreatedBy = usuario
                    });
                }
                catch (Exception ex)
                {
                    var mensaje = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

                    foreach (var item in items)
                        item.Error = mensaje;

                    fallidos += items.Count;

                    context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                    {
                        AccountId = listing.AccountId,
                        ListingId = listing.Id,
                        ItemId = listing.ItemId,
                        Operacion = usarPrecioNuevo ? "PriceBatchAplicar" : "PriceBatchRevertir",
                        Exito = false,
                        Detalle = $"Lote '{batch.Nombre}': {mensaje}",
                        CreatedBy = usuario
                    });

                    _logger.LogError(ex, "Push de lote {BatchId} falló para {ItemId}", batch.Id, listing.ItemId);
                }
            }

            return (exitosos, fallidos);
        }

        private static Dictionary<string, object> CrearPayloadPrecio(decimal precio)
            => new() { ["price"] = precio };

        private static decimal ResolverPrecioItem(MercadoLibrePriceBatchItem item, bool usarPrecioNuevo)
            => usarPrecioNuevo ? item.PrecioNuevo : item.PrecioAnterior;

        // ------------------------------------------------------------------
        // Consultas
        // ------------------------------------------------------------------

        public async Task<List<MercadoLibrePriceBatchListViewModel>> GetBatchesAsync(CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            return await context.MercadoLibrePriceBatches
                .AsNoTracking()
                .OrderByDescending(b => b.FechaSolicitud)
                .Take(100)
                .Select(b => new MercadoLibrePriceBatchListViewModel
                {
                    Id = b.Id,
                    Nombre = b.Nombre,
                    Estado = b.Estado,
                    Origen = b.Origen,
                    ValorAjustePorcentaje = b.ValorAjustePorcentaje,
                    CantidadPublicaciones = b.CantidadPublicaciones,
                    CantidadItems = b.Items.Count(i => !i.IsDeleted),
                    CantidadErrores = b.Items.Count(i => !i.IsDeleted && i.Error != null && i.Error != string.Empty),
                    AplicadoEnSimulacion = b.AplicadoEnSimulacion,
                    SolicitadoPor = b.SolicitadoPor,
                    FechaSolicitud = b.FechaSolicitud,
                    FechaAplicacion = b.FechaAplicacion,
                    FechaReversion = b.FechaReversion
                })
                .ToListAsync(ct);
        }

        public async Task<MercadoLibrePriceBatchDetalleViewModel?> GetBatchAsync(
            int batchId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var batch = await context.MercadoLibrePriceBatches
                .AsNoTracking()
                .Include(b => b.Items)
                    .ThenInclude(i => i.Listing)
                        .ThenInclude(l => l.Producto)
                .FirstOrDefaultAsync(b => b.Id == batchId, ct);

            if (batch is null)
                return null;

            var config = await _configuracionService.GetAsync(ct);
            var filtros = DeserializarFiltros(batch.FiltrosJson);

            return new MercadoLibrePriceBatchDetalleViewModel
            {
                Id = batch.Id,
                Nombre = batch.Nombre,
                Estado = batch.Estado,
                Origen = batch.Origen,
                ValorAjustePorcentaje = batch.ValorAjustePorcentaje,
                AplicadoEnSimulacion = batch.AplicadoEnSimulacion,
                FiltroSoloVinculadas = filtros?.SoloVinculadas ?? false,
                ModoSimulacion = config.ModoSimulacion,
                SolicitadoPor = batch.SolicitadoPor,
                FechaSolicitud = batch.FechaSolicitud,
                AplicadoPor = batch.AplicadoPor,
                FechaAplicacion = batch.FechaAplicacion,
                RevertidoPor = batch.RevertidoPor,
                FechaReversion = batch.FechaReversion,
                MotivoReversion = batch.MotivoReversion,
                Items = batch.Items
                    .Where(i => !i.IsDeleted)
                    .OrderBy(i => i.ItemId).ThenBy(i => i.VariationId)
                    .Select(i => new MercadoLibrePriceBatchItemViewModel
                    {
                        ListingId = i.ListingId,
                        ItemId = i.ItemId,
                        VariationId = i.VariationId,
                        Titulo = i.Titulo,
                        ProductoCodigo = i.Listing.Producto != null ? i.Listing.Producto.Codigo : null,
                        ProductoNombre = i.Listing.Producto != null ? i.Listing.Producto.Nombre : null,
                        OrigenPrecio = batch.Origen == MercadoLibrePriceBatchOrigen.DesdePrecioErp
                            ? "Precio ERP + canal"
                            : "Precio ML actual",
                        MargenEstimadoPorcentaje = i.Listing.Producto != null && i.Listing.Producto.PrecioCompra > 0
                            ? Math.Round((i.PrecioNuevo - i.Listing.Producto.PrecioCompra) / i.Listing.Producto.PrecioCompra * 100m, 2)
                            : null,
                        PrecioAnterior = i.PrecioAnterior,
                        PrecioNuevo = i.PrecioNuevo,
                        DiferenciaPorcentaje = i.DiferenciaPorcentaje,
                        PayloadAplicacionJson = i.PayloadAplicacionJson,
                        TieneAdvertencia = i.TieneAdvertencia,
                        MensajeAdvertencia = i.MensajeAdvertencia,
                        Aplicado = i.Aplicado,
                        Revertido = i.Revertido,
                        Error = i.Error
                    })
                    .ToList()
            };
        }

        private static MercadoLibrePriceBatchRequest? DeserializarFiltros(string filtrosJson)
        {
            try
            {
                return JsonSerializer.Deserialize<MercadoLibrePriceBatchRequest>(filtrosJson);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
