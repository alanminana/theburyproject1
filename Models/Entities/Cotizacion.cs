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

    /// <summary>
    /// Anticipo simulado para Credito personal (intencion, no snapshot calculado): se aplica antes
    /// del recargo. 0 cuando no se cotizo Credito personal o no se ingreso anticipo. Se conserva al
    /// convertir a venta para precargar Configurar Venta (Credito.AnticipoPreseleccionado).
    /// </summary>
    public decimal Anticipo { get; set; }

    public DateTime? FechaVencimiento { get; set; }

    [StringLength(500)]
    public string? MotivoCancelacion { get; set; }

    public virtual Cliente? Cliente { get; set; }
    public virtual ICollection<CotizacionDetalle> Detalles { get; set; } = new List<CotizacionDetalle>();
    public virtual ICollection<CotizacionPagoSimulado> OpcionesPago { get; set; } = new List<CotizacionPagoSimulado>();
}
