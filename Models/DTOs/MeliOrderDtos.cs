using System.Text.Json.Serialization;

namespace TheBuryProject.Models.DTOs
{
    /// <summary>
    /// Orden de Mercado Libre (GET /orders/{id} y /orders/search).
    /// Solo los campos que el ERP consume; el JSON completo se guarda crudo.
    /// </summary>
    public class MeliOrderDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("date_created")]
        public DateTimeOffset? DateCreated { get; set; }

        [JsonPropertyName("date_closed")]
        public DateTimeOffset? DateClosed { get; set; }

        [JsonPropertyName("total_amount")]
        public decimal TotalAmount { get; set; }

        [JsonPropertyName("paid_amount")]
        public decimal? PaidAmount { get; set; }

        [JsonPropertyName("currency_id")]
        public string? CurrencyId { get; set; }

        [JsonPropertyName("buyer")]
        public MeliOrderBuyerDto? Buyer { get; set; }

        [JsonPropertyName("order_items")]
        public List<MeliOrderItemDto> OrderItems { get; set; } = new();

        [JsonPropertyName("payments")]
        public List<MeliOrderPaymentDto>? Payments { get; set; }

        [JsonPropertyName("shipping")]
        public MeliOrderShippingDto? Shipping { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        /// <summary>Comisión total = suma de sale_fee por línea (sale_fee es por unidad).</summary>
        public decimal ComisionTotal()
            => OrderItems.Sum(i => (i.SaleFee ?? 0m) * i.Quantity);
    }

    public class MeliOrderBuyerDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("nickname")]
        public string? Nickname { get; set; }
    }

    public class MeliOrderItemDto
    {
        [JsonPropertyName("item")]
        public MeliOrderItemRefDto Item { get; set; } = new();

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("unit_price")]
        public decimal UnitPrice { get; set; }

        /// <summary>Comisión de ML por unidad vendida.</summary>
        [JsonPropertyName("sale_fee")]
        public decimal? SaleFee { get; set; }

        [JsonPropertyName("currency_id")]
        public string? CurrencyId { get; set; }
    }

    public class MeliOrderItemRefDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("variation_id")]
        public long? VariationId { get; set; }

        [JsonPropertyName("seller_sku")]
        public string? SellerSku { get; set; }

        [JsonPropertyName("seller_custom_field")]
        public string? SellerCustomField { get; set; }

        public string? ResolverSellerSku()
            => !string.IsNullOrWhiteSpace(SellerSku) ? SellerSku : SellerCustomField;
    }

    public class MeliOrderPaymentDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("transaction_amount")]
        public decimal? TransactionAmount { get; set; }

        [JsonPropertyName("shipping_cost")]
        public decimal? ShippingCost { get; set; }
    }

    public class MeliOrderShippingDto
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }
    }

    /// <summary>
    /// Página de /orders/search (results + paging offset clásico).
    /// </summary>
    public class MeliOrderSearchPageDto
    {
        [JsonPropertyName("results")]
        public List<MeliOrderDto> Results { get; set; } = new();

        [JsonPropertyName("paging")]
        public MeliPagingDto? Paging { get; set; }
    }

    /// <summary>
    /// Envío de Mercado Libre (GET /shipments/{id}).
    /// </summary>
    public class MeliShipmentDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("substatus")]
        public string? Substatus { get; set; }

        [JsonPropertyName("tracking_number")]
        public string? TrackingNumber { get; set; }

        [JsonPropertyName("tracking_method")]
        public string? TrackingMethod { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("logistic_type")]
        public string? LogisticType { get; set; }

        [JsonPropertyName("last_updated")]
        public DateTimeOffset? LastUpdated { get; set; }

        [JsonPropertyName("status_history")]
        public MeliShipmentStatusHistoryDto? StatusHistory { get; set; }

        [JsonPropertyName("shipping_option")]
        public MeliShippingOptionDto? ShippingOption { get; set; }

        [JsonIgnore]
        public string? RawJson { get; set; }
    }

    public class MeliShipmentStatusHistoryDto
    {
        [JsonPropertyName("date_shipped")]
        public DateTimeOffset? DateShipped { get; set; }

        [JsonPropertyName("date_delivered")]
        public DateTimeOffset? DateDelivered { get; set; }
    }

    public class MeliShippingOptionDto
    {
        /// <summary>Costo del envío para el comprador.</summary>
        [JsonPropertyName("cost")]
        public decimal? Cost { get; set; }

        /// <summary>Costo de lista del envío (referencia del costo real).</summary>
        [JsonPropertyName("list_cost")]
        public decimal? ListCost { get; set; }
    }
}
