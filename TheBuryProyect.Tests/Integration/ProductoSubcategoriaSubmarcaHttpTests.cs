using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// RESCATE-PRODUCTO-P0 a nivel HTTP (SQLite en memoria): el modal Editar del catálogo envía
/// SubcategoriaId/SubmarcaId a Producto/EditAjax y deben persistirse y volver a presentarse en GetJson.
/// </summary>
[Collection("HttpIntegration")]
public class ProductoSubcategoriaSubmarcaHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int ProductoId = 9300;
    private const int CatId = 9300, SubAId = 9301, SubBId = 9302;
    private const int MarcaId = 9300, SubmarcaAId = 9301, SubmarcaBId = 9302;
    private readonly CustomWebApplicationFactory _factory;

    public ProductoSubcategoriaSubmarcaHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PROD10_EditAjax_CambiaRelaciones_LasGuardaYGetJsonLasVuelveAPresentar()
    {
        await SeedAsync();
        using var client = await ClienteAsync();
        var token = await TokenAsync(client);

        var (status, json) = await EditAjaxAsync(client, token, new()
        {
            ["SubcategoriaId"] = SubBId.ToString(),
            ["SubmarcaId"] = SubmarcaBId.ToString()
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        var bd = await LeerAsync();
        Assert.Equal(SubBId, bd.SubcategoriaId);
        Assert.Equal(SubmarcaBId, bd.SubmarcaId);

        var get = await client.GetAsync($"/Producto/GetJson/{ProductoId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(SubBId, doc.RootElement.GetProperty("subcategoriaId").GetInt32());
        Assert.Equal(SubmarcaBId, doc.RootElement.GetProperty("submarcaId").GetInt32());
    }

    [Fact]
    public async Task PROD02_EditAjax_SelectsVaciosEnviados_QuitanLasRelaciones()
    {
        await SeedAsync();
        using var client = await ClienteAsync();
        var token = await TokenAsync(client);

        // El <select> con "Sin subcategoría"/"Sin submarca" envía el campo con valor vacío.
        var (_, json) = await EditAjaxAsync(client, token, new()
        {
            ["SubcategoriaId"] = "",
            ["SubmarcaId"] = ""
        });

        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        var bd = await LeerAsync();
        Assert.Null(bd.SubcategoriaId);
        Assert.Null(bd.SubmarcaId);
    }

    [Fact]
    public async Task PROD09_EditAjax_ClienteQueOmiteLosCampos_NoBorraLasRelacionesGuardadas()
    {
        await SeedAsync();
        using var client = await ClienteAsync();
        var token = await TokenAsync(client);

        var (_, json) = await EditAjaxAsync(client, token, new(), nombre: "Renombrado sin tocar relaciones");

        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        var bd = await LeerAsync();
        Assert.Equal("Renombrado sin tocar relaciones", bd.Nombre);
        Assert.Equal(SubAId, bd.SubcategoriaId);
        Assert.Equal(SubmarcaAId, bd.SubmarcaId);
    }

    [Fact]
    public async Task PROD04_EditAjax_SubmarcaDeOtraMarca_RespondeErrorYNoGuarda()
    {
        await SeedAsync();
        using var client = await ClienteAsync();
        var token = await TokenAsync(client);

        var (_, json) = await EditAjaxAsync(client, token, new()
        {
            ["SubcategoriaId"] = SubBId.ToString(),
            ["SubmarcaId"] = CatId.ToString() // id de una marca raíz ajena como submarca
        }, nombre: "No debe guardarse");

        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var bd = await LeerAsync();
        Assert.Equal(SubAId, bd.SubcategoriaId);
        Assert.Equal(SubmarcaAId, bd.SubmarcaId);
        Assert.NotEqual("No debe guardarse", bd.Nombre);
    }

    private async Task<HttpClient> ClienteAsync()
    {
        await _factory.SeedUserWithProductoEditConPreciosAsync();
        return _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConPreciosUserId);
    }

    private async Task<(HttpStatusCode, JsonDocument)> EditAjaxAsync(
        HttpClient client, string token, Dictionary<string, string> relaciones, string nombre = "Producto PROD-RESCATE")
    {
        var bdActual = await LeerAsync();
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = ProductoId.ToString(),
            ["Codigo"] = "PROD-RESCATE",
            ["Nombre"] = nombre,
            ["CategoriaId"] = CatId.ToString(),
            ["MarcaId"] = MarcaId.ToString(),
            ["PrecioCompra"] = "1000",
            ["PrecioVenta"] = "1500",
            ["PorcentajeIVA"] = "21",
            ["StockMinimo"] = "0",
            ["StockActual"] = "10",
            ["Activo"] = "true",
            ["RowVersion"] = Convert.ToBase64String(bdActual.RowVersion)
        };
        foreach (var kv in relaciones)
            campos[kv.Key] = kv.Value;

        var response = await client.PostAsync($"/Producto/EditAjax?id={ProductoId}", new FormUrlEncodedContent(campos));
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body));
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Catalogo");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "No se encontró el token antiforgery en /Catalogo.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<AppDbContext> NewContextAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private async Task<Producto> LeerAsync()
    {
        await using var db = await NewContextAsync();
        return await db.Productos.AsNoTracking().FirstAsync(p => p.Id == ProductoId);
    }

    /// <summary>Estado inicial determinista: Subcategoría A y Submarca A (la fábrica comparte la base entre tests).</summary>
    private async Task SeedAsync()
    {
        await using var db = await NewContextAsync();
        if (!await db.Categorias.AnyAsync(c => c.Id == CatId))
        {
            db.Categorias.Add(new Categoria { Id = CatId, Codigo = "RESC-CAT", Nombre = "Cat RESCATE", RowVersion = new byte[8] });
            db.Marcas.Add(new Marca { Id = MarcaId, Codigo = "RESC-MAR", Nombre = "Marca RESCATE", RowVersion = new byte[8] });
            await db.SaveChangesAsync();
            db.Categorias.AddRange(
                new Categoria { Id = SubAId, Codigo = "RESC-SA", Nombre = "Sub A RESCATE", ParentId = CatId, RowVersion = new byte[8] },
                new Categoria { Id = SubBId, Codigo = "RESC-SB", Nombre = "Sub B RESCATE", ParentId = CatId, RowVersion = new byte[8] });
            db.Marcas.AddRange(
                new Marca { Id = SubmarcaAId, Codigo = "RESC-MA", Nombre = "Submarca A RESCATE", ParentId = MarcaId, RowVersion = new byte[8] },
                new Marca { Id = SubmarcaBId, Codigo = "RESC-MB", Nombre = "Submarca B RESCATE", ParentId = MarcaId, RowVersion = new byte[8] });
            await db.SaveChangesAsync();
        }

        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == ProductoId);
        if (producto == null)
        {
            db.Productos.Add(new Producto
            {
                Id = ProductoId, Codigo = "PROD-RESCATE", Nombre = "Producto PROD-RESCATE",
                CategoriaId = CatId, SubcategoriaId = SubAId, MarcaId = MarcaId, SubmarcaId = SubmarcaAId,
                PrecioCompra = 1000m, PrecioVenta = 1500m, PorcentajeIVA = 21m, StockActual = 10m,
                Activo = true, RowVersion = new byte[8]
            });
        }
        else
        {
            producto.Nombre = "Producto PROD-RESCATE";
            producto.SubcategoriaId = SubAId;
            producto.SubmarcaId = SubmarcaAId;
        }
        await db.SaveChangesAsync();
    }
}
