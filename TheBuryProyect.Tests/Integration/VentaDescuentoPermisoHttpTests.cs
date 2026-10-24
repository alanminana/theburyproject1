using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// DESCUENTO-PERMISO-01 (PROMPT 76) sobre el pipeline HTTP + Razor real: el permiso independiente
/// ventas.discount decide qué se renderiza, qué devuelven las APIs/JSON y qué acepta el servidor ante
/// POST manipulados. Los permisos viven en BD y se releen en cada request (PermissionClaimsTransformation),
/// de modo que retirar el permiso a mitad de sesión tiene efecto inmediato.
/// </summary>
[Collection("HttpIntegration")]
public class VentaDescuentoPermisoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string TestUser = "testuser"; // ClaimTypes.Name que emite TestAuthHandler para todos
    private static int _counter = 76000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaDescuentoPermisoHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // =====================================================================
    // Render (vistas)
    // =====================================================================

    [Fact]
    public async Task Create_SinPermisoDescuento_NoRenderizaControlesDeDescuento_NiEnVentaNiEnElCotizador()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var response = await client.GetAsync("/Venta/Create");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-puede-descuentos=\"false\"", html);
        Assert.DoesNotContain("id=\"txt-descuento-item\"", html);
        Assert.DoesNotContain("data-rev-descuento", html);
        Assert.DoesNotContain("id=\"cotizacion-descuento-gral-pct\"", html);
        Assert.DoesNotContain("id=\"cotizacion-descuento-gral-importe\"", html);
        Assert.DoesNotContain("data-descuentos-ambos", html);
        Assert.DoesNotContain("id=\"cotizacion-descuento\"", html);
    }

    // =====================================================================
    // Reportes y exportaciones ordinarias
    // =====================================================================

    [Fact]
    public async Task ReporteVentasExcel_SinPermisoDescuento_NoExponeColumnaNiImporteDeDescuento_ConPermisoSi()
    {
        await _factory.SeedUserWithReporteSinDescuentoAsync();
        await _factory.SeedUserWithReporteConDescuentoAsync();
        var ventaId = await SeedVentaConDescuentosAsync();
        // El reporte excluye presupuestos: la venta sembrada (subtotal 1800, descuento general 500, total 1300) se confirma.
        await ConContextoAsync(async context =>
        {
            // Como en datos reales, Venta.Subtotal es neto de descuento (Subtotal + IVA = Total); el seed lo deja bruto.
            var venta = await context.Ventas.SingleAsync(v => v.Id == ventaId);
            venta.Estado = EstadoVenta.Confirmada;
            venta.Subtotal = venta.Total - venta.IVA;
            await context.SaveChangesAsync();
            return 0;
        });
        const string url = "/Reporte/ExportarVentasExcel?FechaDesde=2026-07-01&FechaHasta=2026-07-31";

        using (var sin = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ReporteSinDescuentoUserId))
        {
            var response = await sin.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
            var textos = wb.Worksheet("Ventas").CellsUsed().Select(c => c.GetString()).ToList();
            Assert.DoesNotContain(textos, t => t.Contains("Descuento", StringComparison.OrdinalIgnoreCase));
            var fila = wb.Worksheet("Ventas").Row(2);
            var importes = fila.CellsUsed().Where(c => c.DataType == ClosedXML.Excel.XLDataType.Number).Select(c => c.GetValue<decimal>()).ToList();
            Assert.Contains(1300m, importes);          // total real
            // Total − (Subtotal + IVA + Recargo) = 0: no se puede reconstruir el descuento por diferencia.
            var celdas = fila.CellsUsed().ToDictionary(c => c.Address.ColumnNumber, c => c);
            Assert.Equal(0m, celdas[9].GetValue<decimal>() - celdas[6].GetValue<decimal>() - celdas[7].GetValue<decimal>() - celdas[8].GetValue<decimal>());
            Assert.DoesNotContain(500m, importes);      // el importe descontado no aparece
            Assert.DoesNotContain(1800m, importes);     // ni el subtotal previo al descuento
        }

        using (var con = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ReporteConDescuentoUserId))
        {
            var response = await con.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
            var textos = wb.Worksheet("Ventas").CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Contains("Descuento ($)", textos);
            var importes = wb.Worksheet("Ventas").Row(2).CellsUsed().Where(c => c.DataType == ClosedXML.Excel.XLDataType.Number).Select(c => c.GetValue<decimal>()).ToList();
            Assert.Contains(500m, importes);
        }
    }

    [Fact]
    public async Task Create_ConPermisoDescuento_RenderizaLosControles()
    {
        await _factory.SeedUserWithVentaConDescuentoAsync();
        await SeedCajaAbiertaAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaConDescuentoUserId);

        var html = await (await client.GetAsync("/Venta/Create")).Content.ReadAsStringAsync();

        Assert.Contains("data-puede-descuentos=\"true\"", html);
        Assert.Contains("id=\"txt-descuento-item\"", html);
        Assert.Contains("data-rev-descuento", html);
        Assert.Contains("id=\"cotizacion-descuento-gral-pct\"", html);
        Assert.Contains("id=\"cotizacion-descuento\"", html);
    }

    [Fact]
    public async Task Edit_SinPermisoDescuento_NoExponeLosDescuentosPersistidosNiEnElHtmlNiEnElJsonInicial()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var ventaId = await SeedVentaConDescuentosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var html = await (await client.GetAsync($"/Venta/Edit/{ventaId}")).Content.ReadAsStringAsync();

        Assert.Contains("data-puede-descuentos=\"false\"", html);
        Assert.DoesNotContain("id=\"txt-descuento-item\"", html);
        var seed = SeedJson(html);
        var linea = seed.GetProperty("detalles")[0];
        Assert.Equal(0m, linea.GetProperty("descuento").GetDecimal());   // el 10 % persistido no viaja al navegador
        Assert.Equal(1800m, linea.GetProperty("subtotal").GetDecimal()); // el importe final sí
        Assert.Equal(ventaId, seed.GetProperty("id").GetInt32());

        // El descuento general persistido (500) tampoco viaja en el hidden del formulario.
        var hidden = Regex.Match(html, "<input[^>]*id=\"hdn-descuento\"[^>]*>").Value;
        Assert.Contains("value=\"0\"", hidden);
        Assert.DoesNotContain("500", hidden);
    }

    [Fact]
    public async Task Edit_ConPermisoDescuento_ExponeElDescuentoDeLaLinea()
    {
        await _factory.SeedUserWithVentaConDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var ventaId = await SeedVentaConDescuentosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaConDescuentoUserId);

        var html = await (await client.GetAsync($"/Venta/Edit/{ventaId}")).Content.ReadAsStringAsync();

        Assert.Contains("id=\"txt-descuento-item\"", html);
        Assert.Equal(10m, SeedJson(html).GetProperty("detalles")[0].GetProperty("descuento").GetDecimal());
        Assert.Contains("500", Regex.Match(html, "<input[^>]*id=\"hdn-descuento\"[^>]*>").Value);
    }

    [Fact]
    public async Task Details_SinPermisoDescuento_NoMuestraColumnasNiFilaDeDescuento_PeroSiElTotal()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var ventaId = await SeedVentaConDescuentosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var html = await (await client.GetAsync($"/Venta/Details/{ventaId}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("text-right\">Descuento</th>", html);
        Assert.DoesNotContain("<span class=\"text-slate-500\">Descuento</span>", html);
        // El importe final real (2.000 − 10 % − 500 = 1.300) sigue visible.
        Assert.Matches(@"1[\.,]?300[\.,]00", html);
    }

    [Fact]
    public async Task Details_ConPermisoDescuento_MuestraColumnasYFilaDeDescuento()
    {
        await _factory.SeedUserWithVentaConDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var ventaId = await SeedVentaConDescuentosAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaConDescuentoUserId);

        var html = await (await client.GetAsync($"/Venta/Details/{ventaId}")).Content.ReadAsStringAsync();

        Assert.Contains("text-right\">Descuento</th>", html);
        Assert.Contains("<span class=\"text-slate-500\">Descuento</span>", html);
    }

    [Fact]
    public async Task CotizacionDetalles_SinPermisoDescuento_NoMuestraElDescuento_ConPermisoSi()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await _factory.SeedUserWithVentaConDescuentoAsync();
        var cotizacionId = await SeedCotizacionConDescuentoAsync();

        using (var sin = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId))
        {
            var html = await (await sin.GetAsync($"/Cotizacion/Detalles/{cotizacionId}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain(">Descuento</p>", html);
            Assert.Contains("md:grid-cols-3", html);

            var imprimir = await (await sin.GetAsync($"/Cotizacion/Imprimir/{cotizacionId}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("Descuento total", imprimir);
            Assert.DoesNotContain("<th class=\"number\">Descuento</th>", imprimir);
        }

        using (var con = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaConDescuentoUserId))
        {
            var html = await (await con.GetAsync($"/Cotizacion/Detalles/{cotizacionId}")).Content.ReadAsStringAsync();
            Assert.Contains(">Descuento</p>", html);

            var imprimir = await (await con.GetAsync($"/Cotizacion/Imprimir/{cotizacionId}")).Content.ReadAsStringAsync();
            Assert.Contains("Descuento total", imprimir);
            Assert.Contains("<th class=\"number\">Descuento</th>", imprimir);
        }
    }

    // =====================================================================
    // APIs JSON
    // =====================================================================

    private static object Preview(decimal descuentoLinea, decimal descuentoGeneral = 0m, int? ventaId = null, int productoId = 0) => new
    {
        ventaId,
        detalles = new[]
        {
            new { productoId, cantidad = 2, precioUnitario = 1000m, descuento = descuentoLinea }
        },
        descuentoGeneral,
        descuentoEsPorcentaje = false,
        tipoPago = 0
    };

    [Fact]
    public async Task PreviewTotales_SinPermisoDescuento_RechazaDescuentoDeLineaYGeneral_Con403()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var linea = await client.PostAsJsonAsync("/api/ventas/CalcularTotalesVenta", Preview(descuentoLinea: 10m));
        var general = await client.PostAsJsonAsync("/api/ventas/CalcularTotalesVenta", Preview(descuentoLinea: 0m, descuentoGeneral: 100m));

        Assert.Equal(HttpStatusCode.Forbidden, linea.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, general.StatusCode);
        Assert.Contains("permiso", await linea.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PreviewTotales_SinPermisoDescuento_SinDescuentos_Devuelve200ConDesgloseEnCero()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var response = await client.PostAsJsonAsync("/api/ventas/CalcularTotalesVenta", Preview(descuentoLinea: 0m));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0m, json.GetProperty("descuentoGeneralAplicado").GetDecimal());
        Assert.Equal(2000m, json.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task PreviewTotales_SinPermisoDescuento_VentaExistente_DevuelveImportesRealesSinElDesglose()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, _) = await SeedVentaConDescuentosConProductoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var response = await client.PostAsJsonAsync(
            "/api/ventas/CalcularTotalesVenta", Preview(descuentoLinea: 0m, ventaId: ventaId, productoId: productoId));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1300m, json.GetProperty("total").GetDecimal());                                  // 2.000 - 10 % - 500
        Assert.Equal(1800m, json.GetProperty("detalles")[0].GetProperty("subtotal").GetDecimal());
        Assert.Equal(0m, json.GetProperty("descuentoGeneralAplicado").GetDecimal());
        Assert.Equal(0m, json.GetProperty("detalles")[0].GetProperty("descuentoGeneralProrrateado").GetDecimal());
    }

    [Fact]
    public async Task PreviewTotales_ConPermisoDescuento_AplicaYDevuelveElDesglose()
    {
        await _factory.SeedUserWithVentaConDescuentoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaConDescuentoUserId);

        var response = await client.PostAsJsonAsync("/api/ventas/CalcularTotalesVenta", Preview(descuentoLinea: 10m, descuentoGeneral: 500m));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500m, json.GetProperty("descuentoGeneralAplicado").GetDecimal());
        Assert.Equal(1300m, json.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task PreviewTotales_PermisoRetiradoEnMitadDeSesion_PasaDe200A403_SinReiniciar()
    {
        await _factory.PrepararUsuarioConDescuentoRetirableAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaDescuentoRetiradoUserId);

        var antes = await client.PostAsJsonAsync("/api/ventas/CalcularTotalesVenta", Preview(descuentoLinea: 10m));
        Assert.Equal(HttpStatusCode.OK, antes.StatusCode);

        await _factory.RetirarPermisoDescuentoAsync();

        var despues = await client.PostAsJsonAsync("/api/ventas/CalcularTotalesVenta", Preview(descuentoLinea: 10m));
        Assert.Equal(HttpStatusCode.Forbidden, despues.StatusCode);
    }

    [Fact]
    public async Task CotizacionSimular_SinPermisoDescuento_RechazaConDescuento_Con403()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        foreach (var cuerpo in new object[]
                 {
                     new { productos = Array.Empty<object>(), descuentoGeneralPorcentaje = 10m },
                     new { productos = Array.Empty<object>(), descuentoGeneralImporte = 100m },
                     new { productos = new[] { new { productoId = 1, cantidad = 1, descuentoPorcentaje = 5m } } },
                     new { productos = new[] { new { productoId = 1, cantidad = 1, descuentoImporte = 5m } } }
                 })
        {
            var response = await client.PostAsJsonAsync("/api/cotizacion/simular", cuerpo);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task CotizacionSimular_SinPermisoDescuento_SinDescuentos_NoEsRechazadoPorPermiso()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var (_, productoId, _) = await SeedVentaConDescuentosConProductoAsync();

        var response = await client.PostAsJsonAsync("/api/cotizacion/simular", new { productos = new[] { new { productoId, cantidad = 1 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CotizacionGuardar_SinPermisoDescuento_RechazaConDescuento_Con403_YNoPersiste()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        var antes = await ContarCotizacionesAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var response = await client.PostAsJsonAsync("/api/cotizacion/guardar", new
        {
            simulacion = new
            {
                productos = new[] { new { productoId = 1, cantidad = 1, descuentoImporte = 50m } }
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(antes, await ContarCotizacionesAsync());
    }

    [Fact]
    public async Task CotizacionConvertir_SinPermisoDescuento_CotizacionConDescuento_EsRechazadaYNoCreaVenta()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        var cotizacionId = await SeedCotizacionConDescuentoAsync();
        var ventasAntes = await ContarVentasAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);

        var response = await client.PostAsJsonAsync(
            $"/api/cotizacion/{cotizacionId}/conversion/convertir",
            new { usarPrecioCotizado = true, confirmarAdvertencias = true });
        var cuerpo = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Aplicar descuentos", cuerpo);
        Assert.Equal(ventasAntes, await ContarVentasAsync());
    }

    // =====================================================================
    // POST manipulados (CreateAjax / EditAjax)
    // =====================================================================

    [Fact]
    public async Task CreateAjax_SinPermisoDescuento_DescuentoManipuladoEnCampoOculto_Responde403YNoPersiste()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (_, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var ventasAntes = await ContarVentasAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, "/Venta/Create");

        var form = FormVenta(token, clienteId, productoId, descuentoLinea: "50", descuentoGeneral: "0");
        var response = await client.PostAsync("/Venta/CreateAjax", new FormUrlEncodedContent(form));
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal(ventasAntes, await ContarVentasAsync());
    }

    [Fact]
    public async Task CreateAjax_SinPermisoDescuento_DescuentoGeneralManipulado_Responde403YNoPersiste()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (_, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var ventasAntes = await ContarVentasAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, "/Venta/Create");

        var form = FormVenta(token, clienteId, productoId, descuentoLinea: "0", descuentoGeneral: "1000");
        var response = await client.PostAsync("/Venta/CreateAjax", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ventasAntes, await ContarVentasAsync());
    }

    [Fact]
    public async Task EditAjax_SinPermisoDescuento_DescuentoManipulado_Responde403YNoModificaLaVenta()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var rowVersion = await RowVersionAsync(ventaId);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, $"/Venta/Edit/{ventaId}");

        var form = FormVenta(token, clienteId, productoId, descuentoLinea: "99", descuentoGeneral: "0", rowVersion: rowVersion, ventaId: ventaId);
        var response = await client.PostAsync($"/Venta/EditAjax/{ventaId}", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var (descuentoLinea, descuentoGeneral, total) = await LeerDescuentosAsync(ventaId);
        Assert.Equal(10m, descuentoLinea);
        Assert.Equal(500m, descuentoGeneral);
        Assert.Equal(1300m, total);
    }

    [Fact]
    public async Task EditAjax_SinPermisoDescuento_FormularioLegitimoSinCamposDeDescuento_ConservaLosDescuentos()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var lineaId = await LineaIdAsync(ventaId);
        var rowVersion = await RowVersionAsync(ventaId);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, $"/Venta/Edit/{ventaId}");

        // Lo que realmente envía el wizard sin permiso: Id de la línea + descuentos en 0 (no se renderizan ni se siembran).
        var form = FormVenta(token, clienteId, productoId, descuentoLinea: "0", descuentoGeneral: "0", rowVersion: rowVersion, ventaId: ventaId, lineaId: lineaId);
        var response = await client.PostAsync($"/Venta/EditAjax/{ventaId}", new FormUrlEncodedContent(form));
        var cuerpo = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, cuerpo);
        Assert.True(JsonDocument.Parse(cuerpo).RootElement.GetProperty("success").GetBoolean(), cuerpo);
        var (descuentoLinea, descuentoGeneral, total) = await LeerDescuentosAsync(ventaId);
        Assert.Equal(10m, descuentoLinea);
        Assert.Equal(500m, descuentoGeneral);
        Assert.Equal(1300m, total);
    }

    [Fact]
    public async Task EditAjax_SinPermisoDescuento_AumentarLaCantidadDeUnaLineaConDescuento_Responde403()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var lineaId = await LineaIdAsync(ventaId);
        var rowVersion = await RowVersionAsync(ventaId);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, $"/Venta/Edit/{ventaId}");

        // Descuentos en 0 (formulario sin permiso) pero 50 unidades en la línea que tiene 10 %.
        var form = FormVenta(token, clienteId, productoId, "0", "0", rowVersion: rowVersion, ventaId: ventaId, lineaId: lineaId, cantidad: 50);
        var response = await client.PostAsync($"/Venta/EditAjax/{ventaId}", new FormUrlEncodedContent(form));
        var cuerpo = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("aumentar la cantidad", cuerpo);
        var (descuentoLinea, descuentoGeneral, total) = await LeerDescuentosAsync(ventaId);
        Assert.Equal(10m, descuentoLinea);
        Assert.Equal(500m, descuentoGeneral);
        Assert.Equal(1300m, total);
    }

    [Fact]
    public async Task EditAjax_PermisoRetiradoEnMitadDeSesion_PasaDeAceptarElCambioA403_ConLaMismaSesion()
    {
        await _factory.PrepararUsuarioConDescuentoRetirableAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaDescuentoRetiradoUserId);

        // Con permiso: cambia el 10 % a 25 % y quita el general.
        var token = await TokenAsync(client, $"/Venta/Edit/{ventaId}");
        var antes = await client.PostAsync($"/Venta/EditAjax/{ventaId}", new FormUrlEncodedContent(
            FormVenta(token, clienteId, productoId, "25", "0", rowVersion: await RowVersionAsync(ventaId), ventaId: ventaId)));
        Assert.True(antes.StatusCode == HttpStatusCode.OK, await antes.Content.ReadAsStringAsync());
        Assert.Equal(25m, (await LeerDescuentosAsync(ventaId)).Linea);

        await _factory.RetirarPermisoDescuentoAsync();

        // Sin permiso (misma sesión, sin relogin): el mismo cambio hacia 40 % es rechazado y nada se modifica.
        var token2 = await TokenAsync(client, $"/Venta/Edit/{ventaId}");
        var despues = await client.PostAsync($"/Venta/EditAjax/{ventaId}", new FormUrlEncodedContent(
            FormVenta(token2, clienteId, productoId, "40", "0", rowVersion: await RowVersionAsync(ventaId), ventaId: ventaId)));

        Assert.Equal(HttpStatusCode.Forbidden, despues.StatusCode);
        Assert.Equal(25m, (await LeerDescuentosAsync(ventaId)).Linea);
    }

    // POST de página completa (sin AJAX): mismos rechazos, con el mensaje de permiso en el formulario.

    [Fact]
    public async Task CreatePost_SinPermisoDescuento_DescuentoManipulado_ReRenderizaConElMensajeYNoPersiste()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (_, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var ventasAntes = await ContarVentasAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, "/Venta/Create");

        var response = await client.PostAsync("/Venta/Create", new FormUrlEncodedContent(
            FormVenta(token, clienteId, productoId, descuentoLinea: "30", descuentoGeneral: "0")));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("permiso para aplicar", html);
        Assert.Equal(ventasAntes, await ContarVentasAsync());
    }

    [Fact]
    public async Task EditPost_SinPermisoDescuento_DescuentoManipulado_ReRenderizaConElMensajeYNoModifica()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var lineaId = await LineaIdAsync(ventaId);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, $"/Venta/Edit/{ventaId}");

        var form = FormVenta(token, clienteId, productoId, "77", "0", rowVersion: await RowVersionAsync(ventaId), ventaId: ventaId, lineaId: lineaId);
        form["accionConfirmacion"] = "guardar";
        var response = await client.PostAsync($"/Venta/Edit/{ventaId}", new FormUrlEncodedContent(form));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("permiso para aplicar", html);
        var (descuentoLinea, descuentoGeneral, total) = await LeerDescuentosAsync(ventaId);
        Assert.Equal(10m, descuentoLinea);
        Assert.Equal(500m, descuentoGeneral);
        Assert.Equal(1300m, total);
    }

    [Fact]
    public async Task EditPost_SinPermisoDescuento_FormularioLegitimo_GuardaYConservaLosDescuentos()
    {
        await _factory.SeedUserWithVentaSinDescuentoAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var lineaId = await LineaIdAsync(ventaId);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSinDescuentoUserId);
        var token = await TokenAsync(client, $"/Venta/Edit/{ventaId}");

        var form = FormVenta(token, clienteId, productoId, "0", "0", rowVersion: await RowVersionAsync(ventaId), ventaId: ventaId, lineaId: lineaId);
        form["accionConfirmacion"] = "guardar";
        var response = await client.PostAsync($"/Venta/Edit/{ventaId}", new FormUrlEncodedContent(form));

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found, $"Esperaba redirect y fue {(int)response.StatusCode}.");
        var (descuentoLinea, descuentoGeneral, total) = await LeerDescuentosAsync(ventaId);
        Assert.Equal(10m, descuentoLinea);
        Assert.Equal(500m, descuentoGeneral);
        Assert.Equal(1300m, total);
    }

    // FIN-S-005: Edit exige ventas.update, pero confirmar exige ventas.create. Encadenar la confirmación desde Edit no
    // puede saltarse ese permiso: el guardado se aplica y la venta NO se confirma.
    [Theory]
    [InlineData("confirmar")]
    [InlineData("confirmar-facturar")]
    public async Task EditPost_ConfirmarSinPermisoCrear_GuardaPeroNoConfirma(string accion)
    {
        await _factory.SeedUserWithVentaSoloActualizarAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, productoId, clienteId) = await SeedVentaConDescuentosConProductoAsync();
        var lineaId = await LineaIdAsync(ventaId);
        var estadoAntes = await EstadoVentaAsync(ventaId);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.VentaSoloActualizarUserId);
        var token = await TokenAsync(client, $"/Venta/Edit/{ventaId}");

        var form = FormVenta(token, clienteId, productoId, "0", "0", rowVersion: await RowVersionAsync(ventaId), ventaId: ventaId, lineaId: lineaId);
        form["accionConfirmacion"] = accion;
        var response = await client.PostAsync($"/Venta/Edit/{ventaId}", new FormUrlEncodedContent(form));

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found, $"Esperaba redirect y fue {(int)response.StatusCode}.");
        Assert.Equal(estadoAntes, await EstadoVentaAsync(ventaId));
        Assert.NotEqual(EstadoVenta.Confirmada, await EstadoVentaAsync(ventaId));
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private Task<EstadoVenta> EstadoVentaAsync(int ventaId) => ConContextoAsync(async c =>
        (await c.Ventas.AsNoTracking().SingleAsync(v => v.Id == ventaId)).Estado);

    private static Dictionary<string, string> FormVenta(
        string token, int clienteId, int productoId, string descuentoLinea, string descuentoGeneral,
        string? rowVersion = null, int? ventaId = null, int? lineaId = null, int cantidad = 2)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ClienteId"] = clienteId.ToString(),
            ["FechaVenta"] = "2026-07-24",
            ["Estado"] = ((int)EstadoVenta.Presupuesto).ToString(),
            ["TipoPago"] = ((int)TipoPago.Efectivo).ToString(),
            ["Descuento"] = descuentoGeneral,
            ["Detalles[0].ProductoId"] = productoId.ToString(),
            ["Detalles[0].Cantidad"] = cantidad.ToString(),
            ["Detalles[0].PrecioUnitario"] = "1000",
            ["Detalles[0].Descuento"] = descuentoLinea,
            ["Detalles[0].Subtotal"] = "2000",
            ["Detalles[0].EnviarADomicilio"] = "true"
        };
        if (lineaId.HasValue) form["Detalles[0].Id"] = lineaId.Value.ToString();
        if (rowVersion != null) form["RowVersion"] = rowVersion;
        if (ventaId.HasValue) form["Id"] = ventaId.Value.ToString();
        return form;
    }

    private static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static JsonElement SeedJson(string html)
    {
        var match = Regex.Match(html, "id=\"venta-inicial-json\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, "No se encontró el seed #venta-inicial-json.");
        return JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value)).RootElement.Clone();
    }

    private async Task<T> ConContextoAsync<T>(Func<AppDbContext, Task<T>> accion)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        return await accion(context);
    }

    private Task<int> ContarVentasAsync() => ConContextoAsync(c => c.Ventas.IgnoreQueryFilters().CountAsync());

    private Task<int> ContarCotizacionesAsync() => ConContextoAsync(c => c.Cotizaciones.IgnoreQueryFilters().CountAsync());


    private Task<string> RowVersionAsync(int ventaId) => ConContextoAsync(async c =>
        Convert.ToBase64String((await c.Ventas.AsNoTracking().SingleAsync(v => v.Id == ventaId)).RowVersion));

    private Task<(decimal Linea, decimal General, decimal Total)> LeerDescuentosAsync(int ventaId) => ConContextoAsync(async c =>
    {
        var venta = await c.Ventas.AsNoTracking().Include(v => v.Detalles).SingleAsync(v => v.Id == ventaId);
        return (venta.Detalles.Single(d => !d.IsDeleted).Descuento, venta.Descuento, venta.Total);
    });

    private Task SeedCajaAbiertaAsync() => ConContextoAsync(async context =>
    {
        if (await context.AperturasCaja.AnyAsync(a => !a.Cerrada && a.UsuarioApertura == TestUser))
            return 0;

        var caja = new Caja { Codigo = "CAJA-DSC-PERM", Nombre = "Caja descuento permiso", IsDeleted = false, RowVersion = new byte[8] };
        context.Cajas.Add(caja);
        await context.SaveChangesAsync();
        context.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id, UsuarioApertura = TestUser, MontoInicial = 0m,
            Cerrada = false, IsDeleted = false, RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
        return 0;
    });

    private async Task<int> SeedVentaConDescuentosAsync() => (await SeedVentaConDescuentosConProductoAsync()).VentaId;

    private Task<int> LineaIdAsync(int ventaId) =>
        ConContextoAsync(c => c.VentaDetalles.Where(d => d.VentaId == ventaId && !d.IsDeleted).Select(d => d.Id).SingleAsync());

    /// <summary>Presupuesto con 2 × 1.000, 10 % de línea (1.800) y 500 de descuento general → total 1.300.</summary>
    private Task<(int VentaId, int ProductoId, int ClienteId)> SeedVentaConDescuentosConProductoAsync() => ConContextoAsync(async context =>
    {
        var suffix = Interlocked.Increment(ref _counter);
        var marca = new Marca { Codigo = $"MD{suffix}", Nombre = $"Marca DP {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CD{suffix}", Nombre = $"Categoria DP {suffix}", Activo = true };
        var cliente = new Cliente
        {
            Nombre = $"ClienteDP{suffix}", Apellido = "DESC-PERMISO", NumeroDocumento = $"50{suffix}",
            Telefono = "1111-2222", Domicilio = "Calle Descuento 76"
        };
        context.Marcas.Add(marca);
        context.Categorias.Add(categoria);
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PD{suffix}", Nombre = $"ProductoDP{suffix}", CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 600m, PrecioVenta = 1000m, PorcentajeIVA = 0m, StockActual = 100, Activo = true
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VDP-{suffix}",
            FechaVenta = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Efectivo,
            Subtotal = 1800m,
            Descuento = 500m,
            IVA = 0m,
            Total = 1300m,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };
        venta.Detalles.Add(new VentaDetalle
        {
            ProductoId = producto.Id,
            Cantidad = 2,
            PrecioUnitario = 1000m,
            Descuento = 10m,
            Subtotal = 1800m,
            DescuentoGeneralProrrateado = 500m,
            PorcentajeIVA = 0m,
            SubtotalFinalNeto = 1300m,
            SubtotalFinalIVA = 0m,
            SubtotalFinal = 1300m
        });
        context.Ventas.Add(venta);
        await context.SaveChangesAsync();

        return (venta.Id, producto.Id, cliente.Id);
    });

    private Task<int> SeedCotizacionConDescuentoAsync() => ConContextoAsync(async context =>
    {
        var suffix = Interlocked.Increment(ref _counter);
        var marca = new Marca { Codigo = $"MC{suffix}", Nombre = $"Marca CT {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CC{suffix}", Nombre = $"Categoria CT {suffix}", Activo = true };
        context.Marcas.Add(marca);
        context.Categorias.Add(categoria);
        await context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PC{suffix}", Nombre = $"ProductoCT{suffix}", CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 600m, PrecioVenta = 1000m, PorcentajeIVA = 0m, StockActual = 100, Activo = true
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var cotizacion = new Cotizacion
        {
            Numero = $"COT-DP-{suffix}",
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida,
            NombreClienteLibre = "Cliente mostrador",
            Subtotal = 2000m,
            DescuentoTotal = 100m,
            TotalBase = 1900m,
            Detalles =
            {
                new CotizacionDetalle
                {
                    ProductoId = producto.Id,
                    CodigoProductoSnapshot = producto.Codigo,
                    NombreProductoSnapshot = producto.Nombre,
                    Cantidad = 2,
                    PrecioUnitarioSnapshot = 1000m,
                    DescuentoImporteSnapshot = 100m,
                    Subtotal = 1900m
                }
            }
        };
        context.Cotizaciones.Add(cotizacion);
        await context.SaveChangesAsync();
        return cotizacion.Id;
    });
}
