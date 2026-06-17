using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Modules.MercadoLibre.Entities
{
    /// <summary>
    /// Atributo de una categoría ML (proviene del array "attributes" del nodo).
    /// Los flags se derivan de "tags" (objeto) y gobiernan qué se muestra/exige
    /// en el formulario dinámico del borrador y qué viaja al payload POST /items.
    /// </summary>
    public class MercadoLibreCategoryAttribute
    {
        public int Id { get; set; }

        [StringLength(10)]
        public string SiteId { get; set; } = "MLA";

        [StringLength(30)]
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>Id técnico del atributo (ej: "BRAND", "PAPER_TYPE").</summary>
        [StringLength(80)]
        public string AttributeId { get; set; } = string.Empty;

        [StringLength(250)]
        public string Name { get; set; } = string.Empty;

        /// <summary>value_type: string | number | number_unit | boolean | list.</summary>
        [StringLength(30)]
        public string? ValueType { get; set; }

        /// <summary>PARENT_PK | CHILD_PK | FAMILY | ...</summary>
        [StringLength(30)]
        public string? Hierarchy { get; set; }

        public int? Relevance { get; set; }

        // Flags derivados de tags.*
        public bool Required { get; set; }
        public bool CatalogRequired { get; set; }
        public bool ConditionalRequired { get; set; }
        public bool NewRequired { get; set; }
        public bool ReadOnly { get; set; }
        public bool Hidden { get; set; }
        public bool AllowVariations { get; set; }
        public bool VariationAttribute { get; set; }
        public bool Multivalued { get; set; }

        public int? ValueMaxLength { get; set; }

        /// <summary>values crudo ([{id,name}, ...]) para poblar selects.</summary>
        public string? ValuesJson { get; set; }

        /// <summary>allowed_units crudo ([{id,name}, ...]) para value_type number_unit.</summary>
        public string? AllowedUnitsJson { get; set; }

        [StringLength(30)]
        public string? DefaultUnit { get; set; }

        [StringLength(80)]
        public string? AttributeGroupId { get; set; }

        [StringLength(120)]
        public string? AttributeGroupName { get; set; }

        [StringLength(500)]
        public string? Hint { get; set; }

        public string? Tooltip { get; set; }

        /// <summary>JSON crudo del atributo. Null por defecto para no duplicar ValuesJson.</summary>
        public string? RawJson { get; set; }

        public DateTime LastSeenAtUtc { get; set; }

        public bool IsDeleted { get; set; }

        public virtual MercadoLibreCategory? Category { get; set; }
        public int CategoryFk { get; set; }
    }
}
