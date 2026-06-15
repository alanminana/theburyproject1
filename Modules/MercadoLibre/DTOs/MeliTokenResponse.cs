using System.Text.Json.Serialization;

namespace TheBuryProject.Modules.MercadoLibre.DTOs
{
    /// <summary>
    /// Respuesta de POST /oauth/token (authorization_code y refresh_token).
    /// </summary>
    public class MeliTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        /// <summary>
        /// Segundos de vida del access token (típicamente 21600 = 6 horas).
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("user_id")]
        public long UserId { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
