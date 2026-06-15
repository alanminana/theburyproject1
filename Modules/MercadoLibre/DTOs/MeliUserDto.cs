using System.Text.Json.Serialization;

namespace TheBuryProject.Modules.MercadoLibre.DTOs
{
    /// <summary>
    /// Respuesta (parcial) de GET /users/me.
    /// </summary>
    public class MeliUserDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("nickname")]
        public string Nickname { get; set; } = string.Empty;

        [JsonPropertyName("site_id")]
        public string SiteId { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}
