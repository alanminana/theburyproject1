using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class MercadoLibreSyncService : IMercadoLibreSyncService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibrePricingService _pricingService;
        private readonly ILogger<MercadoLibreSyncService> _logger;

        public MercadoLibreSyncService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreApiClient apiClient,
            IMercadoLibreAuthService authService,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibrePricingService pricingService,
            ILogger<MercadoLibreSyncService> logger)
        {
            _contextFactory = contextFactory;
            _apiClient = apiClient;
            _authService = authService;
            _configuracionService = configuracionService;
            _pricingService = pricingService;
            _logger = logger;
        }

        public async Task<MercadoLibreSyncPreviewViewModel> PrepararPreviewAsync(
            IReadOnlyCollection<int> listingIds,
            MercadoLibreSyncTipo tipo,
            CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var listings = await context.MercadoLibreListings
                .AsNoTracking()
                .Include(l => l.Producto)
                .Include(l => l.Variaciones)
                    .ThenInclude(v => v.Producto)
                .Include(l => l.Variaciones)
                    .ThenInclude(v => v.ProductoUnidad)
                .Where(l => listingIds.Contains(l.Id))
                .ToListAsync(ct);

            var productoIds = listings
                .Where(l => l.ProductoId.HasValue)
                .Select(l => l.ProductoId!.Value)
                .Concat(listings
                    .SelectMany(l => l.Variaciones)
                    .Where(v => v.ProductoId.HasValue)
                    .Select(v => v.ProductoId!.Value))
                .Distinct()
                .ToList();

            var preciosCanal = await _pricingService.CalcularPrecioCanalAsync(productoIds, ct);

            var preview = new MercadoLibreSyncPreviewViewModel
            {
                ModoSimulacion = config.ModoSimulacion,
                Tipo = tipo.ToString()
            };

            var quiereStock = tipo is MercadoLibreSyncTipo.Stock or MercadoLibreSyncTipo.StockYPrecio;
            var quierePrecio = tipo is MercadoLibreSyncTipo.Precio or MercadoLibreSyncTipo.StockYPrecio;

            foreach (var listing in listings)
            {
                var variacionesActivas = listing.Variaciones.Where(v => !v.IsDeleted).ToList();

                if (variacionesActivas.Count > 0)
                {
                    foreach (var variacion in variacionesActivas.OrderBy(v => v.VariationId))
                    {
                        var permitirFallbackProductoListing = variacionesActivas.Count == 1;
                        var productoEfectivo = variacion.Producto ?? listing.Producto;
                        var productoPrecioId = variacion.ProductoId ?? listing.ProductoId;

                        var itemVariacion = new MercadoLibreSyncPreviewItemViewModel
                        {
                            ListingId = listing.Id,
                            ItemId = listing.ItemId,
                            Titulo = listing.Titulo,
                            Status = listing.Status,
                            TieneVariaciones = true,
                            CantidadVariaciones = variacionesActivas.Count,
                            VariationId = variacion.VariationId,
                            VariationAtributos = ResumirAtributos(variacion.AttributesJson),
                            VariationSellerSku = variacion.SellerSku,
                            ProductoCodigo = productoEfectivo?.Codigo,
                            ProductoNombre = productoEfectivo?.Nombre,
                            StockMl = variacion.AvailableQuantity,
                            PrecioMl = variacion.Precio
                        };

                        if (string.Equals(listing.Status, "closed", StringComparison.OrdinalIgnoreCase))
                        {
                            itemVariacion.Excluida = true;
                            itemVariacion.MotivoExclusion = "Publicación finalizada (closed): ML no permite modificarla.";
                            preview.Items.Add(itemVariacion);
                            continue;
                        }

                        if (quiereStock)
                        {
                            var disponible = await MercadoLibreStockResolver
                                .ResolverStockDisponibleParaVariacionAsync(
                                    context, listing, variacion, config, permitirFallbackProductoListing, ct);

                            itemVariacion.StockObjetivo = disponible.Stock;
                            itemVariacion.OrigenStock = disponible.Origen;

                            if (disponible.Advertencia is not null)
                                itemVariacion.Advertencias.Add(disponible.Advertencia);

                            if (disponible.BloqueaSync)
                            {
                                itemVariacion.Excluida = true;
                                itemVariacion.MotivoExclusion = disponible.Advertencia
                                    ?? "Esta variación requiere vinculación/origen de stock antes de sincronizar.";
                            }
                        }

                        if (quierePrecio)
                        {
                            if (productoPrecioId.HasValue &&
                                preciosCanal.TryGetValue(productoPrecioId.Value, out var precioCanalVariacion))
                            {
                                itemVariacion.PrecioObjetivo = precioCanalVariacion.PrecioCanal;

                                if (precioCanalVariacion.PrecioCanal <= 0)
                                {
                                    itemVariacion.Excluida = true;
                                    itemVariacion.MotivoExclusion = "El precio de canal calculado es 0 o negativo.";
                                }
                                else if (precioCanalVariacion.DebajoDelMargenMinimo)
                                {
                                    itemVariacion.Advertencias.Add(
                                        $"Margen resultante {precioCanalVariacion.MargenResultantePorcentaje}% por debajo del mínimo configurado.");
                                }
                            }
                            else if (!itemVariacion.Excluida)
                            {
                                itemVariacion.Excluida = true;
                                itemVariacion.MotivoExclusion = "Sin producto vinculado. Vincular antes de sincronizar precio.";
                            }
                        }

                        preview.Items.Add(itemVariacion);
                    }

                    continue;
                }

                var item = new MercadoLibreSyncPreviewItemViewModel
                {
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Titulo = listing.Titulo,
                    Status = listing.Status,
                    TieneVariaciones = listing.TieneVariaciones,
                    CantidadVariaciones = 0,
                    ProductoCodigo = listing.Producto?.Codigo,
                    ProductoNombre = listing.Producto?.Nombre,
                    StockMl = listing.AvailableQuantity,
                    PrecioMl = listing.Precio
                };

                if (!listing.ProductoId.HasValue || listing.Producto is null)
                {
                    item.Excluida = true;
                    item.MotivoExclusion = "Sin producto vinculado. Vincular antes de sincronizar.";
                    preview.Items.Add(item);
                    continue;
                }

                if (string.Equals(listing.Status, "closed", StringComparison.OrdinalIgnoreCase))
                {
                    item.Excluida = true;
                    item.MotivoExclusion = "Publicación finalizada (closed): ML no permite modificarla.";
                    preview.Items.Add(item);
                    continue;
                }

                if (quiereStock)
                {
                    if (variacionesActivas.Count > 1)
                    {
                        // El ERP tiene stock por producto, no por variación: no se puede
                        // decidir cómo repartir stock entre N variaciones. Deuda declarada.
                        item.Advertencias.Add(
                            "Stock omitido: la publicación tiene varias variaciones y el ERP no maneja stock por variación.");
                    }
                    else
                    {
                        // Stock publicable según el origen configurado (global u
                        // override de la publicación): lógico, físico o unidad específica.
                        var disponible = await MercadoLibreStockResolver
                            .ResolverStockDisponibleAsync(context, listing, config, ct);

                        item.StockObjetivo = disponible.Stock;
                        item.OrigenStock = disponible.Origen;

                        if (disponible.Advertencia is not null)
                            item.Advertencias.Add(disponible.Advertencia);
                    }
                }

                if (quierePrecio && preciosCanal.TryGetValue(listing.ProductoId.Value, out var precioCanal))
                {
                    item.PrecioObjetivo = precioCanal.PrecioCanal;

                    if (precioCanal.PrecioCanal <= 0)
                    {
                        item.Excluida = true;
                        item.MotivoExclusion = "El precio de canal calculado es 0 o negativo.";
                    }
                    else if (precioCanal.DebajoDelMargenMinimo)
                    {
                        item.Advertencias.Add(
                            $"Margen resultante {precioCanal.MargenResultantePorcentaje}% por debajo del mínimo configurado.");
                    }
                }

                preview.Items.Add(item);
            }

            return preview;
        }

        public async Task<MercadoLibreSyncResultViewModel> AplicarAsync(
            IReadOnlyCollection<int> listingIds,
            MercadoLibreSyncTipo tipo,
            bool confirmarReal,
            string usuario,
            CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);
            var preview = await PrepararPreviewAsync(listingIds, tipo, ct);

            // Simulación si: la config lo impone, o el operador no confirmó el envío real.
            var simulado = config.ModoSimulacion || !confirmarReal;

            var resultado = new MercadoLibreSyncResultViewModel { FueSimulado = simulado };

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            string? accessToken = null;

            foreach (var item in preview.Items)
            {
                if (item.Excluida)
                {
                    resultado.Omitidos++;
                    resultado.Mensajes.Add($"{item.EtiquetaOperacion}: omitida — {item.MotivoExclusion}");
                    continue;
                }

                if (!item.TieneCambios)
                {
                    resultado.Omitidos++;
                    continue;
                }

                var operacion = item.CambiaStock && item.CambiaPrecio
                    ? "PushStockPrecio"
                    : item.CambiaStock ? "PushStock" : "PushPrecio";

                var detalleCambios = DescribirCambios(item);
                var payloadSeguro = ConstruirPayloadSeguro(item);
                var sw = Stopwatch.StartNew();

                if (simulado)
                {
                    context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                    {
                        ListingId = item.ListingId,
                        ItemId = item.ItemId,
                        Operacion = operacion,
                        Exito = true,
                        Detalle = $"SIMULADO por {usuario}: {detalleCambios}. Payload: {JsonSerializer.Serialize(payloadSeguro)}",
                        DuracionMs = 0,
                        CreatedBy = usuario
                    });

                    resultado.Exitosos++;
                    resultado.Mensajes.Add($"{item.EtiquetaOperacion}: SIMULADO — {detalleCambios}");
                    continue;
                }

                try
                {
                    var listing = await context.MercadoLibreListings
                        .Include(l => l.Variaciones)
                        .FirstAsync(l => l.Id == item.ListingId, ct);

                    accessToken ??= await ObtenerTokenAsync(listing.AccountId, ct);

                    if (item.VariationId.HasValue)
                    {
                        var actualizado = await _apiClient.UpdateItemVariationAsync(
                            accessToken, item.ItemId, item.VariationId.Value, payloadSeguro, ct);

                        ActualizarVariacionLocal(listing, item, actualizado);
                    }
                    else
                    {
                        var actualizado = await _apiClient.UpdateItemAsync(accessToken, item.ItemId, payloadSeguro, ct);
                        ActualizarListingLocal(listing, item, actualizado);
                    }

                    sw.Stop();

                    context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                    {
                        AccountId = listing.AccountId,
                        ListingId = listing.Id,
                        ItemId = listing.ItemId,
                        Operacion = operacion,
                        Exito = true,
                        Detalle = $"Aplicado por {usuario}: {detalleCambios}",
                        DuracionMs = sw.ElapsedMilliseconds,
                        CreatedBy = usuario
                    });

                    resultado.Exitosos++;
                    resultado.Mensajes.Add($"{item.EtiquetaOperacion}: OK — {detalleCambios}");
                }
                catch (Exception ex)
                {
                    sw.Stop();

                    context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                    {
                        ListingId = item.ListingId,
                        ItemId = item.ItemId,
                        Operacion = operacion,
                        Exito = false,
                        Detalle = $"Error: {Truncar(ex.Message, 1800)}",
                        DuracionMs = sw.ElapsedMilliseconds,
                        CreatedBy = usuario
                    });

                    resultado.Fallidos++;
                    resultado.Mensajes.Add($"{item.EtiquetaOperacion}: ERROR — {ex.Message}");

                    _logger.LogError(ex, "Push a Mercado Libre falló para {ItemId}", item.ItemId);
                }
            }

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Sync ML {Tipo} por {Usuario}: {Exitosos} OK, {Fallidos} error, {Omitidos} omitidas. Simulado:{Simulado}",
                tipo, usuario, resultado.Exitosos, resultado.Fallidos, resultado.Omitidos, simulado);

            return resultado;
        }

        private async Task<string> ObtenerTokenAsync(int accountId, CancellationToken ct)
            => await _authService.GetValidAccessTokenAsync(accountId, ct);

        private static Dictionary<string, object> ConstruirPayloadSeguro(MercadoLibreSyncPreviewItemViewModel item)
        {
            var payload = new Dictionary<string, object>();

            if (item.CambiaStock)
                payload["available_quantity"] = item.StockObjetivo!.Value;
            if (item.CambiaPrecio)
                payload["price"] = item.PrecioObjetivo!.Value;

            return payload;
        }

        private static void ActualizarListingLocal(
            MercadoLibreListing listing,
            MercadoLibreSyncPreviewItemViewModel item,
            TheBuryProject.Models.DTOs.MeliItemDto actualizado)
        {
            if (item.CambiaPrecio)
            {
                listing.Precio = actualizado.Price ?? item.PrecioObjetivo!.Value;

                foreach (var variacion in listing.Variaciones.Where(v => !v.IsDeleted))
                {
                    var varApi = actualizado.Variations.FirstOrDefault(v => v.Id == variacion.VariationId);
                    variacion.Precio = varApi?.Price ?? item.PrecioObjetivo!.Value;
                }
            }

            if (item.CambiaStock)
            {
                listing.AvailableQuantity = actualizado.AvailableQuantity ?? item.StockObjetivo!.Value;

                foreach (var variacion in listing.Variaciones.Where(v => !v.IsDeleted))
                {
                    var varApi = actualizado.Variations.FirstOrDefault(v => v.Id == variacion.VariationId);
                    if (varApi?.AvailableQuantity is not null)
                        variacion.AvailableQuantity = varApi.AvailableQuantity.Value;
                }
            }

            if (!string.IsNullOrEmpty(actualizado.Status))
                listing.Status = actualizado.Status;

            listing.LastSyncUtc = DateTime.UtcNow;
        }

        private static void ActualizarVariacionLocal(
            MercadoLibreListing listing,
            MercadoLibreSyncPreviewItemViewModel item,
            TheBuryProject.Models.DTOs.MeliVariationDto actualizado)
        {
            var variacion = listing.Variaciones.FirstOrDefault(
                v => v.VariationId == item.VariationId!.Value && !v.IsDeleted);

            if (variacion is not null)
            {
                if (item.CambiaPrecio)
                    variacion.Precio = actualizado.Price ?? item.PrecioObjetivo!.Value;

                if (item.CambiaStock)
                    variacion.AvailableQuantity = actualizado.AvailableQuantity ?? item.StockObjetivo!.Value;
            }

            if (item.CambiaStock)
                listing.AvailableQuantity = listing.Variaciones.Where(v => !v.IsDeleted).Sum(v => v.AvailableQuantity);

            listing.LastSyncUtc = DateTime.UtcNow;
        }

        private static string DescribirCambios(MercadoLibreSyncPreviewItemViewModel item)
        {
            var partes = new List<string>(2);

            if (item.CambiaStock)
                partes.Add($"stock {item.StockMl} → {item.StockObjetivo}");
            if (item.CambiaPrecio)
                partes.Add($"precio {item.PrecioMl:0.##} → {item.PrecioObjetivo:0.##}");

            var detalle = string.Join(", ", partes);
            return item.VariationId.HasValue ? $"var {item.VariationId}: {detalle}" : detalle;
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

                    if (!string.IsNullOrWhiteSpace(nombre) && !string.IsNullOrWhiteSpace(valor))
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
