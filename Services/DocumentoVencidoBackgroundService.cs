using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio en background para marcar documentos vencidos autom�ticamente
    /// Se ejecuta diariamente a las 2:00 AM
    /// </summary>
    public class DocumentoVencidoBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DocumentoVencidoBackgroundService> _logger;
        // Definir la hora de ejecuci�n (2:00 AM)
        private readonly TimeSpan _horaEjecucion = new TimeSpan(2, 0, 0);

        public DocumentoVencidoBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<DocumentoVencidoBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DocumentoVencidoBackgroundService iniciado. Se ejecutar� diariamente a las {HoraEjecucion}", 
                _horaEjecucion.ToString(@"hh\:mm"));

            // Esperar hasta la pr�xima ejecuci�n programada (m�ximo 30 segundos en desarrollo)
            try
            {
                await EsperarHastaProximaEjecucionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("DocumentoVencidoBackgroundService cancelado durante la espera inicial");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Iniciando marcado de documentos vencidos...");

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var documentoService = scope.ServiceProvider
                            .GetRequiredService<IDocumentoClienteService>();
                        
                        await documentoService.MarcarVencidosAsync(stoppingToken);
                    }

                    _logger.LogInformation("Marcado de documentos vencidos completado");

                    // Esperar hasta la pr�xima ejecuci�n (ma�ana a las 2 AM)
                    await EsperarUnDiaAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("DocumentoVencidoBackgroundService cancelado durante la espera programada");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en DocumentoVencidoBackgroundService");
                    // Continuar esperando en caso de error
                    try
                    {
                        await EsperarUnDiaAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("DocumentoVencidoBackgroundService cancelado despu�s de un error");
                        break;
                    }
                }
            }

            _logger.LogInformation("DocumentoVencidoBackgroundService detenido");
        }

        /// <summary>
        /// Calcula cu�nto tiempo esperar hasta la pr�xima ejecuci�n programada
        /// </summary>
        private TimeSpan CalcularTiempoEsperaHastaSiguienteEjecucion()
        {
            var ahora = DateTime.UtcNow;
            var proximaEjecucion = ahora.Date.Add(_horaEjecucion);

            // Si ya pas� la hora hoy, programar para ma�ana
            if (ahora >= proximaEjecucion)
            {
                proximaEjecucion = proximaEjecucion.AddDays(1);
            }

            var tiempoEspera = proximaEjecucion - ahora;
            
            _logger.LogInformation(
                "Pr�xima ejecuci�n programada para: {FechaHora} ({MinutosRestantes} minutos)",
                proximaEjecucion.ToString("dd/MM/yyyy HH:mm:ss"),
                (int)tiempoEspera.TotalMinutes);

            return tiempoEspera;
        }

        /// <summary>
        /// Espera hasta la pr�xima ejecuci�n programada (ma�ana a las 2 AM)
        /// </summary>
        private async Task EsperarUnDiaAsync(CancellationToken stoppingToken)
        {
            var tiempoEspera = CalcularTiempoEsperaHastaSiguienteEjecucion();
            await Task.Delay(tiempoEspera, stoppingToken);
        }

        /// <summary>
        /// Espera hasta la pr�xima ejecuci�n (solo para la primera vez)
        /// </summary>
        private async Task EsperarHastaProximaEjecucionAsync(CancellationToken stoppingToken)
        {
            var tiempoEspera = CalcularTiempoEsperaHastaSiguienteEjecucion();
            
            // En producci�n esperar hasta la hora exacta
            // En desarrollo, esperar m�ximo 30 segundos para testing
            #if DEBUG
            var tiempoEsperaMaximo = TimeSpan.FromSeconds(30);
            if (tiempoEspera > tiempoEsperaMaximo)
            {
                _logger.LogWarning("Modo DEBUG: Esperando m�ximo 30 segundos en lugar de {MinutosRestantes} minutos", 
                    (int)tiempoEspera.TotalMinutes);
                tiempoEspera = tiempoEsperaMaximo;
            }
            #endif

            await Task.Delay(tiempoEspera, stoppingToken);
        }
    }
}