using TheBuryProject.Controllers;

namespace TheBuryProject.Tests.Unit;

public class ProductoControllerIvaTests
{
    [Fact]
    public void AplicarIVA_ConPrecioSinIva100EIva21_Devuelve121()
    {
        var precioFinal = ProductoController.AplicarIVA(100m, 21m);

        Assert.Equal(121m, precioFinal);
    }

    [Fact]
    public void AplicarIVA_ConPrecioSinIva100EIva105_Devuelve11050()
    {
        var precioFinal = ProductoController.AplicarIVA(100m, 10.5m);

        Assert.Equal(110.50m, precioFinal);
    }

    [Fact]
    public void AplicarIVA_ConIvaCero_MantienePrecio()
    {
        var precioFinal = ProductoController.AplicarIVA(100m, 0m);

        Assert.Equal(100m, precioFinal);
    }

    [Fact]
    public void QuitarIVA_ConIvaCero_NoDivideYDevuelvePrecio()
    {
        var precioSinIva = ProductoController.QuitarIVA(100m, 0m);

        Assert.Equal(100m, precioSinIva);
    }
}
