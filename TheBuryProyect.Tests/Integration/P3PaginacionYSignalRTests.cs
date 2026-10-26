using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBuryProject.Tests.Integration;

/// <summary>FE-D-007 (paginación fuera de rango en CambiosPrecios) y SignalR sin ruido de consola (flaky de E2E).</summary>
[Collection("HttpIntegration")]
public class P3PaginacionYSignalRTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public P3PaginacionYSignalRTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/CambiosPrecios?page=-1")]
    [InlineData("/CambiosPrecios?page=0&pageSize=-5")]
    [InlineData("/CambiosPrecios?page=1&pageSize=1000000")]
    public async Task CambiosPrecios_PaginacionFueraDeRango_NoRompe(string url)
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var respuesta = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public void ClienteSignalR_NoEscribeSuPropioLogEnConsola()
    {
        var js = File.ReadAllText(ArchivoRepo("wwwroot", "js", "notificaciones.js"));

        Assert.Contains(".configureLogging(signalR.LogLevel.None)", js);
    }

    private static string ArchivoRepo(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray());
    }
}
