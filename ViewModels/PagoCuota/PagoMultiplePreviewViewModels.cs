using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.PagoCuota;

/// <summary>
/// PUN-ML9-E: request del preview de pago múltiple. Sólo intención — el servidor recalcula
/// capital y punitorio aplicado pendiente de cada cuota, nunca toma importes del navegador.
/// </summary>
public sealed class PagoMultiplePreviewRequestViewModel
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
}

public sealed class PagoMultiplePreviewCuotaViewModel
{
    public required int CuotaId { get; init; }
    public required int CreditoId { get; init; }
    public required string CreditoNumero { get; init; }
    public required int NumeroCuota { get; init; }
    public required decimal CapitalPendiente { get; init; }
    public required decimal PunitorioAplicadoPendiente { get; init; }
    public required decimal Total { get; init; }
    public required decimal RecargoMedioPago { get; init; }
    public required decimal TotalCaja { get; init; }
    public required string CuotaRowVersionBase64 { get; init; }
}

public sealed class PagoMultiplePreviewViewModel
{
    public required int ClienteId { get; init; }
    public required List<PagoMultiplePreviewCuotaViewModel> Cuotas { get; init; }
    public required decimal CapitalTotal { get; init; }
    public required decimal PunitorioTotal { get; init; }
    public required decimal RecargoTotal { get; init; }
    public required decimal TotalCaja { get; init; }
    public required DateOnly FechaComercial { get; init; }
}
