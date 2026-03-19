using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// ✅ MEJORADO: Servicio en background para ejecutar el procesamiento de mora automáticamente
    /// - Lógica de ventana de tiempo mejorada
    /// - Mejor manejo de errores
    /// - Logging detallado
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

                        // ✅ MEJORADO: Comparar solo horas y minutos
                        var ahora = DateTime.UtcNow;
                        var horaEjecucion = configuracion.HoraEjecucionDiaria ?? new TimeSpan(8, 0, 0);
                        var horaActual = new TimeSpan(ahora.Hour, ahora.Minute, 0);
                        var diferencia = (horaEjecucion - horaActual).TotalMinutes;

                        // Ventana de ejecución: ±10 minutos (más tolerante que antes)
                        if (Math.Abs(diferencia) <= 10)
                        {
                            // Verificar si ya se ejecutó hoy
                            if (_ultimaEjecucion.Date < DateTime.Today)
                            {
                                _logger.LogInformation("Ejecutando procesamiento automático de mora a las {Hora}", ahora.ToString("HH:mm"));
                                
                                try
                                {
                                    await moraService.ProcesarMoraAsync();
                                    _ultimaEjecucion = DateTime.UtcNow;
                                    
                                    _logger.LogInformation("Procesamiento de mora completado exitosamente");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error durante la ejecución automática de mora");
                                    // Continuar el servicio incluso si hay error
                                }
                            }
                            else
                            {
                                _logger.LogDebug("Procesamiento de mora ya ejecutado hoy");
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