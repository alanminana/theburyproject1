using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.ViewModels.PagoCuota;

/// <summary>Único modelo bindable del pago individual.</summary>
public sealed class PagarCuotaInputModel
{
    [Display(Name = "Importe ingresado")]
    [Required(ErrorMessage = "El importe es obligatorio.")]
    // ParseLimitsInInvariantCulture es obligatorio: el servidor corre con cultura es-AR y
    // RangeAttribute parsea estos límites con CurrentCulture. Sin esto, "0.01" no se puede
    // convertir a decimal y el tag helper de input revienta al generar los atributos de
    // validación cliente — la pantalla entera devuelve HTTP 500. Mismo patrón de defensa que
    // DecimalModelBinder / DateOnlyModelBinder.
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "El importe debe ser mayor a cero.")]
    [ModelBinder(typeof(DecimalModelBinder))]
    public decimal MontoIngresado { get; set; }

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

public sealed class PagarCuotaPageViewModel
{
    public required PagoCuotaContextoViewModel Contexto { get; init; }
    public required PagarCuotaInputModel Input { get; init; }
    public PagoCuotaPreviewViewModel? Preview { get; init; }
    public PagoCuotaResultadoViewModel? Resultado { get; init; }
}

public sealed class PagoCuotaContextoViewModel
{
    public required int CuotaId { get; init; }
    public required int CreditoId { get; init; }
    public required int NumeroCuota { get; init; }
    public required string NumeroCredito { get; init; }
    public required string ClienteNombre { get; init; }
    public required DateOnly FechaVencimiento { get; init; }
    public required DateOnly FechaComercial { get; init; }
    public required EstadoCuota Estado { get; init; }
    public required int DiasAtraso { get; init; }
    public required decimal CapitalPendiente { get; init; }
    public decimal? PunitorioCalculadoInformativo { get; init; }
    public required EstadoCalculoPunitorioDetalle EstadoCalculoPunitorio { get; init; }
    public string? MotivoNoCalculoPunitorio { get; init; }
    public decimal? PunitorioAplicadoPendiente { get; init; }
    public decimal? TotalCobrableActual { get; init; }
    public required bool HistorialCompleto { get; init; }
    public string? MotivoHistorialIncompleto { get; init; }
    public required string CuotaRowVersionBase64 { get; init; }
    public bool PuedeConfirmar => TotalCobrableActual is > 0m && PunitorioAplicadoPendiente.HasValue;
}

public sealed class PagoCuotaPreviewViewModel
{
    public required decimal ImporteIngresado { get; init; }
    public required decimal AplicadoPunitorio { get; init; }
    public required decimal AplicadoCapital { get; init; }
    public required decimal Excedente { get; init; }
    public required decimal RecargoMedioPago { get; init; }
    public required decimal TotalCaja { get; init; }
    public required decimal PunitorioRestante { get; init; }
    public required decimal CapitalRestante { get; init; }
    public required EstadoCuota EstadoEstimado { get; init; }
    public required string EstadoEstimadoTexto { get; init; }
    public required DateOnly FechaComercial { get; init; }
    public required string CuotaRowVersionBase64 { get; init; }
}

public sealed class PagoCuotaResultadoViewModel
{
    public required int CuotaId { get; init; }
    public required decimal ImporteRecibido { get; init; }
    public required decimal AplicadoPunitorio { get; init; }
    public required decimal AplicadoCapital { get; init; }
    public required decimal RecargoMedioPago { get; init; }
    public required decimal TotalCaja { get; init; }
    public required decimal PunitorioRestante { get; init; }
    public required decimal CapitalRestante { get; init; }
    public required EstadoCuota EstadoFinal { get; init; }
    public required string EstadoFinalTexto { get; init; }
    public required DateOnly FechaComercial { get; init; }
    public required int MovimientoCajaId { get; init; }
    public required int PagoCuotaId { get; init; }
    public required string MedioPago { get; init; }
}
