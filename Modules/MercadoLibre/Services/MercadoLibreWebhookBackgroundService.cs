using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    /// <summary>
    /// Consume MercadoLibreWebhookEvents pendientes cada 30 segundos (Fase J).
    /// El request del webhook nunca se bloquea: el trabajo real pasa por acá.
    /// Mismo patrón que AlertaStockBackgroundService.
    /// </summary>
    public class MercadoLibreWebhookBackgroundService : BackgroundService
    {
        private static readonly TimeSpan IntervaloEjecucion = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(20);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MercadoLibreWebhookBackgroundService> _logger;

        public MercadoLibreWebhookBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<MercadoLibreWebhookBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "MercadoLibreWebhookBackgroundService iniciado. Intervalo: {Segundos}s",
                IntervaloEjecucion.TotalSeconds);

            try
            {
                await Task.Delay(EsperaInicial, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var processor = scope.ServiceProvider.GetRequiredService<IMercadoLibreWebhookProcessor>();

                        var procesados = await processor.ProcesarPendientesAsync(50, stoppingToken);

                        if (procesados > 0)
                            _logger.LogInformation("Webhooks ML procesados: {Cantidad}", procesados);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error en el ciclo de procesamiento de webhooks ML");
                    }

                    await Task.Delay(IntervaloEjecucion, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown normal
            }
            finally
            {
                _logger.LogInformation("MercadoLibreWebhookBackgroundService detenido");
            }
        }
    }
}
