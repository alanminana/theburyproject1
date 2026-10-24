using System.Net;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Backlog de la auditoría QA (2026-10-20), lote A: COM-S-003 (URL de origen de tickets: esquemas peligrosos) y COM-S-018
/// (exportaciones de reportes sin reportes.export). FIN-S-005 se cubre en VentaDescuentoPermisoHttpTests.
/// </summary>
public class UrlOrigenSeguraTests
{
    [Theory]
    [InlineData("/Venta/Details/5", true)]
    [InlineData("/Venta/Details/5?x=1&y=2#frag", true)]
    [InlineData("/", true)]
    [InlineData("https://erp.example.com/Venta/Details/5", true)]
    [InlineData("http://localhost:5187/Caja", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JaVaScRiPt:alert(1)", false)]
    [InlineData("  javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("vbscript:msgbox(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("//evil.example.com/x", false)]
    [InlineData("ftp://host/x", false)]
    [InlineData("Venta/Details/5", false)]
    [InlineData("/Venta\u0000/x", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void EsSegura_SoloRutaRelativaOHttpHttps(string? url, bool esperado)
    {
        Assert.Equal(esperado, UrlOrigenSegura.EsSegura(url));
    }

    [Fact]
    public void EsSegura_RechazaRutaConBarraInvertidaInicial()
    {
        Assert.False(UrlOrigenSegura.EsSegura("/" + (char)92 + "evil.example.com"));
    }

    [Fact]
    public void Normalizar_DescartaLoPeligrosoYConservaLoSeguro()
    {
        Assert.Null(UrlOrigenSegura.Normalizar("javascript:alert(1)"));
        Assert.Equal("/Venta/Details/5", UrlOrigenSegura.Normalizar("  /Venta/Details/5  "));
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)", null)]
    [InlineData("/Venta/Details/5?x=1", "/Venta/Details/5?x=1")]
    public void CrearTicketRequest_MapeaUrlOrigenSoloSiEsSegura(string url, string? esperado)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();

        var ticket = mapper.Map<Ticket>(new CreateTicketRequest
        {
            Titulo = "t", Descripcion = "d", Tipo = TipoTicket.ErrorFuncional, UrlOrigen = url
        });

        Assert.Equal(esperado, ticket.UrlOrigen);
    }
}

[Collection("HttpIntegration")]
public class LoteABacklogSeguridadHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LoteABacklogSeguridadHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ------------------------------------------------------------------ COM-S-003

    private async Task<int> SeedTicketAsync(string? urlOrigen)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var ticket = new Ticket
        {
            Titulo = "Ticket COM-S-003", Descripcion = "descripcion", Tipo = TipoTicket.ErrorFuncional,
            Estado = EstadoTicket.Pendiente, UrlOrigen = urlOrigen, RowVersion = new byte[8]
        };
        context.Tickets.Add(ticket);
        await context.SaveChangesAsync();
        return ticket.Id;
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("//evil.example.com/x")]
    public async Task TicketDetails_UrlOrigenPeligrosa_SeMuestraComoTextoYNuncaComoEnlace(string url)
    {
        await _factory.SeedUserWithTicketsVerAsync();
        var id = await SeedTicketAsync(url);
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.TicketsVerUserId);

        var response = await client.GetAsync($"/Ticket/Details/{id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"//evil", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("URL origen", html);
    }

    [Fact]
    public async Task TicketDetails_UrlOrigenRelativaSegura_SigueSiendoEnlace()
    {
        await _factory.SeedUserWithTicketsVerAsync();
        var id = await SeedTicketAsync("/Venta/Details/5");
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.TicketsVerUserId);

        var html = await (await client.GetAsync($"/Ticket/Details/{id}")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/Venta/Details/5\"", html);
    }

    // ------------------------------------------------------------------ COM-S-018

    [Theory]
    [InlineData("/Reporte/ExportarVentasExcel?FechaDesde=2026-07-01&FechaHasta=2026-07-31")]
    [InlineData("/Reporte/ExportarMargenesExcel")]
    [InlineData("/Reporte/ExportarMorosidadExcel")]
    [InlineData("/Reporte/ExportarVentasPdf?FechaDesde=2026-07-01&FechaHasta=2026-07-31")]
    [InlineData("/Reporte/ExportarMorosidadPdf")]
    public async Task ReporteExportar_SinReportesExport_EsDenegado_ConPermisoNoLo(string url)
    {
        await _factory.SeedUserWithReporteSoloVerAsync();
        await _factory.SeedUserWithReporteSinDescuentoAsync();   // reportes.view + reportes.export

        using (var soloVer = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ReporteSoloVerUserId))
        {
            var response = await soloVer.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var conExport = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ReporteSinDescuentoUserId))
        {
            var response = await conExport.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var tipo = response.Content.Headers.ContentType?.MediaType;
            Assert.True(tipo is "application/pdf" or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Se esperaba un archivo y fue '{tipo}'.");
            Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 0);
        }
    }

    [Fact]
    public async Task ReporteMorosidad_SoloVer_SigueMostrandoLaPantalla()
    {
        await _factory.SeedUserWithReporteSoloVerAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ReporteSoloVerUserId);

        var response = await client.GetAsync("/Reporte/Morosidad");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
