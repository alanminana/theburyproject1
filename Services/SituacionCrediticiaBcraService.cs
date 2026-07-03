using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    public class SituacionCrediticiaBcraService : ISituacionCrediticiaBcraService
    {
        private static readonly Uri BaseUri = new("https://api.bcra.gob.ar/CentralDeDeudores/v1.0/Deudas/");
        private static readonly TimeSpan ErrorRetryCooldown = TimeSpan.FromHours(1);
        private static readonly TimeSpan NetworkRetryBaseDelay = TimeSpan.FromMilliseconds(200);

        // Máximo 2 intentos totales (1 retry) tanto para errores de red como para timeout.
        private const int MaxNetworkAttempts = 2;

        private const string DescripcionCuilNoCargado = "CUIL/CUIT no cargado";
        private const string DescripcionSinRegistro = "Sin registro en BCRA (no encontrado)";
        private const string DescripcionSinDeudas = "Sin deudas registradas";
        private const string DescripcionRespuestaSinResultados = "Respuesta sin resultados";
        private const string DescripcionRateLimit = "BCRA limitó las consultas (429)";
        private const string DescripcionTimeout = "Timeout al consultar BCRA";
        private const string DescripcionErrorConexion = "Error de conexión con BCRA";
        private const string DescripcionRespuestaInvalida = "Respuesta BCRA inválida";

        private static readonly Dictionary<int, string> SituacionDescripcion = new()
        {
            { 1, "Normal" },
            { 2, "Con seguimiento especial / Riesgo bajo" },
            { 3, "Con problemas" },
            { 4, "Con alto riesgo de insolvencia" },
            { 5, "Irrecuperable" }
        };

        private readonly HttpClient _http;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ILogger<SituacionCrediticiaBcraService> _logger;

        public SituacionCrediticiaBcraService(
            HttpClient http,
            IDbContextFactory<AppDbContext> contextFactory,
            ILogger<SituacionCrediticiaBcraService> logger)
        {
            _http = http;
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task ConsultarYActualizarAsync(int clienteId, int cacheDias = 7)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var cliente = await context.Clientes
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente is null) return;

            // Cache vigente: no reconsultar
            if (TieneConsultaExitosaVigente(cliente, cacheDias))
            {
                return;
            }

            // Evitar martillar la API externa cuando hubo un error transitorio reciente.
            if (TieneErrorTransitorioReciente(cliente))
            {
                return;
            }

            await ConsultarBcraAsync(context, cliente);
        }

        public async Task ForzarActualizacionAsync(int clienteId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var cliente = await context.Clientes
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente is null) return;

            await ConsultarBcraAsync(context, cliente);
        }

        private async Task ConsultarBcraAsync(AppDbContext context, TheBuryProject.Models.Entities.Cliente cliente)
        {
            var cuil = cliente.CuilCuit;
            if (string.IsNullOrWhiteSpace(cuil) || cuil.Length != 11 || !cuil.All(char.IsDigit))
            {
                // Sin CUIL válido: marcar como no consultable
                cliente.SituacionCrediticiaBcra = null;
                cliente.SituacionCrediticiaDescripcion = DescripcionCuilNoCargado;
                cliente.SituacionCrediticiaPeriodo = null;
                cliente.SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow;
                cliente.SituacionCrediticiaConsultaOk = false;
                await context.SaveChangesAsync();
                return;
            }

            try
            {
                var requestUri = new Uri(BaseUri, cuil);
                using var response = await GetWithNetworkRetryAsync(requestUri);

                // La Central de Deudores devuelve 404 cuando la identificación no figura
                // en el padrón de deudores del sistema financiero (no encontrado). No es un
                // error: significa que no hay deudas informadas para ese CUIL/CUIT.
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    MarcarExito(cliente, 0, DescripcionSinRegistro, null);
                    await context.SaveChangesAsync();
                    return;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("BCRA API limitó las consultas para CUIL {Cuil}", cuil);
                    MarcarError(cliente, DescripcionRateLimit);
                    await context.SaveChangesAsync();
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("BCRA API retornó {Status} para CUIL {Cuil}", response.StatusCode, cuil);
                    MarcarError(cliente, $"Error API ({(int)response.StatusCode})");
                    await context.SaveChangesAsync();
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("results", out var results))
                {
                    MarcarError(cliente, DescripcionRespuestaSinResultados);
                    await context.SaveChangesAsync();
                    return;
                }

                if (!results.TryGetProperty("periodos", out var periodos) || periodos.GetArrayLength() == 0)
                {
                    // Sin deudas registradas
                    MarcarExito(cliente, 0, DescripcionSinDeudas, null);
                    await context.SaveChangesAsync();
                    return;
                }

                // Tomar el período más reciente (primer elemento)
                var ultimoPeriodo = periodos[0];
                var periodoStr = ultimoPeriodo.TryGetProperty("periodo", out var periodoVal) ? periodoVal.GetString() : null;

                int peorSituacion = 0;
                if (ultimoPeriodo.TryGetProperty("entidades", out var entidades))
                {
                    foreach (var entidad in entidades.EnumerateArray())
                    {
                        if (entidad.TryGetProperty("situacion", out var sit) && sit.TryGetInt32(out var sitVal))
                        {
                            if (sitVal > peorSituacion)
                                peorSituacion = sitVal;
                        }
                    }
                }

                MarcarExito(cliente, peorSituacion, SituacionDescripcion.GetValueOrDefault(peorSituacion, $"Situación {peorSituacion}"), periodoStr);
                await context.SaveChangesAsync();
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Timeout consultando BCRA para CUIL {Cuil}", cuil);
                MarcarError(cliente, DescripcionTimeout);
                await context.SaveChangesAsync();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Error de red consultando BCRA para CUIL {Cuil}", cuil);
                MarcarError(cliente, DescripcionErrorConexion);
                await context.SaveChangesAsync();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Error parseando respuesta BCRA para CUIL {Cuil}", cuil);
                MarcarError(cliente, DescripcionRespuestaInvalida);
                await context.SaveChangesAsync();
            }
        }

        public async Task<SituacionBcraResult?> ConsultarYObtenerAsync(int clienteId, int cacheDias = 7)
        {
            await ConsultarYActualizarAsync(clienteId, cacheDias);

            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Clientes
                .AsNoTracking()
                .Where(c => c.Id == clienteId && !c.IsDeleted)
                .Select(c => new SituacionBcraResult
                {
                    SituacionCrediticiaBcra = c.SituacionCrediticiaBcra,
                    SituacionCrediticiaDescripcion = c.SituacionCrediticiaDescripcion,
                    SituacionCrediticiaPeriodo = c.SituacionCrediticiaPeriodo,
                    SituacionCrediticiaUltimaConsultaUtc = c.SituacionCrediticiaUltimaConsultaUtc,
                    SituacionCrediticiaConsultaOk = c.SituacionCrediticiaConsultaOk,
                    SituacionCrediticiaBcraUltimoExito = c.SituacionCrediticiaBcraUltimoExito,
                    SituacionCrediticiaDescripcionUltimoExito = c.SituacionCrediticiaDescripcionUltimoExito,
                    SituacionCrediticiaPeriodoUltimoExito = c.SituacionCrediticiaPeriodoUltimoExito,
                    SituacionCrediticiaUltimoExitoUtc = c.SituacionCrediticiaUltimoExitoUtc
                })
                .FirstOrDefaultAsync();
        }

        private static void MarcarError(TheBuryProject.Models.Entities.Cliente cliente, string descripcion)
        {
            // Solo actualiza el último intento. El último éxito (si existe) se preserva:
            // un error transitorio no debe borrar la última situación BCRA válida conocida.
            cliente.SituacionCrediticiaBcra = null;
            cliente.SituacionCrediticiaDescripcion = descripcion;
            cliente.SituacionCrediticiaPeriodo = null;
            cliente.SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow;
            cliente.SituacionCrediticiaConsultaOk = false;
        }

        private static void MarcarExito(TheBuryProject.Models.Entities.Cliente cliente, int situacion, string descripcion, string? periodo)
        {
            var ahora = DateTime.UtcNow;

            cliente.SituacionCrediticiaBcra = situacion;
            cliente.SituacionCrediticiaDescripcion = descripcion;
            cliente.SituacionCrediticiaPeriodo = periodo;
            cliente.SituacionCrediticiaUltimaConsultaUtc = ahora;
            cliente.SituacionCrediticiaConsultaOk = true;

            cliente.SituacionCrediticiaBcraUltimoExito = situacion;
            cliente.SituacionCrediticiaDescripcionUltimoExito = descripcion;
            cliente.SituacionCrediticiaPeriodoUltimoExito = periodo;
            cliente.SituacionCrediticiaUltimoExitoUtc = ahora;
        }

        private async Task<HttpResponseMessage> GetWithNetworkRetryAsync(Uri requestUri)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await _http.GetAsync(requestUri);
                }
                catch (HttpRequestException) when (attempt < MaxNetworkAttempts)
                {
                    await Task.Delay(NetworkRetryBaseDelay);
                }
                catch (TaskCanceledException) when (attempt < MaxNetworkAttempts)
                {
                    await Task.Delay(NetworkRetryBaseDelay);
                }
            }
        }

        private static bool TieneConsultaExitosaVigente(
            TheBuryProject.Models.Entities.Cliente cliente,
            int cacheDias)
        {
            return cliente.SituacionCrediticiaUltimaConsultaUtc.HasValue
                && cliente.SituacionCrediticiaConsultaOk == true
                && (DateTime.UtcNow - cliente.SituacionCrediticiaUltimaConsultaUtc.Value).TotalDays < cacheDias;
        }

        private static bool TieneErrorTransitorioReciente(TheBuryProject.Models.Entities.Cliente cliente)
        {
            return cliente.SituacionCrediticiaUltimaConsultaUtc.HasValue
                && cliente.SituacionCrediticiaConsultaOk == false
                && (DateTime.UtcNow - cliente.SituacionCrediticiaUltimaConsultaUtc.Value) < ErrorRetryCooldown
                && EsErrorTransitorioCacheable(cliente.SituacionCrediticiaDescripcion);
        }

        private static bool EsErrorTransitorioCacheable(string? descripcion)
        {
            if (string.IsNullOrWhiteSpace(descripcion))
                return false;

            return descripcion == DescripcionRateLimit
                || descripcion == DescripcionTimeout
                || descripcion == DescripcionErrorConexion
                || descripcion == DescripcionRespuestaInvalida
                || descripcion == DescripcionRespuestaSinResultados
                || descripcion.StartsWith("Error API (", StringComparison.Ordinal);
        }
    }
}
