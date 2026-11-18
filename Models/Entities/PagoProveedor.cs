using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// D-03: pago a un proveedor como registro administrativo. NO genera movimientos de Caja ni modifica
    /// el ciclo de vida del cheque referenciado. La anulación es un contra-registro: el pago queda con sus
    /// imputaciones como historia, deja de contar para los saldos y registra motivo, usuario y fecha.
    /// </summary>
    public class PagoProveedor : AuditableEntity
    {
        public int ProveedorId { get; set; }

        public DateTime Fecha { get; set; }

        public MedioPagoProveedor Medio { get; set; } = MedioPagoProveedor.Transferencia;

        public decimal Monto { get; set; }

        /// <summary>Referencia libre: nº de transferencia, recibo, etc.</summary>
        [StringLength(100)]
        public string? Referencia { get; set; }

        /// <summary>Cheque emitido existente, solo como referencia (no se altera su estado).</summary>
        public int? ChequeId { get; set; }

        /// <summary>
        /// Origen de los fondos (Tesorería administrativa). Obligatorio en todo pago nuevo; nulo solo en pagos anteriores a
        /// Tesorería, que no se reescriben.
        /// </summary>
        public int? OrigenFondosId { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        /// <summary>Clave de idempotencia del envío (evita registrar dos veces el mismo formulario).</summary>
        [StringLength(64)]
        public string? ClaveIdempotencia { get; set; }

        public bool Anulado { get; set; }

        [StringLength(100)]
        public string? AnuladoPor { get; set; }

        public DateTime? FechaAnulacion { get; set; }

        [StringLength(500)]
        public string? MotivoAnulacion { get; set; }

        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual Cheque? Cheque { get; set; }
        public virtual OrigenFondos? OrigenFondos { get; set; }
        public virtual ICollection<PagoProveedorImputacion> Imputaciones { get; set; } = new List<PagoProveedorImputacion>();
    }
}
