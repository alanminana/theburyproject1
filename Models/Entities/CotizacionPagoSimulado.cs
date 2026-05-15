using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Models.Entities;

public class CotizacionPagoSimulado : AuditableEntity
{
    public int CotizacionId { get; set; }

    public CotizacionMedioPagoTipo MedioPago { get; set; }

    [Required]
    [StringLength(100)]
    public string NombreMedioPago { get; set; } = string.Empty;

    public CotizacionOpcionPagoEstado Estado { get; set; }

    [StringLength(200)]
    public string? Plan { get; set; }

    public int? CantidadCuotas { get; set; }
    public decimal RecargoPorcentaje { get; set; }
    public decimal DescuentoPorcentaje { get; set; }
    public decimal InteresPorcentaje { get; set; }
    public decimal? TasaMensual { get; set; }
    public decimal? CostoFinancieroTotal { get; set; }
    public decimal Total { get; set; }
    public decimal? ValorCuota { get; set; }
    public bool Recomendado { get; set; }
    public bool Seleccionado { get; set; }
    public string? AdvertenciasJson { get; set; }

    public virtual Cotizacion Cotizacion { get; set; } = null!;
}
