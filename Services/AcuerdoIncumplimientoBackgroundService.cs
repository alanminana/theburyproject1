using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Verificación diaria del incumplimiento de acuerdos de pago. Corre independientemente de la configuración del proceso
    /// automático de mora (<c>ProcesoAutomaticoActivo</c>): el incumplimiento de un acuerdo no depende de que esté encendido
    /// el cálculo de alertas. Usa la fecha comercial de Argentina para decidir si ya hay que ejecutarla.
    /// </summary>
    public class AcuerdoIncumplimientoBackgroundService : BackgroundService
    {
        private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan IntervaloDeRevision = TimeSpan.FromHours(1);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AcuerdoIncumplimientoBackgroundService> _logger;
        private DateOnly? _ultimaFechaEjecutada;

        public AcuerdoIncumplimientoBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<AcuerdoIncumplimientoBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AcuerdoIncumplimientoBackgroundService iniciado");

            try
            {
                // Al arrancar se pone al día (si el servicio estuvo apagado, el incumplimiento no se pierde).
                await Task.Delay(EsperaInicial, stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await EjecutarSiCorrespondeAsync(stoppingToken);
                    await Task.Delay(IntervaloDeRevision, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // apagado normal
            }
        }

        /// <summary>Ejecuta la verificación una vez por fecha comercial. Devuelve true si corrió.</summary>
        internal async Task<bool> EjecutarSiCorrespondeAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var hoy = scope.ServiceProvider.GetRequiredService<IRelojComercial>().HoyComercial;
                if (_ultimaFechaEjecutada == hoy)
                    return false;

                var marcados = await scope.ServiceProvider.GetRequiredService<IMoraService>()
                    .VerificarIncumplimientoAcuerdosAsync(cancellationToken);
                _ultimaFechaEjecutada = hoy;
                _logger.LogInformation("Verificación de incumplimiento de acuerdos {Fecha}: {Marcados} marcados", hoy, marcados);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Se reintenta en la próxima revisión; el servicio no se cae.
                _logger.LogError(ex, "Error en la verificación diaria de incumplimiento de acuerdos");
                return false;
            }
        }
    }
}
