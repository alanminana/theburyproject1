using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa un cheque emitido como pago a proveedor
    /// </summary>
    public class Cheque  : AuditableEntity
    {
        /// <summary>
        /// N�mero de cheque
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        /// <summary>
        /// Banco emisor
        /// </summary>
        [Required]
        [StringLength(200)]
        public string Banco { get; set; } = string.Empty;

        /// <summary>
        /// Sucursal del banco
        /// </summary>
        [StringLength(100)]
        public string? Sucursal { get; set; }

        /// <summary>
        /// N�mero de cuenta
        /// </summary>
        [StringLength(50)]
        public string? NumeroCuenta { get; set; }

        /// <summary>
        /// Monto del cheque
        /// </summary>
        public decimal Monto { get; set; }

        /// <summary>
        /// Fecha de emisi�n del cheque
        /// </summary>
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Fecha de vencimiento/cobro del cheque
        /// </summary>
        public DateTime? FechaVencimiento { get; set; }
        /// <summary>
        /// Proveedor al que se le entrega el cheque
        /// </summary>
        public int ProveedorId { get; set; }

        /// <summary>
        /// Orden de compra asociada (opcional)
        /// </summary>
        public int? OrdenCompraId { get; set; }

        /// <summary>
        /// Estado del cheque
        /// </summary>
        public EstadoCheque Estado { get; set; } = EstadoCheque.Emitido;

        /// <summary>
        /// Fecha en que se entreg� el cheque al proveedor
        /// </summary>
        public DateTime? FechaEntrega { get; set; }

        /// <summary>
        /// Fecha en que se cobr� el cheque
        /// </summary>
        public DateTime? FechaCobro { get; set; }

        /// <summary>
        /// Observaciones sobre el cheque
        /// </summary>
        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navegaci�n
        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual OrdenCompra? OrdenCompra { get; set; }
    }
}
