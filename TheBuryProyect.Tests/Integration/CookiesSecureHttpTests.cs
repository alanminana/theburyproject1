using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Las cookies de antiforgery y de TempData salían sin `Secure` detrás de Caddy (HTTPS). Con X-Forwarded-Proto: https deben
/// llevar `Secure`; sin él (HTTP directo, desarrollo) no.
/// </summary>
[Collection("HttpIntegration")]
public class CookiesSecureHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CookiesSecureHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<string> CookieAntiforgeryAsync(bool detrasDeHttps)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Identity/Account/Login");
        if (detrasDeHttps) request.Headers.Add("X-Forwarded-Proto", "https");

        var respuesta = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Antiforgery."));
    }

    [Fact]
    public async Task DetrasDeHttps_LaCookieAntiforgery_EsSecure()
        => Assert.Contains("secure", await CookieAntiforgeryAsync(detrasDeHttps: true), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task ConHttpDirecto_LaCookieAntiforgery_NoEsSecure()
        => Assert.DoesNotContain("secure", await CookieAntiforgeryAsync(detrasDeHttps: false), StringComparison.OrdinalIgnoreCase);
}
