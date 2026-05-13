using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

public sealed class ConfiguracionPagoGlobalAdminViewModel
{
    public List<MedioPagoGlobalAdminViewModel> Medios { get; set; } = new();

    public int TotalMedios => Medios.Count;
    public int TotalTarjetas => Medios.Sum(m => m.Tarjetas.Count);
    public int TotalPlanes => Medios.Sum(m => m.Planes.Count);
    public int TotalInactivos => Medios.Count(m => !m.Activo)
        + Medios.Sum(m => m.Tarjetas.Count(t => !t.Activa))
        + Medios.Sum(m => m.Planes.Count(p => !p.Activo));
}

public sealed class MedioPagoGlobalAdminViewModel
{
    public int Id { get; set; }
    public TipoPago TipoPago { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool Activo { get; set; }
    public bool PermiteDescuento { get; set; }
    public decimal? PorcentajeDescuentoMaximo { get; set; }
    public bool TieneRecargo { get; set; }
    public decimal? PorcentajeRecargo { get; set; }
    public List<TarjetaGlobalAdminViewModel> Tarjetas { get; set; } = new();
    public List<PlanPagoGlobalAdminViewModel> Planes { get; set; } = new();
}

public sealed class TarjetaGlobalAdminViewModel
{
    public int Id { get; set; }
    public int ConfiguracionPagoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public TipoTarjeta TipoTarjeta { get; set; }
    public bool Activa { get; set; }
    public bool PermiteCuotas { get; set; }
    public int? CantidadMaximaCuotas { get; set; }
    public TipoCuotaTarjeta? TipoCuota { get; set; }
    public decimal? TasaInteresesMensual { get; set; }
    public bool TieneRecargoDebito { get; set; }
    public decimal? PorcentajeRecargoDebito { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class PlanPagoGlobalAdminViewModel
{
    public int Id { get; set; }
    public int ConfiguracionPagoId { get; set; }
    public int? ConfiguracionTarjetaId { get; set; }
    public string? NombreTarjeta { get; set; }
    public TipoPago TipoPago { get; set; }
    public int CantidadCuotas { get; set; }
    public bool Activo { get; set; }
    public TipoAjustePagoPlan TipoAjuste { get; set; }
    public decimal AjustePorcentaje { get; set; }
    public string? Etiqueta { get; set; }
    public int Orden { get; set; }
    public string? Observaciones { get; set; }

    public string Alcance => ConfiguracionTarjetaId.HasValue
        ? NombreTarjeta ?? $"Tarjeta #{ConfiguracionTarjetaId.Value}"
        : "Medio general";

    public string AjusteDescripcion => AjustePorcentaje switch
    {
        > 0m => $"Recargo {AjustePorcentaje:0.##}%",
        < 0m => $"Descuento {Math.Abs(AjustePorcentaje):0.##}%",
        _ => "Sin ajuste"
    };
}

public sealed class PlanPagoGlobalCommandViewModel
{
    [Required]
    public int ConfiguracionPagoId { get; set; }

    public int? ConfiguracionTarjetaId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "La cantidad de cuotas debe ser al menos 1.")]
    public int CantidadCuotas { get; set; } = 1;

    public bool Activo { get; set; } = true;

    [Required]
    public TipoAjustePagoPlan TipoAjuste { get; set; } = TipoAjustePagoPlan.Porcentaje;

    [Range(typeof(decimal), "-100.0000", "999.9999", ErrorMessage = "El porcentaje debe estar entre -100.0000 y 999.9999.")]
    public decimal AjustePorcentaje { get; set; }

    [StringLength(100, ErrorMessage = "La etiqueta no puede superar 100 caracteres.")]
    public string? Etiqueta { get; set; }

    public int Orden { get; set; }

    [StringLength(500, ErrorMessage = "Las observaciones no pueden superar 500 caracteres.")]
    public string? Observaciones { get; set; }
}
