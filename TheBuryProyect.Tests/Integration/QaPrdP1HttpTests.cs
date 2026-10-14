using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-PRD-P1 (B4, B7, B8, B9) a nivel HTTP, sobre SQLite en memoria (sin datos reales).
/// Cada test aserta el comportamiento DESEADO: mientras el defecto siga abierto el test FALLA y esa
/// falla es la reproducción. Los tests "guarda" fijan lo que NO debe cambiar al corregir.
/// </summary>
[Collection("HttpIntegration")]
public class QaPrdP1HttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int ProductoId = 9200;
    private readonly CustomWebApplicationFactory _factory;

    public QaPrdP1HttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── B4: reintentos duplicados vs. movimientos legítimos ──────────────

    [Fact]
    public async Task B4_MismoRequestIdReenviado_RegistraUnSoloMovimiento()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithStockOperadorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockOperadorUserId);
        var token = await TokenAsync(client, $"/MovimientoStock/Create?productoId={ProductoId}");
        var requestId = Guid.NewGuid().ToString();

        for (var i = 0; i < 3; i++)
            await client.PostAsync("/MovimientoStock/Create", AjusteForm(token, 5, requestId));

        Assert.Equal(1, await ContarMovimientosAsync());
        Assert.Equal(15m, await StockAsync());
    }

    [Fact]
    public async Task B4_MismoRequestIdEnParalelo_RegistraUnSoloMovimiento()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithStockOperadorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockOperadorUserId);
        var token = await TokenAsync(client, $"/MovimientoStock/Create?productoId={ProductoId}");
        var requestId = Guid.NewGuid().ToString();

        await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => client.PostAsync("/MovimientoStock/Create", AjusteForm(token, 5, requestId))));

        Assert.Equal(1, await ContarMovimientosAsync());
        Assert.Equal(15m, await StockAsync());
    }

    [Fact]
    public async Task B4_Guarda_RequestIdDistintoConMismoContenido_SonMovimientosLegitimos()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithStockOperadorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockOperadorUserId);
        var token = await TokenAsync(client, $"/MovimientoStock/Create?productoId={ProductoId}");

        await client.PostAsync("/MovimientoStock/Create", AjusteForm(token, 5, Guid.NewGuid().ToString()));
        await client.PostAsync("/MovimientoStock/Create", AjusteForm(token, 5, Guid.NewGuid().ToString()));

        Assert.Equal(2, await ContarMovimientosAsync());
        Assert.Equal(20m, await StockAsync());
    }

    [Fact]
    public async Task B4_Guarda_SinRequestIdDosEntradasIgualesSiguenSiendoLegitimas()
    {
        // Compatibilidad: el flujo actual no envía token y dos entradas iguales seguidas pueden ser reales.
        await SeedProductoAsync();
        await _factory.SeedUserWithStockOperadorAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.StockOperadorUserId);
        var token = await TokenAsync(client, $"/MovimientoStock/Create?productoId={ProductoId}");

        await client.PostAsync("/MovimientoStock/Create", AjusteForm(token, 5, requestId: null));
        await client.PostAsync("/MovimientoStock/Create", AjusteForm(token, 5, requestId: null));

        Assert.Equal(2, await ContarMovimientosAsync());
        Assert.Equal(20m, await StockAsync());
    }

    // ── B7: el cambio de precio del modal Editar no deja historial reversible ─

    [Fact]
    public async Task B7_EditAjax_CambioDePrecio_QuedaEnElHistorialDelCatalogoYEsReversible()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditConPreciosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConPreciosUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (status, json) = await EditAjaxAsync(client, token, precioCompra: 1000, precioVenta: 1800);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        await using var db = await NewContextAsync();
        var detalle = await db.CambioPrecioDetalles.AsNoTracking()
            .Where(d => d.ProductoId == ProductoId && !d.IsDeleted)
            .OrderByDescending(d => d.Id).FirstOrDefaultAsync();
        Assert.NotNull(detalle);
        Assert.Equal(1500m, detalle!.PrecioAnterior);
        Assert.Equal(1800m, detalle.PrecioNuevo);
    }

    [Fact]
    public async Task B7_Guarda_EditarSinCambiarImportesNoCreaEventosDePrecio()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditConPreciosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConPreciosUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (status, json) = await EditAjaxAsync(client, token, precioCompra: 1000, precioVenta: 1500, nombre: "Otro nombre");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        await using var db = await NewContextAsync();
        Assert.Equal(0, await db.CambioPrecioDetalles.CountAsync(d => d.ProductoId == ProductoId));
        Assert.Equal(0, await db.PreciosHistoricos.CountAsync(h => h.ProductoId == ProductoId));
    }

    // ── B8: venta bajo costo ─────────────────────────────────────────────

    [Fact]
    public async Task B8_EditAjax_PrecioBajoElCosto_SeGuardaPeroDevuelveAdvertencia()
    {
        // Política vigente: no se bloquea (la simulación y el batch solo advierten). Se fija la advertencia
        // no bloqueante; bloquear o exigir permiso especial es decisión de negocio pendiente.
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditConPreciosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConPreciosUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (status, json) = await EditAjaxAsync(client, token, precioCompra: 1000, precioVenta: 900);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        await using var db = await NewContextAsync();
        Assert.Equal(900m, (await db.Productos.AsNoTracking().FirstAsync(p => p.Id == ProductoId)).PrecioVenta);
        Assert.True(json.RootElement.TryGetProperty("advertencias", out var advertencias)
                    && advertencias.ValueKind == JsonValueKind.Array && advertencias.GetArrayLength() > 0,
            "La respuesta debe incluir advertencias[] cuando el precio de venta queda por debajo del costo.");
    }

    [Fact]
    public async Task B8_Guarda_PrecioPorEncimaDelCosto_NoDevuelveAdvertencias()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoEditConPreciosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoEditConPreciosUserId);
        var token = await TokenAsync(client, "/Catalogo");

        var (_, json) = await EditAjaxAsync(client, token, precioCompra: 1000, precioVenta: 1800);

        Assert.True(json.RootElement.GetProperty("success").GetBoolean(), json.RootElement.ToString());
        Assert.False(json.RootElement.TryGetProperty("advertencias", out var a)
                     && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0);
    }

    // ── B9: controles que ofrece la UI vs. permisos efectivos ────────────

    [Fact]
    public async Task B9_Catalogo_SoloLectura_NoOfreceEliminarNiEditarComision()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoSoloLecturaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoSoloLecturaUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("id=\"catalogo-menu-eliminar\"", html);
        Assert.False(Regex.IsMatch(html, "<button[^>]*data-comision-producto-id=\"" + ProductoId + "\"", RegexOptions.Singleline),
            "La comisión se edita con productos.edit: un lector no debe ver el botón que abre el editor.");
    }

    [Fact]
    public async Task B9_Catalogo_SoloCrear_OfreceCrearPeroNoEditarEliminarNiComision()
    {
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoSoloCrearAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoSoloCrearUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.Contains("id=\"btn-crear-producto\"", html);
        Assert.DoesNotContain("data-prod-edit-id=\"" + ProductoId + "\"", html);
        Assert.DoesNotContain("id=\"catalogo-menu-eliminar\"", html);
        Assert.False(Regex.IsMatch(html, "<button[^>]*data-comision-producto-id=\"" + ProductoId + "\"", RegexOptions.Singleline));
    }

    [Fact]
    public async Task B9_Catalogo_SoloCrear_ExponeAlScriptLosPermisosEfectivosDeLaFila()
    {
        // La fila que arma producto-crear-modal.js tras crear un producto debe poder respetar permisos:
        // el contenedor tiene que publicar el permiso efectivo (data-can-*) para que el script lo lea.
        await SeedProductoAsync();
        await _factory.SeedUserWithProductoSoloCrearAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ProductoSoloCrearUserId);

        var html = await (await client.GetAsync("/Catalogo")).Content.ReadAsStringAsync();

        Assert.Matches("data-can-edit=\"false\"", html);
        Assert.Matches("data-can-delete=\"false\"", html);
    }

    [Fact]
    public void B9_ElScriptQueArmaLaFilaNueva_NoAgregaEditarEliminarYComisionSinPermiso()
    {
        var js = LeerRepo("wwwroot", "js", "producto-crear-modal.js");

        foreach (var control in new[] { "data-prod-edit-id", "data-prod-delete-id", "data-comision-producto-id" })
        {
            var idx = js.IndexOf(control, StringComparison.Ordinal);
            Assert.True(idx > 0, $"No se encontró {control} en la fila dinámica.");
            var contexto = js.Substring(Math.Max(0, idx - 400), Math.Min(400, idx));
            Assert.True(Regex.IsMatch(contexto, @"\bcan(Edit|Delete|Comision)\b|dataset\.can|data-can-"),
                $"{control} se agrega a la fila sin consultar un permiso efectivo.");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string LeerRepo(params string[] segmentos)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(segmentos).ToArray()));
    }

    private static FormUrlEncodedContent AjusteForm(string token, decimal cantidad, string? requestId)
    {
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ProductoId"] = ProductoId.ToString(),
            ["Tipo"] = "Entrada",
            ["Cantidad"] = cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Motivo"] = "Reposición QA-PRD-P1"
        };
        if (requestId != null)
            campos["RequestId"] = requestId;
        return new FormUrlEncodedContent(campos);
    }

    private async Task<(HttpStatusCode, JsonDocument)> EditAjaxAsync(
        HttpClient client, string token, int precioCompra, int precioVenta, string nombre = "Producto QA P1")
    {
        byte[] rowVersion;
        await using (var db = await NewContextAsync())
            rowVersion = (await db.Productos.AsNoTracking().FirstAsync(p => p.Id == ProductoId)).RowVersion;

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = ProductoId.ToString(),
            ["Codigo"] = "QA-PRD-P1",
            ["Nombre"] = nombre,
            ["CategoriaId"] = "9200",
            ["MarcaId"] = "9200",
            ["PrecioCompra"] = precioCompra.ToString(),
            ["PrecioVenta"] = precioVenta.ToString(),
            ["PorcentajeIVA"] = "21",
            ["StockMinimo"] = "0",
            ["StockActual"] = "10",
            ["Activo"] = "true",
            ["RowVersion"] = Convert.ToBase64String(rowVersion)
        });
        var response = await client.PostAsync($"/Producto/EditAjax?id={ProductoId}", form);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body));
    }

    private static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"No se encontró el token antiforgery en {url}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<AppDbContext> NewContextAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private async Task SeedProductoAsync()
    {
        await using var db = await NewContextAsync();
        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == ProductoId);
        if (producto == null)
        {
            db.Categorias.Add(new Categoria { Id = 9200, Nombre = "Cat QA-PRD-P1", RowVersion = new byte[8] });
            db.Marcas.Add(new Marca { Id = 9200, Nombre = "Marca QA-PRD-P1", RowVersion = new byte[8] });
            await db.SaveChangesAsync();
            db.Productos.Add(new Producto
            {
                Id = ProductoId,
                Codigo = "QA-PRD-P1",
                Nombre = "Producto QA P1",
                CategoriaId = 9200,
                MarcaId = 9200,
                PrecioCompra = 1000m,
                PrecioVenta = 1500m,
                PorcentajeIVA = 21m,
                StockActual = 10m,
                Activo = true,
                RowVersion = new byte[8]
            });
        }
        else
        {
            // Estado inicial determinista para cada test (la fábrica comparte la base entre tests).
            producto.Nombre = "Producto QA P1";
            producto.PrecioCompra = 1000m;
            producto.PrecioVenta = 1500m;
            producto.StockActual = 10m;
        }

        db.MovimientosStock.RemoveRange(db.MovimientosStock.Where(m => m.ProductoId == ProductoId));
        db.PreciosHistoricos.RemoveRange(db.PreciosHistoricos.Where(h => h.ProductoId == ProductoId));
        db.CambioPrecioDetalles.RemoveRange(db.CambioPrecioDetalles.Where(d => d.ProductoId == ProductoId));
        await db.SaveChangesAsync();
    }

    private async Task<int> ContarMovimientosAsync()
    {
        await using var db = await NewContextAsync();
        return await db.MovimientosStock.CountAsync(m => m.ProductoId == ProductoId);
    }

    private async Task<decimal> StockAsync()
    {
        await using var db = await NewContextAsync();
        return (await db.Productos.AsNoTracking().FirstAsync(p => p.Id == ProductoId)).StockActual;
    }
}
