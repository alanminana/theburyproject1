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
        await _factory.SeedUserWithTesoreriaSoloVerAsync();
        await _factory.SeedUserWithTesoreriaConfiguradorAsync();
        await using var ctx = await NuevoContextoAsync();
        var origen = await ctx.OrigenesFondos.FirstOrDefaultAsync(o => o.Codigo == "HTTP");
        if (origen is null)
        {
            origen = new OrigenFondos { Codigo = "HTTP", Nombre = "Origen HTTP", Tipo = TipoOrigenFondos.Efectivo };
            ctx.OrigenesFondos.Add(origen);
            await ctx.SaveChangesAsync();
        }
        _origenId = origen.Id;
    }

    private int _origenId;

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


    // ── D-03 Tesorería administrativa: permisos, catálogo de orígenes y anticipos ──

    [Fact]
    public async Task Tesoreria_SoloVer_VeElResumenPeroNoConfiguraNada_YSinPermisoNoEntra()
    {
        using var soloVer = _factory.CreateClientWithUserId(CustomWebApplicationFactory.TesoreriaSoloVerUserId);
        Assert.Equal(HttpStatusCode.OK, (await soloVer.GetAsync("/Tesoreria")).StatusCode);
        var html = await (await soloVer.GetAsync("/Tesoreria")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Nuevo origen de fondos", html);
        Assert.Contains("independiente de la Caja de ventas", html);

        AssertDenegado(await soloVer.GetAsync("/Tesoreria/Crear"));
        AssertDenegado(await soloVer.GetAsync("/Tesoreria/Editar/1"));
        AssertDenegado(await PostFormAsync(soloVer, "/Tesoreria/Guardar", ("Codigo", "SV"), ("Nombre", "No debe crearse"), ("Tipo", "1"), ("Activo", "true")));
        AssertDenegado(await PostFormAsync(soloVer, "/Tesoreria/Eliminar/1"));

        using var sinPermiso = _factory.CreateClientWithUserId("usuario-sin-permisos-cxp");
        AssertDenegado(await sinPermiso.GetAsync("/Tesoreria"));
        // Registrar pagos NO da acceso a la configuración de Tesorería: son permisos distintos.
        using var operador = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);
        AssertDenegado(await operador.GetAsync("/Tesoreria"));

        await using var ctx = await NuevoContextoAsync();
        Assert.False(await ctx.OrigenesFondos.AnyAsync(o => o.Codigo == "SV"));
    }

    [Fact]
    public async Task Tesoreria_Configurador_CreaEditaYEliminaUnOrigen_ConAntiforgery()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.TesoreriaConfiguradorUserId);
        Assert.Contains("Nuevo origen de fondos", await (await client.GetAsync("/Tesoreria")).Content.ReadAsStringAsync());

        var sinToken = await client.PostAsync("/Tesoreria/Guardar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Codigo"] = "SINTOKEN", ["Nombre"] = "x", ["Tipo"] = "1", ["Activo"] = "true" }));
        Assert.True(sinToken.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);

        var crear = await PostFormAsync(client, "/Tesoreria/Guardar", ("Codigo", "tes-http"), ("Nombre", "Banco HTTP"), ("Tipo", "2"), ("Orden", "1"), ("Activo", "true"));
        Assert.Equal(HttpStatusCode.Redirect, crear.StatusCode);
        var duplicado = await PostFormAsync(client, "/Tesoreria/Guardar", ("Codigo", "TES-HTTP"), ("Nombre", "Repetido"), ("Tipo", "1"), ("Activo", "true"));
        Assert.Equal(HttpStatusCode.OK, duplicado.StatusCode);
        Assert.Contains("Ya existe un origen de fondos con el código TES-HTTP", WebUtility.HtmlDecode(await duplicado.Content.ReadAsStringAsync()));

        int id;
        byte[] rowVersion;
        await using (var ctx = await NuevoContextoAsync())
        {
            var origen = await ctx.OrigenesFondos.AsNoTracking().SingleAsync(o => o.Codigo == "TES-HTTP");
            (id, rowVersion) = (origen.Id, origen.RowVersion);
            Assert.Equal((TipoOrigenFondos.CuentaBancaria, true), (origen.Tipo, origen.Activo));
            Assert.Equal(1, await ctx.OrigenesFondos.CountAsync(o => o.Codigo == "TES-HTTP"));
        }

        var editar = await PostFormAsync(client, "/Tesoreria/Guardar", ("Id", id.ToString()), ("RowVersion", Convert.ToBase64String(rowVersion)),
            ("Codigo", "TES-HTTP"), ("Nombre", "Banco HTTP editado"), ("Tipo", "2"), ("Orden", "1"), ("Activo", "false"));
        Assert.Equal(HttpStatusCode.Redirect, editar.StatusCode);
        await using (var ctx = await NuevoContextoAsync())
            Assert.Equal(("Banco HTTP editado", false), ((await ctx.OrigenesFondos.AsNoTracking().SingleAsync(o => o.Id == id)) is var o ? (o.Nombre, o.Activo) : default));

        var eliminar = await PostFormAsync(client, $"/Tesoreria/Eliminar/{id}");
        Assert.Equal(HttpStatusCode.Redirect, eliminar.StatusCode);
        await using (var ctx = await NuevoContextoAsync())
            Assert.True((await ctx.OrigenesFondos.AsNoTracking().SingleAsync(o => o.Id == id)).IsDeleted); // baja lógica: no se borra la fila
    }

    [Fact]
    public async Task Anticipo_OperadorRegistraYAplicaAVariasFacturas_YSoloElAnuladorAnula()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var f1 = await SembrarComprobanteAsync(prov, "ANT-1", 300m);
        var f2 = await SembrarComprobanteAsync(prov, "ANT-2", 400m);
        using var operador = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);

        var form = await (await operador.GetAsync($"/CuentaPorPagar/Anticipar?proveedorId={prov}")).Content.ReadAsStringAsync();
        Assert.Contains("Origen de los fondos", form);
        Assert.Contains("Origen HTTP", form);
        Assert.Contains("no de la Caja de ventas", form);

        var clave = "ant-http-" + prov;
        var campos = new[]
        {
            ("ProveedorId", prov.ToString()), ("ClaveIdempotencia", clave), ("Fecha", "2026-11-01"), ("Monto", "500"),
            ("OrigenFondosId", _origenId.ToString()), ("Medio", "Transferencia"), ("Referencia", "TRF-ANT")
        };
        var primero = await PostFormAsync(operador, "/CuentaPorPagar/Anticipar", campos);
        var repetido = await PostFormAsync(operador, "/CuentaPorPagar/Anticipar", campos);
        Assert.Equal(HttpStatusCode.Redirect, primero.StatusCode);
        Assert.Equal(DestinoRedireccion(primero), DestinoRedireccion(repetido)); // el reenvío no duplica
        var anticipoId = int.Parse(DestinoRedireccion(primero).Split('/').Last().Split('?').Last().Replace("id=", ""));

        var aplicar = await PostFormAsync(operador, $"/CuentaPorPagar/AplicarAnticipo/{anticipoId}", ("claveIdempotencia", "aplic-" + prov),
            ("imputaciones[0].ComprobanteId", f1.ToString()), ("imputaciones[0].Monto", "300"),
            ("imputaciones[1].ComprobanteId", f2.ToString()), ("imputaciones[1].Monto", "150"));
        Assert.Equal(HttpStatusCode.Redirect, aplicar.StatusCode);
        var sobreAplicar = await PostFormAsync(operador, $"/CuentaPorPagar/AplicarAnticipo/{anticipoId}", ("claveIdempotencia", "aplic2-" + prov),
            ("imputaciones[0].ComprobanteId", f2.ToString()), ("imputaciones[0].Monto", "100"));
        Assert.Equal(HttpStatusCode.OK, sobreAplicar.StatusCode);
        Assert.Contains("supera el saldo a favor del anticipo", WebUtility.HtmlDecode(await sobreAplicar.Content.ReadAsStringAsync()));

        await using (var ctx = await NuevoContextoAsync())
        {
            Assert.Equal(1, await ctx.AnticiposProveedor.CountAsync(a => a.ProveedorId == prov));
            Assert.Equal(2, await ctx.AnticipoAplicaciones.CountAsync(a => a.AnticipoProveedorId == anticipoId));
        }
        Assert.Equal(0m, (await LeerComprobanteAsync(f1)).SaldoPendiente);
        Assert.Equal(250m, (await LeerComprobanteAsync(f2)).SaldoPendiente);
        var detalle = await (await operador.GetAsync($"/CuentaPorPagar/Anticipo/{anticipoId}")).Content.ReadAsStringAsync();
        Assert.Contains("Facturas a las que se aplicó", detalle);
        Assert.DoesNotContain("Anular anticipo", detalle); // el operador no tiene cuentasporpagar.cancel

        AssertDenegado(await PostFormAsync(operador, $"/CuentaPorPagar/AnularAnticipo/{anticipoId}", ("motivo", "x")));
        int aplicacionId;
        await using (var ctx = await NuevoContextoAsync())
            aplicacionId = (await ctx.AnticipoAplicaciones.AsNoTracking().FirstAsync(a => a.AnticipoProveedorId == anticipoId)).Id;
        AssertDenegado(await PostFormAsync(operador, $"/CuentaPorPagar/AnularAplicacionAnticipo/{aplicacionId}", ("anticipoId", anticipoId.ToString()), ("motivo", "x")));

        using var anulador = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpAnuladorUserId);
        AssertDenegado(await anulador.GetAsync($"/CuentaPorPagar/Anticipar?proveedorId={prov}"));
        AssertDenegado(await anulador.GetAsync($"/CuentaPorPagar/AplicarAnticipo/{anticipoId}"));
        var anularConAplicaciones = await PostFormAsync(anulador, $"/CuentaPorPagar/AnularAnticipo/{anticipoId}", ("motivo", "carga duplicada"));
        Assert.Equal(HttpStatusCode.Redirect, anularConAplicaciones.StatusCode);
        await using (var ctx = await NuevoContextoAsync())
            Assert.False((await ctx.AnticiposProveedor.AsNoTracking().SingleAsync(a => a.Id == anticipoId)).Anulado); // tiene aplicaciones vigentes

        foreach (var a in new[] { aplicacionId })
            Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(anulador, $"/CuentaPorPagar/AnularAplicacionAnticipo/{a}", ("anticipoId", anticipoId.ToString()), ("motivo", "mal aplicado"))).StatusCode);
        await using (var ctx = await NuevoContextoAsync())
        {
            var otra = await ctx.AnticipoAplicaciones.AsNoTracking().Where(a => a.AnticipoProveedorId == anticipoId && !a.Anulada).Select(a => a.Id).SingleAsync();
            Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(anulador, $"/CuentaPorPagar/AnularAplicacionAnticipo/{otra}", ("anticipoId", anticipoId.ToString()), ("motivo", "mal aplicado"))).StatusCode);
        }
        Assert.Equal(300m, (await LeerComprobanteAsync(f1)).SaldoPendiente);
        Assert.Equal(400m, (await LeerComprobanteAsync(f2)).SaldoPendiente);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(anulador, $"/CuentaPorPagar/AnularAnticipo/{anticipoId}", ("motivo", "carga duplicada"))).StatusCode);
        await using (var ctx = await NuevoContextoAsync())
            Assert.True((await ctx.AnticiposProveedor.AsNoTracking().SingleAsync(a => a.Id == anticipoId)).Anulado);
    }

    [Fact]
    public async Task Anticipo_SoloLecturaNoRegistraNiAplicaNiAnula_YElPagoExigeOrigen()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var f = await SembrarComprobanteAsync(prov, "ANT-SL", 300m);
        using var soloLectura = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpSoloLecturaUserId);
        AssertDenegado(await soloLectura.GetAsync($"/CuentaPorPagar/Anticipar?proveedorId={prov}"));
        AssertDenegado(await PostFormAsync(soloLectura, "/CuentaPorPagar/Anticipar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-11-01"),
            ("Monto", "100"), ("OrigenFondosId", _origenId.ToString()), ("Medio", "Transferencia")));
        await using (var ctx = await NuevoContextoAsync())
            Assert.Equal(0, await ctx.AnticiposProveedor.CountAsync(a => a.ProveedorId == prov));

        // Un pago sin origen de fondos se rechaza con un mensaje claro y no guarda nada.
        using var operador = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);
        var sinOrigen = await PostFormAsync(operador, "/CuentaPorPagar/Pagar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-11-01"),
            ("Medio", "Efectivo"), ("Monto", "100"), ("Imputaciones[0].ComprobanteId", f.ToString()), ("Imputaciones[0].Monto", "100"));
        Assert.Equal(HttpStatusCode.OK, sinOrigen.StatusCode);
        Assert.Contains("origen de los fondos", WebUtility.HtmlDecode(await sinOrigen.Content.ReadAsStringAsync()));
        await using (var ctx = await NuevoContextoAsync())
            Assert.Equal(0, await ctx.PagosProveedor.CountAsync(p => p.ProveedorId == prov));
    }

    [Fact]
    public async Task Cheque_UnMismoChequeNoRespaldaDosPagos_PorFormulario()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync();
        var f1 = await SembrarComprobanteAsync(prov, "CHQ-1", 500m);
        var f2 = await SembrarComprobanteAsync(prov, "CHQ-2", 500m);
        int chequeId;
        await using (var ctx = await NuevoContextoAsync())
        {
            var cheque = new Cheque { Numero = "H" + prov, Banco = "Banco", Monto = 500m, ProveedorId = prov, Estado = EstadoCheque.Emitido };
            ctx.Cheques.Add(cheque);
            await ctx.SaveChangesAsync();
            chequeId = cheque.Id;
        }
        using var operador = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CxpOperadorUserId);

        (string, string)[] Campos(int factura, string clave) => new[]
        {
            ("ProveedorId", prov.ToString()), ("ClaveIdempotencia", clave), ("Fecha", "2026-11-01"), ("OrigenFondosId", _origenId.ToString()),
            ("Medio", "Cheque"), ("ChequeId", chequeId.ToString()), ("Monto", "500"),
            ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "500")
        };

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(operador, "/CuentaPorPagar/Pagar", Campos(f1, "chq-a-" + prov))).StatusCode);
        var segundo = await PostFormAsync(operador, "/CuentaPorPagar/Pagar", Campos(f2, "chq-b-" + prov));
        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.Contains("ya respalda otro pago o anticipo vigente", WebUtility.HtmlDecode(await segundo.Content.ReadAsStringAsync()));
        await using var ctx2 = await NuevoContextoAsync();
        Assert.Equal(1, await ctx2.PagosProveedor.CountAsync(p => p.ChequeId == chequeId && !p.Anulado));
        Assert.Equal(500m, (await LeerComprobanteAsync(f2)).SaldoPendiente);
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
            ("OrigenFondosId", _origenId.ToString()), ("Medio", "Efectivo"), ("Monto", "100"), ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "100")));
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
            ("OrigenFondosId", _origenId.ToString()), ("Medio", "Transferencia"), ("Monto", "400"), ("Imputaciones[0].ComprobanteId", id.ToString()), ("Imputaciones[0].Monto", "400"));
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
            ("OrigenFondosId", _origenId.ToString()), ("Medio", "Efectivo"), ("Monto", "100"), ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "100")));

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
            ["ProveedorId"] = prov.ToString(), ["Fecha"] = "2026-11-01", ["OrigenFondosId"] = _origenId.ToString(), ["Medio"] = "Efectivo", ["Monto"] = "100",
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
            ("OrigenFondosId", _origenId.ToString()), ("Medio", "Efectivo"), ("Monto", "600"), ("Imputaciones[0].ComprobanteId", factura.ToString()), ("Imputaciones[0].Monto", "600"));
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
            ("ProveedorId", prov.ToString()), ("ClaveIdempotencia", "clave-doble-clic-" + prov), ("Fecha", "2026-11-01"), ("OrigenFondosId", _origenId.ToString()), ("Medio", "Efectivo"),
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
        var pagoResp = await PostFormAsync(client, "/CuentaPorPagar/Pagar", ("ProveedorId", prov.ToString()), ("Fecha", "2026-10-05"), ("OrigenFondosId", _origenId.ToString()), ("Medio", "Transferencia"),
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
    public async Task AnularImputacionNotaCredito_RedirigeAUnComprobanteRealDeLaAplicacion_NoAlIdArbitrarioDelPost()
    {
        var (prov, _) = await SembrarProveedorConOrdenAsync("Redirect NC");
        var factura = await SembrarComprobanteAsync(prov, "F-RD-1", 1000m);
        var nc = await SembrarComprobanteAsync(prov, "NC-RD-1", 300m, TipoComprobanteProveedor.NotaCredito);
        using var client = _factory.CreateAuthenticatedClient();
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, $"/CuentaPorPagar/ImputarNotaCredito/{nc}", ("claveIdempotencia", "k-rd-" + prov),
            ("imputaciones[0].ComprobanteId", factura.ToString()), ("imputaciones[0].Monto", "100.00"))).StatusCode);
        int impId;
        await using (var ctx = await NuevoContextoAsync())
            impId = (await ctx.ImputacionesNotaCredito.AsNoTracking().SingleAsync(i => i.NotaCreditoId == nc)).Id;

        // El POST trae un comprobanteId ajeno a la aplicación: se redirige a la nota real, no a ese id.
        var ajeno = await PostFormAsync(client, "/CuentaPorPagar/AnularImputacionNotaCredito", ("id", impId.ToString()), ("comprobanteId", "999999"), ("motivo", "error de carga"));
        Assert.EndsWith($"/CuentaPorPagar/Detalle/{nc}", DestinoRedireccion(ajeno));

        // Con un comprobanteId que sí pertenece a la aplicación se conserva el destino de origen (aquí ya anulada: mismo destino).
        var propio = await PostFormAsync(client, "/CuentaPorPagar/AnularImputacionNotaCredito", ("id", impId.ToString()), ("comprobanteId", factura.ToString()), ("motivo", "otra vez"));
        Assert.EndsWith($"/CuentaPorPagar/Detalle/{factura}", DestinoRedireccion(propio));
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
