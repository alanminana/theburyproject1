using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Regresión: Fase 1 del plan de remediación Categoría/Inventario (commit def652d) eliminó
/// el POST Categoria/Delete asumiendo "0 referencias en toda la app", pero
/// categoria-editar-modal.js sí lo invoca (form-delete-categoria, botón "Eliminar" del modal
/// de Catalogo/Index_tw) — el grep de la auditoría original no cubría rutas embebidas como
/// string literal en JS. El botón quedó 404 en producción hasta este fix.
/// </summary>
[Collection("HttpIntegration")]
public class CategoriaControllerDeleteTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CategoriaControllerDeleteTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_CategoriaDelete_ConCategoriaSinHijosNiProductos_RedirigeYMarcaBorrada()
    {
        await _factory.SeedTestUserAsync();
        using var client = _factory.CreateAuthenticatedClient();

        var categoriaId = await CrearCategoriaAsync("QA-DEL-CTRL");
        var token = await GetAntiForgeryTokenAsync(client, "/Catalogo/Index?tab=categorias");

        var response = await client.PostAsync(
            $"/Categoria/Delete/{categoriaId}?returnUrl=%2FCatalogo",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Catalogo", response.Headers.Location?.OriginalString);

        var borrada = await ObtenerIsDeletedAsync(categoriaId);
        Assert.True(borrada);
    }

    private async Task<int> CrearCategoriaAsync(string codigo)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var categoria = new Categoria
        {
            Codigo = codigo,
            Nombre = "Categoría de prueba (borrado)",
            Activo = true
        };
        context.Categorias.Add(categoria);
        await context.SaveChangesAsync();
        return categoria.Id;
    }

    private async Task<bool> ObtenerIsDeletedAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var categoria = await context.Categorias.IgnoreQueryFilters().SingleAsync(c => c.Id == id);
        return categoria.IsDeleted;
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, $"No se encontró el token antiforgery en {url}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
