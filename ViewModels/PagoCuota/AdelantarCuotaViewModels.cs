using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.PagoCuota;

/// <summary>
/// PUN-ML9-E: único modelo bindable del adelanto. A diferencia del pago individual no lleva un
/// importe: el adelanto siempre cancela el saldo completo de la última cuota pendiente, que el
/// servidor resuelve — el navegador nunca controla monto, cuota ni fecha.
/// </summary>
public sealed class AdelantoCuotaInputModel
{
    [Display(Name = "Medio de pago")]
    [Required(ErrorMessage = "El medio de pago es obligatorio.")]
    [StringLength(50)]
    public string MedioPago { get; set; } = "Efectivo";

    [Display(Name = "Número de comprobante")]
    [StringLength(100, ErrorMessage = "El comprobante admite hasta 100 caracteres.")]
    public string? Comprobante { get; set; }

    [Display(Name = "Observaciones")]
    [StringLength(500, ErrorMessage = "Las observaciones admiten hasta 500 caracteres.")]
    public string? Observaciones { get; set; }

    [Required(ErrorMessage = "La versión de la cuota es obligatoria. Recargá la pantalla.")]
    public string CuotaRowVersionBase64 { get; set; } = string.Empty;
}

public sealed class AdelantarCuotaPageViewModel
{
    public required PagoCuotaContextoViewModel Contexto { get; init; }
    public required AdelantoCuotaInputModel Input { get; init; }
    public PagoCuotaPreviewViewModel? Preview { get; init; }
    public PagoCuotaResultadoViewModel? Resultado { get; init; }
}
