using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación de la gestión de mensajes postventa ML.
    /// Regla de oro: en simulación nunca se invoca <see cref="IMercadoLibreApiClient"/>.
    /// </summary>
    public class MercadoLibreMessageService : IMercadoLibreMessageService
    {
        private readonly AppDbContext _context;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly ILogger<MercadoLibreMessageService> _logger;

        public MercadoLibreMessageService(
            AppDbContext context,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibreAuthService authService,
            IMercadoLibreApiClient apiClient,
            ILogger<MercadoLibreMessageService> logger)
        {
            _context = context;
            _configuracionService = configuracionService;
            _authService = authService;
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<List<MercadoLibreMessage>> GetMensajesAsync(string? filtro = null, CancellationToken ct = default)
        {
            var query = _context.MercadoLibreMessages
                .AsNoTracking()
                .Include(m => m.Order)
                .AsQueryable();

            query = filtro switch
            {
                MercadoLibreMessageFiltro.Recibidos => query.Where(m => m.Direccion == MercadoLibreMessageDireccion.Entrante),
                MercadoLibreMessageFiltro.Enviados => query.Where(m => m.Direccion == MercadoLibreMessageDireccion.Saliente),
                MercadoLibreMessageFiltro.Simulados => query.Where(m => m.EsSimulado),
                MercadoLibreMessageFiltro.ConError => query.Where(m => m.Estado == MercadoLibreMessageEstado.Error),
                MercadoLibreMessageFiltro.SinVincular => query.Where(m => m.OrderId == null),
                _ => query
            };

            return await query
                .OrderByDescending(m => m.FechaMensajeUtc)
                .Take(200)
                .ToListAsync(ct);
        }

        public Task<List<MercadoLibreMessage>> GetMensajesPorOrdenAsync(int orderId, CancellationToken ct = default)
            => _context.MercadoLibreMessages
                .AsNoTracking()
                .Where(m => m.OrderId == orderId)
                .OrderBy(m => m.FechaMensajeUtc)
                .Take(100)
                .ToListAsync(ct);

        public Task<MercadoLibreMessage?> GetMensajeAsync(int id, CancellationToken ct = default)
            => _context.MercadoLibreMessages
                .AsNoTracking()
                .Include(m => m.Order)
                .FirstOrDefaultAsync(m => m.Id == id, ct);

        public async Task<MercadoLibreMessageResult> SimularMensajeAsync(
            int orderId, string texto, string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            if (!config.ModoSimulacion && !permitirPorDevelopment)
            {
                return new MercadoLibreMessageResult(false, null,
                    "La simulación de mensajes solo está habilitada en Development o con ModoSimulacion=true.");
            }

            if (string.IsNullOrWhiteSpace(texto))
                return new MercadoLibreMessageResult(false, null, "El texto del mensaje es obligatorio.");

            var orden = await _context.MercadoLibreOrders
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (orden is null)
                return new MercadoLibreMessageResult(false, null, $"Orden {orderId} no encontrada.");

            var textoNormalizado = Truncar(texto.Trim(), 2000);

            var mensaje = new MercadoLibreMessage
            {
                AccountId = orden.AccountId,
                MessageId = GenerarIdSimulado(orden.MeliOrderId),
                OrderId = orden.Id,
                MeliOrderId = orden.MeliOrderId,
                Texto = textoNormalizado,
                Direccion = MercadoLibreMessageDireccion.Entrante,
                Estado = MercadoLibreMessageEstado.Recibido,
                MeliUserId = orden.BuyerId,
                FechaMensajeUtc = DateTime.UtcNow,
                EsSimulado = true,
                RawJson = JsonSerializer.Serialize(new
                {
                    simuladoLocal = true,
                    orderId = orden.MeliOrderId,
                    texto = textoNormalizado
                }),
                CreatedBy = usuario
            };

            _context.MercadoLibreMessages.Add(mensaje);

            RegistrarLog(orden.AccountId, "MensajeQASimulado", true,
                $"Mensaje entrante simulado local sobre orden {orden.MeliOrderId}. No se llamó a Mercado Libre.", usuario);

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreMessageResult(true, mensaje.Id,
                "Mensaje simulado creado. Queda pendiente de respuesta manual.");
        }

        public async Task<MercadoLibreMessageResult> ResponderMensajeAsync(
            int orderId, string texto, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            if (string.IsNullOrWhiteSpace(texto))
                return new MercadoLibreMessageResult(false, null, "El texto del mensaje es obligatorio.");

            var orden = await _context.MercadoLibreOrders
                .Include(o => o.Account)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (orden is null)
                return new MercadoLibreMessageResult(false, null, $"Orden {orderId} no encontrada.");

            var textoNormalizado = Truncar(texto.Trim(), 2000);

            // Conversación real solo si la orden no es simulada local y el modo es real.
            var ordenEsSimuladaLocal = orden.MeliOrderId <= 0 ||
                await _context.MercadoLibreMessages.AnyAsync(
                    m => m.OrderId == orden.Id && m.EsSimulado, ct);

            var envioReal = !config.ModoSimulacion && !ordenEsSimuladaLocal;

            var mensaje = new MercadoLibreMessage
            {
                AccountId = orden.AccountId,
                MessageId = GenerarIdSimulado(orden.MeliOrderId),
                OrderId = orden.Id,
                MeliOrderId = orden.MeliOrderId,
                Texto = textoNormalizado,
                Direccion = MercadoLibreMessageDireccion.Saliente,
                Estado = MercadoLibreMessageEstado.Enviado,
                MeliUserId = orden.Account?.MeliUserId,
                FechaMensajeUtc = DateTime.UtcNow,
                FechaRespuestaUtc = DateTime.UtcNow,
                EsSimulado = !envioReal,
                UsuarioEnvio = usuario,
                CreatedBy = usuario
            };

            if (envioReal)
            {
                if (!confirmarReal)
                {
                    return new MercadoLibreMessageResult(false, orden.Id,
                        "Envío real a Mercado Libre requiere confirmación explícita.");
                }

                if (orden.Account is null || orden.BuyerId is null)
                {
                    return new MercadoLibreMessageResult(false, orden.Id,
                        "Faltan datos de la conversación (vendedor/comprador) para enviar a Mercado Libre.");
                }

                try
                {
                    var token = await _authService.GetValidAccessTokenAsync(orden.AccountId, ct);
                    await _apiClient.SendMessageAsync(
                        token, orden.MeliOrderId, orden.Account.MeliUserId, orden.BuyerId.Value, textoNormalizado, ct);

                    mensaje.RawJson = JsonSerializer.Serialize(new { enviadoReal = true, orderId = orden.MeliOrderId });
                }
                catch (Exception ex)
                {
                    mensaje.Estado = MercadoLibreMessageEstado.Error;
                    mensaje.ErrorProcesamiento = Truncar(ex.Message, 1000);
                    _context.MercadoLibreMessages.Add(mensaje);
                    RegistrarLog(orden.AccountId, "MensajeResponderReal", false,
                        $"Error enviando mensaje de orden {orden.MeliOrderId} a Mercado Libre.", usuario);
                    await _context.SaveChangesAsync(ct);
                    return new MercadoLibreMessageResult(false, orden.Id,
                        $"No se pudo enviar el mensaje a Mercado Libre: {ex.Message}");
                }
            }
            else
            {
                mensaje.RawJson = JsonSerializer.Serialize(new
                {
                    simuladoLocal = true,
                    orderId = orden.MeliOrderId,
                    texto = textoNormalizado
                });
            }

            _context.MercadoLibreMessages.Add(mensaje);

            var detalle = envioReal
                ? $"Mensaje enviado a Mercado Libre (orden {orden.MeliOrderId})."
                : $"Mensaje saliente simulado local en orden {orden.MeliOrderId}. No se llamó a Mercado Libre.";

            RegistrarLog(orden.AccountId,
                envioReal ? "MensajeResponderReal" : "MensajeResponderSimulado", true, detalle, usuario);

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreMessageResult(true, mensaje.Id,
                envioReal ? "Mensaje enviado a Mercado Libre." : "Mensaje simulado guardado localmente.");
        }

        public async Task<int?> RegistrarDesdeWebhookAsync(
            string messageId, int accountId, string? resource, long? meliOrderId,
            MercadoLibreConfiguracion config, CancellationToken ct = default)
        {
            var idNormalizado = Truncar(messageId, 80);

            // Idempotencia por MessageId.
            var existente = await _context.MercadoLibreMessages
                .FirstOrDefaultAsync(m => m.MessageId == idNormalizado, ct);

            if (existente is not null)
                return existente.Id;

            int? orderId = meliOrderId.HasValue
                ? await _context.MercadoLibreOrders
                    .Where(o => o.MeliOrderId == meliOrderId.Value)
                    .Select(o => (int?)o.Id)
                    .FirstOrDefaultAsync(ct)
                : null;

            var mensaje = new MercadoLibreMessage
            {
                AccountId = accountId,
                MessageId = idNormalizado,
                OrderId = orderId,
                MeliOrderId = meliOrderId,
                Texto = "(mensaje recibido; texto pendiente de obtener desde Mercado Libre)",
                Direccion = MercadoLibreMessageDireccion.Entrante,
                Estado = MercadoLibreMessageEstado.Recibido,
                FechaMensajeUtc = DateTime.UtcNow,
                EsSimulado = false,
                RawJson = JsonSerializer.Serialize(new { resource, recibidoUtc = DateTime.UtcNow }),
                CreatedBy = "Sistema (webhook)"
            };

            _context.MercadoLibreMessages.Add(mensaje);
            await _context.SaveChangesAsync(ct);

            return mensaje.Id;
        }

        private void RegistrarLog(int accountId, string operacion, bool exito, string detalle, string usuario)
        {
            _context.MercadoLibreSyncLogs.Add(new MercadoLibreSyncLog
            {
                AccountId = accountId,
                Operacion = operacion,
                Exito = exito,
                Detalle = Truncar(detalle, 2000),
                CreatedBy = usuario
            });
        }

        private static string GenerarIdSimulado(long meliOrderId)
            => $"QA-{meliOrderId}-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}";

        private static string Truncar(string valor, int max)
            => valor.Length > max ? valor[..max] : valor;
    }
}
