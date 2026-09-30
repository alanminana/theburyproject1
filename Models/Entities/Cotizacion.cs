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

    [StringLength(8)]
    public string? DniClienteLibre { get; set; }

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

    /// <summary>
    /// Intención de envío a domicilio declarada en el simulador. No genera ninguna
    /// entidad propia en Cotización: es sólo la señal que, al convertir a venta,
    /// hace que la Venta nazca con un VentaEnvio en estado Pendiente.
    /// </summary>
    public bool TieneEnvio { get; set; }

    /// <summary>
    /// Importe del envío declarado en el simulador (null/0 = sin cargo). Sólo tiene sentido con
    /// <see cref="TieneEnvio"/>. Es un importe separado del precio de los productos: no entra en
    /// Subtotal/DescuentoTotal/TotalBase ni en el total de ninguna opción de pago (no lleva recargo del
    /// plan). Al convertir a venta viaja a <c>VentaEnvio.CostoEnvio</c>.
    /// </summary>
    public decimal? CostoEnvio { get; set; }

    /// <summary>Tipo de envío (Ciudad/Rural) que fijó el precio global. Null en cotizaciones anteriores.</summary>
    public Enums.TipoServicioVenta? TipoEnvio { get; set; }

    /// <summary>Suma de los armados por unidad de todas las líneas (snapshot; 0 si no hay).</summary>
    public decimal ImporteArmados { get; set; }

    /// <summary>
    /// true = TotalBase y los totales de las opciones ya incluyen armados y envío (modelo nuevo, el
    /// recargo del plan los alcanza). false = cotización anterior: el envío se suma aparte.
    /// </summary>
    public bool EnvioIncluidoEnTotal { get; set; }

    /// <summary>Total de la opción elegida (o TotalBase) + envío sólo cuando se cobra aparte (legacy).</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal TotalACobrar => Helpers.VentaMontos.CalcularTotalACobrar(
        TotalSeleccionado ?? TotalBase, EnvioIncluidoEnTotal ? 0m : ImporteEnvio);

    /// <summary>Importe de envío efectivo (nunca negativo). Ver <see cref="Helpers.VentaMontos"/>.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal ImporteEnvio => TieneEnvio ? Helpers.VentaMontos.NormalizarImporteEnvio(CostoEnvio) : 0m;

    public DateTime? FechaVencimiento { get; set; }

    [StringLength(500)]
    public string? MotivoCancelacion { get; set; }

    public virtual Cliente? Cliente { get; set; }
    public virtual ICollection<CotizacionDetalle> Detalles { get; set; } = new List<CotizacionDetalle>();
    public virtual ICollection<CotizacionPagoSimulado> OpcionesPago { get; set; } = new List<CotizacionPagoSimulado>();
}
