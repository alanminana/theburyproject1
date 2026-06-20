using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Publicación existente en Mercado Libre, importada al ERP.
    /// Es una entidad EXTERNA: nunca se mezcla con Producto.
    /// La vinculación con Producto interno es opcional y explícita (ProductoId).
    /// </summary>
    public class MercadoLibreListing : AuditableEntity
    {
        public int AccountId { get; set; }

        /// <summary>
        /// Id de la publicación en Mercado Libre (ej: "MLA123456789"). Único.
        /// </summary>
        [Required]
        [StringLength(30)]
        public string ItemId { get; set; } = string.Empty;

        [Required]
        [StringLength(300)]
        public string Titulo { get; set; } = string.Empty;

        public decimal Precio { get; set; }

        [StringLength(10)]
        public string CurrencyId { get; set; } = "ARS";

        /// <summary>
        /// Stock disponible publicado en Mercado Libre (suma de variaciones si las hay).
        /// </summary>
        public int AvailableQuantity { get; set; }

        public int SoldQuantity { get; set; }

        /// <summary>
        /// Estado de la publicación: active, paused, closed, under_review, etc.
        /// </summary>
        [StringLength(30)]
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Subestados (ej: "out_of_stock"), separados por coma.
        /// </summary>
        [StringLength(200)]
        public string? SubStatus { get; set; }

        [StringLength(500)]
        public string? Permalink { get; set; }

        /// <summary>
        /// Categoría de Mercado Libre (ej: "MLA1055").
        /// </summary>
        [StringLength(30)]
        public string? CategoryId { get; set; }

        /// <summary>
        /// Tipo de publicación: gold_special, gold_pro, free, etc.
        /// </summary>
        [StringLength(30)]
        public string? ListingTypeId { get; set; }

        /// <summary>
        /// SKU del vendedor declarado en la publicación. Se usa para sugerir
        /// vinculación con Producto.Codigo, pero la vinculación es siempre explícita.
        /// </summary>
        [StringLength(100)]
        public string? SellerSku { get; set; }

        [StringLength(20)]
        public string? Condition { get; set; }

        /// <summary>
        /// Indica si la publicación tiene variaciones (color, talle, etc.).
        /// </summary>
        public bool TieneVariaciones { get; set; }

        /// <summary>
        /// Producto interno vinculado. Null = publicación sin vincular.
        /// La vinculación nunca modifica al Producto.
        /// </summary>
        public int? ProductoId { get; set; }

        /// <summary>
        /// Override del origen de stock global para esta publicación.
        /// Null = usa MercadoLibreConfiguracion.OrigenStock.
        /// </summary>
        public MercadoLibreOrigenStock? OrigenStockOverride { get; set; }

        /// <summary>
        /// Unidad física concreta que representa esta publicación cuando el
        /// origen de stock es UnidadFisicaEspecifica (stock publicable: 1 o 0).
        /// </summary>
        public int? ProductoUnidadId { get; set; }

        /// <summary>
        /// Última sincronización (importación o push) en UTC.
        /// </summary>
        public DateTime? LastSyncUtc { get; set; }

        /// <summary>
        /// JSON crudo de la publicación tal como lo devolvió la API (auditoría/diagnóstico).
        /// </summary>
        public string? RawJson { get; set; }

        public virtual MercadoLibreAccount Account { get; set; } = null!;
        public virtual Producto? Producto { get; set; }
        public virtual ProductoUnidad? ProductoUnidad { get; set; }
        public virtual ICollection<MercadoLibreListingVariation> Variaciones { get; set; } = new List<MercadoLibreListingVariation>();
    }
}
