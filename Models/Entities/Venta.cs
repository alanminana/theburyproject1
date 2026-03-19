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

        public int ClienteId { get; set; }

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

        // Cr�dito personal
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

        /// <summary>
        /// Datos del plan de crédito personal en formato JSON.
        /// Se guarda al crear la venta y se usa al confirmar para generar las cuotas.
        /// </summary>
        public string? DatosCreditoPersonallJson { get; set; }

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

        // Navigation properties
        public virtual Cliente Cliente { get; set; } = null!;
        public virtual Credito? Credito { get; set; }
        public virtual AperturaCaja? AperturaCaja { get; set; }
        public virtual ApplicationUser? VendedorUser { get; set; }
        public virtual ICollection<VentaDetalle> Detalles { get; set; } = new List<VentaDetalle>();
        public virtual ICollection<Factura> Facturas { get; set; } = new List<Factura>();
        public virtual DatosTarjeta? DatosTarjeta { get; set; }
        public virtual DatosCheque? DatosCheque { get; set; }
        public virtual ICollection<VentaCreditoCuota> VentaCreditoCuotas { get; set; } = new List<VentaCreditoCuota>();

    }
}
