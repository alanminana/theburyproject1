namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Configuración del módulo MercadoLibre.
    /// ClientId/ClientSecret/RedirectUri NUNCA se hardcodean:
    /// van en UserSecrets (dev) o variables de entorno (prod), sección "MercadoLibre".
    /// </summary>
    public class MercadoLibreOptions
    {
        public const string SectionName = "MercadoLibre";

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Debe coincidir EXACTAMENTE con la Redirect URI configurada en
        /// Mercado Libre Developers (ej: https://xxxx.ngrok-free.app/MercadoLibre/OAuthCallback).
        /// Si cambia la URL de ngrok, actualizar ambos lados.
        /// </summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// Site de operación. MLA = Argentina.
        /// </summary>
        public string SiteId { get; set; } = "MLA";

        /// <summary>
        /// Base del flujo de autorización (Argentina).
        /// </summary>
        public string AuthorizationBaseUrl { get; set; } = "https://auth.mercadolibre.com.ar";

        public string ApiBaseUrl { get; set; } = "https://api.mercadolibre.com";

        /// <summary>
        /// Margen en segundos antes de la expiración real para refrescar el access token.
        /// </summary>
        public int TokenRefreshMarginSeconds { get; set; } = 300;

        /// <summary>
        /// Vigencia máxima del parámetro state del flujo OAuth.
        /// </summary>
        public int OAuthStateLifetimeMinutes { get; set; } = 15;

        public bool EstaConfigurado =>
            !string.IsNullOrWhiteSpace(ClientId) &&
            !string.IsNullOrWhiteSpace(ClientSecret) &&
            !string.IsNullOrWhiteSpace(RedirectUri);
    }
}
