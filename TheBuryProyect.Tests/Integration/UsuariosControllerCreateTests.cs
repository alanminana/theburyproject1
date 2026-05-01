using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

public class UsuariosControllerCreateTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UsuariosControllerCreateTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_UserNameDuplicado_RetornaError()
    {
        await _factory.SeedTestUserAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var existingUserName = $"dup_user_{suffix}";
        await SeedUserAsync(existingUserName, $"dup_user_{suffix}@test.com");

        var client = _factory.CreateAuthenticatedClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var response = await PostCreateAsync(client, token, existingUserName, $"nuevo_{suffix}@test.com");
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(
            $"El nombre de usuario '{existingUserName}' ya está en uso.",
            GetErrors(json));
    }

    [Fact]
    public async Task Create_EmailDuplicado_RetornaError()
    {
        await _factory.SeedTestUserAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var existingEmail = $"dup_email_{suffix}@test.com";
        await SeedUserAsync($"dup_email_{suffix}", existingEmail);

        var client = _factory.CreateAuthenticatedClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var response = await PostCreateAsync(client, token, $"nuevo_email_{suffix}", existingEmail);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(
            $"El email '{existingEmail}' ya está en uso.",
            GetErrors(json));
    }

    [Fact]
    public async Task Create_UsuarioValido_CreaUsuario()
    {
        await _factory.SeedTestUserAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var userName = $"valid_user_{suffix}";
        var email = $"valid_user_{suffix}@test.com";

        var client = _factory.CreateAuthenticatedClient();
        var token = await GetAntiForgeryTokenAsync(client);

        var response = await PostCreateAsync(client, token, userName, email);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        Assert.True(await context.Users.AnyAsync(u => u.UserName == userName && u.Email == email));
    }

    private async Task SeedUserAsync(string userName, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            Activo = true
        };

        var result = await userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Usuarios/Create");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "No se encontró el token antiforgery en el modal de creación.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostCreateAsync(
        HttpClient client,
        string token,
        string userName,
        string email)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["UserName"] = userName,
            ["Email"] = email,
            ["Password"] = "Test123!",
            ["ConfirmPassword"] = "Test123!",
            ["RolesSeleccionados"] = "SuperAdmin",
            ["Activo"] = "true",
            ["EmailConfirmed"] = "true"
        };

        return client.PostAsync("/Usuarios/Create", new FormUrlEncodedContent(form));
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private static List<string> GetErrors(JsonDocument json)
    {
        return json.RootElement
            .GetProperty("errors")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToList();
    }
}
