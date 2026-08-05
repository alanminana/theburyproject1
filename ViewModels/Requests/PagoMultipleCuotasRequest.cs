using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.Requests;

public class PagoMultipleCuotasRequest
{
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "El cliente es requerido.")]
    public int ClienteId { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Debe seleccionar al menos una cuota.")]
    public List<int> CuotaIds { get; set; } = new();

    /// <summary>
    /// PUN-ML9-E: RowVersion (Base64) esperado por cuota, capturado en el preview/listado. El
    /// servidor rechaza toda la operación (rollback total, 409) si falta una entrada o si no
    /// coincide con el valor real al momento de confirmar — protección de doble envío/concurrencia
    /// equivalente a <c>CuotaRowVersionBase64</c> en el pago individual y el adelanto.
    /// </summary>
    [Required]
    public Dictionary<int, string> RowVersionsPorCuota { get; set; } = new();

    [Required]
    [StringLength(50)]
    public string MedioPago { get; set; } = "Efectivo";

    [StringLength(500)]
    public string? Observaciones { get; set; }
}

public class PagoMultipleCuotasResult
{
    public int ClienteId { get; set; }
    public List<int> CuotaIds { get; set; } = new();
    public List<int> CreditoIds { get; set; } = new();
    public int CantidadCuotas { get; set; }
    public int CantidadCreditos { get; set; }
    public decimal Subtotal { get; set; }
    public decimal MoraTotal { get; set; }
    public decimal TotalPagado { get; set; }

    /// <summary>PUN-ML9-E: recargo del medio de pago sumado de todas las cuotas — no salda deuda.</summary>
    public decimal RecargoTotal { get; set; }

    /// <summary>PUN-ML9-E: total realmente movido en caja (TotalPagado + RecargoTotal).</summary>
    public decimal TotalCaja { get; set; }

    public DateTime FechaPago { get; set; }
    public List<PagoMultipleCuotaResult> Cuotas { get; set; } = new();
}

public class PagoMultipleCuotaResult
{
    public int CuotaId { get; set; }
    public int CreditoId { get; set; }
    public string CreditoNumero { get; set; } = string.Empty;
    public int NumeroCuota { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Mora { get; set; }
    public decimal TotalPagado { get; set; }
    public string Estado { get; set; } = string.Empty;

    /// <summary>PUN-ML9-E: fila de ledger generada para esta cuota (una por cuota, nunca compartida).</summary>
    public int PagoCuotaId { get; set; }

    /// <summary>PUN-ML9-E: recargo del medio de pago asignado a esta cuota — cargo separado, no salda deuda.</summary>
    public decimal RecargoMedioPago { get; set; }

    /// <summary>PUN-ML9-E: total realmente movido en caja para esta cuota (TotalPagado + RecargoMedioPago).</summary>
    public decimal TotalCaja { get; set; }
}
