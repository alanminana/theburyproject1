using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// H2-C2: ejecución diaria del vencimiento de borradores de crédito abandonados. Mismo patrón que
    /// <see cref="AcuerdoIncumplimientoBackgroundService"/>: se pone al día al arrancar y corre una vez por fecha
    /// comercial. Es seguro con varias instancias porque cada cancelación se serializa con el lock de crédito/venta y se
    /// re-verifica bajo lock (la segunda instancia encuentra el borrador ya cancelado y lo omite).
    /// </summary>
    public class BorradorCreditoVencimientoBackgroundService : BackgroundService
    {
        private static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan IntervaloDeRevision = TimeSpan.FromHours(1);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BorradorCreditoVencimientoBackgroundService> _logger;
        private readonly bool _habilitado;
        private DateOnly? _ultimaFechaEjecutada;

        /// <summary>
        /// Interruptor de arranque (<c>BorradoresCredito:VencimientoAutomaticoHabilitado</c>). Apagado por defecto: el job
        /// cancela ventas y créditos reales, así que solo corre si el propietario lo habilita de forma explícita.
        /// </summary>
        public const string ClaveHabilitado = "BorradoresCredito:VencimientoAutomaticoHabilitado";

        public BorradorCreditoVencimientoBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<BorradorCreditoVencimientoBackgroundService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            // Falla cerrado: solo un "true" válido lo habilita; un valor mal escrito lo deja apagado (y no tumba el arranque).
            var valor = configuration[ClaveHabilitado];
            _habilitado = bool.TryParse(valor, out var habilitado) && habilitado;
            if (!string.IsNullOrWhiteSpace(valor) && !bool.TryParse(valor, out _))
                _logger.LogWarning(
                    "{Clave} tiene un valor inválido ('{Valor}'): el vencimiento de borradores queda deshabilitado", ClaveHabilitado, valor);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_habilitado)
            {
                _logger.LogInformation(
                    "BorradorCreditoVencimientoBackgroundService deshabilitado ({Clave}=false): no se vencen borradores", ClaveHabilitado);
                return;
            }

            _logger.LogInformation("BorradorCreditoVencimientoBackgroundService iniciado");
            try
            {
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

        /// <summary>Ejecuta el vencimiento una vez por fecha comercial. Devuelve true si corrió.</summary>
        internal async Task<bool> EjecutarSiCorrespondeAsync(CancellationToken cancellationToken)
        {
            if (!_habilitado)
                return false;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
                var hoy = reloj.HoyComercial;
                if (_ultimaFechaEjecutada == hoy)
                    return false;

                var resultado = await scope.ServiceProvider.GetRequiredService<IBorradorCreditoVencimientoService>()
                    .VencerBorradoresAsync(reloj.AhoraUtc, cancellationToken);

                // Con errores no se da el día por cumplido: se reintenta en la próxima revisión (cada hora).
                if (resultado.Fallidos == 0)
                    _ultimaFechaEjecutada = hoy;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el vencimiento diario de borradores de crédito");
                return false;
            }
        }
    }
}
