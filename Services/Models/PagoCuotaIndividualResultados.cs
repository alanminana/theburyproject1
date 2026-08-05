using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

/// <summary>
/// Intención de cobro individual recibida por la capa de servicio. Los importes derivados,
/// la fecha, la distribución y las identidades se resuelven exclusivamente en el servidor.
/// </summary>
public sealed record PagoCuotaIndividualComando(
    int CuotaId,
    decimal MontoIngresado,
    string MedioPago,
    string? Comprobante,
    string? Observaciones,
    byte[] CuotaRowVersionEsperada);

/// <summary>Lectura autoritativa necesaria para renderizar el pago de una cuota.</summary>
public sealed record PagoCuotaContextoResultado(
    int CuotaId,
    int CreditoId,
    int NumeroCuota,
    string NumeroCredito,
    string ClienteNombre,
    DateOnly FechaVencimiento,
    DateOnly FechaComercial,
    EstadoCuota Estado,
    int DiasAtraso,
    decimal CapitalPendiente,
    decimal? PunitorioCalculadoInformativo,
    EstadoCalculoPunitorioDetalle EstadoCalculoPunitorio,
    string? MotivoNoCalculoPunitorio,
    decimal? PunitorioAplicadoPendiente,
    decimal? TotalCobrableActual,
    bool HistorialCompleto,
    string? MotivoHistorialIncompleto,
    string CuotaRowVersionBase64);

/// <summary>
/// Preview read-only calculada por el mismo distribuidor y la misma política de recargo que la
/// confirmación. No crea ledger ni movimiento de caja.
/// </summary>
public sealed record PagoCuotaPreviewResultado(
    int CuotaId,
    decimal ImporteIngresado,
    decimal AplicadoPunitorio,
    decimal AplicadoCapital,
    decimal Excedente,
    decimal RecargoMedioPago,
    decimal TotalCaja,
    decimal PunitorioRestante,
    decimal CapitalRestante,
    EstadoCuota EstadoEstimado,
    DateOnly FechaComercial,
    string CuotaRowVersionBase64);

/// <summary>Resultado persistido real del cobro individual.</summary>
public sealed record PagoCuotaResultado(
    int CuotaId,
    int CreditoId,
    int NumeroCuota,
    decimal ImporteRecibido,
    decimal AplicadoPunitorio,
    decimal AplicadoCapital,
    decimal RecargoMedioPago,
    decimal TotalCaja,
    decimal PunitorioRestante,
    decimal CapitalRestante,
    EstadoCuota EstadoFinal,
    DateOnly FechaComercial,
    int MovimientoCajaId,
    int PagoCuotaId,
    string MedioPago,
    string CuotaRowVersionBase64);

/// <summary>
/// PUN-ML9-E: intención de adelanto recibida por la capa de servicio. A diferencia del pago
/// individual no lleva <c>CuotaId</c> ni importe: el servidor siempre resuelve la última cuota
/// pendiente del plan y cancela su saldo completo (capital + punitorio aplicado pendiente).
/// </summary>
public sealed record AdelantoCuotaComando(
    int CreditoId,
    string MedioPago,
    string? Comprobante,
    string? Observaciones,
    byte[] CuotaRowVersionEsperada);

/// <summary>
/// PUN-ML9-E: composición autoritativa de una cuota dentro de un preview o confirmación de pago
/// múltiple. Reusa la misma prioridad punitorio→capital que el pago individual y el adelanto.
/// </summary>
public sealed record PagoMultiplePreviewCuotaResultado(
    int CuotaId,
    int CreditoId,
    string CreditoNumero,
    int NumeroCuota,
    decimal CapitalPendiente,
    decimal PunitorioAplicadoPendiente,
    decimal Total,
    decimal RecargoMedioPago,
    decimal TotalCaja,
    string CuotaRowVersionBase64);

/// <summary>Preview read-only del pago múltiple: composición por cuota + agregados.</summary>
public sealed record PagoMultiplePreviewResultado(
    int ClienteId,
    IReadOnlyList<PagoMultiplePreviewCuotaResultado> Cuotas,
    decimal CapitalTotal,
    decimal PunitorioTotal,
    decimal RecargoTotal,
    decimal TotalCaja,
    DateOnly FechaComercial);
