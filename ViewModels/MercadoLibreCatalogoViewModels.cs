using System.Text.Json.Serialization;

namespace TheBuryProject.ViewModels
{
    /// <summary>Valor permitido de un atributo de categoría (id técnico + nombre visible).</summary>
    public class CatalogoValorVm
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Atributo de categoría ML completado por el operador en el borrador. Se serializa
    /// con claves snake_case (value_id / value_name) para reflejar el payload de ML.
    /// </summary>
    public class AtributoCompletadoVm
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("value_id")]
        public string? ValueId { get; set; }

        [JsonPropertyName("value_name")]
        public string? ValueName { get; set; }

        /// <summary>Unidad elegida para value_type number_unit (no se persiste por separado: se concatena al value_name).</summary>
        [JsonIgnore]
        public string? Unit { get; set; }

        /// <summary>True si el atributo tiene algún valor cargado (id o nombre).</summary>
        [JsonIgnore]
        public bool TieneValor =>
            !string.IsNullOrWhiteSpace(ValueId) || !string.IsNullOrWhiteSpace(ValueName);
    }

    /// <summary>
    /// Categoría del catálogo local (proyección de <c>MercadoLibreCategory</c>) para
    /// búsqueda y resolución sin tocar Mercado Libre.
    /// </summary>
    public class CatalogoCategoriaVm
    {
        public string CategoryId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Path { get; set; }
        public bool EsHoja { get; set; }
        public bool ListingAllowed { get; set; }
        public int? TotalItems { get; set; }
        public int? MaxTitleLength { get; set; }

        /// <summary>Publicable: hoja + listing_allowed.</summary>
        public bool EsPublicable => EsHoja && ListingAllowed;
    }

    /// <summary>
    /// Atributo de categoría proyectado para el formulario dinámico del borrador.
    /// Las flags de obligatoriedad se calculan según la condición/listing al consultar.
    /// </summary>
    public class CatalogoAtributoVm
    {
        public string AttributeId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>string | number | number_unit | boolean | list.</summary>
        public string? ValueType { get; set; }

        public bool Required { get; set; }
        public bool CatalogRequired { get; set; }
        public bool ConditionalRequired { get; set; }
        public bool NewRequired { get; set; }
        public bool ReadOnly { get; set; }
        public bool Hidden { get; set; }
        public bool Multivalued { get; set; }

        public int? ValueMaxLength { get; set; }
        public string? DefaultUnit { get; set; }
        public string? Hint { get; set; }
        public string? Tooltip { get; set; }
        public string? AttributeGroupName { get; set; }

        public List<CatalogoValorVm> Values { get; set; } = new();
        public List<CatalogoValorVm> AllowedUnits { get; set; } = new();

        /// <summary>
        /// Bloqueante: su ausencia debe impedir la publicación REAL (required, o
        /// new_required cuando la condición es "new"). Se setea según el contexto.
        /// </summary>
        public bool EsBloqueante { get; set; }

        /// <summary>Recomendado (catalog_required / conditional_required): no bloquea.</summary>
        public bool EsRecomendado { get; set; }

        /// <summary>True si conviene un control de tipo select (list/boolean o con valores).</summary>
        public bool TieneValores => Values.Count > 0;
    }

    /// <summary>Estado del catálogo local de categorías ML, para la sección de Configuración.</summary>
    public class MercadoLibreCatalogoEstadoVm
    {
        public bool Importado { get; set; }
        public string SiteId { get; set; } = "MLA";
        public DateTime? UltimaImportacionUtc { get; set; }
        public DateTime? UltimoExitoUtc { get; set; }
        public int Categorias { get; set; }
        public int Hojas { get; set; }
        public int Publicables { get; set; }
        public int Atributos { get; set; }
        public string? UltimoError { get; set; }
        public string? SourceFilePath { get; set; }
        public long DurationMs { get; set; }

        /// <summary>Ruta sugerida en el input del formulario de importación.</summary>
        public string? RutaSugerida { get; set; }
    }
}
