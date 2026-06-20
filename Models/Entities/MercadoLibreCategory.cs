using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Categoría del catálogo de Mercado Libre importada localmente desde el archivo
    /// de categorías con atributos (no se versiona el archivo: ver _ml-cache/).
    /// Es un CACHÉ de lectura: se reconstruye por completo en cada importación.
    /// Solo las hojas con <see cref="ListingAllowed"/> = true admiten publicación.
    /// </summary>
    public class MercadoLibreCategory
    {
        public int Id { get; set; }

        /// <summary>Site de ML. Por ahora siempre "MLA" (Argentina).</summary>
        [StringLength(10)]
        public string SiteId { get; set; } = "MLA";

        /// <summary>Id de categoría ML (ej: "MLA416632").</summary>
        [StringLength(30)]
        public string CategoryId { get; set; } = string.Empty;

        [StringLength(250)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Categoría padre (penúltimo nodo de path_from_root). Null en raíces.</summary>
        [StringLength(30)]
        public string? ParentCategoryId { get; set; }

        /// <summary>path_from_root crudo ([{id,name}, ...]) para reconstruir la ruta legible.</summary>
        public string? PathFromRootJson { get; set; }

        /// <summary>children_categories crudo ([{id,name}, ...]).</summary>
        public string? ChildrenJson { get; set; }

        /// <summary>Hoja del árbol: children_categories vacío. Solo en hojas se publica.</summary>
        public bool IsLeaf { get; set; }

        /// <summary>settings.listing_allowed: si ML permite publicar en la categoría.</summary>
        public bool ListingAllowed { get; set; }

        /// <summary>settings.buying_allowed.</summary>
        public bool BuyingAllowed { get; set; }

        [StringLength(30)]
        public string? Status { get; set; }

        /// <summary>attribute_types ("attributes" | "variations" | ...).</summary>
        [StringLength(30)]
        public string? AttributeTypes { get; set; }

        [StringLength(100)]
        public string? CatalogDomain { get; set; }

        [StringLength(60)]
        public string? Vertical { get; set; }

        [StringLength(60)]
        public string? SubVertical { get; set; }

        public int? MaxTitleLength { get; set; }
        public int? MaxPicturesPerItem { get; set; }
        public int? MaxVariationsAllowed { get; set; }

        /// <summary>settings.item_conditions crudo (["new","used",...]).</summary>
        public string? ItemConditionsJson { get; set; }

        /// <summary>settings.buying_modes crudo.</summary>
        public string? BuyingModesJson { get; set; }

        /// <summary>settings.shipping_options crudo.</summary>
        public string? ShippingOptionsJson { get; set; }

        public int? TotalItemsInThisCategory { get; set; }

        [StringLength(500)]
        public string? Permalink { get; set; }

        [StringLength(500)]
        public string? Picture { get; set; }

        /// <summary>
        /// JSON crudo del nodo categoría SIN el array de atributos (esos viven en
        /// <see cref="MercadoLibreCategoryAttribute"/>). Puede ser null si se omite
        /// para no engordar el caché.
        /// </summary>
        public string? RawJson { get; set; }

        /// <summary>Última importación en la que se vio esta categoría.</summary>
        public DateTime LastSeenAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        /// <summary>Marca de baja lógica (el caché se reconstruye wholesale; se conserva por contrato).</summary>
        public bool IsDeleted { get; set; }

        public virtual ICollection<MercadoLibreCategoryAttribute> Attributes { get; set; }
            = new List<MercadoLibreCategoryAttribute>();
    }
}
