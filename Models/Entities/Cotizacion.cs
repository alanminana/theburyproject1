using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Models.Entities;

public class Cotizacion : AuditableEntity
{
    [Required]
    [StringLength(50)]
    public string Numero { get; set; } = string.Empty;

    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    public EstadoCotizacion Estado { get; set; } = EstadoCotizacion.Emitida;

    public int? ClienteId { get; set; }

    [StringLength(200)]
    public string? NombreClienteLibre { get; set; }

    [StringLength(30)]
    public string? TelefonoClienteLibre { get; set; }

    [StringLength(1000)]
    public string? Observaciones { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DescuentoTotal { get; set; }
    public decimal TotalBase { get; set; }

    public CotizacionMedioPagoTipo? MedioPagoSeleccionado { get; set; }

    [StringLength(200)]
    public string? PlanSeleccionado { get; set; }

    public int? CantidadCuotasSeleccionada { get; set; }
    public decimal? TotalSeleccionado { get; set; }
    public decimal? ValorCuotaSeleccionada { get; set; }
    public DateTime? FechaVencimiento { get; set; }

    public virtual Cliente? Cliente { get; set; }
    public virtual ICollection<CotizacionDetalle> Detalles { get; set; } = new List<CotizacionDetalle>();
    public virtual ICollection<CotizacionPagoSimulado> OpcionesPago { get; set; } = new List<CotizacionPagoSimulado>();
}
