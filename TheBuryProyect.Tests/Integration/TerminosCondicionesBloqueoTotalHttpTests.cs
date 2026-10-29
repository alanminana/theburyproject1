using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Infrastructure;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// SEG-BAJA: un usuario con cookie ya emitida que no aceptó los términos vigentes no puede hacer POST ni llamar APIs
/// (antes el middleware solo redirigía GET con Accept text/html).
/// </summary>
[Collection("HttpIntegration")]
public sealed class TerminosCondicionesBloqueoTotalHttpTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new() { ExigirTerminosEnTodasLasPeticiones = true };

    public void Dispose() => _factory.Dispose();

    private HttpClient Cliente() => _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task SinAceptar_NavegacionHtml_RedirigeAlLogin()
    {
        await _factory.SeedTestUserAsync();
        using var cliente = Cliente();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/Proveedor/Index");
        req.Headers.Accept.ParseAdd("text/html");

        var resp = await cliente.SendAsync(req);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", resp.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("POST", "/Proveedor/Create")]
    [InlineData("POST", "/api/cualquier-cosa")]
    [InlineData("PUT", "/api/cualquier-cosa")]
    [InlineData("DELETE", "/api/cualquier-cosa")]
    [InlineData("GET", "/api/cualquier-cosa")]
    public async Task SinAceptar_PostYApis_Responden403(string metodo, string url)
    {
        await _factory.SeedTestUserAsync();
        using var cliente = Cliente();
        using var req = new HttpRequestMessage(new HttpMethod(metodo), url);
        req.Headers.Accept.ParseAdd("application/json");
        if (metodo != "GET" && metodo != "DELETE") req.Content = new FormUrlEncodedContent(new Dictionary<string, string>());

        var resp = await cliente.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Contains("terminos_pendientes", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SinAceptar_LoginYHealth_NoSeBloquean()
    {
        await _factory.SeedTestUserAsync();
        using var cliente = Cliente();

        foreach (var url in new[] { "/Identity/Account/Login", "/Identity/Account/AccessDenied", "/health/live" })
        {
            var resp = await cliente.GetAsync(url);
            var cuerpo = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("terminos_pendientes", cuerpo);
        }
    }

    [Fact]
    public async Task TrasAceptar_PostYApis_YaNoLosBloqueaElMiddleware()
    {
        await _factory.SeedTestUserAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var terminos = scope.ServiceProvider.GetRequiredService<ITerminosCondicionesService>();
            await terminos.RegistrarAceptacionAsync(TestAuthHandler.UserId, "testuser", "Test");
        }
        using var cliente = Cliente();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/Proveedor/Create") { Content = new FormUrlEncodedContent(new Dictionary<string, string>()) };
        req.Headers.Accept.ParseAdd("application/json");

        var resp = await cliente.SendAsync(req);

        Assert.DoesNotContain("terminos_pendientes", await resp.Content.ReadAsStringAsync());
    }
}
