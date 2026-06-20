using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.Services;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Usa el AppDbContext SCOPED (no la factory) a propósito: así
    /// MovimientoStockService detecta la transacción ambiente y el descuento de
    /// stock queda en la MISMA transacción que la creación de la Venta
    /// (mismo patrón que VentaService).
    /// </summary>
    public class MercadoLibreOrderService : IMercadoLibreOrderService
    {
        private const string StatusPaid = "paid";
        private const string StatusCancelled = "cancelled";
        private const string RawQaMarker = "\"simuladaQa\":true";
        private const string RawOperativaLocalMarker = "\"simuladaOperativaLocal\":true";

        private readonly AppDbContext _context;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMovimientoStockService _movimientoStockService;
        private readonly IProductoUnidadService _productoUnidadService;
        private readonly ICajaService _cajaService;
        private readonly VentaNumberGenerator _numberGenerator;
        private readonly ILogger<MercadoLibreOrderService> _logger;

        public MercadoLibreOrderService(
            AppDbContext context,
            IMercadoLibreApiClient apiClient,
            IMercadoLibreAuthService authService,
            IMercadoLibreConfiguracionService configuracionService,
            IMovimientoStockService movimientoStockService,
            IProductoUnidadService productoUnidadService,
            ICajaService cajaService,
            VentaNumberGenerator numberGenerator,
            ILogger<MercadoLibreOrderService> logger)
        {
            _context = context;
            _apiClient = apiClient;
            _authService = authService;
            _configuracionService = configuracionService;
            _movimientoStockService = movimientoStockService;
            _productoUnidadService = productoUnidadService;
            _cajaService = cajaService;
            _numberGenerator = numberGenerator;
            _logger = logger;
        }

        // ------------------------------------------------------------------
        // Fase C — Importación
        // ------------------------------------------------------------------

        public async Task<MercadoLibreOrder> ImportarOrdenAsync(
            int accountId, long meliOrderId, CancellationToken ct = default)
        {
            var token = await _authService.GetValidAccessTokenAsync(accountId, ct);
            var dto = await _apiClient.GetOrderAsync(token, meliOrderId, ct);

            var config = await _configuracionService.GetAsync(ct);
            var orden = await UpsertOrdenAsync(accountId, dto, config, ct);

            // Venta automática solo si: la config lo permite, la orden está paga
            // y todavía no generó venta. CrearVentaInternaAsync re-chequea todo.
            if (config.CrearVentaAutomatica
                && orden.VentaId is null
                && orden.EstadoInterno != MercadoLibreOrderEstadoInterno.Ignorada
                && EsPaga(orden.Status))
            {
                try
                {
                    await CrearVentaInternaAsync(orden.Id, "Sistema (auto-ML)", ct);
                }
                catch (Exception ex)
                {
                    // La importación no debe fallar porque la venta automática falle.
                    _logger.LogError(ex, "Venta automática falló para orden ML {OrderId}", meliOrderId);
                }
            }

            return orden;
        }

        public async Task<int> ImportarOrdenesRecientesAsync(
            int accountId, DateTime? desdeUtc = null, CancellationToken ct = default)
        {
            var token = await _authService.GetValidAccessTokenAsync(accountId, ct);
            var ordenes = await _apiClient.SearchOrdersAsync(token, await GetSellerIdAsync(accountId, ct), desdeUtc, ct: ct);

            var importadas = 0;

            foreach (var dto in ordenes)
            {
                try
                {
                    await ImportarOrdenAsync(accountId, dto.Id, ct);
                    importadas++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error importando orden ML {OrderId}", dto.Id);
                }
            }

            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = accountId,
                Operacion = "ImportOrders",
                Exito = true,
                Detalle = $"Importación manual: {importadas}/{ordenes.Count} órdenes procesadas."
            });
            await _context.SaveChangesAsync(ct);

            return importadas;
        }

        public async Task<MercadoLibreOrderSimulationResult> CrearOrdenSimuladaAsync(
            string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            if (!config.ModoSimulacion && !permitirPorDevelopment)
            {
                return new MercadoLibreOrderSimulationResult(
                    false,
                    null,
                    "La generacion de ordenes QA solo esta habilitada en Development o con ModoSimulacion=true.");
            }

            var listing = await _context.MercadoLibreListings
                .AsNoTracking()
                .Include(l => l.Variaciones)
                .Where(l => l.ProductoId != null && l.Status != "closed")
                .OrderBy(l => l.Id)
                .FirstOrDefaultAsync(ct);

            if (listing is null)
            {
                return new MercadoLibreOrderSimulationResult(
                    false,
                    null,
                    "Vinculá una publicación a un producto antes de generar una orden simulada.");
            }

            var variacion = listing.Variaciones
                .Where(v => !v.IsDeleted)
                .OrderBy(v => v.Id)
                .FirstOrDefault();

            var precioUnitario = variacion?.Precio > 0 ? variacion.Precio : listing.Precio;
            if (precioUnitario <= 0)
                precioUnitario = 1m;

            var meliOrderId = await GenerarMeliOrderIdSimuladoAsync(ct);
            var comision = Math.Round(precioUnitario * config.ComisionEstimadaPorcentaje / 100m, 2);
            var envio = config.CostoEnvioEstimado > 0 ? config.CostoEnvioEstimado : 0m;

            var orden = new MercadoLibreOrder
            {
                AccountId = listing.AccountId,
                MeliOrderId = meliOrderId,
                Status = StatusPaid,
                TotalAmount = precioUnitario,
                PaidAmount = precioUnitario,
                CurrencyId = string.IsNullOrWhiteSpace(listing.CurrencyId) ? "ARS" : listing.CurrencyId,
                FechaCreacionUtc = DateTime.UtcNow,
                BuyerId = 0,
                BuyerNickname = "QA SIMULADA",
                MontoComision = comision,
                MontoEnvio = envio > 0 ? envio : null,
                NetoEstimado = precioUnitario - comision - envio,
                EstadoInterno = MercadoLibreOrderEstadoInterno.Importada,
                RawJson = JsonSerializer.Serialize(new
                {
                    simuladaQa = true,
                    source = "ERP QA local",
                    listing.ItemId,
                    generadoPor = usuario,
                    generadoUtc = DateTime.UtcNow
                }),
                CreatedBy = usuario
            };

            orden.Items.Add(new MercadoLibreOrderItem
            {
                ItemId = listing.ItemId,
                VariationId = variacion?.VariationId,
                Titulo = listing.Titulo,
                Cantidad = 1,
                PrecioUnitario = precioUnitario,
                CurrencyId = orden.CurrencyId,
                SellerSku = listing.SellerSku,
                SaleFee = comision,
                ProductoId = listing.ProductoId,
                CreatedBy = usuario
            });

            _context.MercadoLibreOrders.Add(orden);
            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = listing.AccountId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Operacion = "OrderQASimulada",
                Exito = true,
                Detalle = $"Orden QA simulada {meliOrderId} creada por {usuario}. No se llamo a Mercado Libre.",
                CreatedBy = usuario
            });

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreOrderSimulationResult(
                true,
                orden.Id,
                $"Orden simulada #{orden.MeliOrderId} generada. No se llamo a Mercado Libre.");
        }

        public async Task<MercadoLibreOrderSimulationResult> CrearOrdenOperativaSimuladaAsync(
            string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            if (!config.ModoSimulacion && !permitirPorDevelopment)
            {
                return new MercadoLibreOrderSimulationResult(
                    false,
                    null,
                    "La generacion de ordenes operativas simuladas solo esta habilitada en Development o con ModoSimulacion=true.");
            }

            var listing = await _context.MercadoLibreListings
                .AsNoTracking()
                .Include(l => l.Producto)
                .Include(l => l.Variaciones)
                .Where(l => l.ProductoId != null
                    && l.Producto != null
                    && !l.Producto.IsDeleted
                    && l.Status != "closed")
                .OrderBy(l => l.Id)
                .FirstOrDefaultAsync(ct);

            if (listing is null)
            {
                return new MercadoLibreOrderSimulationResult(
                    false,
                    null,
                    "Vincula una publicacion a un producto antes de generar una orden operativa simulada.");
            }

            var variacion = listing.Variaciones
                .Where(v => !v.IsDeleted)
                .OrderBy(v => v.Id)
                .FirstOrDefault();

            var precioUnitario = variacion?.Precio > 0 ? variacion.Precio : listing.Precio;
            if (precioUnitario <= 0)
                precioUnitario = 1m;

            var meliOrderId = await GenerarMeliOrderIdSimuladoAsync(ct);
            var comision = Math.Round(precioUnitario * config.ComisionEstimadaPorcentaje / 100m, 2);
            var envio = config.CostoEnvioEstimado > 0 ? config.CostoEnvioEstimado : 0m;
            var generadoUtc = DateTime.UtcNow;

            var orden = new MercadoLibreOrder
            {
                AccountId = listing.AccountId,
                MeliOrderId = meliOrderId,
                Status = StatusPaid,
                TotalAmount = precioUnitario,
                PaidAmount = precioUnitario,
                CurrencyId = string.IsNullOrWhiteSpace(listing.CurrencyId) ? "ARS" : listing.CurrencyId,
                FechaCreacionUtc = generadoUtc,
                BuyerId = 0,
                BuyerNickname = "QA OPERATIVA LOCAL",
                MontoComision = comision,
                MontoEnvio = envio > 0 ? envio : null,
                NetoEstimado = precioUnitario - comision - envio,
                EstadoInterno = MercadoLibreOrderEstadoInterno.Importada,
                RawJson = JsonSerializer.Serialize(new
                {
                    simuladaOperativaLocal = true,
                    source = "ERP QA operativo local",
                    listing.ItemId,
                    generadoPor = usuario,
                    generadoUtc
                }),
                CreatedBy = usuario
            };

            orden.Items.Add(new MercadoLibreOrderItem
            {
                ItemId = listing.ItemId,
                VariationId = variacion?.VariationId,
                Titulo = listing.Titulo,
                Cantidad = 1,
                PrecioUnitario = precioUnitario,
                CurrencyId = orden.CurrencyId,
                SellerSku = listing.SellerSku,
                SaleFee = comision,
                ProductoId = listing.ProductoId,
                CreatedBy = usuario
            });

            _context.MercadoLibreOrders.Add(orden);
            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = listing.AccountId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Operacion = "OrderOperativaSimulada",
                Exito = true,
                Detalle = $"Orden operativa simulada {meliOrderId} creada por {usuario}. No se llamo a Mercado Libre.",
                CreatedBy = usuario
            });

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreOrderSimulationResult(
                true,
                orden.Id,
                $"Orden operativa simulada #{orden.MeliOrderId} generada. No se llamo a Mercado Libre.");
        }

        private async Task<MercadoLibreOrder> UpsertOrdenAsync(
            int accountId, MeliOrderDto dto, MercadoLibreConfiguracion config, CancellationToken ct)
        {
            // IgnoreQueryFilters: si existe soft-deleted, revivirla en vez de chocar
            // contra el índice único de MeliOrderId.
            var orden = await _context.MercadoLibreOrders
                .IgnoreQueryFilters()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.MeliOrderId == dto.Id, ct);

            var esNueva = orden is null;

            if (orden is null)
            {
                orden = new MercadoLibreOrder { AccountId = accountId, MeliOrderId = dto.Id };
                _context.MercadoLibreOrders.Add(orden);
            }

            var statusAnterior = orden.Status;

            orden.IsDeleted = false;
            orden.Status = Truncar(dto.Status, 30) ?? string.Empty;
            orden.TotalAmount = dto.TotalAmount;
            orden.PaidAmount = dto.PaidAmount;
            orden.CurrencyId = Truncar(dto.CurrencyId, 10) ?? "ARS";
            orden.FechaCreacionUtc = dto.DateCreated?.UtcDateTime ?? orden.FechaCreacionUtc;
            orden.BuyerId = dto.Buyer?.Id;
            orden.BuyerNickname = Truncar(dto.Buyer?.Nickname, 100);
            orden.ShipmentId = dto.Shipping?.Id ?? orden.ShipmentId;
            orden.MontoComision = dto.ComisionTotal();
            orden.MontoEnvio ??= config.CostoEnvioEstimado > 0 ? config.CostoEnvioEstimado : null;
            orden.NetoEstimado = dto.TotalAmount - (orden.MontoComision ?? 0m) - (orden.MontoEnvio ?? 0m);
            orden.RawJson = JsonSerializer.Serialize(dto);

            // Las líneas solo se reescriben mientras no haya venta generada:
            // después de crear la venta, el snapshot queda congelado.
            if (orden.VentaId is null)
            {
                if (orden.Items.Count > 0)
                    _context.MercadoLibreOrderItems.RemoveRange(orden.Items);

                foreach (var lineaDto in dto.OrderItems)
                {
                    orden.Items.Add(new MercadoLibreOrderItem
                    {
                        ItemId = Truncar(lineaDto.Item.Id, 30) ?? string.Empty,
                        VariationId = lineaDto.Item.VariationId,
                        Titulo = Truncar(lineaDto.Item.Title, 300) ?? string.Empty,
                        Cantidad = lineaDto.Quantity,
                        PrecioUnitario = lineaDto.UnitPrice,
                        CurrencyId = Truncar(lineaDto.CurrencyId, 10) ?? "ARS",
                        SellerSku = Truncar(lineaDto.Item.ResolverSellerSku(), 100),
                        SaleFee = lineaDto.SaleFee
                    });
                }
            }

            // Cancelación con venta ya creada: NO se revierte stock automáticamente.
            // Queda pendiente de revisión manual (Fase H).
            if (string.Equals(dto.Status, StatusCancelled, StringComparison.OrdinalIgnoreCase)
                && orden.VentaId is not null
                && orden.DevolucionEstado == MercadoLibreDevolucionEstado.Ninguna
                && !string.Equals(statusAnterior, StatusCancelled, StringComparison.OrdinalIgnoreCase))
            {
                orden.DevolucionEstado = MercadoLibreDevolucionEstado.PendienteRevision;

                _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = accountId,
                    Operacion = "OrderCancelled",
                    Exito = true,
                    Detalle = $"Orden {dto.Id} cancelada en ML con venta interna ya creada. " +
                              "Devolución marcada PENDIENTE DE REVISIÓN (el stock no se reingresó)."
                });
            }

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Orden ML {OrderId} {Accion}. Status:{Status} EstadoInterno:{EstadoInterno}",
                dto.Id, esNueva ? "importada" : "actualizada", orden.Status, orden.EstadoInterno);

            return orden;
        }

        // ------------------------------------------------------------------
        // Fase C — Venta interna
        // ------------------------------------------------------------------

        public async Task<MercadoLibreOrderProcessResult> CrearVentaInternaAsync(
            int orderId, string usuario, CancellationToken ct = default)
        {
            var orden = await _context.MercadoLibreOrders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            // Idempotencia: una orden genera a lo sumo UNA venta.
            if (orden.VentaId is not null)
            {
                return new MercadoLibreOrderProcessResult(
                    false, orden.VentaId, null, orden.EstadoInterno,
                    "La orden ya tiene una venta interna asociada.");
            }

            if (orden.EstadoInterno == MercadoLibreOrderEstadoInterno.Ignorada)
                return new MercadoLibreOrderProcessResult(false, null, null, orden.EstadoInterno, "Orden marcada como ignorada.");

            if (EsOrdenSimuladaQa(orden.RawJson))
            {
                return new MercadoLibreOrderProcessResult(
                    false,
                    null,
                    null,
                    orden.EstadoInterno,
                    "Orden QA simulada: no se crea venta ni se descuenta stock real.");
            }

            if (!EsPaga(orden.Status))
            {
                return new MercadoLibreOrderProcessResult(
                    false, null, null, orden.EstadoInterno,
                    $"Solo se generan ventas de órdenes pagas (status actual: {orden.Status}).");
            }

            var config = await _configuracionService.GetAsync(ct);

            if (!config.ClienteMercadoLibreId.HasValue)
            {
                return await MarcarPendienteAsync(orden,
                    "Falta configurar el cliente interno para ventas de Mercado Libre.", ct);
            }

            // Resolver Producto por la vinculación EXPLÍCITA listing→Producto.
            var itemIds = orden.Items.Select(i => i.ItemId).Distinct().ToList();

            var listingsVinculados = await _context.MercadoLibreListings
                .AsNoTracking()
                .Where(l => itemIds.Contains(l.ItemId) && l.ProductoId != null)
                .ToDictionaryAsync(l => l.ItemId, ct);

            var vinculos = listingsVinculados.ToDictionary(kv => kv.Key, kv => kv.Value.ProductoId!.Value);

            var sinVincular = orden.Items
                .Where(i => !vinculos.ContainsKey(i.ItemId))
                .Select(i => i.ItemId)
                .Distinct()
                .ToList();

            if (sinVincular.Count > 0)
            {
                return await MarcarPendienteAsync(orden,
                    $"Publicaciones sin producto vinculado: {string.Join(", ", sinVincular)}.", ct);
            }

            var productoIds = vinculos.Values.Distinct().ToList();

            var productos = await _context.Productos
                .Include(p => p.AlicuotaIVA)
                .Include(p => p.Categoria).ThenInclude(c => c.AlicuotaIVA)
                .Where(p => productoIds.Contains(p.Id) && !p.IsDeleted)
                .ToDictionaryAsync(p => p.Id, ct);

            var faltantes = productoIds.Where(id => !productos.ContainsKey(id)).ToList();
            if (faltantes.Count > 0)
            {
                return await MarcarPendienteAsync(orden,
                    $"Productos vinculados inexistentes o eliminados: {string.Join(", ", faltantes)}.", ct);
            }

            // Plan de unidades físicas (trazabilidad). Si alguna línea trazable no
            // puede resolverse, la orden queda PendienteAsignarUnidad SIN tocar stock.
            var (planUnidades, problemasUnidades) = await ResolverPlanUnidadesAsync(
                orden, listingsVinculados, productos, config, ct);

            if (problemasUnidades.Count > 0)
            {
                orden.EstadoInterno = MercadoLibreOrderEstadoInterno.PendienteAsignarUnidad;
                orden.ErrorProcesamiento = Truncar(string.Join(" | ", problemasUnidades), 1000);
                orden.FechaProcesadoUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync(ct);

                return new MercadoLibreOrderProcessResult(
                    false, null, null, MercadoLibreOrderEstadoInterno.PendienteAsignarUnidad,
                    string.Join(" | ", problemasUnidades));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            try
            {
                var numero = await _numberGenerator.GenerarNumeroAsync(EstadoVenta.Confirmada);
                var ahora = DateTime.UtcNow;

                var venta = new Venta
                {
                    Numero = numero,
                    ClienteId = config.ClienteMercadoLibreId.Value,
                    FechaVenta = orden.FechaCreacionUtc,
                    Estado = EstadoVenta.Confirmada,
                    TipoPago = TipoPago.MercadoPago,
                    FechaConfirmacion = ahora,
                    VendedorNombre = "Mercado Libre",
                    Observaciones = Truncar($"Venta canal Mercado Libre — orden {orden.MeliOrderId}" +
                                            (orden.BuyerNickname is null ? "" : $" — comprador {orden.BuyerNickname}"), 500),
                    CreatedBy = usuario
                };

                decimal subtotalNeto = 0m, totalIva = 0m, total = 0m;
                var unidadesPorDetalle = new List<(VentaDetalle Detalle, int ProductoUnidadId)>();

                foreach (var item in orden.Items)
                {
                    var producto = productos[vinculos[item.ItemId]];

                    var porcentajeIva = ProductoIvaResolver.ResolverPorcentajeIVAProducto(producto);
                    var precioUnitario = item.PrecioUnitario;
                    var precioNeto = Math.Round(precioUnitario / (1 + porcentajeIva / 100m), 2);
                    var ivaUnitario = precioUnitario - precioNeto;

                    VentaDetalle CrearDetalle(int cantidad)
                    {
                        var subtotal = precioUnitario * cantidad;
                        var lineaNeto = precioNeto * cantidad;
                        var lineaIva = ivaUnitario * cantidad;

                        var detalle = new VentaDetalle
                        {
                            ProductoId = producto.Id,
                            Cantidad = cantidad,
                            PrecioUnitario = precioUnitario,
                            Subtotal = subtotal,
                            PorcentajeIVA = porcentajeIva,
                            PrecioUnitarioNeto = precioNeto,
                            IVAUnitario = ivaUnitario,
                            SubtotalNeto = lineaNeto,
                            SubtotalIVA = lineaIva,
                            SubtotalFinalNeto = lineaNeto,
                            SubtotalFinalIVA = lineaIva,
                            SubtotalFinal = subtotal,
                            CostoUnitarioAlMomento = producto.PrecioCompra,
                            CostoTotalAlMomento = producto.PrecioCompra * cantidad,
                            Observaciones = Truncar($"ML {item.ItemId}" +
                                                    (item.VariationId is null ? "" : $" var {item.VariationId}"), 200),
                            CreatedBy = usuario
                        };

                        venta.Detalles.Add(detalle);

                        subtotalNeto += lineaNeto;
                        totalIva += lineaIva;
                        total += subtotal;

                        return detalle;
                    }

                    if (planUnidades.TryGetValue(item.Id, out var unidades) && unidades.Count > 0)
                    {
                        // Trazable: una línea de venta por unidad física (cantidad 1
                        // cada una), misma regla que la venta manual del ERP.
                        foreach (var unidadId in unidades)
                            unidadesPorDetalle.Add((CrearDetalle(1), unidadId));

                        item.UnidadesAsignadas = string.Join(",", unidades);
                    }
                    else
                    {
                        CrearDetalle(item.Cantidad);
                    }

                    item.ProductoId = producto.Id;
                }

                venta.Subtotal = subtotalNeto;
                venta.IVA = totalIva;
                venta.Total = total;

                _context.Ventas.Add(venta);
                await _context.SaveChangesAsync(ct);

                // Descuento de stock con la MISMA primitiva canónica que usa
                // VentaService (valida disponibilidad y escribe el kardex).
                // Comparte la transacción ambiente de este contexto.
                var referencia = $"Venta {numero} (ML {orden.MeliOrderId})";

                var salidas = venta.Detalles
                    .Select(d => (d.ProductoId, (decimal)d.Cantidad, (string?)referencia))
                    .ToList();

                var costos = venta.Detalles
                    .Select(d => new MovimientoStockCostoLinea(
                        d.ProductoId, d.Cantidad, referencia, d.CostoUnitarioAlMomento, "ProductoPrecioCompra"))
                    .ToList();

                await _movimientoStockService.RegistrarSalidasAsync(
                    salidas, $"Venta Mercado Libre — orden {orden.MeliOrderId}", usuario, costos);

                // Unidades físicas asignadas: misma primitiva canónica que la venta
                // manual (EnStock → Vendida con historial). Comparte la transacción
                // ambiente; si una unidad dejó de estar disponible, lanza y se
                // revierte TODO (venta, kardex y unidades).
                foreach (var (detalle, unidadId) in unidadesPorDetalle)
                {
                    await _productoUnidadService.MarcarVendidaAsync(
                        unidadId, detalle.Id, venta.ClienteId, usuario);

                    detalle.ProductoUnidadId = unidadId;
                }

                if (unidadesPorDetalle.Count > 0)
                    await _context.SaveChangesAsync(ct);

                orden.VentaId = venta.Id;
                orden.EstadoInterno = MercadoLibreOrderEstadoInterno.VentaCreada;
                orden.FechaProcesadoUtc = ahora;
                orden.ErrorProcesamiento = null;

                _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = orden.AccountId,
                    Operacion = "OrderToVenta",
                    Exito = true,
                    Detalle = $"Orden {orden.MeliOrderId} → venta {numero} " +
                              $"({venta.Detalles.Count} líneas, total {total:N2}). Stock descontado.",
                    CreatedBy = usuario
                });

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                _logger.LogInformation(
                    "Orden ML {OrderId} generó venta {Numero} (id {VentaId})",
                    orden.MeliOrderId, numero, venta.Id);

                return new MercadoLibreOrderProcessResult(
                    true, venta.Id, numero, MercadoLibreOrderEstadoInterno.VentaCreada, null);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);

                // El rollback deja entidades trackeadas a medias: limpiar y
                // persistir solo el estado de error de la orden.
                _context.ChangeTracker.Clear();

                var mensaje = Truncar(ex.Message, 950) ?? "Error desconocido";

                await _context.MercadoLibreOrders
                    .Where(o => o.Id == orderId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.EstadoInterno, MercadoLibreOrderEstadoInterno.Error)
                        .SetProperty(o => o.ErrorProcesamiento, mensaje)
                        .SetProperty(o => o.FechaProcesadoUtc, DateTime.UtcNow), ct);

                _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    Operacion = "OrderToVenta",
                    Exito = false,
                    Detalle = $"Orden interna {orderId}: {mensaje}",
                    CreatedBy = usuario
                });
                await _context.SaveChangesAsync(ct);

                _logger.LogError(ex, "Error creando venta interna para orden ML interna {OrderId}", orderId);

                return new MercadoLibreOrderProcessResult(
                    false, null, null, MercadoLibreOrderEstadoInterno.Error, mensaje);
            }
        }

        private async Task<MercadoLibreOrderProcessResult> MarcarPendienteAsync(
            MercadoLibreOrder orden, string motivo, CancellationToken ct)
        {
            orden.EstadoInterno = MercadoLibreOrderEstadoInterno.PendienteVinculacion;
            orden.ErrorProcesamiento = Truncar(motivo, 1000);
            orden.FechaProcesadoUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreOrderProcessResult(
                false, null, null, MercadoLibreOrderEstadoInterno.PendienteVinculacion, motivo);
        }

        // ------------------------------------------------------------------
        // Trazabilidad: plan de unidades físicas por línea
        // ------------------------------------------------------------------

        /// <summary>
        /// Decide qué unidades físicas respaldan cada línea según el origen de
        /// stock de la publicación y la trazabilidad del producto. Es solo un
        /// plan: no modifica nada. Las líneas trazables que no pueden resolverse
        /// dejan la orden Pendiente de asignar unidad.
        /// </summary>
        private async Task<(Dictionary<int, List<int>> Plan, List<string> Problemas)> ResolverPlanUnidadesAsync(
            MercadoLibreOrder orden,
            IReadOnlyDictionary<string, MercadoLibreListing> listings,
            IReadOnlyDictionary<int, Producto> productos,
            MercadoLibreConfiguracion config,
            CancellationToken ct)
        {
            var plan = new Dictionary<int, List<int>>();
            var problemas = new List<string>();
            var tomadas = new HashSet<int>();

            foreach (var item in orden.Items.Where(i => !i.IsDeleted))
            {
                var listing = listings[item.ItemId];
                var producto = productos[listing.ProductoId!.Value];
                var origen = MercadoLibreStockResolver.ResolverOrigen(listing, config);

                // 1) Asignación manual previa: tiene prioridad sobre todo.
                var manuales = ParsearUnidades(item.UnidadesAsignadas);
                if (manuales.Count > 0)
                {
                    var errorManual = await ValidarUnidadesAsync(manuales, producto, item.Cantidad, tomadas, ct);
                    if (errorManual is not null)
                    {
                        problemas.Add($"{item.ItemId}: {errorManual}");
                        continue;
                    }

                    plan[item.Id] = manuales;
                    tomadas.UnionWith(manuales);
                    continue;
                }

                // 2) Publicación que representa UNA unidad física concreta.
                if (origen == MercadoLibreOrigenStock.UnidadFisicaEspecifica)
                {
                    if (listing.ProductoUnidadId is null)
                    {
                        problemas.Add($"{item.ItemId}: la publicación usa origen 'unidad física específica' pero no tiene unidad configurada.");
                        continue;
                    }

                    if (item.Cantidad != 1)
                    {
                        problemas.Add($"{item.ItemId}: la publicación representa una única unidad física y la orden pide {item.Cantidad}.");
                        continue;
                    }

                    var ids = new List<int> { listing.ProductoUnidadId.Value };
                    var errorUnica = await ValidarUnidadesAsync(ids, producto, 1, tomadas, ct);
                    if (errorUnica is not null)
                    {
                        problemas.Add($"{item.ItemId}: {errorUnica}");
                        continue;
                    }

                    plan[item.Id] = ids;
                    tomadas.UnionWith(ids);
                    continue;
                }

                // 3) ¿La línea exige unidades físicas?
                var exigeUnidades = producto.RequiereNumeroSerie
                    || origen == MercadoLibreOrigenStock.StockFisicoDisponible;

                if (!exigeUnidades)
                {
                    // Producto flexible: vale el stock no trazado (misma regla que
                    // VentaService). Si alcanza, la línea va sin unidades.
                    var enStock = await MercadoLibreStockResolver
                        .ContarUnidadesEnStockAsync(_context, producto.Id, ct);

                    var noTrazado = producto.StockActual - enStock;

                    if (noTrazado >= item.Cantidad)
                        continue;

                    if (enStock == 0)
                        continue; // sin unidades registradas: RegistrarSalidas valida el stock agregado

                    // Hay unidades pero el stock no trazado no alcanza: intentar FIFO.
                }

                var asignadas = await AsignarFifoAsync(producto.Id, item.Cantidad, tomadas, ct);

                if (asignadas.Count < item.Cantidad)
                {
                    problemas.Add(
                        $"{item.ItemId}: '{producto.Nombre}' necesita {item.Cantidad} unidad(es) física(s) y hay {asignadas.Count} disponible(s). " +
                        "Asignalas manualmente desde el detalle de la orden o registrá unidades.");
                    continue;
                }

                plan[item.Id] = asignadas;
                tomadas.UnionWith(asignadas);
            }

            return (plan, problemas);
        }

        public async Task AsignarUnidadesAsync(
            int orderId, int orderItemId, IReadOnlyCollection<int> unidadIds, string usuario, CancellationToken ct = default)
        {
            var orden = await _context.MercadoLibreOrders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            if (orden.VentaId is not null)
                throw new InvalidOperationException("La orden ya tiene venta interna: no se pueden reasignar unidades.");

            var item = orden.Items.FirstOrDefault(i => i.Id == orderItemId && !i.IsDeleted)
                ?? throw new InvalidOperationException($"Línea {orderItemId} inexistente en la orden.");

            var listing = await _context.MercadoLibreListings
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.ItemId == item.ItemId && l.ProductoId != null, ct)
                ?? throw new InvalidOperationException(
                    "La publicación no tiene producto vinculado. Vinculá esta publicación a un producto interno antes de operar.");

            var producto = await _context.Productos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == listing.ProductoId!.Value && !p.IsDeleted, ct)
                ?? throw new InvalidOperationException("El producto vinculado no existe o fue eliminado.");

            var ids = unidadIds.Distinct().ToList();

            // Unidades ya reservadas por OTRAS líneas de esta misma orden.
            var tomadas = new HashSet<int>(orden.Items
                .Where(i => i.Id != orderItemId && !i.IsDeleted)
                .SelectMany(i => ParsearUnidades(i.UnidadesAsignadas)));

            var error = await ValidarUnidadesAsync(ids, producto, item.Cantidad, tomadas, ct);
            if (error is not null)
                throw new InvalidOperationException($"No se pudieron asignar las unidades: {error}");

            item.UnidadesAsignadas = string.Join(",", ids);
            item.ProductoId = producto.Id;

            // Si estaba pendiente por unidades, vuelve a Importada para reintentar.
            if (orden.EstadoInterno == MercadoLibreOrderEstadoInterno.PendienteAsignarUnidad)
            {
                orden.EstadoInterno = MercadoLibreOrderEstadoInterno.Importada;
                orden.ErrorProcesamiento = null;
            }

            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = orden.AccountId,
                ItemId = item.ItemId,
                Operacion = "AsignarUnidades",
                Exito = true,
                Detalle = $"Orden {orden.MeliOrderId}, línea {item.ItemId}: unidades [{item.UnidadesAsignadas}] asignadas por {usuario}.",
                CreatedBy = usuario
            });

            await _context.SaveChangesAsync(ct);
        }

        private static List<int> ParsearUnidades(string? csv)
            => string.IsNullOrWhiteSpace(csv)
                ? new List<int>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(s => int.TryParse(s, out var id) ? id : 0)
                     .Where(id => id > 0)
                     .Distinct()
                     .ToList();

        private async Task<string?> ValidarUnidadesAsync(
            IReadOnlyCollection<int> unidadIds,
            Producto producto,
            int cantidadRequerida,
            IReadOnlySet<int> tomadas,
            CancellationToken ct)
        {
            if (unidadIds.Count != cantidadRequerida)
                return $"se requieren {cantidadRequerida} unidad(es) y hay {unidadIds.Count} asignada(s).";

            if (unidadIds.Any(tomadas.Contains))
                return "hay unidades repetidas entre líneas de la orden.";

            var unidades = await _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => unidadIds.Contains(u.Id) && !u.IsDeleted)
                .ToListAsync(ct);

            if (unidades.Count != unidadIds.Count)
                return "alguna de las unidades asignadas no existe.";

            foreach (var unidad in unidades)
            {
                if (unidad.ProductoId != producto.Id)
                    return $"la unidad '{unidad.CodigoInternoUnidad}' no pertenece al producto '{producto.Nombre}'.";

                if (unidad.Estado != EstadoUnidad.EnStock)
                    return $"la unidad '{unidad.CodigoInternoUnidad}' no está disponible (estado: {unidad.Estado}).";
            }

            return null;
        }

        /// <summary>FIFO por fecha de ingreso (luego Id) sobre unidades EnStock no tomadas.</summary>
        private async Task<List<int>> AsignarFifoAsync(
            int productoId, int cantidad, IReadOnlySet<int> tomadas, CancellationToken ct)
        {
            var disponibles = await _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => u.ProductoId == productoId && u.Estado == EstadoUnidad.EnStock && !u.IsDeleted)
                .OrderBy(u => u.FechaIngreso).ThenBy(u => u.Id)
                .Select(u => u.Id)
                .ToListAsync(ct);

            return disponibles.Where(id => !tomadas.Contains(id)).Take(cantidad).ToList();
        }

        // ------------------------------------------------------------------
        // Fase D — Liquidación
        // ------------------------------------------------------------------

        public async Task RegistrarLiquidacionAsync(
            int orderId, decimal netoReal, string usuario, CancellationToken ct = default)
        {
            if (netoReal <= 0)
                throw new InvalidOperationException("El neto liquidado debe ser mayor a 0.");

            var orden = await _context.MercadoLibreOrders
                .Include(o => o.Venta)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            var esSimuladaQa = EsOrdenSimuladaQa(orden.RawJson);
            var esSimuladaOperativa = EsOrdenSimuladaOperativa(orden.RawJson);
            var config = await _configuracionService.GetAsync(ct);

            if (esSimuladaQa)
                throw new InvalidOperationException("La orden QA simulada no registra liquidacion ni movimientos de caja.");

            if (esSimuladaOperativa && !config.ModoSimulacion)
                throw new InvalidOperationException(
                    "La liquidacion de una orden operativa simulada local requiere ModoSimulacion=true.");

            if (orden.VentaId is null || orden.EstadoInterno != MercadoLibreOrderEstadoInterno.VentaCreada)
                throw new InvalidOperationException("Solo se liquidan órdenes con venta interna creada.");

            if (orden.MovimientoCajaId is not null)
                throw new InvalidOperationException("La orden ya tiene una liquidación registrada.");

            var apertura = await _cajaService.ObtenerAperturaActivaParaUsuarioAsync(usuario)
                ?? throw new InvalidOperationException(
                    "Registrar la liquidación requiere una caja abierta para el usuario actual.");

            var diferencia = orden.NetoEstimado.HasValue ? netoReal - orden.NetoEstimado.Value : (decimal?)null;
            var prefijoSimulacion = esSimuladaOperativa ? "[SIMULACION] " : string.Empty;
            var observaciones = diferencia is null
                ? "Mercado Pago."
                : $"Mercado Pago. Neto estimado {orden.NetoEstimado:N2}; diferencia {diferencia:N2}.";

            if (esSimuladaOperativa)
                observaciones += " Liquidacion de orden operativa simulada local; no se llamo a Mercado Libre.";

            var movimiento = await _cajaService.RegistrarMovimientoAsync(new MovimientoCajaViewModel
            {
                AperturaCajaId = apertura.Id,
                Tipo = TipoMovimientoCaja.Ingreso,
                Concepto = ConceptoMovimientoCaja.LiquidacionMercadoLibre,
                Monto = netoReal,
                Descripcion = $"Liquidación Mercado Libre — orden {orden.MeliOrderId} — venta {orden.Venta?.Numero}",
                Referencia = $"ML-{orden.MeliOrderId}",
                Observaciones = diferencia is null
                    ? null
                    : $"Neto estimado {orden.NetoEstimado:N2}; diferencia {diferencia:N2}."
            }, usuario);

            if (esSimuladaOperativa)
                movimiento.Descripcion = prefijoSimulacion + movimiento.Descripcion;

            movimiento.Observaciones = observaciones;
            movimiento.TipoPago = TipoPago.MercadoPago;
            movimiento.EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado;
            movimiento.VentaId = orden.VentaId;
            movimiento.ReferenciaId = orden.Id;
            movimiento.MedioPagoDetalle = "Mercado Pago";

            orden.NetoReal = netoReal;
            orden.FechaLiquidacionUtc = DateTime.UtcNow;
            orden.MovimientoCajaId = movimiento.Id;
            orden.EstadoInterno = MercadoLibreOrderEstadoInterno.Liquidada;

            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = orden.AccountId,
                Operacion = "Liquidacion",
                Exito = true,
                Detalle = $"Orden {orden.MeliOrderId}: neto real {netoReal:N2} acreditado en caja " +
                          $"(estimado {orden.NetoEstimado:N2}, diferencia {diferencia:N2}).",
                CreatedBy = usuario
            });

            await _context.SaveChangesAsync(ct);
        }

        // ------------------------------------------------------------------
        // Fase H — Devoluciones y envío
        // ------------------------------------------------------------------

        public async Task DecidirDevolucionAsync(
            int orderId, MercadoLibreDevolucionEstado decision, string? nota, string usuario, CancellationToken ct = default)
        {
            if (decision == MercadoLibreDevolucionEstado.Ninguna)
                throw new InvalidOperationException("Decisión de devolución inválida.");

            var orden = await _context.MercadoLibreOrders
                .Include(o => o.Venta).ThenInclude(v => v!.Detalles)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            if (orden.DevolucionEstado == MercadoLibreDevolucionEstado.StockReingresado)
                throw new InvalidOperationException("El stock de esta devolución ya fue reingresado.");

            if (decision == MercadoLibreDevolucionEstado.StockReingresado)
            {
                if (orden.Venta is null)
                    throw new InvalidOperationException("No hay venta interna: no corresponde reingresar stock.");

                var referencia = $"Devolución ML {orden.MeliOrderId} (venta {orden.Venta.Numero})";

                var entradas = orden.Venta.Detalles
                    .Where(d => !d.IsDeleted)
                    .Select(d => (d.ProductoId, (decimal)d.Cantidad, (string?)referencia))
                    .ToList();

                var costos = orden.Venta.Detalles
                    .Where(d => !d.IsDeleted)
                    .Select(d => new MovimientoStockCostoLinea(
                        d.ProductoId, d.Cantidad, referencia, d.CostoUnitarioAlMomento, "VentaDetalleSnapshot"))
                    .ToList();

                await _movimientoStockService.RegistrarEntradasAsync(
                    entradas, $"Devolución Mercado Libre — orden {orden.MeliOrderId}", usuario, costos: costos);
            }

            orden.DevolucionEstado = decision;
            orden.DevolucionNota = Truncar(nota, 500);

            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = orden.AccountId,
                Operacion = "Devolucion",
                Exito = true,
                Detalle = $"Orden {orden.MeliOrderId}: devolución resuelta como {decision} por {usuario}." +
                          (string.IsNullOrWhiteSpace(nota) ? "" : $" Nota: {Truncar(nota, 300)}"),
                CreatedBy = usuario
            });

            await _context.SaveChangesAsync(ct);
        }

        public async Task<MercadoLibreOrderSimulationResult> SimularClaimAsync(
            int orderId,
            MercadoLibreClaimTipo tipo,
            string? motivo,
            string usuario,
            bool permitirPorDevelopment = false,
            CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            if (!config.ModoSimulacion && !permitirPorDevelopment)
            {
                return new MercadoLibreOrderSimulationResult(
                    false,
                    null,
                    "La simulacion de reclamos/devoluciones solo esta habilitada en Development o con ModoSimulacion=true.");
            }

            var orden = await _context.MercadoLibreOrders
                .Include(o => o.Claims)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            if (orden.Claims.Any(c => c.Estado == MercadoLibreClaimEstado.PendienteRevision && !c.IsDeleted))
            {
                return new MercadoLibreOrderSimulationResult(
                    false,
                    orden.Id,
                    "La orden ya tiene un reclamo/devolucion pendiente de revision.");
            }

            var motivoNormalizado = Truncar(
                string.IsNullOrWhiteSpace(motivo) ? $"QA local: {tipo}" : motivo.Trim(),
                500);

            var claim = new MercadoLibreClaim
            {
                MercadoLibreOrderId = orden.Id,
                MercadoLibreClaimId = $"QA-{orden.MeliOrderId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                Tipo = tipo,
                Estado = MercadoLibreClaimEstado.PendienteRevision,
                Motivo = motivoNormalizado,
                AccionStock = MercadoLibreClaimAccionStock.NoReingresar,
                AccionEconomica = MercadoLibreClaimAccionEconomica.SinImpacto,
                EsSimuladoLocal = true,
                FechaCreacionUtc = DateTime.UtcNow,
                RawJson = JsonSerializer.Serialize(new
                {
                    simuladoLocal = true,
                    tipo = tipo.ToString(),
                    motivo = motivoNormalizado,
                    orderId = orden.MeliOrderId
                }),
                CreatedBy = usuario
            };

            _context.MercadoLibreClaims.Add(claim);

            orden.DevolucionEstado = MercadoLibreDevolucionEstado.PendienteRevision;
            orden.DevolucionNota = motivoNormalizado;
            orden.UpdatedBy = usuario;

            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = orden.AccountId,
                Operacion = "ClaimQASimulado",
                Exito = true,
                Detalle = $"Orden {orden.MeliOrderId}: {tipo} simulado local pendiente de revision. " +
                          "No se llamo a Mercado Libre. No modifica venta, stock ni caja.",
                CreatedBy = usuario
            });

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreOrderSimulationResult(
                true,
                orden.Id,
                $"{tipo} simulado local creado. Queda pendiente de revision manual.");
        }

        public async Task ResolverClaimAsync(
            int claimId,
            MercadoLibreClaimAccionStock accionStock,
            MercadoLibreClaimAccionEconomica accionEconomica,
            string? resolucionManual,
            string? observaciones,
            string usuario,
            CancellationToken ct = default)
        {
            var claim = await _context.MercadoLibreClaims
                .Include(c => c.Order).ThenInclude(o => o.Venta).ThenInclude(v => v!.Detalles)
                .Include(c => c.Order).ThenInclude(o => o.Items)
                .FirstOrDefaultAsync(c => c.Id == claimId, ct)
                ?? throw new InvalidOperationException($"Claim {claimId} no encontrado.");

            if (claim.Estado != MercadoLibreClaimEstado.PendienteRevision)
                throw new InvalidOperationException("El reclamo/devolucion ya fue resuelto.");

            if (claim.MovimientoStockId is not null)
                throw new InvalidOperationException("El stock de este reclamo/devolucion ya fue procesado.");

            if (claim.MovimientoCajaId is not null)
                throw new InvalidOperationException("La caja de este reclamo/devolucion ya fue procesada.");

            if (accionStock == MercadoLibreClaimAccionStock.ReingresarStock && claim.Order.Venta is null)
                throw new InvalidOperationException("No hay venta interna: no corresponde reingresar stock.");

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            try
            {
                MovimientoStock? movimientoStock = null;

                if (accionStock == MercadoLibreClaimAccionStock.ReingresarStock)
                {
                    var venta = claim.Order.Venta!;
                    var referencia = $"Claim ML {claim.MercadoLibreClaimId ?? claim.Id.ToString()} (orden {claim.Order.MeliOrderId})";

                    var entradas = venta.Detalles
                        .Where(d => !d.IsDeleted)
                        .Select(d => (d.ProductoId, (decimal)d.Cantidad, (string?)referencia))
                        .ToList();

                    var costos = venta.Detalles
                        .Where(d => !d.IsDeleted)
                        .Select(d => new MovimientoStockCostoLinea(
                            d.ProductoId, d.Cantidad, referencia, d.CostoUnitarioAlMomento, "VentaDetalleSnapshot"))
                        .ToList();

                    var movimientos = await _movimientoStockService.RegistrarEntradasAsync(
                        entradas,
                        $"Reingreso manual Mercado Libre - claim {claim.MercadoLibreClaimId ?? claim.Id.ToString()}",
                        usuario,
                        costos: costos);

                    movimientoStock = movimientos.FirstOrDefault();

                    await AplicarUnidadesClaimAsync(
                        claim.Order,
                        accionStock,
                        $"Reingreso manual ML claim {claim.MercadoLibreClaimId ?? claim.Id.ToString()}",
                        usuario,
                        ct);
                }
                else if (accionStock is MercadoLibreClaimAccionStock.Danado
                         or MercadoLibreClaimAccionStock.Garantia
                         or MercadoLibreClaimAccionStock.Merma)
                {
                    await AplicarUnidadesClaimAsync(
                        claim.Order,
                        accionStock,
                        $"Claim ML {accionStock}: no vuelve a stock disponible",
                        usuario,
                        ct);
                }

                claim.Estado = MercadoLibreClaimEstado.Resuelto;
                claim.AccionStock = accionStock;
                claim.AccionEconomica = accionEconomica;
                claim.MovimientoStockId = movimientoStock?.Id;
                claim.MovimientoCajaId = null;
                claim.ResolucionManual = Truncar(resolucionManual, 500);
                claim.Observaciones = Truncar(observaciones, 1000);
                claim.FechaResolucionUtc = DateTime.UtcNow;
                claim.UsuarioResolucion = Truncar(usuario, 100);
                claim.UpdatedBy = usuario;

                claim.Order.DevolucionEstado = MapearDevolucionEstado(accionStock);
                claim.Order.DevolucionNota = Truncar(
                    resolucionManual ?? observaciones ?? claim.Motivo,
                    500);
                claim.Order.UpdatedBy = usuario;

                _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
                {
                    AccountId = claim.Order.AccountId,
                    Operacion = "ClaimResolucionManual",
                    Exito = true,
                    Detalle = $"Orden {claim.Order.MeliOrderId}: claim {claim.MercadoLibreClaimId ?? claim.Id.ToString()} " +
                              $"resuelto. Stock={accionStock}; Economia={accionEconomica}. " +
                              "Caja no se mueve automaticamente.",
                    CreatedBy = usuario
                });

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task MarcarIgnoradaAsync(int orderId, string usuario, CancellationToken ct = default)
        {
            var orden = await _context.MercadoLibreOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            if (orden.VentaId is not null)
                throw new InvalidOperationException("No se puede ignorar una orden con venta interna creada.");

            orden.EstadoInterno = MercadoLibreOrderEstadoInterno.Ignorada;
            orden.UpdatedBy = usuario;

            await _context.SaveChangesAsync(ct);
        }

        public async Task ActualizarEnvioAsync(int orderId, CancellationToken ct = default)
        {
            var orden = await _context.MercadoLibreOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            if (orden.ShipmentId is null)
                return;

            var token = await _authService.GetValidAccessTokenAsync(orden.AccountId, ct);
            var envio = await _apiClient.GetShipmentAsync(token, orden.ShipmentId.Value, ct);

            if (envio is null)
                return;

            AplicarShipment(orden, envio);

            // Costo real del envío si la API lo informa (mejora el neto estimado).
            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = orden.AccountId,
                Operacion = "ShipmentUpdate",
                Exito = true,
                Detalle = $"Orden {orden.MeliOrderId}: shipment {orden.ShipmentId} actualizado. " +
                          $"Estado envio: {orden.EstadoEnvioInterno}. No modifica venta, stock ni caja."
            });

            await _context.SaveChangesAsync(ct);
        }

        public async Task<MercadoLibreOrderSimulationResult> SimularEnvioAsync(
            int orderId, string escenario, string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            if (!config.ModoSimulacion && !permitirPorDevelopment)
            {
                return new MercadoLibreOrderSimulationResult(
                    false,
                    null,
                    "La simulacion de envio solo esta habilitada en Development o con ModoSimulacion=true.");
            }

            var orden = await _context.MercadoLibreOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"Orden {orderId} no encontrada.");

            var envio = CrearShipmentSimulado(orden, escenario);
            orden.ShipmentId ??= envio.Id;

            AplicarShipment(orden, envio);
            orden.UpdatedBy = usuario;

            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = orden.AccountId,
                Operacion = "ShipmentQASimulado",
                Exito = true,
                Detalle = $"Orden {orden.MeliOrderId}: envio simulado como {orden.EstadoEnvioInterno} por {usuario}. " +
                          "No se llamo a Mercado Libre. No modifica venta, stock ni caja.",
                CreatedBy = usuario
            });

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreOrderSimulationResult(
                true,
                orden.Id,
                $"Envio simulado como {orden.EstadoEnvioInterno}. No se llamo a Mercado Libre.");
        }

        // ------------------------------------------------------------------
        // Consultas
        // ------------------------------------------------------------------

        public async Task<List<MercadoLibreOrderViewModel>> GetOrdenesAsync(
            string? filtro = null, CancellationToken ct = default)
        {
            var query = _context.MercadoLibreOrders.AsNoTracking();

            query = filtro switch
            {
                "pendientes" => query.Where(o =>
                    o.EstadoInterno == MercadoLibreOrderEstadoInterno.Importada ||
                    o.EstadoInterno == MercadoLibreOrderEstadoInterno.PendienteVinculacion ||
                    o.EstadoInterno == MercadoLibreOrderEstadoInterno.PendienteAsignarUnidad ||
                    o.EstadoInterno == MercadoLibreOrderEstadoInterno.Error),
                "venta-creada" => query.Where(o => o.EstadoInterno == MercadoLibreOrderEstadoInterno.VentaCreada),
                "liquidadas" => query.Where(o => o.EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada),
                "devoluciones" => query.Where(o =>
                    o.DevolucionEstado != MercadoLibreDevolucionEstado.Ninguna ||
                    o.Claims.Any(c => c.Estado == MercadoLibreClaimEstado.PendienteRevision)),
                _ => query
            };

            return await query
                .OrderByDescending(o => o.FechaCreacionUtc)
                .Take(500)
                .Select(o => new MercadoLibreOrderViewModel
                {
                    Id = o.Id,
                    MeliOrderId = o.MeliOrderId,
                    Status = o.Status,
                    EstadoInterno = o.EstadoInterno,
                    EsSimulada = o.RawJson != null
                        && (o.RawJson.Contains(RawQaMarker) || o.RawJson.Contains(RawOperativaLocalMarker)),
                    EsSimuladaQa = o.RawJson != null && o.RawJson.Contains(RawQaMarker),
                    EsSimuladaOperativa = o.RawJson != null && o.RawJson.Contains(RawOperativaLocalMarker),
                    FechaCreacionUtc = o.FechaCreacionUtc,
                    TotalAmount = o.TotalAmount,
                    NetoEstimado = o.NetoEstimado,
                    NetoReal = o.NetoReal,
                    BuyerNickname = o.BuyerNickname,
                    CantidadItems = o.Items.Count(i => !i.IsDeleted),
                    VentaId = o.VentaId,
                    VentaNumero = o.Venta != null ? o.Venta.Numero : null,
                    ErrorProcesamiento = o.ErrorProcesamiento,
                    DevolucionEstado = o.DevolucionEstado,
                    ShipmentId = o.ShipmentId,
                    ShipmentStatus = o.ShipmentStatus,
                    ShipmentSubStatus = o.ShipmentSubStatus,
                    TrackingNumber = o.TrackingNumber,
                    EstadoEnvioInterno = o.EstadoEnvioInterno,
                    ClaimsPendientes = o.Claims.Count(c => c.Estado == MercadoLibreClaimEstado.PendienteRevision),
                    UltimoClaimTipo = o.Claims
                        .OrderByDescending(c => c.FechaCreacionUtc)
                        .Select(c => (MercadoLibreClaimTipo?)c.Tipo)
                        .FirstOrDefault(),
                    UltimoClaimEstado = o.Claims
                        .OrderByDescending(c => c.FechaCreacionUtc)
                        .Select(c => (MercadoLibreClaimEstado?)c.Estado)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);
        }

        public async Task<MercadoLibreOrderDetalleViewModel?> GetOrdenAsync(
            int orderId, CancellationToken ct = default)
        {
            var orden = await _context.MercadoLibreOrders
                .AsNoTracking()
                .Include(o => o.Items)
                .Include(o => o.Venta)
                .Include(o => o.Claims)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (orden is null)
                return null;

            var itemIds = orden.Items.Select(i => i.ItemId).Distinct().ToList();

            var vinculos = await _context.MercadoLibreListings
                .AsNoTracking()
                .Where(l => itemIds.Contains(l.ItemId) && l.ProductoId != null)
                .Select(l => new
                {
                    l.ItemId,
                    ProductoId = l.ProductoId!.Value,
                    l.Producto!.Codigo,
                    l.Producto.Nombre,
                    l.Producto.RequiereNumeroSerie,
                    l.OrigenStockOverride,
                    l.ProductoUnidadId
                })
                .ToDictionaryAsync(l => l.ItemId, ct);

            var config = await _configuracionService.GetAsync(ct);

            // Info de unidades físicas para la UI: asignadas (siempre) y
            // disponibles para elegir (solo mientras no haya venta creada).
            var unidadIdsAsignadas = orden.Items
                .SelectMany(i => ParsearUnidades(i.UnidadesAsignadas))
                .Distinct()
                .ToList();

            var productoIdsConUnidades = vinculos.Values
                .Where(v => v.RequiereNumeroSerie
                    || (v.OrigenStockOverride ?? config.OrigenStock) != MercadoLibreOrigenStock.StockLogicoProducto)
                .Select(v => v.ProductoId)
                .Distinct()
                .ToList();

            var puedeAsignar = orden.VentaId is null;

            var unidades = await _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => !u.IsDeleted &&
                    (unidadIdsAsignadas.Contains(u.Id) ||
                     (puedeAsignar && productoIdsConUnidades.Contains(u.ProductoId) && u.Estado == EstadoUnidad.EnStock)))
                .OrderBy(u => u.FechaIngreso).ThenBy(u => u.Id)
                .Select(u => new { u.Id, u.ProductoId, u.CodigoInternoUnidad, u.NumeroSerie, u.Estado })
                .ToListAsync(ct);

            return new MercadoLibreOrderDetalleViewModel
            {
                Id = orden.Id,
                MeliOrderId = orden.MeliOrderId,
                Status = orden.Status,
                EstadoInterno = orden.EstadoInterno,
                EsSimulada = EsOrdenSimuladaLocal(orden.RawJson),
                EsSimuladaQa = EsOrdenSimuladaQa(orden.RawJson),
                EsSimuladaOperativa = EsOrdenSimuladaOperativa(orden.RawJson),
                ModoSimulacion = config.ModoSimulacion,
                FechaCreacionUtc = orden.FechaCreacionUtc,
                FechaProcesadoUtc = orden.FechaProcesadoUtc,
                CurrencyId = orden.CurrencyId,
                BuyerId = orden.BuyerId,
                BuyerNickname = orden.BuyerNickname,
                TotalAmount = orden.TotalAmount,
                PaidAmount = orden.PaidAmount,
                MontoComision = orden.MontoComision,
                MontoEnvio = orden.MontoEnvio,
                NetoEstimado = orden.NetoEstimado,
                NetoReal = orden.NetoReal,
                FechaLiquidacionUtc = orden.FechaLiquidacionUtc,
                MovimientoCajaId = orden.MovimientoCajaId,
                VentaId = orden.VentaId,
                VentaNumero = orden.Venta?.Numero,
                ShipmentId = orden.ShipmentId,
                ShipmentStatus = orden.ShipmentStatus,
                ShipmentSubStatus = orden.ShipmentSubStatus,
                TrackingNumber = orden.TrackingNumber,
                TrackingMethod = orden.TrackingMethod,
                ShippingMode = orden.ShippingMode,
                ShippingType = orden.ShippingType,
                FechaDespachoUtc = orden.FechaDespachoUtc,
                FechaEntregadoUtc = orden.FechaEntregadoUtc,
                FechaUltimaActualizacionEnvioUtc = orden.FechaUltimaActualizacionEnvioUtc,
                EstadoEnvioInterno = orden.EstadoEnvioInterno,
                DevolucionEstado = orden.DevolucionEstado,
                DevolucionNota = orden.DevolucionNota,
                Claims = orden.Claims
                    .Where(c => !c.IsDeleted)
                    .OrderByDescending(c => c.FechaCreacionUtc)
                    .Select(c => new MercadoLibreClaimViewModel
                    {
                        Id = c.Id,
                        MercadoLibreClaimId = c.MercadoLibreClaimId,
                        Tipo = c.Tipo,
                        Estado = c.Estado,
                        Motivo = c.Motivo,
                        ResolucionManual = c.ResolucionManual,
                        AccionStock = c.AccionStock,
                        AccionEconomica = c.AccionEconomica,
                        MovimientoStockId = c.MovimientoStockId,
                        MovimientoCajaId = c.MovimientoCajaId,
                        Observaciones = c.Observaciones,
                        EsSimuladoLocal = c.EsSimuladoLocal,
                        FechaCreacionUtc = c.FechaCreacionUtc,
                        FechaResolucionUtc = c.FechaResolucionUtc,
                        UsuarioResolucion = c.UsuarioResolucion
                    })
                    .ToList(),
                ErrorProcesamiento = orden.ErrorProcesamiento,
                Items = orden.Items.Where(i => !i.IsDeleted).Select(i =>
                {
                    var vinculo = vinculos.TryGetValue(i.ItemId, out var v) ? v : null;
                    var origen = vinculo is null
                        ? config.OrigenStock
                        : vinculo.OrigenStockOverride ?? config.OrigenStock;

                    var asignadas = ParsearUnidades(i.UnidadesAsignadas);

                    return new MercadoLibreOrderItemViewModel
                    {
                        Id = i.Id,
                        ItemId = i.ItemId,
                        VariationId = i.VariationId,
                        Titulo = i.Titulo,
                        Cantidad = i.Cantidad,
                        PrecioUnitario = i.PrecioUnitario,
                        SaleFee = i.SaleFee,
                        SellerSku = i.SellerSku,
                        ProductoId = i.ProductoId ?? vinculo?.ProductoId,
                        ProductoCodigo = vinculo?.Codigo,
                        ProductoNombre = vinculo?.Nombre,
                        RequiereUnidadFisica = vinculo is not null &&
                            (vinculo.RequiereNumeroSerie || origen != MercadoLibreOrigenStock.StockLogicoProducto),
                        UnidadesAsignadasDetalle = unidades
                            .Where(u => asignadas.Contains(u.Id))
                            .Select(u => new MercadoLibreUnidadOptionViewModel
                            {
                                Id = u.Id,
                                Codigo = u.CodigoInternoUnidad,
                                NumeroSerie = u.NumeroSerie
                            })
                            .ToList(),
                        UnidadesDisponibles = vinculo is null || !puedeAsignar
                            ? new List<MercadoLibreUnidadOptionViewModel>()
                            : unidades
                                .Where(u => u.ProductoId == vinculo.ProductoId && u.Estado == EstadoUnidad.EnStock)
                                .Select(u => new MercadoLibreUnidadOptionViewModel
                                {
                                    Id = u.Id,
                                    Codigo = u.CodigoInternoUnidad,
                                    NumeroSerie = u.NumeroSerie
                                })
                                .ToList()
                    };
                }).ToList()
            };
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static void AplicarShipment(MercadoLibreOrder orden, MeliShipmentDto envio)
        {
            if (envio.Id > 0)
                orden.ShipmentId = envio.Id;

            orden.ShipmentStatus = Truncar(envio.Status, 30);
            orden.ShipmentSubStatus = Truncar(envio.Substatus, 50);
            orden.TrackingNumber = Truncar(envio.TrackingNumber, 60);
            orden.TrackingMethod = Truncar(envio.TrackingMethod, 100);
            orden.ShippingMode = Truncar(envio.Mode, 50);
            orden.ShippingType = Truncar(envio.LogisticType, 50);
            orden.EstadoEnvioInterno = MapearEstadoEnvio(envio.Status, envio.Substatus);
            orden.FechaDespachoUtc = envio.StatusHistory?.DateShipped?.UtcDateTime ?? orden.FechaDespachoUtc;
            orden.FechaEntregadoUtc = envio.StatusHistory?.DateDelivered?.UtcDateTime
                ?? (orden.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.Entregado
                    ? envio.LastUpdated?.UtcDateTime ?? orden.FechaEntregadoUtc
                    : orden.FechaEntregadoUtc);
            orden.FechaUltimaActualizacionEnvioUtc = envio.LastUpdated?.UtcDateTime ?? DateTime.UtcNow;
            orden.RawShipmentJson = !string.IsNullOrWhiteSpace(envio.RawJson)
                ? envio.RawJson
                : JsonSerializer.Serialize(envio);

            var costoEnvio = envio.ShippingOption?.ListCost ?? envio.ShippingOption?.Cost;
            if (costoEnvio is > 0)
            {
                orden.MontoEnvio = costoEnvio.Value;
                orden.NetoEstimado = orden.TotalAmount - (orden.MontoComision ?? 0m) - orden.MontoEnvio.Value;
            }
        }

        private static MeliShipmentDto CrearShipmentSimulado(MercadoLibreOrder orden, string escenario)
        {
            var ahora = DateTimeOffset.UtcNow;
            var shipmentId = orden.ShipmentId
                ?? 8_000_000_000_000_000L + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + orden.Id;

            var normalizado = (escenario ?? string.Empty).Trim().ToLowerInvariant();
            var (status, substatus, tracking) = normalizado switch
            {
                "pendiente" or "pending" => ("pending", null, null),
                "listo" or "listo-para-despachar" or "ready_to_ship" => ("ready_to_ship", "printed", $"QA-{shipmentId}"),
                "despachado" or "shipped" => ("shipped", "in_transit", $"QA-{shipmentId}"),
                "entregado" or "delivered" => ("delivered", null, $"QA-{shipmentId}"),
                "cancelado" or "cancelled" or "canceled" => ("cancelled", "returning_to_sender", $"QA-{shipmentId}"),
                "demorado" or "delayed" => ("shipped", "delayed", $"QA-{shipmentId}"),
                _ => ("unknown", "unknown", $"QA-{shipmentId}")
            };

            var raw = JsonSerializer.Serialize(new
            {
                simulada = true,
                source = "ERP QA shipment local",
                orden.MeliOrderId,
                shipmentId,
                status,
                substatus,
                generadoUtc = ahora
            });

            return new MeliShipmentDto
            {
                Id = shipmentId,
                Status = status,
                Substatus = substatus,
                TrackingNumber = tracking,
                TrackingMethod = "QA local",
                Mode = "qa",
                LogisticType = "simulated",
                LastUpdated = ahora,
                StatusHistory = new MeliShipmentStatusHistoryDto
                {
                    DateShipped = status is "shipped" or "delivered" ? ahora.AddHours(-2) : null,
                    DateDelivered = status == "delivered" ? ahora : null
                },
                ShippingOption = orden.MontoEnvio is > 0
                    ? new MeliShippingOptionDto { ListCost = orden.MontoEnvio }
                    : null,
                RawJson = raw
            };
        }

        private static MercadoLibreShipmentEstadoInterno MapearEstadoEnvio(string? status, string? substatus)
        {
            var estado = (status ?? string.Empty).Trim().ToLowerInvariant();
            var subestado = (substatus ?? string.Empty).Trim().ToLowerInvariant();

            if (estado.Length == 0)
                return MercadoLibreShipmentEstadoInterno.Desconocido;

            if (estado.Contains("delay", StringComparison.OrdinalIgnoreCase)
                || subestado.Contains("delay", StringComparison.OrdinalIgnoreCase)
                || subestado.Contains("delayed", StringComparison.OrdinalIgnoreCase)
                || subestado.Contains("stale", StringComparison.OrdinalIgnoreCase)
                || estado == "not_delivered")
            {
                return MercadoLibreShipmentEstadoInterno.Demorado;
            }

            return estado switch
            {
                "pending" or "to_be_agreed" => MercadoLibreShipmentEstadoInterno.Pendiente,
                "ready_to_ship" or "handling" => MercadoLibreShipmentEstadoInterno.ListoParaDespachar,
                "shipped" when subestado is "in_transit" or "out_for_delivery" => MercadoLibreShipmentEstadoInterno.EnCamino,
                "shipped" => MercadoLibreShipmentEstadoInterno.Despachado,
                "delivered" => MercadoLibreShipmentEstadoInterno.Entregado,
                "cancelled" or "canceled" => MercadoLibreShipmentEstadoInterno.Cancelado,
                _ => MercadoLibreShipmentEstadoInterno.Desconocido
            };
        }

        private async Task AplicarUnidadesClaimAsync(
            MercadoLibreOrder orden,
            MercadoLibreClaimAccionStock accionStock,
            string motivo,
            string usuario,
            CancellationToken ct)
        {
            var unidadIds = orden.Items
                .Where(i => !i.IsDeleted)
                .SelectMany(i => ParsearUnidades(i.UnidadesAsignadas))
                .Distinct()
                .ToList();

            if (unidadIds.Count == 0)
                return;

            var unidades = await _context.ProductoUnidades
                .AsNoTracking()
                .Where(u => unidadIds.Contains(u.Id) && !u.IsDeleted)
                .Select(u => new { u.Id, u.Estado })
                .ToListAsync(ct);

            if (accionStock == MercadoLibreClaimAccionStock.ReingresarStock)
            {
                foreach (var unidad in unidades.Where(u => u.Estado == EstadoUnidad.Vendida))
                {
                    await _productoUnidadService.RevertirVentaAsync(
                        unidad.Id,
                        motivo,
                        usuario,
                        $"MercadoLibreClaim:{orden.MeliOrderId}");
                }

                return;
            }

            if (accionStock is not (MercadoLibreClaimAccionStock.Danado
                or MercadoLibreClaimAccionStock.Garantia
                or MercadoLibreClaimAccionStock.Merma))
            {
                return;
            }

            foreach (var unidad in unidades.Where(u => u.Estado == EstadoUnidad.Vendida))
            {
                await _productoUnidadService.MarcarDevueltaAsync(
                    unidad.Id,
                    motivo,
                    usuario);
            }
        }

        private static MercadoLibreDevolucionEstado MapearDevolucionEstado(MercadoLibreClaimAccionStock accionStock)
            => accionStock switch
            {
                MercadoLibreClaimAccionStock.ReingresarStock => MercadoLibreDevolucionEstado.StockReingresado,
                MercadoLibreClaimAccionStock.Danado => MercadoLibreDevolucionEstado.Danado,
                MercadoLibreClaimAccionStock.Garantia => MercadoLibreDevolucionEstado.Garantia,
                MercadoLibreClaimAccionStock.Merma => MercadoLibreDevolucionEstado.Merma,
                _ => MercadoLibreDevolucionEstado.NoReingresa
            };

        private async Task<long> GetSellerIdAsync(int accountId, CancellationToken ct)
        {
            return await _context.MercadoLibreAccounts
                .AsNoTracking()
                .Where(a => a.Id == accountId)
                .Select(a => a.MeliUserId)
                .FirstAsync(ct);
        }

        private async Task<long> GenerarMeliOrderIdSimuladoAsync(CancellationToken ct)
        {
            var candidate = 9_000_000_000_000_000L + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            while (await _context.MercadoLibreOrders.AnyAsync(o => o.MeliOrderId == candidate, ct))
                candidate++;

            return candidate;
        }

        private static bool EsOrdenSimuladaQa(string? rawJson)
            => rawJson?.Contains(RawQaMarker, StringComparison.OrdinalIgnoreCase) == true;

        private static bool EsOrdenSimuladaOperativa(string? rawJson)
            => rawJson?.Contains(RawOperativaLocalMarker, StringComparison.OrdinalIgnoreCase) == true;

        private static bool EsOrdenSimuladaLocal(string? rawJson)
            => EsOrdenSimuladaQa(rawJson) || EsOrdenSimuladaOperativa(rawJson);

        private static bool EsPaga(string status)
            => string.Equals(status, StatusPaid, StringComparison.OrdinalIgnoreCase);

        private static string? Truncar(string? valor, int max)
            => valor is null ? null : (valor.Length <= max ? valor : valor[..max]);
    }
}
