using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace TheBuryProject.Tests.Integration;

/// <summary>H9-M6 — el índice de Seguridad (y la auditoría) por el pipeline HTTP real.</summary>
[Collection("HttpIntegration")]
public class SeguridadIndiceHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    public SeguridadIndiceHttpTests(CustomWebApplicationFactory factory) => _factory = factory;
    public Task InitializeAsync() => _factory.SeedTestUserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Index_SinParametros_Responde()
    {
        var client = _factory.CreateAuthenticatedClient();
        var resp = await client.GetAsync("/Seguridad");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{(int)resp.StatusCode}\n{body[..Math.Min(body.Length, 4000)]}");
    }

    private async Task<(HttpResponseMessage Resp, string Body)> GetAsync(string url)
    {
        var client = _factory.CreateAuthenticatedClient();
        var resp = await client.GetAsync(url);
        return (resp, await resp.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/Seguridad?tab=usuarios")]
    [InlineData("/Seguridad?tab=roles")]
    [InlineData("/Seguridad?tab=permisos-rol")]
    [InlineData("/Seguridad?tab=auditoria")]
    public async Task Index_CadaPestana_Responde200(string url)
    {
        var (resp, body) = await GetAsync(url);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{(int)resp.StatusCode} {url}");
        Assert.Contains("Seguridad", body);
    }

    [Theory]
    [InlineData("/Seguridad?tab=auditoria&pagina=2147483647")]
    [InlineData("/Seguridad?tab=auditoria&pagina=-5")]
    [InlineData("/Seguridad?tab=auditoria&pagina=0")]
    public async Task Auditoria_PaginaEnormeONegativa_NoDaError(string url)
    {
        var (resp, _) = await GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Auditoria_RangoInvertido_RedirigeSinDesdeNiHasta()
    {
        var (resp, _) = await GetAsync("/Seguridad?tab=auditoria&desde=2026-10-10&hasta=2026-10-01");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var destino = resp.Headers.Location!.ToString();
        Assert.Contains("tab=auditoria", destino);
        Assert.DoesNotContain("desde=", destino);
        Assert.DoesNotContain("hasta=", destino);
    }

    [Fact]
    public async Task Auditoria_RangoValido_Responde200()
    {
        var (resp, _) = await GetAsync("/Seguridad?tab=auditoria&desde=2026-10-01&hasta=2026-10-10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task RutaLegadaAuditoria_RedirigeALaPestana()
    {
        var (resp, _) = await GetAsync("/Seguridad/Auditoria?pagina=3");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("tab=auditoria", resp.Headers.Location!.ToString());
    }
}
