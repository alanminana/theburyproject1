using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Condicion de pago declarada para un producto y medio de pago.
    /// </summary>
    public class ProductoCondicionPago : AuditableEntity
    {
        public int ProductoId { get; set; }

        public TipoPago TipoPago { get; set; }

        public bool? Permitido { get; set; }

        public int? MaxCuotasSinInteres { get; set; }

        public int? MaxCuotasConInteres { get; set; }

        public int? MaxCuotasCredito { get; set; }

        public decimal? PorcentajeRecargo { get; set; }

        public decimal? PorcentajeDescuentoMaximo { get; set; }

        public bool Activo { get; set; } = true;

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public virtual Producto Producto { get; set; } = null!;

        public virtual ICollection<ProductoCondicionPagoTarjeta> Tarjetas { get; set; } =
            new List<ProductoCondicionPagoTarjeta>();

        public virtual ICollection<ProductoCondicionPagoPlan> Planes { get; set; } =
            new List<ProductoCondicionPagoPlan>();
    }
}
