using System.IO;
using Xunit;

namespace TheBuryProyect.Tests.Unit;

/// <summary>
/// Contrato de UI del rediseño del detalle de caja (apertura + cierre): cards
/// Vendido/Cobrado/Pendiente/Caja esperada, tabs, libro mayor con saldo acumulado,
/// conteo físico y la separación explícita "impacta caja física".
/// El contenido vive en partials compartidos (_Conciliacion*); por eso se leen varios archivos.
/// </summary>
public class CajaDetallesAperturaContractTests
{
    private static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Archivo no encontrado: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void DetallesApertura_DelegaEnCardsYTabsCompartidos()
    {
        var view = Read("Views/Caja/DetallesApertura_tw.cshtml");
        Assert.Contains("_ConciliacionCards", view);
        Assert.Contains("_ConciliacionTabs", view);
    }

    [Fact]
    public void DetallesCierre_DelegaEnCardsYTabsCompartidos()
    {
        var view = Read("Views/Caja/DetallesCierre_tw.cshtml");
        Assert.Contains("_ConciliacionCards", view);
        Assert.Contains("_ConciliacionTabs", view);
    }

    [Fact]
    public void Cards_SeparanCajaEsperadaVendidoCobradoPendiente()
    {
        var cards = Read("Views/Caja/_ConciliacionCards.cshtml");
        Assert.Contains("Caja esperada", cards);
        Assert.Contains("Vendido", cards);
        Assert.Contains("Cobrado", cards);
        Assert.Contains("Pendiente", cards);
    }

    [Fact]
    public void Tabs_TieneLasCincoSecciones()
    {
        var tabs = Read("Views/Caja/_ConciliacionTabs.cshtml");
        Assert.Contains("data-cc-tab=\"resumen\"", tabs);
        Assert.Contains("data-cc-tab=\"ventas\"", tabs);
        Assert.Contains("data-cc-tab=\"movimientos\"", tabs);
        Assert.Contains("data-cc-tab=\"conciliacion\"", tabs);
        Assert.Contains("data-cc-tab=\"auditoria\"", tabs);
    }

    [Fact]
    public void Ventas_MuestraCobradoAhoraYPendienteYFiltros()
    {
        var ventas = Read("Views/Caja/_ConciliacionVentasTab.cshtml");
        Assert.Contains("Cobrado ahora", ventas);
        Assert.Contains("Pendiente", ventas);
        Assert.Contains("Impacta caja", ventas);
        Assert.Contains("data-venta-filter", ventas);
    }

    [Fact]
    public void Movimientos_UsaColumnasEntraYSaleSeparadas()
    {
        var mov = Read("Views/Caja/_ConciliacionMovimientosTab.cshtml");
        Assert.Contains(">Entra<", mov);
        Assert.Contains(">Sale<", mov);
        Assert.Contains("data-mov-tipo", mov);
    }

    [Fact]
    public void Conciliacion_TieneLibroMayorSaldoEsperadoYConteoFisico()
    {
        var conc = Read("Views/Caja/_ConciliacionConciliacionTab.cshtml");
        Assert.Contains("Libro mayor de caja", conc);
        Assert.Contains("Saldo esperado", conc);
        Assert.Contains("Ventas sin impacto en caja", conc);
        Assert.Contains("Conteo físico", conc);
        Assert.Contains("Caja contada", conc);
        Assert.Contains("data-conteo", conc);
    }

    [Fact]
    public void Auditoria_EstaSeparadaEnSuPropioTab()
    {
        var aud = Read("Views/Caja/_ConciliacionAuditoriaTab.cshtml");
        Assert.Contains("Auditoría del turno", aud);
        Assert.Contains("Acción", aud);
        Assert.Contains("Entidad", aud);
    }

    [Fact]
    public void Partials_NoUsanHtmlRaw()
    {
        foreach (var f in new[]
        {
            "Views/Caja/_ConciliacionResumenTab.cshtml",
            "Views/Caja/_ConciliacionVentasTab.cshtml",
            "Views/Caja/_ConciliacionMovimientosTab.cshtml",
            "Views/Caja/_ConciliacionConciliacionTab.cshtml",
            "Views/Caja/_ConciliacionAuditoriaTab.cshtml",
        })
        {
            Assert.DoesNotContain("Html.Raw", Read(f));
        }
    }
}
