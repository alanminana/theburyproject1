using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio en background para ejecutar el procesamiento de mora automáticamente.
    /// </summary>
    public class MoraBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MoraBackgroundService> _logger;
        private DateTime _ultimaEjecucion = DateTime.MinValue;

        public MoraBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<MoraBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Decide si corresponde correr hoy: hora comercial dentro de ±10 min de la configurada y sin ejecución
        /// previa el mismo día comercial (ni en memoria ni persistida en la base).
        /// </summary>
        internal static bool DebeEjecutar(
            TimeSpan? horaConfigurada,
            DateTime ahoraComercial,
            DateTime? ultimaEjecucionPersistidaUtc,
            IRelojComercial reloj,
            DateTime ultimaEjecucionMemoriaUtc)
        {
            var hora = horaConfigurada ?? new TimeSpan(8, 0, 0);
            var horaActual = new TimeSpan(ahoraComercial.Hour, ahoraComercial.Minute, 0);
            if (Math.Abs((hora - horaActual).TotalMinutes) > 10)
                return false;

            var hoy = reloj.HoyComercial;
            return !YaEjecutadoEseDia(ultimaEjecucionMemoriaUtc, hoy, reloj)
                && !YaEjecutadoEseDia(ultimaEjecucionPersistidaUtc, hoy, reloj);
        }

        private static bool YaEjecutadoEseDia(DateTime? ultimaUtc, DateOnly hoy, IRelojComercial reloj)
        {
            if (ultimaUtc is null || ultimaUtc.Value == DateTime.MinValue)
                return false;
            var utc = DateTime.SpecifyKind(ultimaUtc.Value, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, reloj.ZonaComercial);
            return DateOnly.FromDateTime(local) >= hoy;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MoraBackgroundService iniciado");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var moraService = scope.ServiceProvider.GetRequiredService<IMoraService>();
                        var configuracion = await moraService.GetConfiguracionAsync();

                        if (!configuracion.ProcesoAutomaticoActivo)
                        {
                            _logger.LogDebug("Job de mora desactivado, esperando...");
                            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                            continue;
                        }

                        // La hora configurada es hora comercial (Argentina), no UTC. La última ejecución sale de
                        // la base (ConfiguracionMora.UltimaEjecucion) para no repetir el proceso al reiniciar la app.
                        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
                        var ahora = reloj.AhoraComercial;
                        if (DebeEjecutar(configuracion.HoraEjecucionDiaria, ahora, configuracion.UltimaEjecucion, reloj, _ultimaEjecucion))
                        {
                            _logger.LogInformation("Ejecutando procesamiento automático de mora a las {Hora}", ahora.ToString("HH:mm"));

                            try
                            {
                                await moraService.ProcesarMoraAsync(stoppingToken);
                                _ultimaEjecucion = reloj.AhoraUtc;

                                _logger.LogInformation("Procesamiento de mora completado exitosamente");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error durante la ejecución automática de mora");
                                // Continuar el servicio incluso si hay error
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error crítico en MoraBackgroundService");
                }

                // Esperar 10 minutos antes de la próxima verificación
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }

            _logger.LogInformation("MoraBackgroundService detenido");
        }
    }
}