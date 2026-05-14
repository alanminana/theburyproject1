namespace TheBuryProject.Tests.Unit;

public class VentaEditUiContractTests
{
    // ── Trazabilidad individual (Fase 8.2.S) ─────────────────────────────

    [Fact]
    public void EditView_TienePanelSelectorUnidad()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("id=\"panel-selector-unidad\"", view);
        Assert.Contains("id=\"select-producto-unidad\"", view);
    }

    [Fact]
    public void EditView_TieneAvisoSinUnidades()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("id=\"aviso-sin-unidades\"", view);
    }

    [Fact]
    public void EditView_TieneLinkGestionarUnidades()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("id=\"link-gestionar-unidades\"", view);
    }

    [Fact]
    public void EditView_TieneScriptSeedVentaInicial()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("window.ventaInicial", view);
        Assert.Contains("ventaInicialJson", view);
    }

    [Fact]
    public void EditView_TieneContenedorDetallesHiddenInputs()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("id=\"detalles-hidden-inputs\"", view);
    }

    [Fact]
    public void VentaCreateJs_TieneSeedVentaInicial()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("window.ventaInicial", js);
        Assert.Contains("ventaInicial.detalles", js);
    }

    [Fact]
    public void VentaCreateJs_SeedPreservaProductoUnidadId()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("productoUnidadId: d.productoUnidadId", js);
        Assert.Contains("requiereNumeroSerie: !!d.requiereNumeroSerie", js);
    }

    [Fact]
    public void VentaCreateJs_RenderDetallesMuestraEtiquetaUnidad()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("requiereNumeroSerie", js);
        Assert.Contains("productoUnidadLabel", js);
    }

    [Fact]
    public void EditView_TieneHiddenInputsParaDatosTarjetaGlobal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("name=\"DatosTarjeta.ConfiguracionTarjetaId\"", view);
        Assert.Contains("name=\"DatosTarjeta.NombreTarjeta\"", view);
        Assert.Contains("id=\"hdn-tarjeta-nombre\"", view);
        Assert.Contains("name=\"DatosTarjeta.TipoTarjeta\"", view);
        Assert.Contains("id=\"hdn-tarjeta-tipo\"", view);
        Assert.Contains("name=\"DatosTarjeta.ConfiguracionPagoPlanId\"", view);
        Assert.Contains("id=\"hdn-configuracion-pago-plan-id\"", view);
    }

    [Fact]
    public void EditView_MercadoPagoPuedeRenderizarSelectorDePlanesGlobales()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("id=\"configuracion-pagos-global-estado\"", view);
        Assert.Contains("id=\"panel-planes-pago\"", view);
        Assert.Contains("id=\"lista-planes-pago\"", view);
        Assert.DoesNotContain("name=\"DatosTarjeta.ProductoCondicionPagoPlanId\"", view);
        Assert.DoesNotContain("id=\"modal-pago-item\"", view);
        Assert.DoesNotContain("btn-configurar-pago-item", view);
    }

    [Fact]
    public void EditView_ReutilizaVentaCreateJs()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("<script src=\"~/js/venta-create.js\"", view);
    }

    [Fact]
    public void VentaViewBagBuilder_PuedeMostrarTarjetaHistoricaSinOfrecerlaParaNuevasVentas()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Helpers", "VentaViewBagBuilder.cs"));

        Assert.Contains("tipoPagoSeleccionado != TipoPago.Tarjeta", source);
        Assert.Contains("Tarjeta (historico)", source);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
