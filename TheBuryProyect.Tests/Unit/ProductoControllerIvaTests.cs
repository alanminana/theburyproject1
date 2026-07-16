using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class ProductoControllerIvaTests
{
    [Fact]
    public void AplicarIVA_ConPrecioSinIva100EIva21_Devuelve121()
    {
        var precioFinal = PrecioIvaCalculator.AplicarIVA(100m, 21m);

        Assert.Equal(121m, precioFinal);
    }

    [Fact]
    public void AplicarIVA_ConPrecioSinIva100EIva105_Devuelve11050()
    {
        var precioFinal = PrecioIvaCalculator.AplicarIVA(100m, 10.5m);

        Assert.Equal(110.50m, precioFinal);
    }

    [Fact]
    public void AplicarIVA_ConIvaCero_MantienePrecio()
    {
        var precioFinal = PrecioIvaCalculator.AplicarIVA(100m, 0m);

        Assert.Equal(100m, precioFinal);
    }

    [Fact]
    public void QuitarIVA_ConIvaCero_NoDivideYDevuelvePrecio()
    {
        var precioSinIva = PrecioIvaCalculator.QuitarIVA(100m, 0m);

        Assert.Equal(100m, precioSinIva);
    }

    [Fact]
    public void QuitarIVA_ConIva21_DevuelvePrecioSinIva()
    {
        var precioSinIva = PrecioIvaCalculator.QuitarIVA(121m, 21m);

        Assert.Equal(100m, precioSinIva);
    }

    [Fact]
    public void QuitarIVA_ConIva105_DevuelvePrecioSinIva()
    {
        var precioSinIva = PrecioIvaCalculator.QuitarIVA(110.50m, 10.5m);

        Assert.Equal(100m, precioSinIva);
    }

    // Desglose: el IVA incluido se calcula como diferencia para que neto + IVA = total.

    [Fact]
    public void CalcularIvaIncluido_Con100000EIva21_DesglosaSinModificarTotal()
    {
        var neto = PrecioIvaCalculator.QuitarIVA(100_000m, 21m);
        var iva = PrecioIvaCalculator.CalcularIvaIncluido(100_000m, 21m);

        Assert.Equal(82_644.63m, neto);
        Assert.Equal(17_355.37m, iva);
        Assert.Equal(100_000m, neto + iva);
    }

    [Fact]
    public void CalcularIvaIncluido_Con10404160EIva21_DesglosaVentaSinModificarTotal()
    {
        var neto = PrecioIvaCalculator.QuitarIVA(104_041.60m, 21m);
        var iva = PrecioIvaCalculator.CalcularIvaIncluido(104_041.60m, 21m);

        Assert.Equal(85_984.79m, neto);
        Assert.Equal(18_056.81m, iva);
        Assert.Equal(104_041.60m, neto + iva);
    }

    [Fact]
    public void CalcularIvaIncluido_ConIvaCero_DevuelveCero()
    {
        var iva = PrecioIvaCalculator.CalcularIvaIncluido(100m, 0m);

        Assert.Equal(0m, iva);
    }

    // Los inputs type="number" postean con punto decimal; en servidores con cultura
    // es-AR el binding por defecto lo interpreta como separador de miles (x100).
    // Estos campos deben usar DecimalModelBinder (parseo invariante flexible).

    [Theory]
    [InlineData(nameof(ProductoViewModel.PrecioCompra))]
    [InlineData(nameof(ProductoViewModel.PrecioVenta))]
    [InlineData(nameof(ProductoViewModel.PorcentajeIVA))]
    public void ProductoViewModel_CamposDePrecioEIva_UsanDecimalModelBinder(string propiedad)
    {
        var attribute = typeof(ProductoViewModel).GetProperty(propiedad)?
            .GetCustomAttributes(typeof(ModelBinderAttribute), inherit: false)
            .Cast<ModelBinderAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(DecimalModelBinder), attribute!.BinderType);
    }
}
