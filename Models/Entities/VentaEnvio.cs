using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Datos de envío/entrega a domicilio de una venta, 1:1 con Venta (igual patrón que
    /// DatosTarjeta/DatosCheque). Sólo existe si la venta tiene envío: la presencia de la
    /// fila es la única autoridad, Venta no tiene un flag "TieneEnvio" propio.
    /// El costo es informativo: no impacta Subtotal/IVA/Total/caja/crédito de la venta.
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
        /// Costo del envío a fines informativos/logísticos. No se suma a Venta.Total.
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
