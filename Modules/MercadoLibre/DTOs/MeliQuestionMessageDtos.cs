using System.Text.Json.Serialization;

namespace TheBuryProject.Modules.MercadoLibre.DTOs
{
    /// <summary>
    /// Pregunta preventa devuelta por GET /questions/{id} o /questions/search.
    /// Mapea solo lo que el ERP necesita; el resto del payload vive en RawJson.
    /// </summary>
    public class MeliQuestionDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("item_id")]
        public string? ItemId { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("from")]
        public MeliQuestionFromDto? From { get; set; }

        [JsonPropertyName("date_created")]
        public DateTimeOffset? DateCreated { get; set; }

        [JsonPropertyName("answer")]
        public MeliQuestionAnswerDto? Answer { get; set; }
    }

    public class MeliQuestionFromDto
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }
    }

    public class MeliQuestionAnswerDto
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("date_created")]
        public DateTimeOffset? DateCreated { get; set; }
    }

    /// <summary>Página de GET /questions/search.</summary>
    public class MeliQuestionSearchPageDto
    {
        [JsonPropertyName("questions")]
        public List<MeliQuestionDto> Questions { get; set; } = new();
    }

    /// <summary>
    /// Mensaje postventa devuelto por GET /messages/packs/{packId}/sellers/{sellerId}.
    /// </summary>
    public class MeliMessageDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("from")]
        public MeliMessageUserDto? From { get; set; }

        [JsonPropertyName("to")]
        public MeliMessageUserDto? To { get; set; }

        [JsonPropertyName("message_date")]
        public MeliMessageDateDto? MessageDate { get; set; }
    }

    public class MeliMessageUserDto
    {
        [JsonPropertyName("user_id")]
        public long? UserId { get; set; }
    }

    public class MeliMessageDateDto
    {
        [JsonPropertyName("created")]
        public DateTimeOffset? Created { get; set; }
    }

    /// <summary>Respuesta de GET /messages/packs/{packId}/sellers/{sellerId}.</summary>
    public class MeliMessagesResponseDto
    {
        [JsonPropertyName("messages")]
        public List<MeliMessageDto> Messages { get; set; } = new();
    }
}
