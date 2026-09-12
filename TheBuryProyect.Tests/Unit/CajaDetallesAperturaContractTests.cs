using System.IO;
using Xunit;

namespace TheBuryProyect.Tests.Unit;

/// <summary>
/// Contrato de UI del rediseño del detalle de caja (apertura + cierre): cards
/// Vendido/Cobrado/Pendiente/Caja esperada, tabs, libro mayor con saldo acumulado,
/// conteo físico y la separación explícita "impacta caja física".
/// El contenido vive en partials compartidos (_Conciliacion*); por eso se leen varios archivos.
///
/// Evolución del contrato (fusión Movimientos → Conciliación): el tab "Movimientos" mostraba
/// exactamente el mismo array de movimientos que el libro mayor de "Conciliación" (ver
/// CajaConciliacionBuilder.BuildLibroMayor), solo sin la fila de apertura ni el saldo
/// acumulado — dos tabs con la misma tabla. Se fusionó en un solo tab (4 en vez de 5) sin
/// perder capacidad de filtrado: el libro mayor absorbió los filtros de tipo/medio/usuario
/// que antes eran exclusivos de "Movimientos".
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
    public void Tabs_TieneLasCuatroSecciones()
    {
        var tabs = Read("Views/Caja/_ConciliacionTabs.cshtml");
        Assert.Contains("data-cc-tab=\"resumen\"", tabs);
        Assert.Contains("data-cc-tab=\"ventas\"", tabs);
        Assert.Contains("data-cc-tab=\"conciliacion\"", tabs);
        Assert.Contains("data-cc-tab=\"auditoria\"", tabs);
        Assert.DoesNotContain("data-cc-tab=\"movimientos\"", tabs);
    }

    [Fact]
    public void Tabs_TienenAriaCompletoYRovingTabindex()
    {
        // §5 del estándar UI: id + aria-controls (tab → panel) + aria-labelledby (panel → tab) +
        // role="tabpanel" + roving tabindex, mismo patrón ya cerrado en Venta/Index.
        var tabs = Read("Views/Caja/_ConciliacionTabs.cshtml");
        Assert.Contains("aria-controls=\"cc-panel-resumen\"", tabs);
        Assert.Contains("role=\"tabpanel\"", tabs);
        Assert.Contains("aria-labelledby=\"cc-tab-resumen\"", tabs);
        Assert.Contains("tabindex=\"0\"", tabs);
        Assert.Contains("tabindex=\"-1\"", tabs);
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
    public void Ventas_AgrupaEfectivasYPendientesConEncabezadoPropio()
    {
        // Reapertura (auditoría 4 capas): una venta Confirmada/PendienteFinanciacion sin
        // facturar no debe competir en pie de igualdad con una Facturada en la misma lista sin
        // separación — ver docs/fase-kira-caja-detalle-ventas-ux.md y docs/ui/UI-REFACTOR-STATUS.md.
        var ventas = Read("Views/Caja/_ConciliacionVentasTab.cshtml");
        Assert.Contains("Ventas efectivas", ventas);
        Assert.Contains("Operaciones pendientes", ventas);
        Assert.Contains("data-venta-group=\"efectiva\"", ventas);
        Assert.Contains("data-venta-group=\"pendiente\"", ventas);
        Assert.Contains("data-venta-categoria", ventas);
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
    public void Conciliacion_LibroMayorUsaColumnasEntraYSaleYAbsorbeFiltrosDeMovimientos()
    {
        var conc = Read("Views/Caja/_ConciliacionConciliacionTab.cshtml");
        Assert.Contains(">Entra<", conc);
        Assert.Contains(">Sale<", conc);
        // Filtros heredados del extinto tab "Movimientos": dirección (antes data-mov-tipo),
        // medio y usuario (antes data-mov-filter="medio"/"usuario").
        Assert.Contains("data-lm-dir", conc);
        Assert.Contains("data-lm-filter=\"medio\"", conc);
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
            "Views/Caja/_ConciliacionConciliacionTab.cshtml",
            "Views/Caja/_ConciliacionAuditoriaTab.cshtml",
        })
        {
            Assert.DoesNotContain("Html.Raw", Read(f));
        }
    }

    [Fact]
    public void RegistrarMovimiento_ConceptoTieneOpcionesServerRenderizadas()
    {
        // El <select> de Concepto dependía 100% de JS para tener <option>s: un fallo de carga
        // dejaba un campo obligatorio vacío. Ahora las 2 optgroups (Ingresos/Egresos) están
        // siempre en el HTML servido; el JS solo oculta la que no corresponde al tipo activo.
        var view = Read("Views/Caja/RegistrarMovimiento_tw.cshtml");
        Assert.Contains("data-concepto-group=\"0\"", view);
        Assert.Contains("data-concepto-group=\"1\"", view);
        Assert.Contains("<option value=\"0\">", view);
        Assert.Contains("<option value=\"99\">", view);
    }
}
