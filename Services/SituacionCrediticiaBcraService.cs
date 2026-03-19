using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class SituacionCrediticiaBcraService : ISituacionCrediticiaBcraService
    {
        private static readonly Uri BaseUri = new("https://api.bcra.gob.ar/CentralDeDeudores/v1.0/Deudas/");

        private static readonly Dictionary<int, string> SituacionDescripcion = new()
        {
            { 1, "Normal" },
            { 2, "En observación" },
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
            if (cliente.SituacionCrediticiaUltimaConsultaUtc.HasValue
                && cliente.SituacionCrediticiaConsultaOk == true
                && (DateTime.UtcNow - cliente.SituacionCrediticiaUltimaConsultaUtc.Value).TotalDays < cacheDias)
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
                cliente.SituacionCrediticiaDescripcion = "CUIL/CUIT no cargado";
                cliente.SituacionCrediticiaPeriodo = null;
                cliente.SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow;
                cliente.SituacionCrediticiaConsultaOk = false;
                await context.SaveChangesAsync();
                return;
            }

            try
            {
                var requestUri = new Uri(BaseUri, cuil);
                var response = await _http.GetAsync(requestUri);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("BCRA API retornó {Status} para CUIL {Cuil}", response.StatusCode, cuil);
                    MarcarError(cliente, $"Error API ({(int)response.StatusCode})");
                    await context.SaveChangesAsync();
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("results", out var results))
                {
                    MarcarError(cliente, "Respuesta sin resultados");
                    await context.SaveChangesAsync();
                    return;
                }

                if (!results.TryGetProperty("periodos", out var periodos) || periodos.GetArrayLength() == 0)
                {
                    // Sin deudas registradas
                    cliente.SituacionCrediticiaBcra = 0;
                    cliente.SituacionCrediticiaDescripcion = "Sin deudas registradas";
                    cliente.SituacionCrediticiaPeriodo = null;
                    cliente.SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow;
                    cliente.SituacionCrediticiaConsultaOk = true;
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

                cliente.SituacionCrediticiaBcra = peorSituacion;
                cliente.SituacionCrediticiaDescripcion = SituacionDescripcion.GetValueOrDefault(peorSituacion, $"Situación {peorSituacion}");
                cliente.SituacionCrediticiaPeriodo = periodoStr;
                cliente.SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow;
                cliente.SituacionCrediticiaConsultaOk = true;
                await context.SaveChangesAsync();
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Timeout consultando BCRA para CUIL {Cuil}", cuil);
                MarcarError(cliente, "Timeout al consultar BCRA");
                await context.SaveChangesAsync();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Error de red consultando BCRA para CUIL {Cuil}", cuil);
                MarcarError(cliente, "Error de conexión con BCRA");
                await context.SaveChangesAsync();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Error parseando respuesta BCRA para CUIL {Cuil}", cuil);
                MarcarError(cliente, "Respuesta BCRA inválida");
                await context.SaveChangesAsync();
            }
        }

        private static void MarcarError(TheBuryProject.Models.Entities.Cliente cliente, string descripcion)
        {
            cliente.SituacionCrediticiaBcra = null;
            cliente.SituacionCrediticiaDescripcion = descripcion;
            cliente.SituacionCrediticiaPeriodo = null;
            cliente.SituacionCrediticiaUltimaConsultaUtc = DateTime.UtcNow;
            cliente.SituacionCrediticiaConsultaOk = false;
        }
    }
}
