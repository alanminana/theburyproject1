using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Detalle opcional de tarjeta para una condicion de pago por producto.
    /// ConfiguracionTarjetaId null representa la regla general del medio.
    /// </summary>
    public class ProductoCondicionPagoTarjeta : AuditableEntity
    {
        public int ProductoCondicionPagoId { get; set; }

        public int? ConfiguracionTarjetaId { get; set; }

        public bool? Permitido { get; set; }

        public int? MaxCuotasSinInteres { get; set; }

        public int? MaxCuotasConInteres { get; set; }

        public decimal? PorcentajeRecargo { get; set; }

        public decimal? PorcentajeDescuentoMaximo { get; set; }

        public bool Activo { get; set; } = true;

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public virtual ProductoCondicionPago ProductoCondicionPago { get; set; } = null!;

        public virtual ConfiguracionTarjeta? ConfiguracionTarjeta { get; set; }
    }
}
