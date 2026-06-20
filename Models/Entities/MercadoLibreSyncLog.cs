using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Registro de cada operación de sincronización con Mercado Libre
    /// (importación de publicaciones, push de stock/precio, importación de órdenes).
    /// </summary>
    public class MercadoLibreSyncLog : AuditableEntity
    {
        public int? AccountId { get; set; }

        public int? ListingId { get; set; }

        /// <summary>
        /// Id de la publicación de ML afectada, si aplica.
        /// </summary>
        [StringLength(30)]
        public string? ItemId { get; set; }

        /// <summary>
        /// Operación: ImportListings, LinkProducto, UnlinkProducto, PushStock, PushPrecio, ImportOrders, TestConnection.
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Operacion { get; set; } = string.Empty;

        public bool Exito { get; set; }

        /// <summary>
        /// Detalle del resultado o del error (sin tokens ni datos sensibles).
        /// </summary>
        [StringLength(2000)]
        public string? Detalle { get; set; }

        public long? DuracionMs { get; set; }
    }
}
