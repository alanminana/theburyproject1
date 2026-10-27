using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// RESCATE-FACTURA-SELECTOR-P0. Razor real + SQLite en memoria.
/// Bug: el modal "Emitir factura" de Venta/Details dejaba al navegador marcar la primera opción (Factura A)
/// aunque el modelo (FacturaViewModel.Tipo) vale B, y los selectores de emisión ofrecían Nota de Crédito / Débito.
/// Contrato: la emisión de una factura nueva solo acepta A, B o C; un tipo inválido se rechaza ANTES de
/// confirmar la venta o guardar cambios, sin dejar efectos (estado, stock, facturas, caja).
/// </summary>
[Collection("HttpIntegration")]
public class VentaFacturaTipoEmisionHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string TestUser = "testuser";
    private static int _counter = 77000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaFacturaTipoEmisionHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ------------------------------------------------------------------ selectores (GET)

    [Fact]
    public async Task Details_ModalFactura_MarcaBYSoloOfreceABC()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var client = _factory.CreateAuthenticatedClient();

        var html = await GetHtmlAsync(client, $"/Venta/Details/{seed.VentaId}");
        var select = ExtraerSelect(html, "modal-tipo-factura");

        Assert.Equal(new[] { "A", "B", "C" }, OpcionesDe(select).Select(o => o.Value));
        Assert.Equal("B", Assert.Single(OpcionesDe(select), o => o.Selected).Value);
        Assert.DoesNotContain("Nota", select);
        Assert.Contains("name=\"Tipo\"", select);
    }

    [Fact]
    public async Task FacturarTw_Get_MarcaBYSoloOfreceABC()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var client = _factory.CreateAuthenticatedClient();

        var html = await GetHtmlAsync(client, $"/Venta/Facturar/{seed.VentaId}");
        var select = ExtraerSelect(html, "Tipo");

        Assert.Equal(new[] { "A", "B", "C" }, OpcionesDe(select).Select(o => o.Value));
        Assert.Equal("B", Assert.Single(OpcionesDe(select), o => o.Selected).Value);
        Assert.DoesNotContain("Nota", select);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("C")]
    public async Task FacturarTw_ReRenderConModeloExplicito_RespetaElTipoElegido(string tipo)
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        // PuntoVenta > 50 caracteres => ModelState inválido => Facturar_tw se re-renderiza con Tipo del modelo.
        var response = await PostFacturarAsync(client, token, seed.VentaId, tipo, puntoVenta: new string('9', 60));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var opciones = OpcionesDe(ExtraerSelect(html, "Tipo"));
        Assert.Equal(new[] { "A", "B", "C" }, opciones.Select(o => o.Value));
        Assert.Equal(tipo, Assert.Single(opciones, o => o.Selected).Value);
        Assert.Equal(EstadoVenta.Confirmada, (await LeerAsync(seed)).Estado);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public async Task PartialEmision_MarcaElTipoDelModeloYNoOfreceNotas(string tipo)
    {
        await _factory.SeedTestUserAsync();
        var model = new FacturaViewModel { Tipo = Enum.Parse<TipoFactura>(tipo) };
        // Incluso si el host le pasa NC/ND, el partial no las ofrece.
        var todos = new SelectList(Enum.GetValues(typeof(TipoFactura)));

        var html = await RenderPartialAsync("~/Views/Shared/_FacturaCamposEmision.cshtml", model, todos);
        var select = ExtraerSelect(html, "modal-tipo-factura");

        Assert.Equal(new[] { "A", "B", "C" }, OpcionesDe(select).Select(o => o.Value));
        Assert.Equal(tipo, Assert.Single(OpcionesDe(select), o => o.Selected).Value);
        Assert.Contains("name=\"Tipo\"", select);
    }

    [Fact]
    public async Task PartialEmision_SinNames_MantieneElInvarianteDelCotizador()
    {
        await _factory.SeedTestUserAsync();
        var vd = new Dictionary<string, object?> { ["IncludeNames"] = false, ["IdPrefix"] = "cotizacion-factura-" };

        var html = await RenderPartialAsync(
            "~/Views/Shared/_FacturaCamposEmision.cshtml",
            new FacturaViewModel(),
            TipoFacturaEmision.CrearSelectList(),
            vd);
        var select = ExtraerSelect(html, "cotizacion-factura-tipo-factura");

        Assert.DoesNotContain("name=", select);
        Assert.Equal("B", Assert.Single(OpcionesDe(select), o => o.Selected).Value);
    }

    [Fact]
    public async Task Cotizador_MantieneBInicialYNoOfreceNotas()
    {
        await _factory.SeedTestUserAsync();
        var client = _factory.CreateAuthenticatedClient();

        var html = await GetHtmlAsync(client, "/Cotizacion");
        var select = ExtraerSelect(html, "cotizacion-factura-tipo-factura");

        Assert.Equal(new[] { "A", "B", "C" }, OpcionesDe(select).Select(o => o.Value));
        Assert.Equal("B", Assert.Single(OpcionesDe(select), o => o.Selected).Value);
        Assert.DoesNotContain("Nota", select);
        Assert.DoesNotContain("name=", select);
    }

    [Fact]
    public async Task Wizard_SigueOfreciendoABC()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Presupuesto);
        var client = _factory.CreateAuthenticatedClient();

        var html = await GetHtmlAsync(client, $"/Venta/Edit/{seed.VentaId}");
        var select = ExtraerSelect(html, "modal-tipo-factura");

        Assert.Equal(new[] { "A", "B", "C" }, OpcionesDe(select).Select(o => o.Value));
        Assert.Equal("B", Assert.Single(OpcionesDe(select), o => o.Selected).Value);
        Assert.Contains("name=\"tipoFactura\"", select);
    }

    // ------------------------------------------------------------------ Facturar POST

    [Theory]
    [InlineData("NotaCredito")]
    [InlineData("NotaDebito")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("99")]
    public async Task FacturarPost_TipoInvalido_RechazaSinEfectos(string tipo)
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var antes = await LeerAsync(seed);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        var response = await PostFacturarAsync(client, token, seed.VentaId, tipo);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // vuelve al formulario con el error
        // "99" ni siquiera es un valor del enum: el model binder ya lo rechaza (error de binding propio de MVC).
        if (tipo != "99") Assert.Contains(TipoFacturaEmision.MensajeInvalido, html);
        var despues = await LeerAsync(seed);
        Assert.Equal(EstadoVenta.Confirmada, despues.Estado);
        Assert.Equal(0, despues.Facturas);
        Assert.Equal(antes.Movimientos, despues.Movimientos);
        Assert.Equal(antes.Stock, despues.Stock);
    }

    [Theory]
    [InlineData("A", TipoFactura.A)]
    [InlineData("B", TipoFactura.B)]
    [InlineData("C", TipoFactura.C)]
    public async Task FacturarPost_TipoValido_Factura(string tipo, TipoFactura esperado)
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        var response = await PostFacturarAsync(client, token, seed.VentaId, tipo);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Venta/Details/{seed.VentaId}", response.Headers.Location?.OriginalString ?? "");
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        Assert.Equal(EstadoVenta.Facturada, await ctx.Ventas.Where(v => v.Id == seed.VentaId).Select(v => v.Estado).SingleAsync());
        var factura = await ctx.Facturas.SingleAsync(f => f.VentaId == seed.VentaId);
        Assert.Equal(esperado, factura.Tipo);
    }

    // VALID-L1: PuntoVenta (1 a 5 dígitos) y FechaEmision (ni futura ni anterior a la venta) se validan en el servidor.
    [Theory]
    [InlineData("abc")]
    [InlineData("<script>")]
    [InlineData("123456")]
    [InlineData("00 1")]
    public async Task FacturarPost_PuntoVentaInvalido_RechazaSinEfectos(string puntoVenta)
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        var response = await PostFacturarAsync(client, token, seed.VentaId, "B", puntoVenta);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await LeerAsync(seed)).Facturas);
    }

    [Theory]
    [InlineData("0001")]
    [InlineData("1")]
    [InlineData("99999")]
    public async Task FacturarPost_PuntoVentaValido_Factura(string puntoVenta)
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        var response = await PostFacturarAsync(client, token, seed.VentaId, "B", puntoVenta);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, (await LeerAsync(seed)).Facturas);
    }

    [Theory]
    [InlineData("1900-01-01")]
    [InlineData("2999-12-31")]
    [InlineData("2026-10-01")]   // anterior a la venta (19/10/2026)
    public async Task FacturarPost_FechaEmisionFueraDeRango_RechazaSinEfectos(string fecha)
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Confirmada);
        var antes = await LeerAsync(seed);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/Venta/Facturar")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["VentaId"] = seed.VentaId.ToString(),
                ["Tipo"] = "B",
                ["FechaEmision"] = fecha,
            })
        });

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect);
        var despues = await LeerAsync(seed);
        Assert.Equal(EstadoVenta.Confirmada, despues.Estado);
        Assert.Equal(0, despues.Facturas);
        Assert.Equal(antes.Movimientos, despues.Movimientos);
    }

    // FIN-S-002 (O2): un rechazo de negocio al facturar debe llegar visible al usuario (antes se perdía en el redirect).
    [Fact]
    public async Task FacturarPost_RechazoDeNegocio_MuestraMensajeEnDetailsSinFiltrarInterno()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Presupuesto);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        var response = await PostFacturarAsync(client, token, seed.VentaId, "B");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? "";
        Assert.Contains($"/Venta/Details/{seed.VentaId}", location);
        var html = WebUtility.HtmlDecode(await GetHtmlAsync(client, location));
        Assert.Contains("No se pudo facturar la venta", html);
        Assert.DoesNotContain("StackTrace", html);
        Assert.Equal(0, (await LeerAsync(seed)).Facturas);
    }

    // ------------------------------------------------------------------ Edit POST (guardar + confirmar + facturar)

    [Theory]
    [InlineData("NotaCredito")]
    [InlineData("NotaDebito")]
    [InlineData("99")]
    public async Task EditPost_ConfirmarFacturarConTipoInvalido_NoConfirmaNiGuardaNiFactura(string tipo)
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var seed = await SeedVentaAsync(EstadoVenta.Presupuesto);
        var antes = await LeerAsync(seed);
        var client = _factory.CreateAuthenticatedClient();
        var token = await GetTokenAsync(client, seed.VentaId);

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = seed.VentaId.ToString(),
            ["accionConfirmacion"] = "confirmar-facturar",
            ["tipoFactura"] = tipo
        };
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/Venta/Edit/{seed.VentaId}")
        {
            Content = new FormUrlEncodedContent(form)
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Vuelve a la edición (no redirige a Details como lo haría un guardado/confirmación exitosa)...
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (tipo != "99") Assert.Contains(TipoFacturaEmision.MensajeInvalido, html);
        // ...y no quedó ningún efecto.
        var despues = await LeerAsync(seed);
        Assert.Equal(EstadoVenta.Presupuesto, despues.Estado);
        Assert.Equal(antes.Stock, despues.Stock);
        Assert.Equal(0, despues.Facturas);
        Assert.Equal(antes.Movimientos, despues.Movimientos);
    }

    // ------------------------------------------------------------------ helpers

    private static string ExtraerSelect(string html, string id)
    {
        var m = Regex.Match(html, "<select\\b[^>]*\\bid=\"" + Regex.Escape(id) + "\"[^>]*>.*?</select>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(m.Success, $"No se encontró <select id=\"{id}\"> en el HTML.");
        return m.Value;
    }

    private static List<(string Value, bool Selected)> OpcionesDe(string select) =>
        Regex.Matches(select, "<option\\b([^>]*)>", RegexOptions.CultureInvariant)
            .Select(m => (
                Value: Regex.Match(m.Groups[1].Value, "value=\"([^\"]*)\"").Groups[1].Value,
                Selected: Regex.IsMatch(m.Groups[1].Value, "\\bselected\\b")))
            .ToList();

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> GetTokenAsync(HttpClient client, int ventaId)
    {
        var html = await GetHtmlAsync(client, $"/Venta/Details/{ventaId}");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success, "No se encontró el token antiforgery en Venta/Details.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostFacturarAsync(
        HttpClient client, string token, int ventaId, string tipo, string? puntoVenta = null)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["VentaId"] = ventaId.ToString(),
            ["Tipo"] = tipo,
            ["FechaEmision"] = DateTime.Today.ToString("yyyy-MM-dd"),
        };
        if (puntoVenta != null) form["PuntoVenta"] = puntoVenta;
        return client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/Venta/Facturar")
        {
            Content = new FormUrlEncodedContent(form)
        });
    }

    private async Task<string> RenderPartialAsync(
        string path, FacturaViewModel model, SelectList tipos, IDictionary<string, object?>? extraViewData = null)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var engine = sp.GetRequiredService<IRazorViewEngine>();
        var result = engine.GetView(executingFilePath: null, viewPath: path, isMainPage: false);
        Assert.True(result.Success, $"No se encontró la vista {path}.");

        var viewData = new ViewDataDictionary<FacturaViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        viewData["TiposFactura"] = tipos;
        if (extraViewData != null)
            foreach (var kv in extraViewData) viewData[kv.Key] = kv.Value;

        await using var writer = new StringWriter();
        var tempData = new TempDataDictionary(httpContext, sp.GetRequiredService<ITempDataProvider>());
        var viewContext = new ViewContext(actionContext, result.View, viewData, tempData, writer, new HtmlHelperOptions());
        await result.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private sealed record Seed(int VentaId, int ProductoId);

    private sealed record Snapshot(EstadoVenta Estado, int Facturas, int Movimientos, decimal Stock);

    private async Task<Snapshot> LeerAsync(Seed seed)
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        var estado = await ctx.Ventas.Where(v => v.Id == seed.VentaId).Select(v => v.Estado).SingleAsync();
        var facturas = await ctx.Facturas.CountAsync(f => f.VentaId == seed.VentaId);
        var movimientos = await ctx.MovimientosCaja.CountAsync();
        var stock = await ctx.Productos.Where(p => p.Id == seed.ProductoId).Select(p => p.StockActual).SingleAsync();
        return new Snapshot(estado, facturas, movimientos, stock);
    }

    private async Task SeedCajaAbiertaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        if (await ctx.AperturasCaja.AnyAsync(a => !a.Cerrada && a.UsuarioApertura == TestUser))
            return;

        var caja = new Caja { Codigo = "CAJA-TIPO-FACT", Nombre = "Caja Tipo Factura", IsDeleted = false, RowVersion = new byte[8] };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();
        ctx.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id, UsuarioApertura = TestUser, MontoInicial = 0m,
            Cerrada = false, IsDeleted = false, RowVersion = new byte[8]
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Seed> SeedVentaAsync(EstadoVenta estado)
    {
        var suffix = Interlocked.Increment(ref _counter);
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();

        var marca = new Marca { Codigo = $"MTF{suffix}", Nombre = $"Marca {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CTF{suffix}", Nombre = $"Categoria {suffix}", Activo = true };
        var cliente = new Cliente
        {
            Nombre = $"ClienteTipoFact{suffix}", Apellido = "RESCATE-FACTURA",
            NumeroDocumento = $"43{suffix}", Telefono = "1111-4444", Domicilio = "Calle Tipo Factura 123"
        };
        ctx.Marcas.Add(marca);
        ctx.Categorias.Add(categoria);
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PTF{suffix}", Nombre = $"ProductoTipoFact{suffix}",
            CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 100m, PrecioVenta = 121m, PorcentajeIVA = 21m, StockActual = 10m, Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VTF-{suffix}",
            FechaVenta = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc),
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            Subtotal = 300m, IVA = 63m, Total = 363m,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };
        venta.Detalles.Add(new VentaDetalle
        {
            ProductoId = producto.Id, Cantidad = 3, PrecioUnitario = 121m, Subtotal = 363m,
            PorcentajeIVA = 21m, AlicuotaIVANombre = "IVA 21%",
            SubtotalFinalNeto = 300m, SubtotalFinalIVA = 63m, SubtotalFinal = 363m
        });
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return new Seed(venta.Id, producto.Id);
    }
}
