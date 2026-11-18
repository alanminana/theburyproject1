using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// D-03: comprobante (factura, nota de débito o nota de crédito) emitido por un proveedor, registrado
    /// como libro administrativo de cuentas por pagar. Los importes son los que INFORMA el proveedor:
    /// el ERP no calcula IVA crédito fiscal, no cambia el costo de los productos, no mueve stock ni Caja.
    /// No se borra: se anula (<see cref="Estado"/> = Anulado) y queda el motivo y quién lo hizo.
    /// </summary>
    public class ComprobanteProveedor : AuditableEntity
    {
        public int ProveedorId { get; set; }

        /// <summary>Orden de compra relacionada (informativa, opcional).</summary>
        public int? OrdenCompraId { get; set; }

        public TipoComprobanteProveedor Tipo { get; set; } = TipoComprobanteProveedor.Factura;

        /// <summary>Número tal como figura en el comprobante, recortado y en mayúsculas (clave de unicidad).</summary>
        [Required]
        [StringLength(50)]
        public string NumeroComprobante { get; set; } = string.Empty;

        public DateTime FechaEmision { get; set; }

        public DateTime? FechaVencimiento { get; set; }

        public decimal ImporteNeto { get; set; }

        public decimal ImporteIva { get; set; }

        public decimal ImporteTotal { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        /// <summary>
        /// Persistido y recalculado en cada operación a partir de las imputaciones activas.
        /// Factura/nota de débito: deuda pendiente. Nota de crédito: importe aún sin aplicar.
        /// </summary>
        public decimal SaldoPendiente { get; set; }

        public EstadoComprobanteProveedor Estado { get; set; } = EstadoComprobanteProveedor.Pendiente;

        [StringLength(100)]
        public string? AnuladoPor { get; set; }

        public DateTime? FechaAnulacion { get; set; }

        [StringLength(500)]
        public string? MotivoAnulacion { get; set; }

        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual OrdenCompra? OrdenCompra { get; set; }
        public virtual ICollection<PagoProveedorImputacion> ImputacionesPago { get; set; } = new List<PagoProveedorImputacion>();
        /// <summary>Imputaciones de notas de crédito RECIBIDAS por este comprobante (cuando es factura/nota de débito).</summary>
        public virtual ICollection<ImputacionNotaCredito> ImputacionesRecibidas { get; set; } = new List<ImputacionNotaCredito>();
        /// <summary>Imputaciones que ESTE comprobante hace (cuando es nota de crédito).</summary>
        public virtual ICollection<ImputacionNotaCredito> ImputacionesEmitidas { get; set; } = new List<ImputacionNotaCredito>();
        /// <summary>Anticipos aplicados a este comprobante.</summary>
        public virtual ICollection<AnticipoAplicacion> AplicacionesAnticipo { get; set; } = new List<AnticipoAplicacion>();
    }
}
