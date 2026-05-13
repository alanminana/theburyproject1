namespace TheBuryProject.Tests.Unit;

public class VentaEditUiContractTests
{
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
