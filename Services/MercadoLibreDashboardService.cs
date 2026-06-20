using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Dashboard operativo ML: agrega KPIs, alertas y listas recientes.
    /// Solo lectura (AsNoTracking), sin llamadas a la API ni efectos sobre
    /// stock/caja/ventas. Ante datos faltantes devuelve ceros, nunca rompe.
    /// </summary>
    public class MercadoLibreDashboardService : IMercadoLibreDashboardService
    {
        // Umbral de reintentos de webhook a partir del cual se consideran agotados.
        private const int ReintentosAgotadosUmbral = 5;

        // Tamaño de las listas "recientes" del dashboard.
        private const int TopRecientes = 8;

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly ILogger<MercadoLibreDashboardService> _logger;

        public MercadoLibreDashboardService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreConfiguracionService configuracionService,
            ILogger<MercadoLibreDashboardService> logger)
        {
            _contextFactory = contextFactory;
            _configuracionService = configuracionService;
            _logger = logger;
        }

        public async Task<MercadoLibreDashboardViewModel> GetDashboardAsync(CancellationToken ct = default)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

            var ahora = DateTime.UtcNow;
            var vm = new MercadoLibreDashboardViewModel { GeneradoUtc = ahora };
            var config = await _configuracionService.GetAsync(ct);

            await CargarConexionAsync(ctx, config, vm, ct);
            await CargarPublicacionesAsync(ctx, vm, ct);
            await CargarStockPrecioAsync(ctx, vm, ct);
            await CargarOrdenesAsync(ctx, vm, ahora, ct);
            await CargarEnviosReclamosAsync(ctx, vm, ct);
            await CargarAumentosAsync(ctx, vm, ct);
            await CargarPreguntasMensajesAsync(ctx, vm, ct);
            await CargarWebhooksAsync(ctx, vm, ct);

            ConstruirAlertas(vm);

            return vm;
        }

        // ------------------------------------------------------------------
        // 1. Conexión + simulación
        // ------------------------------------------------------------------
        private async Task CargarConexionAsync(
            AppDbContext ctx, MercadoLibreConfiguracion config, MercadoLibreDashboardViewModel vm, CancellationToken ct)
        {
            vm.Conexion.ModoSimulacion = config.ModoSimulacion;
            vm.Conexion.PermitirPublicacionDesdeErp = config.PermitirPublicacionDesdeErp;
            vm.Conexion.ModuloConfigurado = true;

            // Cuenta efectiva: la configurada, o la primera activa.
            var cuenta = await ctx.MercadoLibreAccounts
                .AsNoTracking()
                .Where(a => a.Activa && (config.AccountId == null || a.Id == config.AccountId))
                .OrderByDescending(a => a.Id == config.AccountId)
                .FirstOrDefaultAsync(ct);

            if (cuenta is not null)
            {
                vm.Conexion.CuentaConectada = true;
                vm.Conexion.Nickname = cuenta.Nickname;
                vm.Conexion.MeliUserId = cuenta.MeliUserId;
                vm.Conexion.SiteId = cuenta.SiteId;
                vm.Conexion.UltimaImportacionUtc = cuenta.UltimaImportacionListingsUtc;
                vm.Conexion.UltimaPruebaConexionUtc = cuenta.UltimaPruebaConexionUtc;
                vm.Conexion.UltimaPruebaConexionOk = cuenta.UltimaPruebaConexionOk;
                vm.Conexion.AccessTokenExpiraUtc = cuenta.AccessTokenExpiresAtUtc;
            }

            // Último sync (push) y último error desde el log.
            var ultimoSync = await ctx.MercadoLibreSyncLogs
                .AsNoTracking()
                .Where(l => l.Operacion.StartsWith("Push"))
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => (DateTime?)l.CreatedAt)
                .FirstOrDefaultAsync(ct);
            vm.Conexion.UltimoSyncUtc = ultimoSync;

            var ultimoError = await ctx.MercadoLibreSyncLogs
                .AsNoTracking()
                .Where(l => !l.Exito)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new { l.CreatedAt, l.Detalle })
                .FirstOrDefaultAsync(ct);
            if (ultimoError is not null)
            {
                vm.Conexion.UltimoErrorUtc = ultimoError.CreatedAt;
                vm.Conexion.UltimoErrorDetalle = ultimoError.Detalle;
            }
        }

        // ------------------------------------------------------------------
        // 2. Publicaciones
        // ------------------------------------------------------------------
        private async Task CargarPublicacionesAsync(
            AppDbContext ctx, MercadoLibreDashboardViewModel vm, CancellationToken ct)
        {
            var listings = await ctx.MercadoLibreListings
                .AsNoTracking()
                .Select(l => new { l.Status, l.TieneVariaciones, l.ProductoId, l.AvailableQuantity })
                .ToListAsync(ct);

            var p = vm.Publicaciones;
            p.Total = listings.Count;
            p.Activas = listings.Count(l => l.Status == "active");
            p.Pausadas = listings.Count(l => l.Status == "paused");
            p.Finalizadas = listings.Count(l => l.Status == "closed" || l.Status == "finished");
            p.OtroEstado = p.Total - p.Activas - p.Pausadas - p.Finalizadas;
            p.ConVariaciones = listings.Count(l => l.TieneVariaciones);
            p.SinVincular = listings.Count(l => l.ProductoId == null);
            p.SinStock = listings.Count(l => l.AvailableQuantity <= 0);

            // Publicaciones con error de sync: log sin éxito con ListingId.
            var idsConError = await ctx.MercadoLibreSyncLogs
                .AsNoTracking()
                .Where(l => !l.Exito && l.ListingId.HasValue)
                .Select(l => l.ListingId!.Value)
                .Distinct()
                .CountAsync(ct);
            p.ConError = idsConError;

            // Variaciones sin resolución posible: sin producto propio y sin
            // producto en la publicación (no hay fallback) → bloquean sync.
            var variaciones = await ctx.MercadoLibreListingVariations
                .AsNoTracking()
                .Select(v => new { v.ProductoId, ListingProductoId = v.Listing.ProductoId })
                .ToListAsync(ct);
            p.VariacionesSinVincular = variaciones.Count(v => v.ProductoId == null && v.ListingProductoId == null);
        }

        // ------------------------------------------------------------------
        // 3. Stock y precio (diferencias ERP vs ML)
        // ------------------------------------------------------------------
        private async Task CargarStockPrecioAsync(
            AppDbContext ctx, MercadoLibreDashboardViewModel vm, CancellationToken ct)
        {
            // Solo publicaciones vinculadas: comparamos contra el stock lógico y
            // el precio de venta del producto. El detalle por origen vive en el
            // preview de sync; acá es un indicador.
            var vinculadas = await ctx.MercadoLibreListings
                .AsNoTracking()
                .Where(l => l.ProductoId != null && l.Producto != null)
                .Select(l => new
                {
                    l.AvailableQuantity,
                    l.Precio,
                    StockErp = l.Producto!.StockActual,
                    PrecioErp = l.Producto.PrecioVenta
                })
                .ToListAsync(ct);

            var s = vm.StockPrecio;
            s.PublicacionesVinculadas = vinculadas.Count;

            foreach (var v in vinculadas)
            {
                var stockErp = (int)Math.Floor(v.StockErp);
                if (v.AvailableQuantity != stockErp)
                {
                    s.ConDiferenciaStock++;
                    if (v.AvailableQuantity > stockErp) s.StockMlMayorErp++;
                    else s.StockErpMayorMl++;
                }

                if (Math.Abs(v.Precio - v.PrecioErp) >= 0.01m)
                    s.ConDiferenciaPrecio++;
            }

            s.UltimosSyncOk = await ctx.MercadoLibreSyncLogs
                .AsNoTracking()
                .CountAsync(l => l.Exito && l.Operacion.StartsWith("Push"), ct);

            s.UltimosSyncFallidos = await ctx.MercadoLibreSyncLogs
                .AsNoTracking()
                .CountAsync(l => !l.Exito, ct);
        }

        // ------------------------------------------------------------------
        // 4. Órdenes / ventas / liquidaciones
        // ------------------------------------------------------------------
        private async Task CargarOrdenesAsync(
            AppDbContext ctx, MercadoLibreDashboardViewModel vm, DateTime ahora, CancellationToken ct)
        {
            var ordenes = await ctx.MercadoLibreOrders
                .AsNoTracking()
                .Select(o => new
                {
                    o.EstadoInterno,
                    o.EstadoEnvioInterno,
                    o.TotalAmount,
                    o.NetoEstimado,
                    o.NetoReal,
                    o.FechaCreacionUtc,
                    o.TrackingNumber
                })
                .ToListAsync(ct);

            var o = vm.Ordenes;
            o.Total = ordenes.Count;
            o.Pendientes = ordenes.Count(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.Importada);
            o.ConVentaCreada = ordenes.Count(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.VentaCreada);
            o.Liquidadas = ordenes.Count(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada);
            o.ConError = ordenes.Count(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.Error);
            o.Ignoradas = ordenes.Count(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.Ignorada);
            o.PendientesUnidad = ordenes.Count(x =>
                x.EstadoInterno == MercadoLibreOrderEstadoInterno.PendienteAsignarUnidad ||
                x.EstadoInterno == MercadoLibreOrderEstadoInterno.PendienteVinculacion);

            // "Venta creada" = la orden alcanzó el estado de venta interna
            // (VentaCreada o Liquidada). Es la verdad del ciclo de vida ML.
            var conVenta = ordenes.Where(x =>
                x.EstadoInterno == MercadoLibreOrderEstadoInterno.VentaCreada ||
                x.EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada).ToList();
            o.VentasCreadas = conVenta.Count;
            o.MontoBrutoTotal = conVenta.Sum(x => x.TotalAmount);
            o.Ventas24h = conVenta.Count(x => x.FechaCreacionUtc >= ahora.AddDays(-1));
            o.Ventas7d = conVenta.Count(x => x.FechaCreacionUtc >= ahora.AddDays(-7));
            o.Ventas30d = conVenta.Count(x => x.FechaCreacionUtc >= ahora.AddDays(-30));

            // Liquidaciones
            var liq = vm.Liquidaciones;
            liq.Pendientes = ordenes.Count(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.VentaCreada);
            liq.Liquidadas = ordenes.Count(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada);
            liq.NetoEstimadoPendiente = ordenes
                .Where(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.VentaCreada && x.NetoEstimado.HasValue)
                .Sum(x => x.NetoEstimado!.Value);
            liq.NetoRealAcreditado = ordenes
                .Where(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada && x.NetoReal.HasValue)
                .Sum(x => x.NetoReal!.Value);
            liq.DiferenciaEstimadoVsReal = ordenes
                .Where(x => x.EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada && x.NetoReal.HasValue && x.NetoEstimado.HasValue)
                .Sum(x => x.NetoReal!.Value - x.NetoEstimado!.Value);

            // Envíos (estado logístico)
            var e = vm.Envios;
            e.Pendientes = ordenes.Count(x => x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.Pendiente);
            e.ListosParaDespachar = ordenes.Count(x => x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.ListoParaDespachar);
            e.Despachados = ordenes.Count(x => x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.Despachado);
            e.EnCamino = ordenes.Count(x => x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.EnCamino);
            e.Entregados = ordenes.Count(x => x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.Entregado);
            e.Cancelados = ordenes.Count(x => x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.Cancelado);
            e.Demorados = ordenes.Count(x => x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.Demorado);
            e.ConTracking = ordenes.Count(x => !string.IsNullOrWhiteSpace(x.TrackingNumber));
            e.SinTracking = ordenes.Count(x =>
                string.IsNullOrWhiteSpace(x.TrackingNumber) &&
                (x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.Despachado ||
                 x.EstadoEnvioInterno == MercadoLibreShipmentEstadoInterno.EnCamino));

            // Últimas órdenes (top N)
            vm.UltimasOrdenes = await ctx.MercadoLibreOrders
                .AsNoTracking()
                .OrderByDescending(x => x.FechaCreacionUtc)
                .Take(TopRecientes)
                .Select(x => new MercadoLibreOrdenResumenRow
                {
                    Id = x.Id,
                    MeliOrderId = x.MeliOrderId,
                    EstadoInterno = x.EstadoInterno.ToString(),
                    EstadoEnvio = x.EstadoEnvioInterno.ToString(),
                    TotalAmount = x.TotalAmount,
                    CurrencyId = x.CurrencyId,
                    FechaCreacionUtc = x.FechaCreacionUtc,
                    TieneVenta = x.EstadoInterno == MercadoLibreOrderEstadoInterno.VentaCreada ||
                                 x.EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada
                })
                .ToListAsync(ct);
        }

        // ------------------------------------------------------------------
        // 5. Reclamos / devoluciones / garantías
        // ------------------------------------------------------------------
        private async Task CargarEnviosReclamosAsync(
            AppDbContext ctx, MercadoLibreDashboardViewModel vm, CancellationToken ct)
        {
            var claims = await ctx.MercadoLibreClaims
                .AsNoTracking()
                .Select(c => new { c.Estado, c.Tipo, c.AccionStock })
                .ToListAsync(ct);

            var r = vm.Reclamos;
            r.PendientesRevision = claims.Count(c => c.Estado == MercadoLibreClaimEstado.PendienteRevision);
            r.Resueltos = claims.Count(c => c.Estado == MercadoLibreClaimEstado.Resuelto);
            r.Abiertos = claims.Count(c =>
                c.Estado != MercadoLibreClaimEstado.Resuelto &&
                c.Estado != MercadoLibreClaimEstado.Rechazado);
            r.ReingresosStock = claims.Count(c => c.AccionStock == MercadoLibreClaimAccionStock.ReingresarStock);
            r.Danados = claims.Count(c => c.AccionStock == MercadoLibreClaimAccionStock.Danado);
            r.Mermas = claims.Count(c => c.AccionStock == MercadoLibreClaimAccionStock.Merma);
            r.Garantias = claims.Count(c =>
                c.Tipo == MercadoLibreClaimTipo.Garantia ||
                c.AccionStock == MercadoLibreClaimAccionStock.Garantia);
        }

        // ------------------------------------------------------------------
        // 6. Aumentos masivos
        // ------------------------------------------------------------------
        private async Task CargarAumentosAsync(
            AppDbContext ctx, MercadoLibreDashboardViewModel vm, CancellationToken ct)
        {
            var batches = await ctx.MercadoLibrePriceBatches
                .AsNoTracking()
                .Select(b => new { b.Estado })
                .ToListAsync(ct);

            var a = vm.Aumentos;
            a.Total = batches.Count;
            a.Simulados = batches.Count(b => b.Estado == MercadoLibrePriceBatchEstado.Simulado);
            a.Aplicados = batches.Count(b =>
                b.Estado == MercadoLibrePriceBatchEstado.Aplicado ||
                b.Estado == MercadoLibrePriceBatchEstado.AplicadoParcial);
            a.Revertidos = batches.Count(b => b.Estado == MercadoLibrePriceBatchEstado.Revertido);
            a.Cancelados = batches.Count(b => b.Estado == MercadoLibrePriceBatchEstado.Cancelado);

            vm.UltimosAumentos = await ctx.MercadoLibrePriceBatches
                .AsNoTracking()
                .OrderByDescending(b => b.FechaSolicitud)
                .Take(TopRecientes)
                .Select(b => new MercadoLibreAumentoRow
                {
                    Id = b.Id,
                    Nombre = b.Nombre,
                    Estado = b.Estado.ToString(),
                    CantidadPublicaciones = b.CantidadPublicaciones,
                    AplicadoEnSimulacion = b.AplicadoEnSimulacion,
                    FechaSolicitud = b.FechaSolicitud
                })
                .ToListAsync(ct);
        }

        // ------------------------------------------------------------------
        // 6b. Preguntas / mensajes (Fase 16)
        // ------------------------------------------------------------------
        private async Task CargarPreguntasMensajesAsync(
            AppDbContext ctx, MercadoLibreDashboardViewModel vm, CancellationToken ct)
        {
            var preguntas = await ctx.MercadoLibreQuestions
                .AsNoTracking()
                .Select(q => new { q.Estado, q.EsSimulada, q.ListingId })
                .ToListAsync(ct);

            var p = vm.Preguntas;
            p.Total = preguntas.Count;
            p.Pendientes = preguntas.Count(x => x.Estado == MercadoLibreQuestionEstado.Pendiente);
            p.Respondidas = preguntas.Count(x => x.Estado == MercadoLibreQuestionEstado.Respondida);
            p.ConError = preguntas.Count(x => x.Estado == MercadoLibreQuestionEstado.Error);
            p.Simuladas = preguntas.Count(x => x.EsSimulada);
            p.SinPublicacion = preguntas.Count(x => x.ListingId == null);

            var mensajes = await ctx.MercadoLibreMessages
                .AsNoTracking()
                .Select(m => new { m.Estado, m.Direccion, m.EsSimulado, m.OrderId })
                .ToListAsync(ct);

            var m = vm.Mensajes;
            m.Total = mensajes.Count;
            m.RecibidosPendientes = mensajes.Count(x =>
                x.Direccion == MercadoLibreMessageDireccion.Entrante &&
                x.Estado == MercadoLibreMessageEstado.Recibido);
            m.Enviados = mensajes.Count(x => x.Estado == MercadoLibreMessageEstado.Enviado);
            m.ConError = mensajes.Count(x => x.Estado == MercadoLibreMessageEstado.Error);
            m.Simulados = mensajes.Count(x => x.EsSimulado);
            m.SinOrden = mensajes.Count(x => x.OrderId == null);
        }

        // ------------------------------------------------------------------
        // 7. Webhooks / background
        // ------------------------------------------------------------------
        private async Task CargarWebhooksAsync(
            AppDbContext ctx, MercadoLibreDashboardViewModel vm, CancellationToken ct)
        {
            var eventos = await ctx.MercadoLibreWebhookEvents
                .AsNoTracking()
                .Select(w => new { w.Topic, w.Procesado, w.ErrorProcesamiento, w.IntentosProcesamiento })
                .ToListAsync(ct);

            var w = vm.Webhooks;
            w.Recibidos = eventos.Count;
            w.Procesados = eventos.Count(x => x.Procesado);
            w.ConError = eventos.Count(x => !string.IsNullOrEmpty(x.ErrorProcesamiento));
            w.Pendientes = eventos.Count(x => !x.Procesado && string.IsNullOrEmpty(x.ErrorProcesamiento));
            w.ReintentosAgotados = eventos.Count(x => x.IntentosProcesamiento >= ReintentosAgotadosUmbral);
            w.TopicItems = eventos.Count(x => x.Topic == "items");
            w.TopicOrders = eventos.Count(x => x.Topic == "orders_v2" || x.Topic == "orders");
            w.TopicShipments = eventos.Count(x => x.Topic == "shipments");
            w.TopicClaims = eventos.Count(x => x.Topic == "claims");
            w.TopicPreguntas = eventos.Count(x => x.Topic == "questions" || x.Topic == "messages");

            vm.UltimosWebhooks = await ctx.MercadoLibreWebhookEvents
                .AsNoTracking()
                .OrderByDescending(x => x.RecibidoUtc)
                .Take(TopRecientes)
                .Select(x => new MercadoLibreWebhookRow
                {
                    Topic = x.Topic,
                    Procesado = x.Procesado,
                    Intentos = x.IntentosProcesamiento,
                    RecibidoUtc = x.RecibidoUtc,
                    Error = x.ErrorProcesamiento
                })
                .ToListAsync(ct);

            vm.UltimosLogs = await ctx.MercadoLibreSyncLogs
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .Take(TopRecientes)
                .Select(l => new MercadoLibreLogRow
                {
                    Operacion = l.Operacion,
                    Exito = l.Exito,
                    Detalle = l.Detalle,
                    Fecha = l.CreatedAt
                })
                .ToListAsync(ct);
        }

        // ------------------------------------------------------------------
        // 8. Alertas operativas priorizadas
        // ------------------------------------------------------------------
        private static void ConstruirAlertas(MercadoLibreDashboardViewModel vm)
        {
            var alertas = vm.Alertas;

            if (!vm.Conexion.ModoSimulacion)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Sincronización real activa",
                    "Las sincronizaciones automáticas de stock/precio y respuestas impactan Mercado Libre real.",
                    0, "Configuracion", null, "Revisar configuración");
            }

            if (vm.Conexion.ModuloConfigurado && !vm.Conexion.CuentaConectada)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Sin cuenta conectada",
                    "No hay una cuenta de Mercado Libre activa. Conectá una para operar.",
                    0, "Index", null, "Ir a conexión");
            }

            if (vm.Publicaciones.SinVincular > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Publicaciones sin vincular",
                    "Hay publicaciones sin producto del ERP: no sincronizan stock/precio ni generan ventas.",
                    vm.Publicaciones.SinVincular, "Listings", "sin-vincular", "Ver publicaciones");
            }

            if (vm.Publicaciones.VariacionesSinVincular > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Variaciones sin vínculo/origen",
                    "Variaciones sin producto propio ni fallback de publicación: bloquean el sync.",
                    vm.Publicaciones.VariacionesSinVincular, "Listings", "con-variaciones", "Ver variaciones");
            }

            if (vm.StockPrecio.StockMlMayorErp > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Stock ML mayor que ERP",
                    "Publicaciones con stock en ML superior al del ERP: riesgo de sobreventa.",
                    vm.StockPrecio.StockMlMayorErp, "Listings", "vinculadas", "Revisar stock");
            }

            if (vm.Ordenes.ConError > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Error,
                    "Órdenes con error",
                    "Órdenes que fallaron al procesarse contra el ERP.",
                    vm.Ordenes.ConError, "Ordenes", null, "Ver órdenes");
            }

            if (vm.Ordenes.PendientesUnidad > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Órdenes pendientes de producto/unidad",
                    "Órdenes que esperan vinculación de producto o asignación de unidades físicas.",
                    vm.Ordenes.PendientesUnidad, "Ordenes", "pendientes", "Resolver órdenes");
            }

            if (vm.Liquidaciones.Pendientes > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Info,
                    "Liquidaciones pendientes",
                    "Ventas ML con stock descontado pendientes de liquidar en caja.",
                    vm.Liquidaciones.Pendientes, "Ordenes", "venta-creada", "Liquidar");
            }

            if (vm.Envios.Demorados > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Envíos demorados",
                    "Envíos marcados como demorados por Mercado Libre.",
                    vm.Envios.Demorados, "Ordenes", null, "Ver envíos");
            }

            if (vm.Reclamos.Abiertos > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Reclamos/devoluciones pendientes",
                    "Casos abiertos que requieren decisión manual (stock y económica).",
                    vm.Reclamos.Abiertos, "Ordenes", "devoluciones", "Ver reclamos");
            }

            if (vm.Preguntas.Pendientes > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Preguntas pendientes",
                    "Preguntas preventa sin responder. La respuesta es siempre manual.",
                    vm.Preguntas.Pendientes, "Preguntas", "pendientes", "Responder preguntas");
            }

            if (vm.Preguntas.ConError > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Error,
                    "Preguntas con error",
                    "Preguntas que fallaron al procesarse o al responderse.",
                    vm.Preguntas.ConError, "Preguntas", "con-error", "Ver preguntas");
            }

            if (vm.Mensajes.RecibidosPendientes > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Warning,
                    "Mensajes sin responder",
                    "Mensajes postventa recibidos pendientes de respuesta manual.",
                    vm.Mensajes.RecibidosPendientes, "Mensajes", "recibidos", "Responder mensajes");
            }

            if (vm.Mensajes.ConError > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Error,
                    "Mensajes con error",
                    "Mensajes que fallaron al procesarse o al enviarse.",
                    vm.Mensajes.ConError, "Mensajes", "con-error", "Ver mensajes");
            }

            if (vm.Webhooks.ConError > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Error,
                    "Webhooks con error",
                    "Notificaciones de Mercado Libre que fallaron al procesarse.",
                    vm.Webhooks.ConError, null, null, "Sin pantalla dedicada");
            }

            if (vm.StockPrecio.UltimosSyncFallidos > 0)
            {
                Agregar(alertas, MercadoLibreAlertaSeveridad.Error,
                    "Sincronizaciones fallidas",
                    "Operaciones de sincronización registradas con error.",
                    vm.StockPrecio.UltimosSyncFallidos, "Listings", "con-error", "Ver con error");
            }

            // Prioridad: severidad alta primero; dentro de la misma, mayor contador arriba.
            foreach (var a in alertas)
                a.Prioridad = (MercadoLibreAlertaSeveridad.Critical - a.Severidad) * 1000 - Math.Min(a.Contador, 999);

            vm.Alertas = alertas.OrderBy(a => a.Prioridad).ToList();
        }

        private static void Agregar(
            List<MercadoLibreDashboardAlerta> destino,
            MercadoLibreAlertaSeveridad severidad,
            string titulo, string descripcion, int contador,
            string? linkAccion, string? linkFiltro, string linkTexto)
        {
            destino.Add(new MercadoLibreDashboardAlerta
            {
                Severidad = severidad,
                Titulo = titulo,
                Descripcion = descripcion,
                Contador = contador,
                LinkAccion = linkAccion,
                LinkFiltro = linkFiltro,
                LinkTexto = linkTexto
            });
        }
    }
}
