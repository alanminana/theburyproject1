using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests HTTP de extremo a extremo para el bug real encontrado en preproducción: un ReturnUrl no
/// local (externo, malformado, protocol-relative) enviado a Login mientras el usuario acepta los
/// Términos y Condiciones hacía que <c>LocalRedirect(returnUrl)</c> lanzara
/// <see cref="InvalidOperationException"/> (no capturada por la Razor Page), devolviendo HTTP 500 en
/// lugar de redirigir de forma segura. El fix valida <c>Url.IsLocalUrl</c> ANTES de cada
/// LocalRedirect en LoginModel (OnGetAsync/OnPostAsync/OnPostDesafiarAsync), con fallback al home.
/// Reutiliza el mismo flujo real ("desafiar a los dioses") que <see cref="DesafioALosDiosesTests"/>
/// porque ya ejercita OnPostDesafiarAsync con autenticación + antiforgery reales.
/// </summary>
[Collection("HttpIntegration")]
public class LoginReturnUrlSeguridadTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LoginReturnUrlSeguridadTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://evil.example.com/phish")]
    [InlineData("//evil.example.com")]
    [InlineData("/\t/evil.example.com")]
    [InlineData(" not a url at all ")]
    public async Task PostDesafiar_ReturnUrlNoLocal_NoLanza500_YCaeAFallbackSeguro(string returnUrlPeligroso)
    {
        var userId = await SeedUsuarioAsync("returnurl-nolocal");
        var client = _factory.CreateClientWithUserId(userId);

        var token = await GetAntiForgeryTokenAsync(client);
        var response = await PostDesafiarAsync(client, token, returnUrlPeligroso);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // ObtenerReturnUrlSeguraOFallback cae siempre a Url.Content("~/") cuando el returnUrl
        // recibido no pasa Url.IsLocalUrl: la ruta interna segura esperada es exactamente "/",
        // no una heurística aparte de "no es externa" (evita reimplementar la validación real).
        var location = response.Headers.Location?.ToString();
        Assert.Equal("/", location);
    }

    [Fact]
    public async Task PostDesafiar_ReturnUrlLocalValida_RedirigeAEsaRuta()
    {
        var userId = await SeedUsuarioAsync("returnurl-local");
        var client = _factory.CreateClientWithUserId(userId);

        var token = await GetAntiForgeryTokenAsync(client);
        var response = await PostDesafiarAsync(client, token, "/Ventas");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Ventas", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostDesafiar_ReturnUrlNullOVacia_CaeAHome()
    {
        var userId = await SeedUsuarioAsync("returnurl-vacia");
        var client = _factory.CreateClientWithUserId(userId);

        var token = await GetAntiForgeryTokenAsync(client);
        var response = await PostDesafiarAsync(client, token, returnUrl: "");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetLogin_ReturnUrlExterna_UsuarioYaAceptoTerminos_NoLanza500_YCaeAFallbackSeguro()
    {
        // El otro punto de entrada real: usuario autenticado que YA aceptó términos, entra a
        // Login con un ReturnUrl externo en la query (ej. copiado/pegado o manipulado) — antes de
        // llegar a mostrar el paso de términos, OnGetAsync ya intentaba el LocalRedirect.
        var userId = await SeedUsuarioAsync("returnurl-get");
        await AceptarTerminosAsync(userId);
        var client = _factory.CreateClientWithUserId(userId);

        var response = await client.GetAsync("/Identity/Account/Login?ReturnUrl=" +
            Uri.EscapeDataString("https://evil.example.com"));

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    // -------------------------------------------------------------------------
    // Helpers (alineados con DesafioALosDiosesTests para reusar el mismo fixture)
    // -------------------------------------------------------------------------

    private async Task<string> SeedUsuarioAsync(string prefijo)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var userName = $"{prefijo}_{Guid.NewGuid():N}";
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@test.com",
            EmailConfirmed = true,
            Activo = true
        };

        var result = await userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private async Task AceptarTerminosAsync(string userId)
    {
        var client = _factory.CreateClientWithUserId(userId);
        var token = await GetAntiForgeryTokenAsync(client);
        var response = await PostDesafiarAsync(client, token, "/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Identity/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "No se encontró el token antiforgery en la pantalla de Términos y Condiciones.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostDesafiarAsync(HttpClient client, string token, string returnUrl)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = returnUrl
        };
        return client.PostAsync("/Identity/Account/Login?handler=Desafiar", new FormUrlEncodedContent(form));
    }
}
