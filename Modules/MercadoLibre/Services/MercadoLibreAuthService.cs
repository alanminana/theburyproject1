using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Exceptions;
using TheBuryProject.Modules.MercadoLibre.Options;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.Services
{
    public class MercadoLibreAuthService : IMercadoLibreAuthService
    {
        private const string StatePurpose = "TheBuryProject.MercadoLibre.OAuthState.v1";

        // Serializa los refresh para evitar dos refresh concurrentes con el mismo
        // refresh_token (Mercado Libre invalida el anterior al usarlo).
        private static readonly SemaphoreSlim RefreshLock = new(1, 1);

        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IMercadoLibreApiClient _apiClient;
        private readonly IMercadoLibreTokenProtector _tokenProtector;
        private readonly IDataProtector _stateProtector;
        private readonly MercadoLibreOptions _options;
        private readonly ILogger<MercadoLibreAuthService> _logger;

        public MercadoLibreAuthService(
            IDbContextFactory<AppDbContext> contextFactory,
            IMercadoLibreApiClient apiClient,
            IMercadoLibreTokenProtector tokenProtector,
            IDataProtectionProvider dataProtectionProvider,
            IOptions<MercadoLibreOptions> options,
            ILogger<MercadoLibreAuthService> logger)
        {
            _contextFactory = contextFactory;
            _apiClient = apiClient;
            _tokenProtector = tokenProtector;
            _stateProtector = dataProtectionProvider.CreateProtector(StatePurpose);
            _options = options.Value;
            _logger = logger;
        }

        public bool EstaConfigurado => _options.EstaConfigurado;

        public string BuildAuthorizationUrl()
        {
            if (!EstaConfigurado)
                throw new InvalidOperationException(
                    "Mercado Libre no está configurado. Completar MercadoLibre:ClientId, ClientSecret y RedirectUri (UserSecrets o variables de entorno).");

            var state = CrearState();

            return $"{_options.AuthorizationBaseUrl.TrimEnd('/')}/authorization" +
                   $"?response_type=code" +
                   $"&client_id={Uri.EscapeDataString(_options.ClientId)}" +
                   $"&redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}" +
                   $"&state={Uri.EscapeDataString(state)}";
        }

        public bool ValidarState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
                return false;

            try
            {
                var payload = _stateProtector.Unprotect(state);
                var parts = payload.Split('|');

                if (parts.Length != 2)
                    return false;

                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                    return false;

                var emitido = new DateTime(ticks, DateTimeKind.Utc);
                var vigencia = TimeSpan.FromMinutes(_options.OAuthStateLifetimeMinutes);

                return DateTime.UtcNow - emitido <= vigencia;
            }
            catch
            {
                // State corrupto o firmado con otra clave: rechazar sin loguear contenido.
                return false;
            }
        }

        public async Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
        {
            var token = await _apiClient.ExchangeAuthorizationCodeAsync(code, ct);
            var usuario = await _apiClient.GetCurrentUserAsync(token.AccessToken, ct);

            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var cuenta = await context.MercadoLibreAccounts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.MeliUserId == usuario.Id, ct);

            if (cuenta is null)
            {
                cuenta = new MercadoLibreAccount { MeliUserId = usuario.Id };
                context.MercadoLibreAccounts.Add(cuenta);
            }

            cuenta.Nickname = usuario.Nickname;
            cuenta.SiteId = string.IsNullOrWhiteSpace(usuario.SiteId) ? _options.SiteId : usuario.SiteId;
            cuenta.Scope = token.Scope;
            cuenta.Activa = true;
            cuenta.IsDeleted = false;

            AplicarTokens(cuenta, token.AccessToken, token.RefreshToken, token.ExpiresIn);

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Cuenta de Mercado Libre conectada: user_id {MeliUserId}, nickname {Nickname}",
                cuenta.MeliUserId, cuenta.Nickname);

            return cuenta;
        }

        public async Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(ct);

            var cuenta = await context.MercadoLibreAccounts
                .FirstOrDefaultAsync(a => a.Id == accountId && a.Activa, ct)
                ?? throw new MercadoLibreApiException($"Cuenta de Mercado Libre {accountId} inexistente o inactiva.");

            var margen = TimeSpan.FromSeconds(_options.TokenRefreshMarginSeconds);

            if (cuenta.AccessTokenExpiresAtUtc - margen > DateTime.UtcNow)
                return _tokenProtector.Unprotect(cuenta.AccessTokenEncrypted);

            await RefreshLock.WaitAsync(ct);
            try
            {
                // Releer dentro del lock: otro hilo pudo haber refrescado ya.
                await context.Entry(cuenta).ReloadAsync(ct);

                if (cuenta.AccessTokenExpiresAtUtc - margen > DateTime.UtcNow)
                    return _tokenProtector.Unprotect(cuenta.AccessTokenEncrypted);

                var refreshToken = _tokenProtector.Unprotect(cuenta.RefreshTokenEncrypted);
                var nuevo = await _apiClient.RefreshAccessTokenAsync(refreshToken, ct);

                AplicarTokens(cuenta, nuevo.AccessToken, nuevo.RefreshToken, nuevo.ExpiresIn);
                await context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Access token de Mercado Libre refrescado para cuenta {AccountId} (expira {ExpiraUtc:u})",
                    cuenta.Id, cuenta.AccessTokenExpiresAtUtc);

                return nuevo.AccessToken;
            }
            finally
            {
                RefreshLock.Release();
            }
        }

        private void AplicarTokens(MercadoLibreAccount cuenta, string accessToken, string refreshToken, int expiresInSeconds)
        {
            cuenta.AccessTokenEncrypted = _tokenProtector.Protect(accessToken);

            // Mercado Libre rota el refresh_token en cada uso: guardar siempre el nuevo.
            if (!string.IsNullOrEmpty(refreshToken))
                cuenta.RefreshTokenEncrypted = _tokenProtector.Protect(refreshToken);

            cuenta.AccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);
        }

        private string CrearState()
        {
            var payload = string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.UtcNow.Ticks}|{Guid.NewGuid():N}");

            return _stateProtector.Protect(payload);
        }
    }
}
