using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Data;
using TheBuryProject.Modules.MercadoLibre.Entities;
using Microsoft.EntityFrameworkCore;

namespace TheBuryProject.Modules.MercadoLibre.Controllers
{
    /// <summary>
    /// Receptor de notificaciones (webhooks) de Mercado Libre.
    /// Contrato: persistir el evento crudo y responder 200 lo más rápido posible.
    /// El procesamiento real se hace después, en forma idempotente (campo Procesado).
    /// </summary>
    [ApiController]
    [AllowAnonymous]
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

        [HttpPost]
        public async Task<IActionResult> Recibir(CancellationToken ct)
        {
            string rawBody;

            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync(ct);
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

                if (root.TryGetProperty("topic", out var topic))
                    evento.Topic = Truncar(topic.GetString(), 50) ?? string.Empty;

                if (root.TryGetProperty("resource", out var resource))
                    evento.Resource = Truncar(resource.GetString(), 300) ?? string.Empty;

                if (root.TryGetProperty("user_id", out var userId) && userId.ValueKind == JsonValueKind.Number)
                    evento.MeliUserId = userId.GetInt64();

                if (root.TryGetProperty("attempts", out var attempts) && attempts.ValueKind == JsonValueKind.Number)
                    evento.Attempts = attempts.GetInt32();
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
