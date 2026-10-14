using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-PRD-P1 B9: la UI del catálogo ofrece exactamente lo que el servidor va a aceptar (camino positivo y
/// negativo) para productos y marcas, y el servidor sigue rechazando el POST sin permiso.
/// </summary>
[Collection("HttpIntegration")]
public class QaPrdP1B9CorreccionesHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int ProductoId = 9210;
    private const int MarcaId = 9210;
    private readonly CustomWebApplicationFactory _factory;

    public QaPrdP1B9CorreccionesHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task B9_UsuarioCompleto_VeEliminarComisionEditableYAccionesDeMarca()
    {
        await SeedAsync();
        await _factory.SeedUserWithProductoYMarcaCompletoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoMarcaCompletoUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.Contains("id=\"catalogo-menu-eliminar\"", html);
        Assert.True(Regex.IsMatch(html, "<button[^>]*data-comision-producto-id=\"" + ProductoId + "\"", RegexOptions.Singleline));
        Assert.Contains("data-prod-edit-id=\"" + ProductoId + "\"", html);
        Assert.Contains("data-marca-edit-id=\"" + MarcaId + "\"", html);
        Assert.Contains("data-marca-delete-id=\"" + MarcaId + "\"", html);
        Assert.Matches("<tbody id=\"productos-tbody\"[^>]*data-can-edit=\"true\"[^>]*data-can-delete=\"true\"[^>]*data-can-comision=\"true\"", html);
        Assert.Matches("<tbody id=\"marcas-tbody\"[^>]*data-can-edit=\"true\"[^>]*data-can-delete=\"true\"", html);
    }

    [Fact]
    public async Task B9_SoloLectura_NoVeAccionesDeMarcaNiPermisosEfectivosEnElTbody()
    {
        await SeedAsync();
        await _factory.SeedUserWithProductoYMarcaSoloLecturaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoMarcaSoloLecturaUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("data-marca-edit-id=\"" + MarcaId + "\"", html);
        Assert.DoesNotContain("data-marca-delete-id=\"" + MarcaId + "\"", html);
        Assert.Matches("<tbody id=\"productos-tbody\"[^>]*data-can-edit=\"false\"[^>]*data-can-delete=\"false\"[^>]*data-can-comision=\"false\"", html);
        Assert.Matches("<tbody id=\"marcas-tbody\"[^>]*data-can-edit=\"false\"[^>]*data-can-delete=\"false\"", html);
        // La comisión se sigue mostrando, sin el botón que abre el editor.
        Assert.Contains("data-producto-comision", html);
    }

    [Fact]
    public async Task B9_ServidorRechazaEliminarMarcaSinPermisoAunqueSeFuerceElPost()
    {
        await SeedAsync();
        await _factory.SeedUserWithProductoYMarcaSoloLecturaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoMarcaSoloLecturaUserId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync($"/Marca/Delete/{MarcaId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, $"Se obtuvo {(int)response.StatusCode}.");
        await using var db = await NewContextAsync();
        Assert.False((await db.Marcas.IgnoreQueryFilters().AsNoTracking().FirstAsync(m => m.Id == MarcaId)).IsDeleted);
    }

    [Fact]
    public void B9_ElScriptDeMarcaNuevaTampocoOfreceAccionesSinPermiso()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "wwwroot", "js", "marca-crear-modal.js"));

        Assert.Matches(@"canEdit\s*\?\s*'<button[^']*data-marca-edit-id", js);
        Assert.Matches(@"canDelete\s*\?\s*'<button[^']*data-marca-delete-id", js);
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<AppDbContext> NewContextAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private async Task SeedAsync()
    {
        await using var db = await NewContextAsync();
        if (await db.Productos.AnyAsync(p => p.Id == ProductoId)) return;

        db.Categorias.Add(new Categoria { Id = 9210, Nombre = "Cat QA-PRD-P1 B9", RowVersion = new byte[8] });
        db.Marcas.Add(new Marca { Id = MarcaId, Nombre = "Marca QA-PRD-P1 B9", RowVersion = new byte[8] });
        await db.SaveChangesAsync();
        db.Productos.Add(new Producto
        {
            Id = ProductoId, Codigo = "QA-PRD-P1-B9", Nombre = "Producto B9", CategoriaId = 9210, MarcaId = MarcaId,
            PrecioCompra = 1000m, PrecioVenta = 1500m, PorcentajeIVA = 21m, StockActual = 10m, Activo = true,
            RowVersion = new byte[8]
        });
        await db.SaveChangesAsync();
    }
}
