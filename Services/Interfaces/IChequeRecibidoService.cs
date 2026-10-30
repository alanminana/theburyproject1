using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Ciclo de vida del cheque RECIBIDO de un cliente (venta o cuota): acreditación manual y rechazo, asentados en
/// la caja abierta vigente cuando la caja del ingreso original ya cerró. No toca el cheque EMITIDO a proveedores
/// (<see cref="IChequeService"/>).
/// </summary>
public interface IChequeRecibidoService
{
    /// <summary>
    /// Acredita un cheque Pendiente. Con la caja del ingreso abierta cambia el estado en el lugar; con la caja
    /// cerrada asienta un NUEVO ingreso bancario (no físico) en la caja abierta del usuario, que referencia al
    /// original, y marca el original Acreditado sin tocar el resto de la caja cerrada. Idempotente y bajo lock.
    /// </summary>
    Task<ChequeAccionResultado> AcreditarChequeAsync(int movimientoId, string usuario);

    /// <summary>
    /// Rechaza un cheque Pendiente o Acreditado con motivo obligatorio. Pendiente: queda Rechazado. Acreditado:
    /// además genera un egreso de contrapartida (no físico) en la caja abierta vigente. La venta no se cancela.
    /// </summary>
    Task<ChequeAccionResultado> RechazarChequeAsync(int movimientoId, string motivo, string usuario);

    /// <summary>Cheques recibidos Pendientes, por vencimiento (los de venta con fecha primero; los de cuota después).</summary>
    Task<List<ChequePendienteViewModel>> ObtenerChequesPendientesAsync();

    /// <summary>Estado del cheque de una venta (datos + acreditación/rechazo), o null si la venta no tiene cheque.</summary>
    Task<ChequeVentaEstadoViewModel?> ObtenerEstadoChequeVentaAsync(int ventaId);
}

public sealed record ChequeAccionResultado(
    int MovimientoOrigenId,
    int? MovimientoAsentadoId,
    int AperturaAsentadaId,
    EstadoAcreditacionMovimientoCaja EstadoOrigen);
