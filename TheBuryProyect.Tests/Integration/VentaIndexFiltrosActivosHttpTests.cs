using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Venta/Index: las pestañas Pendientes, Cotizaciones, Devoluciones y Envíos se arman en memoria
/// desde el mismo listado filtrado de Operaciones, pero solo Operaciones avisaba "Filtros activos".
/// Estas pruebas renderizan la vista Razor real (SQLite en memoria) y verifican que cada pestaña
/// afectada avise y ofrezca "Limpiar filtros" aunque el listado no esté vacío, sin alterar
/// resultados ni conteos.
/// </summary>
[Collection("HttpIntegration")]
public class VentaIndexFiltrosActivosHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] PestanasDerivadas = { "pendientes", "cotizaciones", "devoluciones", "envios" };
    private static int _counter = 62000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaIndexFiltrosActivosHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SinFiltros_NoMuestraAvisoDeFiltrosActivosEnNingunaPestana()
    {
        var seed = await SeedAsync();
        var html = await GetIndexAsync("/Venta/Index");

        Assert.DoesNotContain("data-venta-filtros-activos", html);
        Assert.DoesNotContain("Filtros activos", html);
        Assert.Contains(seed.NumeroCotizacion1, html);
        Assert.Contains(seed.NumeroCotizacion2, html);
    }

    [Fact]
    public async Task ConFiltroPorNumero_CotizacionesAvisaYOfreceLimpiar_AunqueHayaResultados()
    {
        var seed = await SeedAsync();
        var html = await GetIndexAsync($"/Venta/Index?Numero={seed.NumeroCotizacion1}");
        var panel = Panel(html, "cotizaciones");

        // Hay resultados (la cotización filtrada) y la otra existente queda fuera del listado...
        Assert.Contains(seed.NumeroCotizacion1, panel);
        Assert.DoesNotContain(seed.NumeroCotizacion2, panel);
        Assert.DoesNotContain("empty-state", panel);
        // ...y aun así la pestaña avisa que está restringida y ofrece limpiar.
        Assert.Contains("data-venta-filtros-activos=\"cotizaciones\"", panel);
        Assert.Contains("Filtros activos", panel);
        Assert.Contains("Limpiar filtros", panel);
        Assert.Matches(new Regex("href=\"/Venta(/Index)?#cotizaciones\""), panel);
    }

    [Fact]
    public async Task ConFiltro_TodasLasPestanasDerivadasMuestranElAviso()
    {
        var seed = await SeedAsync();
        var html = await GetIndexAsync($"/Venta/Index?Numero={seed.NumeroCotizacion1}");

        foreach (var pestana in PestanasDerivadas)
        {
            var panel = Panel(html, pestana);
            Assert.Contains($"data-venta-filtros-activos=\"{pestana}\"", panel);
            Assert.Contains("Limpiar filtros", panel);
        }
    }

    [Fact]
    public async Task ConFiltroPorCliente_AvisaAunqueNoTengaControlEnLaBarra()
    {
        var seed = await SeedAsync();
        var html = await GetIndexAsync($"/Venta/Index?ClienteId={seed.ClienteId}");

        Assert.Contains("data-venta-filtros-activos=\"cotizaciones\"", Panel(html, "cotizaciones"));
        Assert.Contains("pill-blue\">Filtros activos", Panel(html, "operaciones"));
    }

    [Fact]
    public async Task ConFiltroPorEstadoEnvio_AvisaEnLasPestanasDerivadas()
    {
        await SeedAsync();
        var html = await GetIndexAsync("/Venta/Index?EstadoEnvio=Entregado");

        foreach (var pestana in PestanasDerivadas)
        {
            Assert.Contains($"data-venta-filtros-activos=\"{pestana}\"", Panel(html, pestana));
        }
    }

    [Fact]
    public async Task LimpiarFiltros_DevuelveElListadoSinRestricciones()
    {
        var seed = await SeedAsync();
        var filtrado = await GetIndexAsync($"/Venta/Index?Numero={seed.NumeroCotizacion1}");
        var href = Regex.Match(Panel(filtrado, "cotizaciones"), "href=\"(/Venta[^\"]*)#cotizaciones\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(href));

        var limpio = await GetIndexAsync(href);
        var panel = Panel(limpio, "cotizaciones");

        Assert.Contains(seed.NumeroCotizacion1, panel);
        Assert.Contains(seed.NumeroCotizacion2, panel);
        Assert.DoesNotContain("data-venta-filtros-activos", limpio);
    }

    [Fact]
    public async Task Operaciones_ConservaSusControlesDeFiltroActivo()
    {
        var seed = await SeedAsync();
        var html = await GetIndexAsync($"/Venta/Index?Numero={seed.NumeroCotizacion1}");
        var operaciones = Panel(html, "operaciones");

        Assert.Contains("pill-blue\">Filtros activos", operaciones);
        Assert.Contains("1 registros", operaciones);
        Assert.Matches(new Regex("href=\"/Venta(/Index)?\"[^>]*>\\s*<span[^>]*>clear_all</span>\\s*Limpiar"), operaciones);
        // El aviso derivado no se duplica dentro de Operaciones.
        Assert.DoesNotContain("data-venta-filtros-activos", operaciones);
    }

    [Fact]
    public async Task EstadoVacio_SigueExplicandoQueLosFiltrosLimitanElListado()
    {
        await SeedAsync();
        var html = await GetIndexAsync("/Venta/Index?Numero=NO-EXISTE-ZZZ");
        var panel = Panel(html, "cotizaciones");

        Assert.Contains("Sin cotizaciones ni presupuestos en esta vista", panel);
        Assert.Contains("Hay filtros activos en Operaciones que limitan este listado.", panel);
        Assert.Contains("data-venta-filtros-activos=\"cotizaciones\"", panel);
    }

    [Fact]
    public async Task ElAviso_NoCambiaResultadosNiConteos()
    {
        var seed = await SeedAsync();
        var sinFiltro = await GetIndexAsync("/Venta/Index");
        var conFiltro = await GetIndexAsync($"/Venta/Index?Numero={seed.NumeroCotizacion1}");

        Assert.Contains($"{seed.TotalVentas} registros", Panel(sinFiltro, "operaciones"));
        Assert.Contains("1 registros", Panel(conFiltro, "operaciones"));
        Assert.Equal(1, Regex.Matches(Panel(conFiltro, "cotizaciones"), "class=\"sale-number").Count);
        Assert.Equal(2, Regex.Matches(Panel(sinFiltro, "cotizaciones"), "class=\"sale-number").Count);
    }

    private async Task<string> GetIndexAsync(string url)
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"Status {response.StatusCode} para {url}. Body:\n{body}");
        }
        return body;
    }

    /// <summary>Recorta el HTML del panel <c>panel-{clave}</c> hasta el siguiente panel (o el final).</summary>
    private static string Panel(string html, string clave)
    {
        var inicio = html.IndexOf($"id=\"panel-{clave}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"No se encontró el panel {clave}.");
        var siguiente = html.IndexOf("id=\"panel-", inicio + 10, StringComparison.Ordinal);
        return siguiente < 0 ? html[inicio..] : html[inicio..siguiente];
    }

    private async Task<SeedResult> SeedAsync()
    {
        await _factory.SeedTestUserAsync();
        var suffix = Interlocked.Increment(ref _counter);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        // Cada prueba arranca con un conjunto conocido: se limpian las ventas de pruebas anteriores
        // de esta clase (la base es en memoria y exclusiva de este fixture).
        context.Ventas.RemoveRange(await context.Ventas.IgnoreQueryFilters().ToListAsync());
        await context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = $"ClienteFiltros{suffix}",
            Apellido = "FILTROS-ACTIVOS",
            NumeroDocumento = $"43{suffix}",
            Telefono = "1111-4444",
            Domicilio = "Calle Filtros 123"
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var fecha = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        Venta Nueva(string numero, EstadoVenta estado) => new()
        {
            ClienteId = cliente.Id,
            Numero = numero,
            FechaVenta = fecha,
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            Subtotal = 100m,
            IVA = 21m,
            Total = 121m,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };

        var num1 = $"VFA-{suffix}-A";
        var num2 = $"VFA-{suffix}-B";
        context.Ventas.AddRange(
            Nueva(num1, EstadoVenta.Cotizacion),
            Nueva(num2, EstadoVenta.Presupuesto),
            Nueva($"VFA-{suffix}-C", EstadoVenta.Confirmada));
        await context.SaveChangesAsync();

        return new SeedResult(cliente.Id, num1, num2, 3);
    }

    private sealed record SeedResult(int ClienteId, string NumeroCotizacion1, string NumeroCotizacion2, int TotalVentas);
}
