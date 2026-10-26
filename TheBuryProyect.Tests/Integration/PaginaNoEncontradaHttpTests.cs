using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Infrastructure;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FE-D-002: un 404 en navegación HTML mostraba una página en blanco. Ahora se muestra una página con el layout,
/// conservando el código 404; las llamadas AJAX/fetch siguen recibiendo un 404 sin cuerpo.
/// </summary>
[Collection("HttpIntegration")]
public class PaginaNoEncontradaHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaginaNoEncontradaHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClienteAsync()
    {
        await _factory.SeedTestUserAsync();
        // Las navegaciones HTML pasan por TerminosCondicionesMiddleware: el usuario de prueba ya aceptó los términos.
        using (var scope = _factory.Services.CreateScope())
        {
            var terminos = scope.ServiceProvider.GetRequiredService<ITerminosCondicionesService>();
            if (!await terminos.UsuarioAceptoVersionActualAsync(TestAuthHandler.UserId))
                await terminos.RegistrarAceptacionAsync(TestAuthHandler.UserId, "testuser", "Test");
        }
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Navegacion_404_MuestraPaginaConLayout_ConservaCodigo()
    {
        var cliente = await ClienteAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/Proveedor/Details/99999999");
        req.Headers.Accept.ParseAdd("text/html");

        var respuesta = await cliente.SendAsync(req);
        var html = await respuesta.Content.ReadAsStringAsync();

        Assert.True(respuesta.StatusCode == HttpStatusCode.NotFound, "inesperado " + respuesta.StatusCode);
        Assert.Contains("No encontramos lo que buscás", html);
        Assert.Contains("<h1", html);
    }

    [Fact]
    public async Task Ajax_404_SigueSinCuerpo()
    {
        var cliente = await ClienteAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/Proveedor/Details/99999999");
        req.Headers.Accept.ParseAdd("text/html");
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var respuesta = await cliente.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_404_SigueSinCuerpo()
    {
        var cliente = await ClienteAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/ruta-inexistente");
        req.Headers.Accept.ParseAdd("text/html");

        var respuesta = await cliente.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.DoesNotContain("No encontramos", await respuesta.Content.ReadAsStringAsync());
    }
}
