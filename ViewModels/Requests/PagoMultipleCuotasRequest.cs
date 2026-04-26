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
}
