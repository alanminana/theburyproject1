using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// H2-C2: vencimiento de borradores de crédito abandonados. Entrar al paso Crédito de Venta/Create crea una venta
    /// en PendienteFinanciacion con su crédito en PendienteConfiguracion, que reserva cupo del cliente. Si se abandona,
    /// este servicio la cancela con el mismo mecanismo que la baja manual (<see cref="IVentaService.VencerBorradorCreditoAsync"/>
    /// -> cancelación de venta -> crédito Cancelado, SaldoPendiente 0), de modo que el cupo se libera con las mismas reglas.
    /// Cada borrador se procesa en su propia transacción y se re-verifica bajo lock: dos instancias a la vez no
    /// cancelan dos veces, y una venta que ganó una operación real entre la consulta y el lock no se toca.
    /// </summary>
    public class BorradorCreditoVencimientoService : IBorradorCreditoVencimientoService
    {
        private readonly AppDbContext _context;
        private readonly IVentaService _ventaService;
        private readonly ILogger<BorradorCreditoVencimientoService> _logger;

        public BorradorCreditoVencimientoService(
            AppDbContext context,
            IVentaService ventaService,
            ILogger<BorradorCreditoVencimientoService> logger)
        {
            _context = context;
            _ventaService = ventaService;
            _logger = logger;
        }

        public async Task<BorradorCreditoVencimientoResultado> VencerBorradoresAsync(
            DateTime ahoraUtc, CancellationToken cancellationToken = default)
        {
            var config = await _context.ConfiguracionesCredito
                .AsNoTracking()
                .OrderByDescending(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // Sin fila de configuración rige el valor por defecto provisorio; null/<1 en la fila = desactivado.
            var plazo = config == null
                ? ConfiguracionCredito.DiasVencimientoBorradorCreditoPorDefecto
                : config.DiasVencimientoBorradorCredito ?? 0;

            if (plazo < 1)
            {
                _logger.LogInformation("Vencimiento de borradores de crédito desactivado (DiasVencimientoBorradorCredito no configurado).");
                return new BorradorCreditoVencimientoResultado(false, plazo, 0, Array.Empty<int>(), 0, 0);
            }

            var limite = ahoraUtc.AddDays(-plazo);

            // Prefiltro barato; la decisión final se toma bajo lock en VencerBorradorCreditoAsync.
            var candidatos = await _context.Ventas
                .AsNoTracking()
                .Where(v => !v.IsDeleted
                            && v.Estado == EstadoVenta.PendienteFinanciacion
                            && v.TipoPago == TipoPago.CreditoPersonal
                            && v.CreditoId != null
                            && v.Credito!.Estado == EstadoCredito.PendienteConfiguracion
                            && (v.UpdatedAt ?? v.CreatedAt) <= limite)
                .OrderBy(v => v.Id)
                .Select(v => new { v.Id, v.Numero })
                .ToListAsync(cancellationToken);

            var vencidas = new List<int>();
            var omitidos = 0;
            var fallidos = 0;

            foreach (var candidato in candidatos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var motivo = $"Borrador de crédito vencido: sin actividad por {plazo} día(s) (cancelación automática).";
                    if (await _ventaService.VencerBorradorCreditoAsync(candidato.Id, limite, motivo))
                    {
                        vencidas.Add(candidato.Id);
                        _logger.LogInformation(
                            "AUDITORIA borrador de crédito vencido: venta {VentaId} ({Numero}) cancelada automáticamente por Sistema; plazo {Plazo} día(s), límite {Limite:O}. Cupo liberado.",
                            candidato.Id, candidato.Numero, plazo, limite);
                    }
                    else
                    {
                        omitidos++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Una guardia de la cancelación (anticipo cobrado, pagos, devoluciones) o un conflicto de
                    // concurrencia deja el borrador intacto; se reintenta en la próxima corrida.
                    fallidos++;
                    _logger.LogWarning(ex, "No se pudo vencer el borrador de crédito de la venta {VentaId}; se omite.", candidato.Id);
                    _context.ChangeTracker.Clear();
                }
            }

            _logger.LogInformation(
                "Vencimiento de borradores de crédito: plazo {Plazo} día(s), {Evaluados} evaluados, {Vencidos} vencidos, {Omitidos} omitidos, {Fallidos} con error.",
                plazo, candidatos.Count, vencidas.Count, omitidos, fallidos);

            return new BorradorCreditoVencimientoResultado(true, plazo, candidatos.Count, vencidas, omitidos, fallidos);
        }
    }
}
