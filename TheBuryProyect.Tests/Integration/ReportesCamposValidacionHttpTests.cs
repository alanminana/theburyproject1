using System.Net;
using TheBuryProject.Controllers;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L8 — filtros de fecha de los reportes. Un rango invertido devolvía un reporte vacío (o en cero) sin avisar
/// y las fechas extremas terminaban en un error genérico: ahora se rechazan con un mensaje claro.
/// </summary>
[Collection("HttpIntegration")]
public class ReportesCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    public ReportesCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/Reporte/Iva?FechaDesde=2026-10-01&FechaHasta=2026-01-01")]
    [InlineData("/Reporte/Iva?FechaDesde=0001-01-01&FechaHasta=9999-12-31")]
    [InlineData("/Reporte/ComisionesVendedores?FechaDesde=2026-10-01&FechaHasta=2026-01-01")]
    [InlineData("/Reporte/ComisionesVendedores?FechaDesde=1999-12-31&FechaHasta=2026-01-01")]
    [InlineData("/Reporte/ExportarVentasExcel?FechaDesde=2026-10-01&FechaHasta=2026-01-01")]
    [InlineData("/Reporte/ExportarVentasPdf?FechaDesde=2026-10-01&FechaHasta=2026-01-01")]
    [InlineData("/Reporte/ExportarMovimientosValorizadosExcel?FechaDesde=2026-10-01&FechaHasta=2026-01-01")]
    public async Task Reporte_RangoInvalido_SeRechazaConRedireccion(string url)
    {
        var resp = await _factory.CreateAuthenticatedClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    [Theory]
    [InlineData("fechaDesde=2026-10-01&fechaHasta=2026-01-01")]
    [InlineData("fechaDesde=0001-01-01&fechaHasta=9999-12-31")]
    public async Task VentasAgrupadas_RangoInvalido_Devuelve400ConMensaje(string query)
    {
        var resp = await _factory.CreateAuthenticatedClient().GetAsync($"/Reporte/VentasAgrupadas?{query}&agruparPor=dia");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("fecha", (await resp.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Reporte/Iva?FechaDesde=2026-01-01&FechaHasta=2026-10-01")]
    [InlineData("/Reporte/Iva?FechaDesde=2026-05-01&FechaHasta=2026-05-01")]
    [InlineData("/Reporte/ComisionesVendedores?FechaDesde=2026-01-01&FechaHasta=2026-10-01")]
    [InlineData("/Reporte/VentasAgrupadas?fechaDesde=2026-01-01&fechaHasta=2026-10-01&agruparPor=dia")]
    [InlineData("/Reporte/ExportarVentasExcel?FechaDesde=2026-01-01&FechaHasta=2026-10-01")]
    public async Task Reporte_RangoValido_SeGenera(string url)
    {
        var resp = await _factory.CreateAuthenticatedClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-10-01", true)]
    [InlineData("2026-05-01", "2026-05-01", true)]
    [InlineData("2000-01-01", "2100-12-31", true)]
    [InlineData(null, null, true)]
    [InlineData("2026-01-01", null, true)]
    [InlineData("2026-10-01", "2026-01-01", false)]
    [InlineData("1999-12-31", "2026-01-01", false)]
    [InlineData("2026-01-01", "2101-01-01", false)]
    [InlineData("0001-01-01", "9999-12-31", false)]
    public void ErrorRango_Reglas(string? desde, string? hasta, bool valido)
    {
        DateTime? d = desde == null ? null : DateTime.Parse(desde);
        DateTime? h = hasta == null ? null : DateTime.Parse(hasta);

        Assert.Equal(valido, ReporteController.ErrorRango(d, h) == null);
    }
}
