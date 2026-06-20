using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class MercadoLibreWebhookProcessor : IMercadoLibreWebhookProcessor
    {
        /// <summary>Reintentos máximos por evento antes de dejar de intentar.</summary>
        public const int MaxIntentos = 5;

        private static readonly Regex OrderIdRegex = new(@"/orders/(\d+)", RegexOptions.Compiled);
        private static readonly Regex ItemIdRegex = new(@"(MLA\d+)", RegexOptions.Compiled);
        private static readonly Regex ShipmentIdRegex = new(@"/shipments/(\d+)", RegexOptions.Compiled);
        private static readonly Regex QuestionIdRegex = new(@"/questions/(\d+)", RegexOptions.Compiled);
        private static readonly Regex MessageIdRegex = new(@"/messages/([^/?]+)", RegexOptions.Compiled);
        private static readonly Regex PackIdRegex = new(@"/(?:packs|orders)/(\d+)", RegexOptions.Compiled);

        private readonly AppDbContext _context;
        private readonly IMercadoLibreOrderService _orderService;
        private readonly IMercadoLibreSyncService _syncService;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibreQuestionService _questionService;
        private readonly IMercadoLibreMessageService _messageService;
        private readonly ILogger<MercadoLibreWebhookProcessor> _logger;

        public MercadoLibreWebhookProcessor(
            AppDbContext context,
            IMercadoLibreOrderService orderService,
            IMercadoLibreSyncService syncService,
            IMercadoLibreAuthService authService,
            IMercadoLibreApiClient apiClient,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibreQuestionService questionService,
            IMercadoLibreMessageService messageService,
            ILogger<MercadoLibreWebhookProcessor> logger)
        {
            _context = context;
            _orderService = orderService;
            _syncService = syncService;
            _authService = authService;
            _apiClient = apiClient;
            _configuracionService = configuracionService;
            _questionService = questionService;
            _messageService = messageService;
            _logger = logger;
        }

        public async Task<int> ProcesarPendientesAsync(int max = 50, CancellationToken ct = default)
        {
            var pendientes = await _context.MercadoLibreWebhookEvents
                .Where(e => !e.Procesado && e.IntentosProcesamiento < MaxIntentos)
                .OrderBy(e => e.RecibidoUtc)
                .Take(max)
                .ToListAsync(ct);

            if (pendientes.Count == 0)
                return 0;

            var config = await _configuracionService.GetAsync(ct);
            var procesados = 0;

            // Idempotencia por (topic, resource): N notificaciones repetidas del
            // mismo recurso se procesan UNA vez y se marcan todas.
            foreach (var grupo in pendientes.GroupBy(e => (e.Topic, e.Resource)))
            {
                var eventos = grupo.OrderBy(e => e.RecibidoUtc).ToList();
                var representante = eventos[^1]; // el más reciente manda

                try
                {
                    var resultado = await ProcesarEventoAsync(representante, config, ct);

                    foreach (var evento in eventos)
                    {
                        evento.Procesado = true;
                        evento.ProcesadoUtc = DateTime.UtcNow;
                        evento.ErrorProcesamiento = resultado;
                        procesados++;
                    }
                }
                catch (Exception ex)
                {
                    foreach (var evento in eventos)
                    {
                        evento.IntentosProcesamiento++;
                        evento.ErrorProcesamiento = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;

                        // Tras agotar reintentos, marcar procesado con error para
                        // no bloquear la cola (queda auditado).
                        if (evento.IntentosProcesamiento >= MaxIntentos)
                        {
                            evento.Procesado = true;
                            evento.ProcesadoUtc = DateTime.UtcNow;
                        }
                    }

                    _logger.LogError(ex,
                        "Error procesando webhook ML {Topic} {Resource} (intento {Intento}/{Max})",
                        grupo.Key.Topic, grupo.Key.Resource, eventos[0].IntentosProcesamiento, MaxIntentos);
                }

                await _context.SaveChangesAsync(ct);
            }

            return procesados;
        }

        /// <summary>
        /// Devuelve null si el evento se procesó con acción, o una nota si se
        /// marcó procesado sin acción (config apagada, tópico fuera del MVP, etc.).
        /// </summary>
        private async Task<string?> ProcesarEventoAsync(
            MercadoLibreWebhookEvent evento, MercadoLibreConfiguracion config, CancellationToken ct)
        {
            switch (evento.Topic.ToLowerInvariant())
            {
                case "orders_v2":
                case "orders":
                {
                    if (!config.ImportacionAutomaticaOrdenes)
                        return "Importación automática de órdenes desactivada en configuración.";

                    var match = OrderIdRegex.Match(evento.Resource);
                    if (!match.Success)
                        return $"Resource sin id de orden reconocible: {evento.Resource}";

                    var accountId = await ResolverAccountIdAsync(evento.MeliUserId, config, ct);
                    if (accountId is null)
                        return "Sin cuenta ML activa que coincida con el user_id del evento.";

                    await _orderService.ImportarOrdenAsync(accountId.Value, long.Parse(match.Groups[1].Value), ct);
                    return null;
                }

                case "items":
                {
                    var match = ItemIdRegex.Match(evento.Resource);
                    if (!match.Success)
                        return $"Resource sin item id reconocible: {evento.Resource}";

                    return await RefrescarListingAsync(match.Groups[1].Value, config, ct);
                }

                case "shipments":
                {
                    var match = ShipmentIdRegex.Match(evento.Resource);
                    if (!match.Success)
                        return $"Resource sin shipment id reconocible: {evento.Resource}";

                    var shipmentId = long.Parse(match.Groups[1].Value);

                    var orden = await _context.MercadoLibreOrders
                        .AsNoTracking()
                        .FirstOrDefaultAsync(o => o.ShipmentId == shipmentId, ct);

                    if (orden is null)
                        return $"Shipment {shipmentId} sin orden conocida (se procesará cuando llegue la orden).";

                    await _orderService.ActualizarEnvioAsync(orden.Id, ct);
                    return null;
                }

                case "claims":
                {
                    // Fase H parcial: marcar la orden asociada como pendiente de revisión
                    // si se puede deducir; la gestión completa de claims es deuda declarada.
                    return "Claim almacenado. Gestión de claims/reclamos: revisar manualmente en Mercado Libre.";
                }

                case "questions":
                {
                    var match = QuestionIdRegex.Match(evento.Resource);
                    if (!match.Success)
                        return $"Resource sin id de pregunta reconocible: {evento.Resource}";

                    var accountId = await ResolverAccountIdAsync(evento.MeliUserId, config, ct);
                    if (accountId is null)
                        return "Sin cuenta ML activa que coincida con el user_id del evento.";

                    await _questionService.RegistrarDesdeWebhookAsync(
                        long.Parse(match.Groups[1].Value), accountId.Value, evento.Resource, config, ct);
                    return null;
                }

                case "messages":
                {
                    var match = MessageIdRegex.Match(evento.Resource);
                    if (!match.Success)
                        return $"Resource sin id de mensaje reconocible: {evento.Resource}";

                    var accountId = await ResolverAccountIdAsync(evento.MeliUserId, config, ct);
                    if (accountId is null)
                        return "Sin cuenta ML activa que coincida con el user_id del evento.";

                    var packMatch = PackIdRegex.Match(evento.Resource);
                    long? meliOrderId = packMatch.Success ? long.Parse(packMatch.Groups[1].Value) : null;

                    await _messageService.RegistrarDesdeWebhookAsync(
                        match.Groups[1].Value, accountId.Value, evento.Resource, meliOrderId, config, ct);
                    return null;
                }

                default:
                    return $"Tópico '{evento.Topic}' sin handler; evento almacenado.";
            }
        }

        /// <summary>
        /// items webhook: refresca el espejo local desde ML y, si la publicación
        /// está vinculada y la config lo permite, re-empuja los valores del ERP
        /// (el ERP es la fuente de verdad). El push respeta ModoSimulacion.
        /// </summary>
        private async Task<string?> RefrescarListingAsync(
            string itemId, MercadoLibreConfiguracion config, CancellationToken ct)
        {
            var listing = await _context.MercadoLibreListings
                .Include(l => l.Variaciones)
                .FirstOrDefaultAsync(l => l.ItemId == itemId, ct);

            if (listing is null)
                return $"Publicación {itemId} no importada en el ERP; usar Importar publicaciones.";

            var token = await _authService.GetValidAccessTokenAsync(listing.AccountId, ct);
            var items = await _apiClient.GetItemsAsync(token, new[] { itemId }, ct);
            var dto = items.FirstOrDefault();

            if (dto is null)
                return $"ML no devolvió datos para {itemId}.";

            listing.Precio = dto.Price ?? listing.Precio;
            listing.AvailableQuantity = dto.AvailableQuantity ?? listing.AvailableQuantity;
            listing.SoldQuantity = dto.SoldQuantity ?? listing.SoldQuantity;
            listing.Status = dto.Status ?? listing.Status;
            listing.SubStatus = dto.SubStatus.Count > 0 ? string.Join(",", dto.SubStatus) : null;
            listing.LastSyncUtc = DateTime.UtcNow;

            foreach (var variacionDto in dto.Variations)
            {
                var variacion = listing.Variaciones.FirstOrDefault(v => v.VariationId == variacionDto.Id && !v.IsDeleted);
                if (variacion is not null)
                {
                    variacion.Precio = variacionDto.Price ?? variacion.Precio;
                    variacion.AvailableQuantity = variacionDto.AvailableQuantity ?? variacion.AvailableQuantity;
                    variacion.SoldQuantity = variacionDto.SoldQuantity ?? variacion.SoldQuantity;
                }
            }

            await _context.SaveChangesAsync(ct);

            // ERP fuente de verdad: si la sync automática está habilitada y la
            // publicación está vinculada, re-alinear ML con el ERP.
            if (listing.ProductoId.HasValue && (config.SyncAutomaticaStock || config.SyncAutomaticaPrecio))
            {
                var tipo = config.SyncAutomaticaStock && config.SyncAutomaticaPrecio
                    ? MercadoLibreSyncTipo.StockYPrecio
                    : config.SyncAutomaticaStock ? MercadoLibreSyncTipo.Stock : MercadoLibreSyncTipo.Precio;

                // confirmarReal=true: la decisión humana ya se tomó al habilitar la
                // automatización en la configuración. ModoSimulacion sigue mandando.
                await _syncService.AplicarAsync(
                    new[] { listing.Id }, tipo, confirmarReal: true, usuario: "Sistema (auto-ML)", ct);
            }

            return null;
        }

        private async Task<int?> ResolverAccountIdAsync(
            long? meliUserId, MercadoLibreConfiguracion config, CancellationToken ct)
        {
            if (meliUserId.HasValue)
            {
                var porUserId = await _context.MercadoLibreAccounts
                    .AsNoTracking()
                    .Where(a => a.MeliUserId == meliUserId.Value && a.Activa)
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(ct);

                if (porUserId is not null)
                    return porUserId;
            }

            return config.AccountId;
        }
    }
}
