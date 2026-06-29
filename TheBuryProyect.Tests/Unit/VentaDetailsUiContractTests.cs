using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class VentaDetailsUiContractTests
{
    [Fact]
    public void DetailsView_MantieneAccionPrepararVentaParaCreditoPersonal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Model.PuedePrepararVenta", view);
        Assert.Contains("asp-action=\"PrepararVenta\"", view);
        Assert.Contains("Preparar venta", view);
    }

    [Fact]
    public void DetailsView_OfreceConfirmarYFacturarEnUnPaso()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Model.PuedeConfirmarYFacturar", view);
        Assert.Contains("asp-action=\"ConfirmarYFacturar\"", view);
    }

    [Fact]
    public void VentaViewModel_CotizacionSinCreditoConfirmaDirectoSinPreparar()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.Efectivo };

        Assert.False(venta.PuedePrepararVenta);
        Assert.True(venta.PuedeConfirmar);
        Assert.True(venta.PuedeConfirmarYFacturar);
        Assert.False(venta.PuedeFacturar);
    }

    [Fact]
    public void VentaViewModel_CotizacionCreditoPersonalConservaPrepararVenta()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.CreditoPersonal };

        Assert.True(venta.PuedePrepararVenta);
        Assert.False(venta.PuedeConfirmar);
        Assert.False(venta.PuedeConfirmarYFacturar);
    }

    [Fact]
    public void VentaViewModel_PresupuestoPuedeConfirmarPeroNoPreparar()
    {
        var venta = new VentaViewModel { Estado = EstadoVenta.Presupuesto, TipoPago = TipoPago.Efectivo };

        Assert.False(venta.PuedePrepararVenta);
        Assert.True(venta.PuedeConfirmar);
        Assert.True(venta.PuedeConfirmarYFacturar);
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
