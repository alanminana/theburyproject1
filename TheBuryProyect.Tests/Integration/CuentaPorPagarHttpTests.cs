using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-03 AC-03-7 (permisos separados) y recorrido completo por el pipeline HTTP real (antiforgery + autorización + binding +
/// servicio + vistas Razor): orden de compra → factura → pago parcial → nota de crédito → saldo final.
/// </summary>
[Collection("HttpIntegration.4")]
public class CuentaPorPagarHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static long _cuit = 30_000_000_000;
    private readonly CustomWebApplicationFactory _factory;

    public CuentaPorPagarHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await _factory.SeedUserWithCxpSoloLecturaAsync();
        await _factory.SeedUserWithCxpOperadorAsync();
        await _factory.SeedUserWithCxpAnuladorAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── helpers ─────────────────────────────────────────────────────────

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private async Task<(int ProveedorId, int OrdenId)> SembrarProveedorConOrdenAsync(string razon = "Proveedor HTTP")
    {
        await using var ctx = await NuevoContextoAsync();
        var prov = new Proveedor { Cuit = Interlocked.Increment(ref _cuit).ToString(), RazonSocial = razon + " " + _cuit, Activo = true };
        ctx.Proveedores.Add(prov);
        await ctx.SaveChangesAsync();
        var oc = new OrdenCompra { Numero = "OC-HTTP-" + prov.Id, ProveedorId = prov.Id, Estado = EstadoOrdenCompra.Confirmada, FechaEmision = DateTime.UtcNow, Total = 1210m };
        ctx.OrdenesCompra.Add(oc);
        await ctx.SaveChangesAsync();
        return (prov.Id, oc.Id);
    }

    private async Task<int> SembrarComprobanteAsync(int proveedorId, string numero, decimal total, TipoComprobanteProveedor tipo = TipoComprobanteProveedor.Factura)
    {
        await using var ctx = await NuevoContextoAsync();
        var c = new ComprobanteProveedor
        {
            ProveedorId = proveedorId, Tipo = tipo, NumeroComprobante = numero, FechaEmision = DateTime.UtcNow.Date,
            ImporteNeto = total, ImporteIva = 0m, ImporteTotal = total, SaldoPendiente = total, Estado = EstadoComprobanteProveedor.Pendiente
        };
        ctx.ComprobantesProveedor.Add(c);
        await ctx.SaveChangesAsync();
        return c.Id;
    }

    private async Task<ComprobanteProveedor> LeerComprobanteAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.ComprobantesProveedor.AsNoTracking().SingleAsync(c => c.Id == id);
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

    private static void AssertDenegado(HttpResponseMessage resp) =>
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Redirect,
            $"Se esperaba 403 o redirección a acceso denegado, llegó {(int)resp.StatusCode}.");

    private static string DestinoRedireccion(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        return resp.Headers.Location!.OriginalString;
    }

    // ── AC-03-7: permisos separados ─────────────────────────────────────

    [Fact]
    public async Task SoloLectura_VeLasPantallasPeroNoRecibeNiEjecutaNingunaEscritura()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var factura = await SembrarComprobanteAsync(prov, "SL-1", 500m);
        var nc = await SembrarComprobanteAsync(prov, "SL-NC", 100m, TipoComprobanteProveedor.NotaCredito);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpSoloLecturaUserId);

        foreach (var url in new[] { "/CuentaPorPagar", $"/CuentaPorPagar/Detalle/{factura}", $"/CuentaPorPagar/EstadoCuenta?proveedorId={prov}", "/CuentaPorPagar/Antiguedad" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);

        var index = await (await client.GetAsync("/CuentaPorPagar")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Nuevo comprobante", index);
        Assert.DoesNotContain("Registrar pago", index);
        var detalle = await (await client.GetAsync($"/CuentaPorPagar/Detalle/{factura}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Anular comprobante", detalle);
        Assert.DoesNotContain("Registrar pago", detalle);

        AssertDenegado(await client.GetAsync($"/CuentaPorPagar/Crear?proveedorId={prov}"));
        AssertDenegado(await client.GetAsync($"/CuentaPorPagar/Pagar?proveedorId={prov}"));
        AssertDenegado(await client.GetAsync($"/CuentaPorPagar/ImputarNotaCredito/{nc}"));
        AssertDenegado(await PostFormAsync(client, "/CuentaPorPagar/Crear", ("ProveedorId", prov.ToString()), ("NumeroComprobante", "SL-X"),
            ("FechaEmision", "2026-11-01"), ("ImporteNeto", "100"), ("ImporteIva", "0"), ("ImporteTotal", "100")));
        AssertDenegado(await PostFormAsync(client, "/CuentaPorPagar/Pagar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-11-01"),
            ("Medio", "Efectivo"), ("Monto", "100"), ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "100")));
        AssertDenegado(await PostFormAsync(client, $"/CuentaPorPagar/Anular/{factura}", ("motivo", "x")));
        AssertDenegado(await PostFormAsync(client, $"/CuentaPorPagar/ImputarNotaCredito/{nc}",
            ("imputaciones[0].ComprobanteId", factura.ToString()), ("imputaciones[0].Monto", "50")));

        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(2, await ctx.ComprobantesProveedor.CountAsync(c => c.ProveedorId == prov));
        Assert.Equal(0, await ctx.PagosProveedor.CountAsync(p => p.ProveedorId == prov));
        Assert.Equal(0, await ctx.ImputacionesNotaCredito.CountAsync(i => i.NotaCreditoId == nc));
        var f = await LeerComprobanteAsync(factura);
        Assert.Equal((500m, EstadoComprobanteProveedor.Pendiente), (f.SaldoPendiente, f.Estado));
    }

    [Fact]
    public async Task SinPermisoDeCuentasPorPagar_NoAccedeANingunaPantalla()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var factura = await SembrarComprobanteAsync(prov, "SP-1", 500m);
        using var client = _factory.CreateClientWithUserId("usuario-sin-permisos-cxp");

        foreach (var url in new[] { "/CuentaPorPagar", $"/CuentaPorPagar/Detalle/{factura}", $"/CuentaPorPagar/EstadoCuenta?proveedorId={prov}", "/CuentaPorPagar/Antiguedad", "/CuentaPorPagar/Crear" })
            AssertDenegado(await client.GetAsync(url));
    }

    [Fact]
    public async Task Operador_RegistraComprobantesYPagosPeroNoAnula()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);

        var crear = await PostFormAsync(client, "/CuentaPorPagar/Crear", ("ProveedorId", prov.ToString()), ("Tipo", "Factura"),
            ("NumeroComprobante", "op-1"), ("FechaEmision", "2026-11-01"), ("ImporteNeto", "1000"), ("ImporteIva", "210"), ("ImporteTotal", "1210"));
        var destino = DestinoRedireccion(crear);
        var id = int.Parse(destino[(destino.LastIndexOf('/') + 1)..].Split('?')[0]);
        var factura = await LeerComprobanteAsync(id);
        Assert.Equal("OP-1", factura.NumeroComprobante);
        Assert.False(string.IsNullOrWhiteSpace(factura.CreatedBy)); // el alta queda sellada con el usuario autenticado

        var detalle = await (await client.GetAsync($"/CuentaPorPagar/Detalle/{id}")).Content.ReadAsStringAsync();
        Assert.Contains("Registrar pago", detalle);
        Assert.DoesNotContain("Anular comprobante", detalle);

        var pago = await PostFormAsync(client, "/CuentaPorPagar/Pagar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-11-05"),
            ("Medio", "Transferencia"), ("Monto", "400"), ("Imputaciones[0].ComprobanteId", id.ToString()), ("Imputaciones[0].Monto", "400"));
        Assert.Equal(HttpStatusCode.Redirect, pago.StatusCode);
        Assert.Equal(810m, (await LeerComprobanteAsync(id)).SaldoPendiente);

        await using var ctx = await NuevoContextoAsync();
        var pagoId = (await ctx.PagosProveedor.AsNoTracking().SingleAsync(p => p.ProveedorId == prov)).Id;
        AssertDenegado(await PostFormAsync(client, $"/CuentaPorPagar/AnularPago/{pagoId}", ("motivo", "x")));
        AssertDenegado(await PostFormAsync(client, $"/CuentaPorPagar/Anular/{id}", ("motivo", "x")));
        Assert.False((await ctx.PagosProveedor.AsNoTracking().SingleAsync(p => p.Id == pagoId)).Anulado);
    }

    [Fact]
    public async Task Anulador_AnulaPeroNoRegistraComprobantesNiPagos()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var factura = await SembrarComprobanteAsync(prov, "AN-1", 500m);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpAnuladorUserId);

        AssertDenegado(await client.GetAsync($"/CuentaPorPagar/Crear?proveedorId={prov}"));
        AssertDenegado(await PostFormAsync(client, "/CuentaPorPagar/Pagar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-11-01"),
            ("Medio", "Efectivo"), ("Monto", "100"), ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "100")));

        var detalle = await (await client.GetAsync($"/CuentaPorPagar/Detalle/{factura}")).Content.ReadAsStringAsync();
        Assert.Contains("Anular comprobante", detalle);

        var anular = await PostFormAsync(client, $"/CuentaPorPagar/Anular/{factura}", ("motivo", "carga errónea"));
        Assert.Equal(HttpStatusCode.Redirect, anular.StatusCode);
        var c = await LeerComprobanteAsync(factura);
        Assert.Equal(EstadoComprobanteProveedor.Anulado, c.Estado);
        Assert.Equal("carga errónea", c.MotivoAnulacion);
        Assert.False(string.IsNullOrWhiteSpace(c.AnuladoPor));
    }

    [Fact]
    public async Task SinTokenAntiforgery_LasEscriturasSeRechazan()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var factura = await SembrarComprobanteAsync(prov, "AF-1", 500m);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);

        var resp = await client.PostAsync("/CuentaPorPagar/Pagar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ProveedorId"] = prov.ToString(), ["Fecha"] = "2026-11-01", ["Medio"] = "Efectivo", ["Monto"] = "100",
            ["Imputaciones[0].ComprobanteId"] = factura.ToString(), ["Imputaciones[0].Monto"] = "100"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(500m, (await LeerComprobanteAsync(factura)).SaldoPendiente);
    }

    [Fact]
    public async Task Operador_PagoConImporteInvalidoMuestraElErrorYNoGuardaNada()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var factura = await SembrarComprobanteAsync(prov, "IN-1", 500m);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);

        var resp = await PostFormAsync(client, "/CuentaPorPagar/Pagar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-11-01"),
            ("Medio", "Efectivo"), ("Monto", "600"), ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "600"));
        var html = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("id=\"cxp-error\"", html);
        Assert.Contains("supera su saldo pendiente", html);
        Assert.Equal(500m, (await LeerComprobanteAsync(factura)).SaldoPendiente);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(0, await ctx.PagosProveedor.CountAsync(p => p.ProveedorId == prov));
    }

    [Fact]
    public async Task Operador_ElMismoFormularioEnviadoDosVecesRegistraUnSoloPago()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var factura = await SembrarComprobanteAsync(prov, "DB-1", 500m);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);
        var campos = new[]
        {
            ("ProveedorId", prov.ToString()), ("ClaveIdempotencia", "clave-doble-clic-" + prov), ("Fecha", "2026-11-01"), ("Medio", "Efectivo"),
            ("Monto", "200"), ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "200")
        };

        var primero = await PostFormAsync(client, "/CuentaPorPagar/Pagar", campos);
        var segundo = await PostFormAsync(client, "/CuentaPorPagar/Pagar", campos);

        Assert.Equal(HttpStatusCode.Redirect, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, segundo.StatusCode);
        Assert.Equal(DestinoRedireccion(primero), DestinoRedireccion(segundo));
        Assert.Equal(300m, (await LeerComprobanteAsync(factura)).SaldoPendiente);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(1, await ctx.PagosProveedor.CountAsync(p => p.ProveedorId == prov));
    }

    // ── AC-03-9 (versión HTTP): OC → factura → pago parcial → NC → saldo final ──

    [Fact]
    public async Task Recorrido_OrdenDeCompraFacturaPagoParcialNotaDeCreditoYSaldoFinal()
    {
        var (prov, oc) = await SembrarProveedorConOrdenAsync("Recorrido");
        using var client = _factory.CreateAuthenticatedClient();

        // El detalle de la orden ofrece registrar la factura y el alta llega prellenada con el proveedor y la orden.
        var ordenHtml = await (await client.GetAsync($"/OrdenCompra/Details/{oc}")).Content.ReadAsStringAsync();
        Assert.Contains($"/CuentaPorPagar/Crear?ordenCompraId={oc}", ordenHtml);
        var form = await (await client.GetAsync($"/CuentaPorPagar/Crear?ordenCompraId={oc}")).Content.ReadAsStringAsync();
        Assert.Contains($"name=\"ProveedorId\" value=\"{prov}\"", form);
        Assert.Contains($"<option value=\"{oc}\" selected=\"selected\">", form);

        // Factura de 1.210 con vencimiento ya vencido.
        var factura = await PostFormAsync(client, "/CuentaPorPagar/Crear", ("ProveedorId", prov.ToString()), ("OrdenCompraId", oc.ToString()),
            ("Tipo", "Factura"), ("NumeroComprobante", "A-0001-0001"), ("FechaEmision", "2026-09-01"), ("FechaVencimiento", "2026-09-30"),
            ("ImporteNeto", "1000.00"), ("ImporteIva", "210.00"), ("ImporteTotal", "1210.00"));
        var facturaId = int.Parse(DestinoRedireccion(factura).Split('/').Last().Split('?')[0]);
        Assert.Equal(oc, (await LeerComprobanteAsync(facturaId)).OrdenCompraId);

        // Pago parcial de 400.
        var pagoResp = await PostFormAsync(client, "/CuentaPorPagar/Pagar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-10-05"), ("Medio", "Transferencia"),
            ("Monto", "400.00"), ("Referencia", "TRF-77"), ("Imputaciones[0].ComprobanteId", facturaId.ToString()), ("Imputaciones[0].Monto", "400.00"));
        Assert.Equal(HttpStatusCode.Redirect, pagoResp.StatusCode);
        var parcial = await LeerComprobanteAsync(facturaId);
        Assert.Equal((810m, EstadoComprobanteProveedor.ParcialmentePagado), (parcial.SaldoPendiente, parcial.Estado));

        // Nota de crédito de 100 aplicada a la factura.
        var ncResp = await PostFormAsync(client, "/CuentaPorPagar/Crear", ("ProveedorId", prov.ToString()), ("Tipo", "NotaCredito"),
            ("NumeroComprobante", "NC-0001-0001"), ("FechaEmision", "2026-10-10"), ("ImporteNeto", "100.00"), ("ImporteIva", "0"), ("ImporteTotal", "100.00"));
        var ncId = int.Parse(DestinoRedireccion(ncResp).Split('/').Last().Split('?')[0]);
        var pantallaNc = await (await client.GetAsync($"/CuentaPorPagar/ImputarNotaCredito/{ncId}")).Content.ReadAsStringAsync();
        Assert.Contains("A-0001-0001", pantallaNc);
        var aplicar = await PostFormAsync(client, $"/CuentaPorPagar/ImputarNotaCredito/{ncId}", ("claveIdempotencia", "k-nc-" + prov),
            ("imputaciones[0].ComprobanteId", facturaId.ToString()), ("imputaciones[0].Monto", "100.00"));
        Assert.Equal(HttpStatusCode.Redirect, aplicar.StatusCode);

        // Saldo final: 1210 - 400 - 100 = 710; la nota quedó aplicada por completo.
        var final = await LeerComprobanteAsync(facturaId);
        Assert.Equal((710m, EstadoComprobanteProveedor.ParcialmentePagado), (final.SaldoPendiente, final.Estado));
        Assert.Equal(EstadoComprobanteProveedor.Pagado, (await LeerComprobanteAsync(ncId)).Estado);

        // Las pantallas muestran lo mismo, en formato argentino y con el comprobante vencido señalado.
        var estadoCuenta = await (await client.GetAsync($"/CuentaPorPagar/EstadoCuenta?proveedorId={prov}")).Content.ReadAsStringAsync();
        Assert.Contains("710,00", estadoCuenta);
        Assert.Contains("1.210,00", estadoCuenta);
        Assert.DoesNotContain("1,210.00", estadoCuenta);
        var antiguedad = await (await client.GetAsync($"/CuentaPorPagar/Antiguedad?proveedorId={prov}")).Content.ReadAsStringAsync();
        Assert.Contains("710,00", antiguedad);
        var index = await (await client.GetAsync($"/CuentaPorPagar?ProveedorId={prov}")).Content.ReadAsStringAsync();
        Assert.Contains("A-0001-0001", index);
        Assert.Contains("NC-0001-0001", index);
        var detalleNc = await (await client.GetAsync($"/CuentaPorPagar/Detalle/{ncId}")).Content.ReadAsStringAsync();
        Assert.Contains("A-0001-0001", detalleNc);
    }

    [Fact]
    public async Task ElMenuYLosEnlacesRespetanElPermiso()
    {
        var (prov, oc) = await SembrarProveedorConOrdenAsync();
        using var conPermiso = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpSoloLecturaUserId);
        using var admin = _factory.CreateAuthenticatedClient();

        var menuConPermiso = await (await conPermiso.GetAsync("/CuentaPorPagar")).Content.ReadAsStringAsync();
        Assert.Contains("href=\"/CuentaPorPagar\"", menuConPermiso);
        var proveedorAdmin = await (await admin.GetAsync($"/Proveedor/Details/{prov}")).Content.ReadAsStringAsync();
        Assert.Contains($"/CuentaPorPagar/EstadoCuenta?proveedorId={prov}", proveedorAdmin);
        var ordenAdmin = await (await admin.GetAsync($"/OrdenCompra/Details/{oc}")).Content.ReadAsStringAsync();
        Assert.Contains($"/CuentaPorPagar/Crear?ordenCompraId={oc}", ordenAdmin);
    }
}
