using TheBuryProject.Helpers;

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
}
