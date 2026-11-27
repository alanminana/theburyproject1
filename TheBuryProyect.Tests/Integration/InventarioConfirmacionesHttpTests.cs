using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Las confirmaciones declarativas de Inventario viajan en <c>data-inv-confirm</c>. Si el JSON no queda codificado como
/// atributo (p. ej. con <c>@Json.Serialize</c>), el navegador lo corta en la primera comilla y el operador confirma una
/// operación sensible sin ver qué cambia. Se verifica sobre el HTML que entrega el servidor.
/// </summary>
[Collection("HttpIntegration.2")]
public class InventarioConfirmacionesHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InventarioConfirmacionesHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(false, "¿Activar la trazabilidad individual?")]
    [InlineData(true, "¿Desactivar la trazabilidad individual?")]
    public async Task Unidades_LaConfirmacionDeTrazabilidad_LlegaComoJsonCompleto(bool requiereSerie, string titulo)
    {
        var productoId = await SembrarProductoAsync(requiereSerie);
        using var client = _factory.CreateAuthenticatedClient();

        var respuesta = await client.GetAsync($"/Producto/Unidades/{productoId}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var confirmaciones = Confirmaciones(await respuesta.Content.ReadAsStringAsync());

        var confirmacion = Assert.Single(confirmaciones, c => c.GetProperty("titulo").GetString() == titulo);
        Assert.NotEmpty(confirmacion.GetProperty("consecuencias").EnumerateArray());
        Assert.NotEmpty(confirmacion.GetProperty("datos").EnumerateArray());
    }

    [Fact]
    public async Task Productos_LosEnlacesDelMenuDeFila_ExistenEnElServidor()
    {
        var productoId = await SembrarProductoAsync(requiereSerie: false);
        using var client = _factory.CreateAuthenticatedClient();
        var html = await (await client.GetAsync("/Catalogo?searchTerm=de%20prueba")).Content.ReadAsStringAsync();
        var fila = Regex.Match(html, "data-row-actions-open[^>]*data-producto-id=\"" + productoId + "\"[^>]*>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(fila);

        foreach (var atributo in new[] { "data-inventario-url", "data-kardex-url" })
        {
            var url = WebUtility.HtmlDecode(Regex.Match(fila, atributo + "=\"([^\"]+)\"").Groups[1].Value);
            Assert.NotEmpty(url);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        }
    }

    private static List<JsonElement> Confirmaciones(string html) =>
        Regex.Matches(html, "data-inv-confirm=\"([^\"]*)\"")
            .Select(m => JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value)).RootElement.Clone())
            .ToList();

    private async Task<int> SembrarProductoAsync(bool requiereSerie)
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var codigo = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var categoria = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        db.AddRange(categoria, marca);
        await db.SaveChangesAsync();
        // Comillas en el nombre: el peor caso para un atributo.
        var producto = new Producto
        {
            Codigo = "C" + codigo, Nombre = "Producto \"de prueba\" " + codigo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 50m, PrecioVenta = 100m, PorcentajeIVA = 21m, StockActual = 2m, Activo = true,
            RequiereNumeroSerie = requiereSerie
        };
        db.Productos.Add(producto);
        await db.SaveChangesAsync();
        return producto.Id;
    }
}
