using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class VentaDetailsUiContractTests
{
    [Fact]
    public void DetailsView_MuestraAccionPrepararVentaParaCotizacion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Model.PuedePrepararVenta", view);
        Assert.Contains("asp-action=\"PrepararVenta\"", view);
        Assert.Contains("Preparar venta", view);
    }

    [Fact]
    public void VentaViewModel_CotizacionPuedePrepararPeroNoFacturar()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Cotizacion };

        Assert.True(venta.PuedePrepararVenta);
        Assert.False(venta.PuedeConfirmar);
        Assert.False(venta.PuedeFacturar);
    }

    [Fact]
    public void VentaViewModel_PresupuestoPuedeConfirmarPeroNoPreparar()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Presupuesto };

        Assert.False(venta.PuedePrepararVenta);
        Assert.True(venta.PuedeConfirmar);
        Assert.False(venta.PuedeFacturar);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "TheBuryProyect.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new InvalidOperationException("No se encontro raiz del repo.");
    }
}
