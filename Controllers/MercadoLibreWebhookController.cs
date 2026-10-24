using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Receptor de notificaciones (webhooks) de Mercado Libre.
    /// Contrato: persistir el evento crudo y responder 200 lo más rápido posible.
    /// El procesamiento real se hace después, en forma idempotente (campo Procesado).
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [EnableRateLimiting("mercadolibre-webhook")]
    [Route("api/mercadolibre/webhook")]
    public class MercadoLibreWebhookController : ControllerBase
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ILogger<MercadoLibreWebhookController> _logger;

        public MercadoLibreWebhookController(
            IDbContextFactory<AppDbContext> contextFactory,
            ILogger<MercadoLibreWebhookController> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        /// <summary>
        /// Tope del cuerpo (caracteres). Las notificaciones de Mercado Libre pesan unos cientos de bytes; el endpoint es
        /// anónimo (la firma no se puede verificar sin el secreto de la aplicación), así que no se lee ni se persiste
        /// un cuerpo arbitrariamente grande.
        /// </summary>
        internal const int MaxBodyChars = 64 * 1024;

        [HttpPost]
        public async Task<IActionResult> Recibir(CancellationToken ct)
        {
            if (Request.ContentLength is > MaxBodyChars * 4L)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);

            string rawBody;

            using (var reader = new StreamReader(Request.Body))
            {
                var buffer = new char[MaxBodyChars + 1];
                var leidos = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
                if (leidos > MaxBodyChars)
                {
                    _logger.LogWarning("Webhook de Mercado Libre descartado: el cuerpo supera {Max} caracteres", MaxBodyChars);
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                rawBody = new string(buffer, 0, leidos);
            }

            if (string.IsNullOrWhiteSpace(rawBody))
                return Ok();

            var evento = new MercadoLibreWebhookEvent
            {
                RawBody = rawBody,
                RecibidoUtc = DateTime.UtcNow
            };

            // Parse defensivo: si el body no es el JSON esperado, igual se guarda crudo.
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;

                // Cada campo se lee solo si tiene el tipo esperado: un JSON válido pero de otra forma (topic numérico,
                // user_id decimal o fuera de rango, raíz que no es objeto) ya no termina en un 500 que ML reintenta.
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("topic", out var topic) && topic.ValueKind == JsonValueKind.String)
                        evento.Topic = Truncar(topic.GetString(), 50) ?? string.Empty;

                    if (root.TryGetProperty("resource", out var resource) && resource.ValueKind == JsonValueKind.String)
                        evento.Resource = Truncar(resource.GetString(), 300) ?? string.Empty;

                    if (root.TryGetProperty("user_id", out var userId)
                        && userId.ValueKind == JsonValueKind.Number && userId.TryGetInt64(out var meliUserId))
                        evento.MeliUserId = meliUserId;

                    if (root.TryGetProperty("attempts", out var attempts)
                        && attempts.ValueKind == JsonValueKind.Number && attempts.TryGetInt32(out var intentos))
                        evento.Attempts = intentos;
                }
            }
            catch (JsonException)
            {
                _logger.LogWarning("Webhook de Mercado Libre con body no parseable; se guarda crudo");
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(ct);
                context.MercadoLibreWebhookEvents.Add(evento);
                await context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Nunca devolver 5xx evitable: ML reintenta y deshabilita webhooks con muchos fallos.
                _logger.LogError(ex, "No se pudo persistir el webhook de Mercado Libre (topic {Topic})", evento.Topic);
            }

            return Ok();
        }

        private static string? Truncar(string? valor, int max) =>
            valor is null ? null : (valor.Length <= max ? valor : valor[..max]);
    }
}
