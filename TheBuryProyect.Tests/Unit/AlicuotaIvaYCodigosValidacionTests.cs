using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>VALID-L3: alícuotas de IVA argentinas y formato de código en los ViewModels que crean productos.</summary>
public class AlicuotaIvaYCodigosValidacionTests
{
    private static bool Falla(object vm, string propiedad)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(vm, new ValidationContext(vm), resultados, validateAllProperties: true);
        return resultados.Any(r => r.MemberNames.Contains(propiedad));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2.5, false)]
    [InlineData(5, false)]
    [InlineData(10.5, false)]
    [InlineData(21, false)]
    [InlineData(27, false)]
    [InlineData(19, true)]
    [InlineData(33, true)]
    [InlineData(-1, true)]
    [InlineData(100, true)]
    public void MercadoLibre_CrearProducto_Iva_SoloAlicuotasArgentinas(double iva, bool invalido)
    {
        var vm = new MercadoLibreCrearProductoViewModel { PorcentajeIVA = (decimal)iva };
        Assert.Equal(invalido, Falla(vm, nameof(vm.PorcentajeIVA)));
    }

    [Theory]
    [InlineData("A/B", true)]
    [InlineData("<script>", true)]
    [InlineData("-X", true)]
    [InlineData("CAT-01", false)]
    [InlineData("001002", false)]
    [InlineData("cat-0099", false)]
    public void MercadoLibre_CrearProducto_Codigo_Formato(string codigo, bool invalido)
    {
        var vm = new MercadoLibreCrearProductoViewModel { Codigo = codigo };
        Assert.Equal(invalido, Falla(vm, nameof(vm.Codigo)));
    }

    [Theory]
    [InlineData(0, true)]          // un producto a $0 no se puede vender (precio unitario mínimo 0,01)
    [InlineData(0.01, false)]
    [InlineData(20_000_000, false)]
    [InlineData(20_000_001, true)]
    [InlineData(-5, true)]
    public void MercadoLibre_CrearProducto_PrecioVenta_Rango(double precio, bool invalido)
    {
        var vm = new MercadoLibreCrearProductoViewModel { PrecioVenta = (decimal)precio };
        Assert.Equal(invalido, Falla(vm, nameof(vm.PrecioVenta)));
    }

    [Theory]
    [InlineData("CAT-01", false)]
    [InlineData("SINGER", false)]
    [InlineData("001002", false)]
    [InlineData("A/B", true)]
    [InlineData("<b>", true)]
    [InlineData("ñ", true)]
    public void Categoria_Y_Marca_Codigo_Formato(string codigo, bool invalido)
    {
        Assert.Equal(invalido, Falla(new CategoriaViewModel { Codigo = codigo }, nameof(CategoriaViewModel.Codigo)));
        Assert.Equal(invalido, Falla(new MarcaViewModel { Codigo = codigo }, nameof(MarcaViewModel.Codigo)));
    }

    [Fact]
    public void MontoArgentino_ConMinimoFraccionario_MuestraElMinimoRealEnElMensaje()
    {
        var atributo = new TheBuryProject.Validation.MontoArgentinoAttribute { Minimo = 0.01 };

        var mensaje = atributo.FormatErrorMessage("Precio");

        Assert.Contains("0,01", mensaje);
    }

    [Fact]
    public void MontoArgentino_ConMinimoEntero_SigueMostrandoCero()
    {
        var mensaje = new TheBuryProject.Validation.MontoArgentinoAttribute().FormatErrorMessage("Monto");

        Assert.Contains("entre 0 y", mensaje);
    }
}
