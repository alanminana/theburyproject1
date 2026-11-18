using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// D-03: anticipo entregado a un proveedor antes de recibir la factura (R-09). Es un egreso de un
    /// <see cref="OrigenFondos"/> (NO de la Caja de ventas) que queda como saldo a favor y se aplica después a una o varias
    /// facturas del MISMO proveedor mediante <see cref="AnticipoAplicacion"/>. Saldo a favor = Monto - aplicaciones vigentes.
    /// La anulación es un contra-registro (motivo, usuario y fecha) y solo procede sin aplicaciones vigentes.
    /// </summary>
    public class AnticipoProveedor : AuditableEntity
    {
        public int ProveedorId { get; set; }

        public DateTime Fecha { get; set; }

        public decimal Monto { get; set; }

        public int OrigenFondosId { get; set; }

        public MedioPagoProveedor Medio { get; set; } = MedioPagoProveedor.Transferencia;

        [StringLength(100)]
        public string? Referencia { get; set; }

        /// <summary>Cheque emitido existente que respalda el anticipo (un cheque físico respalda un único pago o anticipo vigente).</summary>
        public int? ChequeId { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        [StringLength(64)]
        public string? ClaveIdempotencia { get; set; }

        public bool Anulado { get; set; }

        [StringLength(100)]
        public string? AnuladoPor { get; set; }

        public DateTime? FechaAnulacion { get; set; }

        [StringLength(500)]
        public string? MotivoAnulacion { get; set; }

        public virtual Proveedor Proveedor { get; set; } = null!;
        public virtual OrigenFondos OrigenFondos { get; set; } = null!;
        public virtual Cheque? Cheque { get; set; }
        public virtual ICollection<AnticipoAplicacion> Aplicaciones { get; set; } = new List<AnticipoAplicacion>();
    }

    /// <summary>D-03: parte de un anticipo aplicada a una factura (o nota de débito) del mismo proveedor. No mueve dinero: compensa deuda.</summary>
    public class AnticipoAplicacion : AuditableEntity
    {
        public int AnticipoProveedorId { get; set; }

        public int ComprobanteProveedorId { get; set; }

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

        public virtual AnticipoProveedor Anticipo { get; set; } = null!;
        public virtual ComprobanteProveedor Comprobante { get; set; } = null!;
    }
}
