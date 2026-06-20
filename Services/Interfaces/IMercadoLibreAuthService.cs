using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Flujo OAuth Authorization Code de Mercado Libre y ciclo de vida de tokens.
    /// </summary>
    public interface IMercadoLibreAuthService
    {
        /// <summary>
        /// Indica si ClientId/ClientSecret/RedirectUri están configurados.
        /// </summary>
        bool EstaConfigurado { get; }

        /// <summary>
        /// Construye la URL de autorización (auth.mercadolibre.com.ar) con un
        /// state firmado anti-CSRF.
        /// </summary>
        string BuildAuthorizationUrl();

        /// <summary>
        /// Valida el state firmado del callback. Devuelve false si es inválido o expiró.
        /// </summary>
        bool ValidarState(string? state);

        /// <summary>
        /// Intercambia el code por tokens, consulta /users/me y crea o actualiza
        /// la cuenta con los tokens cifrados.
        /// </summary>
        Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default);

        /// <summary>
        /// Devuelve un access token vigente para la cuenta, refrescándolo si está
        /// vencido o por vencer. Persiste SIEMPRE el refresh_token nuevo.
        /// </summary>
        Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default);
    }
}
