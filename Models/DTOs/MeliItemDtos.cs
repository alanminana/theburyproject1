using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheBuryProject.Models.DTOs
{
    /// <summary>
    /// Página de GET /users/{id}/items/search con search_type=scan.
    /// </summary>
    public class MeliItemSearchPageDto
    {
        [JsonPropertyName("results")]
        public List<string> Results { get; set; } = new();

        [JsonPropertyName("scroll_id")]
        public string? ScrollId { get; set; }

        [JsonPropertyName("paging")]
        public MeliPagingDto? Paging { get; set; }
    }

    public class MeliPagingDto
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("offset")]
        public int Offset { get; set; }
    }

    /// <summary>
    /// Entrada del multiget GET /items?ids=... — cada elemento trae code + body.
    /// </summary>
    public class MeliMultigetEntryDto
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("body")]
        public MeliItemDto? Body { get; set; }
    }

    /// <summary>
    /// Publicación (parcial) de GET /items/{id}.
    /// </summary>
    public class MeliItemDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [JsonPropertyName("currency_id")]
        public string? CurrencyId { get; set; }

        [JsonPropertyName("available_quantity")]
        public int? AvailableQuantity { get; set; }

        [JsonPropertyName("sold_quantity")]
        public int? SoldQuantity { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("sub_status")]
        public List<string> SubStatus { get; set; } = new();

        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("category_id")]
        public string? CategoryId { get; set; }

        [JsonPropertyName("listing_type_id")]
        public string? ListingTypeId { get; set; }

        [JsonPropertyName("condition")]
        public string? Condition { get; set; }

        /// <summary>
        /// SKU "viejo" del vendedor. Mercado Libre recomienda el atributo SELLER_SKU,
        /// pero muchas publicaciones siguen usando este campo.
        /// </summary>
        [JsonPropertyName("seller_custom_field")]
        public string? SellerCustomField { get; set; }

        [JsonPropertyName("attributes")]
        public List<MeliAttributeDto> Attributes { get; set; } = new();

        [JsonPropertyName("pictures")]
        public List<MeliPictureDto> Pictures { get; set; } = new();

        [JsonPropertyName("variations")]
        public List<MeliVariationDto> Variations { get; set; } = new();

        /// <summary>
        /// Resuelve el seller_sku efectivo: primero atributo SELLER_SKU, después seller_custom_field.
        /// </summary>
        public string? ResolverSellerSku()
        {
            var attr = Attributes.FirstOrDefault(a =>
                string.Equals(a.Id, "SELLER_SKU", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(attr?.ValueName))
                return attr.ValueName;

            return string.IsNullOrWhiteSpace(SellerCustomField) ? null : SellerCustomField;
        }
    }

    public class MeliAttributeDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("value_name")]
        public string? ValueName { get; set; }
    }

    public class MeliPictureDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("secure_url")]
        public string? SecureUrl { get; set; }

        /// <summary>URL pública efectiva de la imagen (prefiere https).</summary>
        public string? UrlEfectiva => SecureUrl ?? Url;
    }

    public class MeliVariationDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [JsonPropertyName("available_quantity")]
        public int? AvailableQuantity { get; set; }

        [JsonPropertyName("sold_quantity")]
        public int? SoldQuantity { get; set; }

        [JsonPropertyName("seller_custom_field")]
        public string? SellerCustomField { get; set; }

        [JsonPropertyName("attribute_combinations")]
        public JsonElement? AttributeCombinations { get; set; }

        [JsonPropertyName("attributes")]
        public List<MeliAttributeDto> Attributes { get; set; } = new();

        public string? ResolverSellerSku()
        {
            var attr = Attributes.FirstOrDefault(a =>
                string.Equals(a.Id, "SELLER_SKU", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(attr?.ValueName))
                return attr.ValueName;

            return string.IsNullOrWhiteSpace(SellerCustomField) ? null : SellerCustomField;
        }
    }
}
