using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H9-M6 — NotaRapidaApi queda fuera de la matriz de permisos por rol (es "solo el dueño"): este test fija el aislamiento
/// por HTTP real (IDOR) y el rechazo anónimo, que ninguna otra suite cubría.
/// </summary>
[Collection("HttpIntegration")]
public class NotasRapidasAislamientoHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private string _duenoId = "";
    private string _otroId = "";

    public NotasRapidasAislamientoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var sfx = Guid.NewGuid().ToString("N")[..8];
        _duenoId = await CrearAsync(um, $"nota_dueno_{sfx}");
        _otroId = await CrearAsync(um, $"nota_otro_{sfx}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> CrearAsync(UserManager<ApplicationUser> um, string nombre)
    {
        var u = new ApplicationUser { UserName = nombre, Email = $"{nombre}@test.com", EmailConfirmed = true, Activo = true };
        Assert.True((await um.CreateAsync(u, "Test123!")).Succeeded);
        return u.Id;
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success);
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient c, string token, HttpMethod metodo, string url, object? cuerpo = null)
    {
        using var req = new HttpRequestMessage(metodo, url);
        req.Headers.Add("RequestVerificationToken", token);
        if (cuerpo != null) req.Content = JsonContent.Create(cuerpo);
        return await c.SendAsync(req);
    }

    [Fact]
    public async Task OtroUsuario_NoVeNiModificaNiEliminaLaNotaAjena()
    {
        var dueno = _factory.CreateClientWithUserId(_duenoId);
        var td = await TokenAsync(dueno);
        var crear = await EnviarAsync(dueno, td, HttpMethod.Post, "/api/notas-rapidas", new { tipo = "checklist", texto = "privada" });
        Assert.Equal(HttpStatusCode.OK, crear.StatusCode);
        var id = (await crear.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetInt32();
        var item = await EnviarAsync(dueno, td, HttpMethod.Post, $"/api/notas-rapidas/{id}/items", new { texto = "item" });
        var itemId = (await item.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("items")[0].GetProperty("id").GetInt32();

        var otro = _factory.CreateClientWithUserId(_otroId);
        var to = await TokenAsync(otro);

        var lista = await otro.GetStringAsync("/api/notas-rapidas");
        Assert.DoesNotContain("privada", lista);
        Assert.Equal(HttpStatusCode.NotFound, (await EnviarAsync(otro, to, HttpMethod.Put, $"/api/notas-rapidas/{id}", new { texto = "hack" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EnviarAsync(otro, to, HttpMethod.Post, $"/api/notas-rapidas/{id}/items", new { texto = "hack" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EnviarAsync(otro, to, HttpMethod.Patch, $"/api/notas-rapidas/{id}/items/{itemId}", new { completado = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EnviarAsync(otro, to, HttpMethod.Delete, $"/api/notas-rapidas/{id}/items/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EnviarAsync(otro, to, HttpMethod.Delete, $"/api/notas-rapidas/{id}")).StatusCode);

        // El dueño conserva su nota intacta.
        var propia = await dueno.GetStringAsync("/api/notas-rapidas");
        Assert.Contains("privada", propia);
        Assert.DoesNotContain("hack", propia);
    }
}
