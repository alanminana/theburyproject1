using System.Text.Json.Serialization;

namespace TheBuryProject.Modules.MercadoLibre.DTOs
{
    /// <summary>
    /// Nodo simple del árbol de categorías ML: aparece en path_from_root,
    /// children_categories y en el listado de categorías raíz de un site.
    /// </summary>
    public class MeliCategoryNodeDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Detalle de GET /categories/{id}. Una categoría es HOJA cuando
    /// children_categories viene vacío. settings.listing_allowed indica si
    /// se puede publicar en ella.
    /// </summary>
    public class MeliCategoryDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("total_items_in_this_category")]
        public long? TotalItemsInThisCategory { get; set; }

        [JsonPropertyName("path_from_root")]
        public List<MeliCategoryNodeDto> PathFromRoot { get; set; } = new();

        [JsonPropertyName("children_categories")]
        public List<MeliCategoryNodeDto> ChildrenCategories { get; set; } = new();

        [JsonPropertyName("settings")]
        public MeliCategorySettingsDto? Settings { get; set; }

        /// <summary>Hoja del árbol: sin categorías hijas. Solo en hojas se puede publicar.</summary>
        [JsonIgnore]
        public bool EsHoja => ChildrenCategories.Count == 0;

        /// <summary>Ruta legible "Raíz &gt; ... &gt; Hoja" a partir de path_from_root.</summary>
        [JsonIgnore]
        public string PathString => string.Join(" > ", PathFromRoot.Select(p => p.Name));
    }

    public class MeliCategorySettingsDto
    {
        [JsonPropertyName("listing_allowed")]
        public bool ListingAllowed { get; set; }

        [JsonPropertyName("max_title_length")]
        public int? MaxTitleLength { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    /// <summary>
    /// Sugerencia de GET /sites/{site}/domain_discovery/search?q=...
    /// El predictor mapea un título de producto a su categoría hoja más probable.
    /// </summary>
    public class MeliCategoryPredictionDto
    {
        [JsonPropertyName("domain_id")]
        public string? DomainId { get; set; }

        [JsonPropertyName("domain_name")]
        public string? DomainName { get; set; }

        [JsonPropertyName("category_id")]
        public string CategoryId { get; set; } = string.Empty;

        [JsonPropertyName("category_name")]
        public string CategoryName { get; set; } = string.Empty;
    }
}
