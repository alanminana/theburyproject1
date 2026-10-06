using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using Microsoft.AspNetCore.Identity;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    public class Venta  : AuditableEntity
    {
        [Required]
        [StringLength(20)]
        public string Numero { get; set; } = string.Empty;

        public int? ClienteId { get; set; }

        [StringLength(200)]
        public string? NombreClienteLibre { get; set; }

        [StringLength(8)]
        public string? DniClienteLibre { get; set; }

        [StringLength(30)]
        public string? TelefonoClienteLibre { get; set; }

        [Required]
        public DateTime FechaVenta { get; set; } = DateTime.UtcNow;

        [Required]
        public EstadoVenta Estado { get; set; } = EstadoVenta.Cotizacion;

        [Required]
        public TipoPago TipoPago { get; set; } = TipoPago.Efectivo;

        public decimal Subtotal { get; set; }
        public decimal Descuento { get; set; } = 0;
        public decimal IVA { get; set; }
        public decimal Total { get; set; }

        // Crédito personal
        public int? CreditoId { get; set; }

        // Snapshot de límite aplicado al momento de crear la operación
        public decimal? LimiteAplicado { get; set; }
        public decimal? PuntajeAlMomento { get; set; }
        public int? PresetIdAlMomento { get; set; }
        public decimal? OverrideAlMomento { get; set; }
        public decimal? ExcepcionAlMomento { get; set; }

        // Autorización
        public EstadoAutorizacionVenta EstadoAutorizacion { get; set; } = EstadoAutorizacionVenta.NoRequiere;
        public bool RequiereAutorizacion { get; set; } = false;

        [StringLength(200)]
        public string? UsuarioSolicita { get; set; }

        public DateTime? FechaSolicitudAutorizacion { get; set; }

        [StringLength(200)]
        public string? UsuarioAutoriza { get; set; }

        public DateTime? FechaAutorizacion { get; set; }

        [StringLength(1000)]
        public string? MotivoAutorizacion { get; set; }

        [StringLength(1000)]
        public string? MotivoRechazo { get; set; }

        /// <summary>
        /// Razones de autorización en formato JSON (TipoRazonAutorizacion[])
        /// </summary>
        public string? RazonesAutorizacionJson { get; set; }

        /// <summary>
        /// Requisitos pendientes en formato JSON (TipoRequisitoPendiente[])
        /// </summary>
        public string? RequisitosPendientesJson { get; set; }

        // Información adicional
        public int? AperturaCajaId { get; set; }

        [StringLength(450)]
        public string? VendedorUserId { get; set; }

        [StringLength(200)]
        public string? VendedorNombre { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public DateTime? FechaConfirmacion { get; set; }
        public DateTime? FechaFacturacion { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public DateTime? FechaCancelacion { get; set; }

        /// <summary>
        /// Fecha en que se configuró el financiamiento del crédito personal.
        /// Se usa para evitar redireccionamientos repetidos a ConfigurarVenta.
        /// </summary>
        public DateTime? FechaConfiguracionCredito { get; set; }

        [StringLength(500)]
        public string? MotivoCancelacion { get; set; }

        // Trazabilidad: cotización origen (nullable — solo ventas generadas por conversión)
        public int? CotizacionOrigenId { get; set; }

        // Navigation properties
        public virtual Cliente? Cliente { get; set; }
        public virtual Cotizacion? CotizacionOrigen { get; set; }
        public virtual Credito? Credito { get; set; }
        public virtual AperturaCaja? AperturaCaja { get; set; }
        public virtual ApplicationUser? VendedorUser { get; set; }
        public virtual ICollection<VentaDetalle> Detalles { get; set; } = new List<VentaDetalle>();
        public virtual ICollection<Factura> Facturas { get; set; } = new List<Factura>();
        public virtual DatosTarjeta? DatosTarjeta { get; set; }
        public virtual DatosCheque? DatosCheque { get; set; }
        public virtual VentaEnvio? Envio { get; set; }
        public virtual ICollection<MovimientoCaja> MovimientosCaja { get; set; } = new List<MovimientoCaja>();

        // Montos derivados (no persistidos). Ventas nuevas: Total = productos + armados + envío, todo con el
        // recargo del medio de pago. Ventas legacy (VentaEnvio.IncluidoEnTotal = false): Total = sólo productos
        // y el envío se cobra aparte. Fórmula única en VentaMontos.
        // ImporteEnvio (importe del envío, esté o no dentro de Total) requiere Envio cargado (Include): sin él vale 0.
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal ImporteEnvio => Helpers.VentaMontos.NormalizarImporteEnvio(Envio?.CostoEnvio);

        /// <summary>Envío que se suma a Total para llegar a lo que hay que cobrar (sólo envíos legacy).</summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal ImporteEnvioFueraDelTotal => Helpers.VentaMontos.NormalizarImporteEnvio(Envio?.CostoFueraDelTotal);

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal TotalACobrar => Helpers.VentaMontos.CalcularTotalACobrar(Total, Envio?.CostoFueraDelTotal);

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public decimal TotalFacturable => Helpers.VentaMontos.CalcularTotalFacturable(Total);
    }
}
