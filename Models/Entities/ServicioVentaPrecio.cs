using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Precio fijo global de un servicio de venta (Envío Ciudad/Rural, Armado N.º 1..6).
    /// Una fila por <see cref="TipoServicioVenta"/> (sembradas por la migración, precio inicial 0).
    /// La venta guarda un snapshot del precio al momento de la operación
    /// (<c>VentaEnvio.CostoEnvio</c>, <c>VentaDetalle.ArmadoPrecioUnitario</c>): cambiar el precio
    /// global no altera ventas ya registradas.
    /// </summary>
    public class ServicioVentaPrecio : AuditableEntity
    {
        [Required]
        public TipoServicioVenta Tipo { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Precio { get; set; }

        /// <summary>Un servicio inactivo no se ofrece en ventas nuevas.</summary>
        public bool Activo { get; set; } = true;
    }
}
