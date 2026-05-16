using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio en background para vencer automáticamente cotizaciones emitidas con fecha expirada.
    /// Se ejecuta diariamente a las 3:00 AM.
    /// </summary>
    public class CotizacionVencimientoBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CotizacionVencimientoBackgroundService> _logger;
        private readonly TimeSpan _horaEjecucion = new TimeSpan(3, 0, 0);

        public CotizacionVencimientoBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<CotizacionVencimientoBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "CotizacionVencimientoBackgroundService iniciado. Se ejecutará diariamente a las {HoraEjecucion}",
                _horaEjecucion.ToString(@"hh\:mm"));

            try
            {
                await EsperarHastaProximaEjecucionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("CotizacionVencimientoBackgroundService cancelado durante espera inicial");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Iniciando vencimiento automático de cotizaciones...");

                    using var scope = _serviceProvider.CreateScope();
                    var cotizacionService = scope.ServiceProvider.GetRequiredService<ICotizacionService>();
                    var resultado = await cotizacionService.VencerEmitidasAsync(DateTime.UtcNow, "Sistema", stoppingToken);

                    _logger.LogInformation(
                        "Vencimiento automático completado: {CantidadVencidas} cotizaciones vencidas de {CantidadEvaluadas} evaluadas.",
                        resultado.CantidadVencidas, resultado.CantidadEvaluadas);

                    await EsperarUnDiaAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("CotizacionVencimientoBackgroundService cancelado durante ejecución");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en CotizacionVencimientoBackgroundService");
                    try
                    {
                        await EsperarUnDiaAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("CotizacionVencimientoBackgroundService cancelado después de un error");
                        break;
                    }
                }
            }

            _logger.LogInformation("CotizacionVencimientoBackgroundService detenido");
        }

        private TimeSpan CalcularTiempoEsperaHastaSiguienteEjecucion()
        {
            var ahora = DateTime.UtcNow;
            var proximaEjecucion = ahora.Date.Add(_horaEjecucion);

            if (ahora >= proximaEjecucion)
                proximaEjecucion = proximaEjecucion.AddDays(1);

            var tiempoEspera = proximaEjecucion - ahora;

            _logger.LogInformation(
                "Próxima ejecución de vencimiento programada para: {FechaHora} ({MinutosRestantes} minutos)",
                proximaEjecucion.ToString("dd/MM/yyyy HH:mm:ss"),
                (int)tiempoEspera.TotalMinutes);

            return tiempoEspera;
        }

        private async Task EsperarUnDiaAsync(CancellationToken stoppingToken)
        {
            var tiempoEspera = CalcularTiempoEsperaHastaSiguienteEjecucion();
            await Task.Delay(tiempoEspera, stoppingToken);
        }

        private async Task EsperarHastaProximaEjecucionAsync(CancellationToken stoppingToken)
        {
            var tiempoEspera = CalcularTiempoEsperaHastaSiguienteEjecucion();

#if DEBUG
            var tiempoEsperaMaximo = TimeSpan.FromSeconds(30);
            if (tiempoEspera > tiempoEsperaMaximo)
            {
                _logger.LogWarning(
                    "Modo DEBUG: Esperando máximo 30 segundos en lugar de {MinutosRestantes} minutos",
                    (int)tiempoEspera.TotalMinutes);
                tiempoEspera = tiempoEsperaMaximo;
            }
#endif

            await Task.Delay(tiempoEspera, stoppingToken);
        }
    }
}
