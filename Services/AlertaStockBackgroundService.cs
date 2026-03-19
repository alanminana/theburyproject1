using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Servicio en background para generar alertas de stock automáticamente.
    /// Se ejecuta cada 2 horas para detectar productos con stock bajo.
    /// </summary>
    public class AlertaStockBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AlertaStockBackgroundService> _logger;

        private readonly TimeSpan _intervaloEjecucion = TimeSpan.FromHours(2);
        private readonly TimeSpan _intervaloLimpieza = TimeSpan.FromDays(1);
        private DateTime _ultimaLimpieza = DateTime.MinValue;

        public AlertaStockBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<AlertaStockBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "AlertaStockBackgroundService iniciado. Ejecutará cada {Intervalo} horas",
                _intervaloEjecucion.TotalHours);

            try
            {
                // Esperar 30 segundos antes de la primera ejecución (para que la app termine de iniciar)
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await GenerarAlertasStockAsync(stoppingToken);

                        // Limpieza de alertas antiguas una vez al día
                        if ((DateTime.UtcNow - _ultimaLimpieza) >= _intervaloLimpieza)
                        {
                            await LimpiarAlertasAntiguasAsync(stoppingToken);
                            _ultimaLimpieza = DateTime.UtcNow;
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // Shutdown normal
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error en AlertaStockBackgroundService");
                    }

                    _logger.LogInformation(
                        "Próxima verificación de stock en {Minutos} minutos",
                        _intervaloEjecucion.TotalMinutes);

                    await Task.Delay(_intervaloEjecucion, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown normal durante el delay inicial
            }
            finally
            {
                _logger.LogInformation("AlertaStockBackgroundService detenido");
            }
        }

        private async Task GenerarAlertasStockAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alertaStockService = scope.ServiceProvider.GetRequiredService<IAlertaStockService>();

                _logger.LogInformation("Iniciando verificación de stock.");
                var alertasCreadas = await alertaStockService.GenerarAlertasStockBajoAsync();

                if (alertasCreadas > 0)
                {
                    _logger.LogWarning("Se generaron {AlertasCreadas} nuevas alertas de stock", alertasCreadas);
                }
                else
                {
                    _logger.LogInformation("Verificación completada. No se generaron nuevas alertas");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown normal
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar alertas de stock");
            }
        }

        private async Task LimpiarAlertasAntiguasAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alertaStockService = scope.ServiceProvider.GetRequiredService<IAlertaStockService>();

                _logger.LogInformation("Iniciando limpieza de alertas antiguas.");
                var alertasEliminadas = await alertaStockService.LimpiarAlertasAntiguasAsync(30);

                if (alertasEliminadas > 0)
                {
                    _logger.LogInformation(
                        "Limpieza completada. Se eliminaron {AlertasEliminadas} alertas antiguas",
                        alertasEliminadas);
                }
                else
                {
                    _logger.LogInformation("Limpieza completada. No hay alertas antiguas para eliminar");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown normal
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al limpiar alertas antiguas");
            }
        }
    }
}
