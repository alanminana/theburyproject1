using System.Net;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-01 AC-01-6 (permisos por acción, antiforgery y auditoría), AC-01-7 (exportación del historial) y AC-01-9
/// (alcanzabilidad desde el menú) por el pipeline HTTP real: autorización + antiforgery + binding + servicio + vistas Razor.
/// </summary>
[Collection("HttpIntegration.3")]
public class ListaPrecioHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ListaPrecioHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await _factory.SeedUserWithListaPrecioAdminAsync();
        await _factory.SeedUserWithListaPrecioSoloVerAsync();
        await _factory.SeedUserWithListaPrecioGerenteAsync();
        await _factory.SeedUserWithClienteSoloLecturaAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private static string CodigoNuevo() => "T" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    private async Task<ListaPrecio> SembrarListaAsync(string? codigo = null, Action<ListaPrecio>? ajustar = null)
    {
        await using var ctx = await NuevoContextoAsync();
        codigo ??= CodigoNuevo();
        var lista = new ListaPrecio
        {
            Nombre = "Lista " + codigo, Codigo = codigo,
            Tipo = TipoListaPrecio.Contado, Activa = true, Orden = 1
        };
        ajustar?.Invoke(lista);
        ctx.ListasPrecios.Add(lista);
        await ctx.SaveChangesAsync();
        return lista;
    }

    private async Task<Producto> SembrarProductoAsync(decimal costo = 100m)
    {
        await using var ctx = await NuevoContextoAsync();
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = "P" + code, Nombre = "Producto " + code, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = costo, PrecioVenta = 150m, PorcentajeIVA = 21m, StockActual = 5m, Activo = true
        };
        ctx.Productos.Add(prod);
        await ctx.SaveChangesAsync();
        return prod;
    }

    private async Task<ListaPrecio?> LeerListaAsync(string codigo)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.ListasPrecios.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Codigo == codigo);
    }

    private async Task<int> ContarEventosAsync(string accion, string? detalleContiene = null)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.SeguridadEventosAuditoria.CountAsync(e =>
            e.Modulo == "precios" && e.Accion == accion && (detalleContiene == null || (e.Detalle != null && e.Detalle.Contains(detalleContiene))));
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, params (string, string)[] campos)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(campos.Select(c => new KeyValuePair<string, string>(c.Item1, c.Item2)))
        };
        req.Headers.Add("RequestVerificationToken", await TokenAsync(client));
        return await client.SendAsync(req);
    }

    private static (string, string)[] FormLista(string codigo, string nombre = "Lista de prueba", params (string, string)[] extra)
    {
        var baseCampos = new List<(string, string)>
        {
            ("Nombre", nombre), ("Codigo", codigo), ("Tipo", "Contado"), ("Orden", "3"),
            ("Activa", "true"), ("EsPredeterminada", "false"), ("ReglaRedondeo", "")
        };
        foreach (var e in extra)
        {
            baseCampos.RemoveAll(c => c.Item1 == e.Item1);
            baseCampos.Add(e);
        }
        return baseCampos.ToArray();
    }

    private static void AssertDenegado(HttpResponseMessage resp) =>
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Redirect,
            $"Se esperaba 403 o redirección a acceso denegado, llegó {(int)resp.StatusCode}.");

    // ── AC-01-6: permisos por acción ─────────────────────────────────────

    [Fact]
    public async Task SoloVer_VeElListadoYLosPreciosPeroNoRecibeNingunaAccionDeEscritura()
    {
        var lista = await SembrarListaAsync();
        var prod = await SembrarProductoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioSoloVerUserId);

        var index = await client.GetAsync("/ListaPrecio");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains(lista.Codigo, html);
        Assert.DoesNotContain("Nueva lista", html);
        Assert.DoesNotContain("/ListaPrecio/Editar/", html);
        Assert.DoesNotContain("data-lp-baja", html);

        var precios = await client.GetAsync($"/ListaPrecio/Precios/{lista.Id}");
        var preciosHtml = await precios.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, precios.StatusCode);
        Assert.Contains(prod.Codigo, preciosHtml);
        Assert.DoesNotContain("/ListaPrecio/FijarPrecio", preciosHtml);
    }

    [Fact]
    public async Task SoloVer_NoPuedeCrearEditarDarDeBajaNiFijarPrecios_YNoCambiaNada()
    {
        var lista = await SembrarListaAsync();
        var prod = await SembrarProductoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioSoloVerUserId);
        var codigoNuevo = CodigoNuevo();

        AssertDenegado(await client.GetAsync("/ListaPrecio/Crear"));
        AssertDenegado(await client.GetAsync($"/ListaPrecio/Editar/{lista.Id}"));
        AssertDenegado(await client.GetAsync($"/ListaPrecio/FijarPrecio?listaId={lista.Id}&productoId={prod.Id}"));
        AssertDenegado(await PostFormAsync(client, "/ListaPrecio/Crear", FormLista(codigoNuevo)));
        AssertDenegado(await PostFormAsync(client, $"/ListaPrecio/Editar/{lista.Id}", FormLista(lista.Codigo, "Cambiada", ("Id", lista.Id.ToString()))));
        AssertDenegado(await PostFormAsync(client, $"/ListaPrecio/Baja/{lista.Id}", ("rowVersion", Convert.ToBase64String(lista.RowVersion))));
        AssertDenegado(await PostFormAsync(client, $"/ListaPrecio/FijarPrecio?listaId={lista.Id}&productoId={prod.Id}", ("precio", "500")));

        Assert.Null(await LeerListaAsync(codigoNuevo));
        var bd = await LeerListaAsync(lista.Codigo);
        Assert.False(bd!.IsDeleted);
        Assert.Equal("Lista " + lista.Codigo, bd.Nombre);
        await using var ctx = await NuevoContextoAsync();
        Assert.False(await ctx.ProductosPrecios.AnyAsync(p => p.ProductoId == prod.Id));
    }

    [Fact]
    public async Task SinPermisoDePrecios_NoPuedeVerElListado()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteSoloLecturaUserId);

        AssertDenegado(await client.GetAsync("/ListaPrecio"));
    }

    [Fact]
    public async Task Gerente_PuedeVerYFijarPrecios_PeroNoAdministrarLasListas()
    {
        var lista = await SembrarListaAsync();
        var prod = await SembrarProductoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioGerenteUserId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/ListaPrecio/Precios/{lista.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/ListaPrecio/FijarPrecio?listaId={lista.Id}&productoId={prod.Id}")).StatusCode);
        var fijar = await PostFormAsync(client, $"/ListaPrecio/FijarPrecio?listaId={lista.Id}&productoId={prod.Id}", ("precio", "175.50"));
        Assert.Equal(HttpStatusCode.Redirect, fijar.StatusCode);

        AssertDenegado(await client.GetAsync("/ListaPrecio/Crear"));
        AssertDenegado(await PostFormAsync(client, $"/ListaPrecio/Baja/{lista.Id}", ("rowVersion", Convert.ToBase64String(lista.RowVersion))));
        Assert.False((await LeerListaAsync(lista.Codigo))!.IsDeleted);
    }

    [Fact]
    public async Task Admin_SinTokenAntiforgery_SeRechazaElAlta()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);
        var codigo = CodigoNuevo();
        var req = new HttpRequestMessage(HttpMethod.Post, "/ListaPrecio/Crear")
        {
            Content = new FormUrlEncodedContent(FormLista(codigo).Select(c => new KeyValuePair<string, string>(c.Item1, c.Item2)))
        };

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Null(await LeerListaAsync(codigo));
    }

    // ── AC-01-1 / AC-01-2 / AC-01-6: ABM por HTTP con auditoría ──────────

    [Fact]
    public async Task Admin_CreaEditaYDaDeBajaUnaLista_ConEventosDeAuditoria()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);
        var codigo = CodigoNuevo();

        var alta = await PostFormAsync(client, "/ListaPrecio/Crear",
            FormLista(codigo.ToLowerInvariant(), "Mayorista HTTP", ("Tipo", "Mayorista"), ("MargenPorcentaje", "35.5"), ("MargenMinimoPorcentaje", "20"),
                ("RecargoPorcentaje", "5"), ("ReglaRedondeo", "decena")));
        Assert.Equal(HttpStatusCode.Redirect, alta.StatusCode);
        var creada = await LeerListaAsync(codigo);
        Assert.NotNull(creada);
        Assert.Equal(TipoListaPrecio.Mayorista, creada!.Tipo);
        Assert.Equal(35.5m, creada.MargenPorcentaje);
        Assert.Equal(20m, creada.MargenMinimoPorcentaje);
        Assert.Equal("decena", creada.ReglaRedondeo);
        Assert.Equal(1, await ContarEventosAsync("crear-lista", codigo));

        var form = await client.GetAsync($"/ListaPrecio/Editar/{creada.Id}");
        var formHtml = await form.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Contains("name=\"RowVersion\"", formHtml);
        Assert.Contains("value=\"35.5\"", formHtml);

        var edicion = await PostFormAsync(client, $"/ListaPrecio/Editar/{creada.Id}",
            FormLista(codigo, "Mayorista renombrada", ("Id", creada.Id.ToString()), ("RowVersion", Convert.ToBase64String(creada.RowVersion)),
                ("MargenPorcentaje", "40"), ("MargenMinimoPorcentaje", "25"), ("Notas", "revisar en enero")));
        Assert.Equal(HttpStatusCode.Redirect, edicion.StatusCode);
        var editada = await LeerListaAsync(codigo);
        Assert.Equal("Mayorista renombrada", editada!.Nombre);
        Assert.Equal(25m, editada.MargenMinimoPorcentaje);
        Assert.Equal("revisar en enero", editada.Notas);
        Assert.Equal(1, await ContarEventosAsync("editar-lista", codigo));

        var baja = await PostFormAsync(client, $"/ListaPrecio/Baja/{creada.Id}", ("rowVersion", Convert.ToBase64String(editada.RowVersion)));
        Assert.Equal(HttpStatusCode.Redirect, baja.StatusCode);
        Assert.True((await LeerListaAsync(codigo))!.IsDeleted);
        Assert.Equal(1, await ContarEventosAsync("baja-lista", $"id={creada.Id}"));
    }

    [Fact]
    public async Task Admin_AltaConCodigoRepetidoOReglasInvalidas_MuestraElErrorYNoCreaNada()
    {
        var existente = await SembrarListaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);

        var repetido = await PostFormAsync(client, "/ListaPrecio/Crear", FormLista(existente.Codigo.ToLowerInvariant(), "Duplicada"));
        var repetidoHtml = WebUtility.HtmlDecode(await repetido.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, repetido.StatusCode);
        Assert.Contains("Ya existe una lista con el código", repetidoHtml);

        var codigo = CodigoNuevo();
        var minimo = await PostFormAsync(client, "/ListaPrecio/Crear", FormLista(codigo, "Mínimo alto", ("MargenPorcentaje", "10"), ("MargenMinimoPorcentaje", "30")));
        Assert.Contains("El margen mínimo no puede ser mayor que el margen de la lista.", WebUtility.HtmlDecode(await minimo.Content.ReadAsStringAsync()));

        var fuera = await PostFormAsync(client, "/ListaPrecio/Crear", FormLista(codigo, "Fuera de rango", ("RecargoPorcentaje", "1500")));
        Assert.Contains("El recargo debe estar entre 0 y 999,99.", WebUtility.HtmlDecode(await fuera.Content.ReadAsStringAsync()));

        Assert.Null(await LeerListaAsync(codigo));
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(1, await ctx.ListasPrecios.CountAsync(l => l.Codigo == existente.Codigo));
        Assert.Equal(0, await ContarEventosAsync("crear-lista", codigo));
    }

    [Fact]
    public async Task Admin_EdicionConRowVersionObsoleto_AvisaQueOtroUsuarioLaModifico()
    {
        var lista = await SembrarListaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);

        var resp = await PostFormAsync(client, $"/ListaPrecio/Editar/{lista.Id}",
            FormLista(lista.Codigo, "Pisada", ("Id", lista.Id.ToString()), ("RowVersion", Convert.ToBase64String(new byte[] { 7, 7, 7, 7, 7, 7, 7, 7 }))));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("modificada por otro usuario", WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync()));
        Assert.Equal("Lista " + lista.Codigo, (await LeerListaAsync(lista.Codigo))!.Nombre);
    }

    [Fact]
    public async Task Admin_NoPuedeDarDeBajaLaPredeterminada_YElListadoNoOfreceLaBaja()
    {
        var codigo = CodigoNuevo();
        await using (var ctx = await NuevoContextoAsync())
        {
            // La base de la fábrica es compartida por la clase: se deja el estado como estaba al terminar.
            foreach (var otra in await ctx.ListasPrecios.Where(l => l.EsPredeterminada).ToListAsync())
                otra.EsPredeterminada = false;
            await ctx.SaveChangesAsync();
        }
        var predeterminada = await SembrarListaAsync(codigo, l => l.EsPredeterminada = true);
        try
        {
            using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);

            var index = WebUtility.HtmlDecode(await (await client.GetAsync("/ListaPrecio")).Content.ReadAsStringAsync());
            var fila = Regex.Match(index, $"<tr[^>]*>(?:(?!</tr>).)*{codigo}(?:(?!</tr>).)*</tr>", RegexOptions.Singleline).Value;
            Assert.Contains("Predeterminada", fila);
            Assert.DoesNotContain("data-lp-baja", fila);

            var baja = await PostFormAsync(client, $"/ListaPrecio/Baja/{predeterminada.Id}", ("rowVersion", Convert.ToBase64String(predeterminada.RowVersion)));
            Assert.Equal(HttpStatusCode.Redirect, baja.StatusCode);
            var seguimiento = await client.GetAsync("/ListaPrecio");
            Assert.Contains("No se puede dar de baja la lista predeterminada", WebUtility.HtmlDecode(await seguimiento.Content.ReadAsStringAsync()));
            Assert.False((await LeerListaAsync(codigo))!.IsDeleted);
        }
        finally
        {
            await using var ctx = await NuevoContextoAsync();
            var l = await ctx.ListasPrecios.SingleAsync(x => x.Id == predeterminada.Id);
            l.EsPredeterminada = false;
            await ctx.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Admin_FijarPrecio_ValidaYPersiste()
    {
        var lista = await SembrarListaAsync(null, l => { l.MargenPorcentaje = 50m; l.MargenMinimoPorcentaje = 20m; l.ReglaRedondeo = "ninguno"; });
        var prod = await SembrarProductoAsync(costo: 100m);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);
        var url = $"/ListaPrecio/FijarPrecio?listaId={lista.Id}&productoId={prod.Id}";

        var bajo = await PostFormAsync(client, url, ("precio", "110"));
        Assert.Equal(HttpStatusCode.OK, bajo.StatusCode);
        Assert.Contains("inferior al mínimo permitido", WebUtility.HtmlDecode(await bajo.Content.ReadAsStringAsync()));

        var vacio = await PostFormAsync(client, url, ("precio", ""));
        Assert.Contains("Ingresá un precio mayor a cero", WebUtility.HtmlDecode(await vacio.Content.ReadAsStringAsync()));

        await using (var ctx = await NuevoContextoAsync())
            Assert.False(await ctx.ProductosPrecios.AnyAsync(p => p.ProductoId == prod.Id));
        Assert.Equal(0, await ContarEventosAsync("fijar-precio", $"producto={prod.Id} "));

        var ok = await PostFormAsync(client, url, ("precio", "135.50"), ("notas", "promoción de noviembre"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        await using (var ctx = await NuevoContextoAsync())
        {
            var guardado = await ctx.ProductosPrecios.AsNoTracking().SingleAsync(p => p.ProductoId == prod.Id && p.ListaId == lista.Id);
            Assert.Equal(135.50m, guardado.Precio);
            Assert.Equal(100m, guardado.Costo); // el costo sale del producto, no del formulario
            Assert.True(guardado.EsVigente && guardado.EsManual);
            Assert.Equal("promoción de noviembre", guardado.Notas);
        }
        Assert.Equal(1, await ContarEventosAsync("fijar-precio", $"producto={prod.Id} "));

        var listado = WebUtility.HtmlDecode(await (await client.GetAsync($"/ListaPrecio/Precios/{lista.Id}?busqueda={prod.Codigo}")).Content.ReadAsStringAsync());
        Assert.Contains("135,50", listado);
    }

    [Fact]
    public async Task Admin_FijarPrecio_ConIdsInexistentes_Devuelve404()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ListaPrecio/FijarPrecio?listaId=999999&productoId=999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ListaPrecio/Precios/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ListaPrecio/Editar/999999")).StatusCode);
    }

    // ── AC-01-7: exportación del historial ───────────────────────────────

    [Fact]
    public async Task ExportarHistorial_ConPermisoDeHistorial_DevuelveUnExcelConLosPreciosDelProducto()
    {
        var lista = await SembrarListaAsync();
        var prod = await SembrarProductoAsync();
        using var admin = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);
        await PostFormAsync(admin, $"/ListaPrecio/FijarPrecio?listaId={lista.Id}&productoId={prod.Id}", ("precio", "321.25"));

        var resp = await admin.GetAsync($"/CambiosPrecios/ExportarHistorial?productoId={prod.Id}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", resp.Content.Headers.ContentType?.MediaType);
        Assert.Contains(".xlsx", resp.Content.Headers.ContentDisposition?.FileName ?? string.Empty);
        using var libro = new XLWorkbook(await resp.Content.ReadAsStreamAsync());
        var hoja = libro.Worksheet("HistorialPrecios");
        Assert.Equal(prod.Codigo, hoja.Cell(2, 2).GetString());
        Assert.Equal(lista.Nombre, hoja.Cell(2, 4).GetString());
        Assert.Equal(321.25m, hoja.Cell(2, 7).GetValue<decimal>());
    }

    [Fact]
    public async Task ExportarHistorial_SinPermisoDeHistorial_OConProductoInexistente_NoEntregaNada()
    {
        var prod = await SembrarProductoAsync();
        using var soloVer = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioSoloVerUserId);
        using var admin = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);

        AssertDenegado(await soloVer.GetAsync($"/CambiosPrecios/ExportarHistorial?productoId={prod.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/CambiosPrecios/ExportarHistorial?productoId=999999")).StatusCode);

        var invertido = await admin.GetAsync($"/CambiosPrecios/ExportarHistorial?productoId={prod.Id}&desde=2026-05-01&hasta=2026-01-01");
        Assert.Equal(HttpStatusCode.Redirect, invertido.StatusCode);
    }

    [Fact]
    public async Task Historial_MuestraElBotonDeExportarSoloConPermisoDeHistorial()
    {
        var prod = await SembrarProductoAsync();
        using var admin = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioAdminUserId);
        using var soloVer = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioSoloVerUserId);

        Assert.Contains("ExportarHistorial", await (await admin.GetAsync($"/CambiosPrecios/Historial?productoId={prod.Id}")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("ExportarHistorial", await (await soloVer.GetAsync($"/CambiosPrecios/Historial?productoId={prod.Id}")).Content.ReadAsStringAsync());
    }

    // ── AC-01-9: alcanzabilidad ──────────────────────────────────────────

    [Fact]
    public async Task ElMenuOfreceListasDePreciosSoloAQuienTienePreciosView_YLasPantallasSeEnlazanEntreSi()
    {
        using var soloVer = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ListaPrecioSoloVerUserId);
        using var sinPrecios = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteSoloLecturaUserId);

        var conPermiso = await (await soloVer.GetAsync("/ListaPrecio")).Content.ReadAsStringAsync();
        var sinPermiso = await (await sinPrecios.GetAsync("/Cliente")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/ListaPrecio\"", conPermiso);
        Assert.Contains("href=\"/CambiosPrecios\"", conPermiso); // las listas llevan al circuito por lotes
        Assert.DoesNotContain("href=\"/ListaPrecio\"", sinPermiso);

        var lotes = await (await soloVer.GetAsync("/CambiosPrecios")).Content.ReadAsStringAsync();
        Assert.Contains("href=\"/ListaPrecio\"", lotes);
    }
}
