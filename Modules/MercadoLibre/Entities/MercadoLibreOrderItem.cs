using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Modules.MercadoLibre.Entities
{
    /// <summary>
    /// Línea de una orden de Mercado Libre. Snapshot de lo vendido en ML;
    /// ProductoId se resuelve al procesar vía la vinculación listing→Producto.
    /// </summary>
    public class MercadoLibreOrderItem : AuditableEntity
    {
        public int OrderId { get; set; }

        /// <summary>Id de la publicación ML (ej: "MLA123...").</summary>
        [Required]
        [StringLength(30)]
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Id de variación si la publicación tiene variaciones.</summary>
        public long? VariationId { get; set; }

        [StringLength(300)]
        public string Titulo { get; set; } = string.Empty;

        public int Cantidad { get; set; }

        public decimal PrecioUnitario { get; set; }

        [StringLength(10)]
        public string CurrencyId { get; set; } = "ARS";

        [StringLength(100)]
        public string? SellerSku { get; set; }

        /// <summary>Comisión de venta (sale_fee) informada por ML para esta línea.</summary>
        public decimal? SaleFee { get; set; }

        /// <summary>Producto interno resuelto al procesar la orden. Null = sin vincular.</summary>
        public int? ProductoId { get; set; }

        /// <summary>
        /// Ids de ProductoUnidad asignadas (CSV) para productos trazables.
        /// Se asignan automáticamente (FIFO) o manualmente antes de crear la venta.
        /// </summary>
        [StringLength(500)]
        public string? UnidadesAsignadas { get; set; }

        public virtual MercadoLibreOrder Order { get; set; } = null!;
    }
}
