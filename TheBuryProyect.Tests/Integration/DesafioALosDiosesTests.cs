using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests HTTP de extremo a extremo para la vía alternativa "desafiar a los dioses"
/// en la pantalla de Términos y Condiciones del login. Cada test usa un usuario
/// propio (GUID) para no depender ni contaminar el estado de otros tests del
/// mismo fixture compartido.
/// </summary>
[Collection("HttpIntegration")]
public class DesafioALosDiosesTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DesafioALosDiosesTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetLogin_TerminosPendientes_MuestraEnlaceDesafiarDebajoDelBotonPrincipal()
    {
        var userId = await SeedUsuarioAsync("pendiente");
        var client = _factory.CreateClientWithUserId(userId);

        var response = await client.GetAsync("/Identity/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Términos y Condiciones de uso", html);
        Assert.Contains("desafiar a los dioses", html);

        var indexBoton = html.IndexOf("Aceptar y continuar", StringComparison.Ordinal);
        var indexEnlace = html.IndexOf("desafiar a los dioses", StringComparison.Ordinal);
        Assert.True(indexBoton >= 0 && indexEnlace > indexBoton,
            "El enlace 'desafiar a los dioses' debe aparecer después del botón principal.");
    }

    [Fact]
    public async Task PostLogin_AceptacionNormal_NoActivaElFlag()
    {
        var userId = await SeedUsuarioAsync("normal");
        var client = _factory.CreateClientWithUserId(userId);

        var token = await GetAntiForgeryTokenAsync(client);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.NombreCompleto"] = "Usuario Normal",
            ["Input.AceptaTerminos"] = "true",
            ["ReturnUrl"] = "/"
        };
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var fila = await GetFilaAsync(userId);
        Assert.NotNull(fila);
        Assert.False(fila!.DesafioALosDiosesActivado);
        Assert.Null(fila.DesafioALosDiosesActivadoEnUtc);
    }

    [Fact]
    public async Task PostDesafiar_ActivaFlagYRegistraFechaUtc()
    {
        var userId = await SeedUsuarioAsync("desafio");
        var client = _factory.CreateClientWithUserId(userId);

        var token = await GetAntiForgeryTokenAsync(client);
        var antes = DateTime.UtcNow;
        var response = await PostDesafiarAsync(client, token);
        var despues = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var fila = await GetFilaAsync(userId);
        Assert.NotNull(fila);
        Assert.True(fila!.DesafioALosDiosesActivado);
        Assert.NotNull(fila.DesafioALosDiosesActivadoEnUtc);
        Assert.InRange(fila.DesafioALosDiosesActivadoEnUtc!.Value, antes.AddSeconds(-2), despues.AddSeconds(2));
    }

    [Fact]
    public async Task PostDesafiar_TrasActivar_PuedeContinuarYTerminosNoVuelvenAAparecer()
    {
        var userId = await SeedUsuarioAsync("continua");
        var client = _factory.CreateClientWithUserId(userId);

        var token = await GetAntiForgeryTokenAsync(client);
        await PostDesafiarAsync(client, token);

        var getDeNuevo = await client.GetAsync("/Identity/Account/Login");

        Assert.Equal(HttpStatusCode.Redirect, getDeNuevo.StatusCode);
        Assert.Equal("/", getDeNuevo.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostDesafiar_LlamadoDosVeces_EsIdempotente()
    {
        var userId = await SeedUsuarioAsync("idem");
        var client = _factory.CreateClientWithUserId(userId);

        var token = await GetAntiForgeryTokenAsync(client);
        var r1 = await PostDesafiarAsync(client, token);
        var r2 = await PostDesafiarAsync(client, token);

        Assert.Equal(HttpStatusCode.Redirect, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, r2.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var cantidad = await context.TerminosCondicionesAceptaciones.CountAsync(a => a.UsuarioId == userId);
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task PostDesafiar_SoloAfectaAlUsuarioAutenticadoEnSesion_NuncaAOtro()
    {
        var javierId = await SeedUsuarioAsync("javier");
        var otroId = await SeedUsuarioAsync("otro");

        var clientJavier = _factory.CreateClientWithUserId(javierId);
        var tokenJavier = await GetAntiForgeryTokenAsync(clientJavier);
        var respJavier = await PostDesafiarAsync(clientJavier, tokenJavier);
        Assert.Equal(HttpStatusCode.Redirect, respJavier.StatusCode);

        var clientOtro = _factory.CreateClientWithUserId(otroId);
        var getOtro = await clientOtro.GetAsync("/Identity/Account/Login");
        var bodyOtro = await getOtro.Content.ReadAsStringAsync();
        Assert.Contains("Términos y Condiciones de uso", bodyOtro);

        var filaJavier = await GetFilaAsync(javierId);
        Assert.NotNull(filaJavier);
        Assert.True(filaJavier!.DesafioALosDiosesActivado);

        var filaOtro = await GetFilaAsync(otroId);
        Assert.Null(filaOtro);
    }

    // -------------------------------------------------------------------------
    // Helpers
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

    private async Task<TerminoCondicionAceptacion?> GetFilaAsync(string usuarioId)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.TerminosCondicionesAceptaciones.FirstOrDefaultAsync(a => a.UsuarioId == usuarioId);
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

    private static Task<HttpResponseMessage> PostDesafiarAsync(HttpClient client, string token, string returnUrl = "/")
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = returnUrl
        };
        return client.PostAsync("/Identity/Account/Login?handler=Desafiar", new FormUrlEncodedContent(form));
    }
}
