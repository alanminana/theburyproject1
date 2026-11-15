using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// D-03: parte de una nota de crédito del proveedor aplicada a una factura (o nota de débito) del MISMO proveedor.
    /// Es una entidad separada de <see cref="PagoProveedorImputacion"/> porque su origen no es un pago y
    /// no mueve dinero: solo compensa deuda. Se anula (no se borra) conservando motivo, usuario y fecha.
    /// </summary>
    public class ImputacionNotaCredito : AuditableEntity
    {
        public int NotaCreditoId { get; set; }

        public int ComprobanteId { get; set; }

        /// <summary>Siempre mayor que cero.</summary>
        public decimal Monto { get; set; }

        /// <summary>Token por formulario: reenviar la misma aplicación no la duplica (las filas de un mismo envío lo comparten).</summary>
        [StringLength(64)]
        public string? ClaveIdempotencia { get; set; }

        public bool Anulada { get; set; }

        [StringLength(100)]
        public string? AnuladaPor { get; set; }

        public DateTime? FechaAnulacion { get; set; }

        [StringLength(500)]
        public string? MotivoAnulacion { get; set; }

        public virtual ComprobanteProveedor NotaCredito { get; set; } = null!;
        public virtual ComprobanteProveedor Comprobante { get; set; } = null!;
    }
}
