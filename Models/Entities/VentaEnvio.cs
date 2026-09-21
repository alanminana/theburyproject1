using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Datos de envío/entrega a domicilio de una venta, 1:1 con Venta (igual patrón que
    /// DatosTarjeta/DatosCheque). Sólo existe si la venta tiene envío: la presencia de la
    /// fila es la única autoridad, Venta no tiene un flag "TieneEnvio" propio.
    /// El costo de envío es un importe que el cliente paga: se suma a lo que hay que cobrar
    /// (<see cref="Venta.TotalACobrar"/>) y a lo que registra Caja, pero NO a Subtotal/IVA/Total ni
    /// al comprobante ni al crédito de la venta (ver Helpers/VentaMontos).
    /// </summary>
    public class VentaEnvio : AuditableEntity
    {
        public int VentaId { get; set; }

        [Required]
        public EstadoEnvio Estado { get; set; } = EstadoEnvio.Pendiente;

        [Required]
        [StringLength(200)]
        public string Destinatario { get; set; } = string.Empty;

        [StringLength(30)]
        public string? Telefono { get; set; }

        [Required]
        [StringLength(300)]
        public string Domicilio { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Localidad { get; set; }

        [StringLength(100)]
        public string? Provincia { get; set; }

        [StringLength(20)]
        public string? CodigoPostal { get; set; }

        [StringLength(150)]
        public string? Transportista { get; set; }

        [StringLength(100)]
        public string? NumeroSeguimiento { get; set; }

        /// <summary>
        /// Importe del envío que se le cobra al cliente (null/0 = sin cargo). Fuente única del
        /// importe: se suma a Venta.TotalACobrar, nunca a Venta.Total. Una vez confirmada la venta
        /// no se edita (Venta/Update sólo admite estados previos a la confirmación).
        /// </summary>
        public decimal? CostoEnvio { get; set; }

        public DateTime? FechaProgramada { get; set; }
        public DateTime? FechaDespacho { get; set; }
        public DateTime? FechaEntregaReal { get; set; }

        [StringLength(500)]
        public string? MotivoNoEntrega { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navigation
        public virtual Venta Venta { get; set; } = null!;
    }
}
