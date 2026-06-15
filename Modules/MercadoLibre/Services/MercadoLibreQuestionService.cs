using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    /// <summary>
    /// Implementación de la gestión de preguntas preventa ML.
    /// Regla de oro: en simulación nunca se invoca <see cref="IMercadoLibreApiClient"/>.
    /// </summary>
    public class MercadoLibreQuestionService : IMercadoLibreQuestionService
    {
        private readonly AppDbContext _context;
        private readonly IMercadoLibreConfiguracionService _configuracionService;
        private readonly IMercadoLibreAuthService _authService;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly ILogger<MercadoLibreQuestionService> _logger;

        public MercadoLibreQuestionService(
            AppDbContext context,
            IMercadoLibreConfiguracionService configuracionService,
            IMercadoLibreAuthService authService,
            IMercadoLibreApiClient apiClient,
            ILogger<MercadoLibreQuestionService> logger)
        {
            _context = context;
            _configuracionService = configuracionService;
            _authService = authService;
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<List<MercadoLibreQuestion>> GetPreguntasAsync(string? filtro = null, CancellationToken ct = default)
        {
            var query = _context.MercadoLibreQuestions
                .AsNoTracking()
                .Include(q => q.Listing)
                .Include(q => q.Producto)
                .AsQueryable();

            query = filtro switch
            {
                MercadoLibreQuestionFiltro.Pendientes => query.Where(q => q.Estado == MercadoLibreQuestionEstado.Pendiente),
                MercadoLibreQuestionFiltro.Respondidas => query.Where(q => q.Estado == MercadoLibreQuestionEstado.Respondida),
                MercadoLibreQuestionFiltro.Simuladas => query.Where(q => q.EsSimulada),
                MercadoLibreQuestionFiltro.ConError => query.Where(q => q.Estado == MercadoLibreQuestionEstado.Error),
                MercadoLibreQuestionFiltro.SinVincular => query.Where(q => q.ListingId == null),
                _ => query
            };

            return await query
                .OrderByDescending(q => q.FechaPreguntaUtc)
                .Take(200)
                .ToListAsync(ct);
        }

        public Task<List<MercadoLibreQuestion>> GetPreguntasPorListingAsync(int listingId, CancellationToken ct = default)
            => _context.MercadoLibreQuestions
                .AsNoTracking()
                .Where(q => q.ListingId == listingId)
                .OrderByDescending(q => q.FechaPreguntaUtc)
                .Take(50)
                .ToListAsync(ct);

        public Task<MercadoLibreQuestion?> GetPreguntaAsync(int id, CancellationToken ct = default)
            => _context.MercadoLibreQuestions
                .AsNoTracking()
                .Include(q => q.Listing)
                .Include(q => q.Producto)
                .FirstOrDefaultAsync(q => q.Id == id, ct);

        public async Task<MercadoLibreQuestionResult> SimularPreguntaAsync(
            int listingId, string texto, string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            if (!config.ModoSimulacion && !permitirPorDevelopment)
            {
                return new MercadoLibreQuestionResult(false, null,
                    "La simulación de preguntas solo está habilitada en Development o con ModoSimulacion=true.");
            }

            if (string.IsNullOrWhiteSpace(texto))
                return new MercadoLibreQuestionResult(false, null, "El texto de la pregunta es obligatorio.");

            var listing = await _context.MercadoLibreListings
                .FirstOrDefaultAsync(l => l.Id == listingId, ct);

            if (listing is null)
                return new MercadoLibreQuestionResult(false, null, $"Publicación {listingId} no encontrada.");

            var textoNormalizado = Truncar(texto.Trim(), 2000);
            var questionId = GenerarIdSimulado();

            var pregunta = new MercadoLibreQuestion
            {
                AccountId = listing.AccountId,
                QuestionId = questionId,
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                ProductoId = listing.ProductoId,
                TextoPregunta = textoNormalizado,
                Estado = MercadoLibreQuestionEstado.Pendiente,
                FechaPreguntaUtc = DateTime.UtcNow,
                EsSimulada = true,
                RawJson = JsonSerializer.Serialize(new
                {
                    simuladoLocal = true,
                    itemId = listing.ItemId,
                    texto = textoNormalizado
                }),
                CreatedBy = usuario
            };

            _context.MercadoLibreQuestions.Add(pregunta);

            RegistrarLog(listing.AccountId, "PreguntaQASimulada", true,
                $"Pregunta simulada local sobre {listing.ItemId}. No se llamó a Mercado Libre.", usuario);

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreQuestionResult(true, pregunta.Id,
                "Pregunta simulada creada. Queda pendiente de respuesta manual.");
        }

        public async Task<MercadoLibreQuestionResult> ResponderPreguntaAsync(
            int id, string respuesta, bool confirmarReal, string usuario, CancellationToken ct = default)
        {
            var config = await _configuracionService.GetAsync(ct);

            var pregunta = await _context.MercadoLibreQuestions
                .FirstOrDefaultAsync(q => q.Id == id, ct);

            if (pregunta is null)
                return new MercadoLibreQuestionResult(false, null, $"Pregunta {id} no encontrada.");

            if (pregunta.Estado == MercadoLibreQuestionEstado.Respondida)
                return new MercadoLibreQuestionResult(false, pregunta.Id, "La pregunta ya fue respondida.");

            if (string.IsNullOrWhiteSpace(respuesta))
                return new MercadoLibreQuestionResult(false, pregunta.Id, "El texto de la respuesta es obligatorio.");

            var respuestaNormalizada = Truncar(respuesta.Trim(), 2000);

            // -------- Modo real: doble cerrojo (ModoSimulacion=false Y confirmación) --------
            var envioReal = !config.ModoSimulacion && !pregunta.EsSimulada;

            if (envioReal)
            {
                if (!confirmarReal)
                {
                    return new MercadoLibreQuestionResult(false, pregunta.Id,
                        "Envío real a Mercado Libre requiere confirmación explícita.");
                }

                try
                {
                    var token = await _authService.GetValidAccessTokenAsync(pregunta.AccountId, ct);
                    await _apiClient.AnswerQuestionAsync(token, pregunta.QuestionId, respuestaNormalizada, ct);
                }
                catch (Exception ex)
                {
                    pregunta.Estado = MercadoLibreQuestionEstado.Error;
                    pregunta.ErrorProcesamiento = Truncar(ex.Message, 1000);
                    RegistrarLog(pregunta.AccountId, "PreguntaResponderReal", false,
                        $"Error respondiendo pregunta {pregunta.QuestionId} en Mercado Libre.", usuario);
                    await _context.SaveChangesAsync(ct);
                    return new MercadoLibreQuestionResult(false, pregunta.Id,
                        $"No se pudo enviar la respuesta a Mercado Libre: {ex.Message}");
                }
            }
            else if (!config.ModoSimulacion && pregunta.EsSimulada)
            {
                // Pregunta simulada local: nunca se envía a ML aunque el modo sea real.
                pregunta.EsSimulada = true;
            }

            pregunta.RespuestaTexto = respuestaNormalizada;
            pregunta.Estado = MercadoLibreQuestionEstado.Respondida;
            pregunta.FechaRespuestaUtc = DateTime.UtcNow;
            pregunta.UsuarioRespuesta = usuario;
            pregunta.UpdatedBy = usuario;
            // EsSimulada=true si no hubo envío real (simulación o pregunta simulada local).
            if (!envioReal)
                pregunta.EsSimulada = true;

            var detalle = envioReal
                ? $"Respuesta enviada a Mercado Libre (pregunta {pregunta.QuestionId})."
                : $"Respuesta simulada local a pregunta {pregunta.QuestionId}. No se llamó a Mercado Libre.";

            RegistrarLog(pregunta.AccountId,
                envioReal ? "PreguntaResponderReal" : "PreguntaResponderSimulada", true, detalle, usuario);

            await _context.SaveChangesAsync(ct);

            return new MercadoLibreQuestionResult(true, pregunta.Id,
                envioReal ? "Respuesta enviada a Mercado Libre." : "Respuesta simulada guardada localmente.");
        }

        public async Task<int?> RegistrarDesdeWebhookAsync(
            long questionId, int accountId, string? resource, MercadoLibreConfiguracion config, CancellationToken ct = default)
        {
            // Idempotencia por QuestionId.
            var existente = await _context.MercadoLibreQuestions
                .FirstOrDefaultAsync(q => q.QuestionId == questionId, ct);

            if (existente is not null)
                return existente.Id;

            string? itemId = null;
            string? texto = null;
            long? meliUserId = null;
            DateTime fecha = DateTime.UtcNow;
            string? rawJson = JsonSerializer.Serialize(new { resource, recibidoUtc = DateTime.UtcNow });

            // Solo en modo real consultamos la API para completar el texto.
            if (!config.ModoSimulacion)
            {
                try
                {
                    var token = await _authService.GetValidAccessTokenAsync(accountId, ct);
                    var dto = await _apiClient.GetQuestionAsync(token, questionId, ct);
                    if (dto is not null)
                    {
                        itemId = dto.ItemId;
                        texto = dto.Text;
                        meliUserId = dto.From?.Id;
                        if (dto.DateCreated.HasValue) fecha = dto.DateCreated.Value.UtcDateTime;
                        rawJson = JsonSerializer.Serialize(dto);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "No se pudo obtener la pregunta {QuestionId} desde Mercado Libre; se guarda pendiente.", questionId);
                }
            }

            var listingId = itemId is null
                ? (int?)null
                : await _context.MercadoLibreListings
                    .Where(l => l.ItemId == itemId)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(ct);

            var productoId = itemId is null
                ? (int?)null
                : await _context.MercadoLibreListings
                    .Where(l => l.ItemId == itemId)
                    .Select(l => l.ProductoId)
                    .FirstOrDefaultAsync(ct);

            var pregunta = new MercadoLibreQuestion
            {
                AccountId = accountId,
                QuestionId = questionId,
                ListingId = listingId,
                ItemId = itemId,
                ProductoId = productoId,
                MeliUserId = meliUserId,
                TextoPregunta = string.IsNullOrWhiteSpace(texto)
                    ? "(pregunta recibida; texto pendiente de obtener desde Mercado Libre)"
                    : Truncar(texto.Trim(), 2000),
                Estado = MercadoLibreQuestionEstado.Pendiente,
                FechaPreguntaUtc = fecha,
                EsSimulada = false,
                RawJson = rawJson,
                CreatedBy = "Sistema (webhook)"
            };

            _context.MercadoLibreQuestions.Add(pregunta);
            await _context.SaveChangesAsync(ct);

            return pregunta.Id;
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

        private static long GenerarIdSimulado()
            // Rango negativo: nunca colisiona con ids reales de ML (positivos).
            => -DateTime.UtcNow.Ticks;

        private static string Truncar(string valor, int max)
            => valor.Length > max ? valor[..max] : valor;
    }
}
