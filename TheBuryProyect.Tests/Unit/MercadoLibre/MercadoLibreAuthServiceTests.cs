using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Helpers;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase 1: cifrado de tokens, state anti-CSRF, callback OAuth
/// y refresh automático con rotación de refresh_token.
/// </summary>
public class MercadoLibreAuthServiceTests
{
    private static MercadoLibreOptions OpcionesValidas(int stateLifetimeMinutes = 15) => new()
    {
        ClientId = "test-client-id",
        ClientSecret = "test-client-secret",
        RedirectUri = "https://test.ngrok-free.app/MercadoLibre/OAuthCallback",
        TokenRefreshMarginSeconds = 300,
        OAuthStateLifetimeMinutes = stateLifetimeMinutes
    };

    private static (MercadoLibreAuthService Servicio, FakeMercadoLibreApiClient Api, TestDbContextFactory Factory, MercadoLibreTokenProtector Protector)
        BuildServicio(MercadoLibreOptions? opciones = null)
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var dataProtection = new EphemeralDataProtectionProvider();
        var protector = new MercadoLibreTokenProtector(dataProtection);
        var api = new FakeMercadoLibreApiClient();

        var servicio = new MercadoLibreAuthService(
            factory,
            api,
            protector,
            dataProtection,
            Microsoft.Extensions.Options.Options.Create(opciones ?? OpcionesValidas()),
            NullLogger<MercadoLibreAuthService>.Instance);

        return (servicio, api, factory, protector);
    }

    // -----------------------------------------------------------------------
    // Cifrado de tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenProtector_Roundtrip_RecuperaElValorOriginal()
    {
        var protector = new MercadoLibreTokenProtector(new EphemeralDataProtectionProvider());

        var cifrado = protector.Protect("APP_USR-secreto-123");

        Assert.NotEqual("APP_USR-secreto-123", cifrado);
        Assert.DoesNotContain("secreto", cifrado);
        Assert.Equal("APP_USR-secreto-123", protector.Unprotect(cifrado));
    }

    // -----------------------------------------------------------------------
    // State anti-CSRF
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAuthorizationUrl_IncluyeParametrosObligatoriosYStateValido()
    {
        var (servicio, _, _, _) = BuildServicio();

        var url = servicio.BuildAuthorizationUrl();

        Assert.StartsWith("https://auth.mercadolibre.com.ar/authorization", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=test-client-id", url);
        Assert.Contains("redirect_uri=", url);

        var state = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"];
        Assert.True(servicio.ValidarState(state));
    }

    [Fact]
    public void BuildAuthorizationUrl_SinConfiguracion_Lanza()
    {
        var (servicio, _, _, _) = BuildServicio(new MercadoLibreOptions());

        Assert.False(servicio.EstaConfigurado);
        Assert.Throws<InvalidOperationException>(() => servicio.BuildAuthorizationUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("basura-no-firmada")]
    public void ValidarState_Invalido_DevuelveFalse(string? state)
    {
        var (servicio, _, _, _) = BuildServicio();

        Assert.False(servicio.ValidarState(state));
    }

    [Fact]
    public void ValidarState_Expirado_DevuelveFalse()
    {
        // Vigencia 0 minutos: cualquier state ya nació vencido.
        var (servicio, _, _, _) = BuildServicio(OpcionesValidas(stateLifetimeMinutes: 0));

        var url = servicio.BuildAuthorizationUrl();
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"];

        Assert.False(servicio.ValidarState(state));
    }

    // -----------------------------------------------------------------------
    // Callback OAuth
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleOAuthCallback_CreaCuentaConTokensCifrados()
    {
        var (servicio, api, factory, protector) = BuildServicio();

        api.TokenParaCode = new MeliTokenResponse
        {
            AccessToken = "APP_USR-access-1",
            RefreshToken = "TG-refresh-1",
            ExpiresIn = 21600,
            Scope = "offline_access read write",
            UserId = 123456
        };
        api.Usuario = new MeliUserDto { Id = 123456, Nickname = "VENDEDOR_TEST", SiteId = "MLA" };

        var cuenta = await servicio.HandleOAuthCallbackAsync("code-abc");

        await using var ctx = factory.CreateDbContext();
        var persistida = await ctx.MercadoLibreAccounts.SingleAsync();

        Assert.Equal(123456, persistida.MeliUserId);
        Assert.Equal("VENDEDOR_TEST", persistida.Nickname);
        Assert.Equal("MLA", persistida.SiteId);
        Assert.True(persistida.Activa);

        // Nunca en texto plano
        Assert.NotEqual("APP_USR-access-1", persistida.AccessTokenEncrypted);
        Assert.NotEqual("TG-refresh-1", persistida.RefreshTokenEncrypted);
        Assert.DoesNotContain("APP_USR", persistida.AccessTokenEncrypted);

        // Pero recuperables con el protector
        Assert.Equal("APP_USR-access-1", protector.Unprotect(persistida.AccessTokenEncrypted));
        Assert.Equal("TG-refresh-1", protector.Unprotect(persistida.RefreshTokenEncrypted));

        Assert.True(persistida.AccessTokenExpiresAtUtc > DateTime.UtcNow.AddHours(5));
        Assert.Equal(cuenta.Id, persistida.Id);
    }

    [Fact]
    public async Task HandleOAuthCallback_DosVecesMismoUsuario_NoDuplicaCuenta()
    {
        var (servicio, api, factory, _) = BuildServicio();

        api.TokenParaCode = new MeliTokenResponse
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresIn = 21600,
            UserId = 999
        };
        api.Usuario = new MeliUserDto { Id = 999, Nickname = "USUARIO", SiteId = "MLA" };

        await servicio.HandleOAuthCallbackAsync("code-1");

        api.TokenParaCode = new MeliTokenResponse
        {
            AccessToken = "access-2",
            RefreshToken = "refresh-2",
            ExpiresIn = 21600,
            UserId = 999
        };

        await servicio.HandleOAuthCallbackAsync("code-2");

        await using var ctx = factory.CreateDbContext();
        Assert.Equal(1, await ctx.MercadoLibreAccounts.CountAsync());
    }

    // -----------------------------------------------------------------------
    // Refresh automático
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetValidAccessToken_TokenVigente_NoRefresca()
    {
        var (servicio, api, factory, protector) = BuildServicio();

        await using (var ctx = factory.CreateDbContext())
        {
            ctx.MercadoLibreAccounts.Add(new MercadoLibreAccount
            {
                MeliUserId = 1,
                Nickname = "TEST",
                AccessTokenEncrypted = protector.Protect("access-vigente"),
                RefreshTokenEncrypted = protector.Protect("refresh-vigente"),
                AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(5),
                Activa = true
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = factory.CreateDbContext();
        var accountId = (await ctx2.MercadoLibreAccounts.SingleAsync()).Id;

        var token = await servicio.GetValidAccessTokenAsync(accountId);

        Assert.Equal("access-vigente", token);
        Assert.Equal(0, api.RefreshCalls);
    }

    [Fact]
    public async Task GetValidAccessToken_TokenVencido_RefrescaYRotaRefreshToken()
    {
        var (servicio, api, factory, protector) = BuildServicio();

        api.TokenParaRefresh = new MeliTokenResponse
        {
            AccessToken = "access-nuevo",
            RefreshToken = "refresh-nuevo",
            ExpiresIn = 21600,
            UserId = 1
        };

        int accountId;
        await using (var ctx = factory.CreateDbContext())
        {
            var cuenta = new MercadoLibreAccount
            {
                MeliUserId = 1,
                Nickname = "TEST",
                AccessTokenEncrypted = protector.Protect("access-viejo"),
                RefreshTokenEncrypted = protector.Protect("refresh-viejo"),
                AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10),
                Activa = true
            };
            ctx.MercadoLibreAccounts.Add(cuenta);
            await ctx.SaveChangesAsync();
            accountId = cuenta.Id;
        }

        var token = await servicio.GetValidAccessTokenAsync(accountId);

        Assert.Equal("access-nuevo", token);
        Assert.Equal(1, api.RefreshCalls);
        Assert.Equal("refresh-viejo", api.UltimoRefreshTokenUsado);

        // El refresh_token nuevo SIEMPRE reemplaza al anterior (ML los rota).
        await using var ctx3 = factory.CreateDbContext();
        var actualizada = await ctx3.MercadoLibreAccounts.SingleAsync();
        Assert.Equal("refresh-nuevo", protector.Unprotect(actualizada.RefreshTokenEncrypted));
        Assert.Equal("access-nuevo", protector.Unprotect(actualizada.AccessTokenEncrypted));
        Assert.True(actualizada.AccessTokenExpiresAtUtc > DateTime.UtcNow.AddHours(5));
    }
}
