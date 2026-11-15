using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>D-03: parte de un pago aplicada a un comprobante (factura o nota de débito) del mismo proveedor.</summary>
    public class PagoProveedorImputacion : AuditableEntity
    {
        public int PagoProveedorId { get; set; }

        public int ComprobanteProveedorId { get; set; }

        /// <summary>Siempre mayor que cero.</summary>
        public decimal Monto { get; set; }

        public virtual PagoProveedor Pago { get; set; } = null!;
        public virtual ComprobanteProveedor Comprobante { get; set; } = null!;
    }
}
