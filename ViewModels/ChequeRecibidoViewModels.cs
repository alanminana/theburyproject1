using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>Fila de la lista informativa "Cheques recibidos" (pago pendiente; el ERP no hace seguimiento de acreditación).</summary>
public class ChequePendienteViewModel
{
    public int MovimientoId { get; set; }
    public int AperturaId { get; set; }
    public bool AperturaCerrada { get; set; }
    public int? VentaId { get; set; }
    public string? Referencia { get; set; }
    public string Origen { get; set; } = "Venta";
    public string? Banco { get; set; }
    public string? NumeroCheque { get; set; }
    public string? Titular { get; set; }
    /// <summary>Vencimiento del cheque. Null en cheques de cuota (el dato viaja en <see cref="Detalle"/>).</summary>
    public DateTime? FechaVencimiento { get; set; }
    public decimal Monto { get; set; }
    public string? Detalle { get; set; }
    /// <summary>Días hasta el vencimiento (negativo = vencido). Null si no hay fecha.</summary>
    public int? DiasParaVencer { get; set; }
    public bool PorVencer { get; set; }
    public bool Vencido { get; set; }
    /// <summary>Solo en cheques ya acreditados: cuando y quien los acredito.</summary>
    public DateTime? AcreditadoAt { get; set; }
    public string? AcreditadoPor { get; set; }
}

/// <summary>Tarjeta "Cheque" de Venta/Details.</summary>
public class ChequeVentaEstadoViewModel
{
    public string NumeroCheque { get; set; } = string.Empty;
    public string Banco { get; set; } = string.Empty;
    public string Titular { get; set; } = string.Empty;
    public string? CUIT { get; set; }
    public DateTime FechaEmision { get; set; }
    public DateTime FechaVencimiento { get; set; }
    public decimal Monto { get; set; }
    public string? Observaciones { get; set; }

    /// <summary>Pendiente / Acreditado / Rechazado / Anulado / Sin cobrar (todavía sin ingreso en caja).</summary>
    public string Estado { get; set; } = "Sin cobrar";
    public int? MovimientoId { get; set; }
    public int? AperturaId { get; set; }
    public DateTime? AcreditadoAt { get; set; }
    public string? AcreditadoPor { get; set; }
    public DateTime? RechazadoAt { get; set; }
    public string? RechazadoPor { get; set; }
    public string? MotivoRechazo { get; set; }
    public bool Rechazado => RechazadoAt.HasValue;
}
