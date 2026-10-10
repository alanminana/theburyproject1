using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contrato de normalización de campos (Argentina) sobre los ViewModels reales: nombres,
/// DNI de 7 u 8 dígitos y montos con tope de 20.000.000. Valida con DataAnnotations, igual
/// que el model binding del servidor.
/// </summary>
public class NormalizacionCamposViewModelsTests
{
    private static List<ValidationResult> Validar(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static bool Falla(object model, string campo) =>
        Validar(model).Any(r => r.MemberNames.Contains(campo));

    private static ClienteViewModel ClienteValido() => new()
    {
        TipoDocumento = "DNI",
        NumeroDocumento = "12345678",
        Apellido = "Pérez",
        Nombre = "Juan Carlos",
    };

    [Fact]
    public void Cliente_NombreCompuestoYDniDeSieteDigitos_EsValido()
    {
        var vm = ClienteValido();
        vm.Apellido = "Pérez-Gómez";
        vm.NumeroDocumento = "7654321";

        Assert.False(Falla(vm, nameof(vm.Apellido)));
        Assert.False(Falla(vm, nameof(vm.Nombre)));
        Assert.False(Falla(vm, nameof(vm.NumeroDocumento)));
    }

    [Theory]
    [InlineData("Maximiliano")]            // 11 letras
    [InlineData("Juan Carlos Maria Jose")] // 4 palabras
    [InlineData("Juan3")]                  // número
    [InlineData("Juan@")]                  // símbolo
    public void Cliente_NombreInvalido(string valor)
    {
        var vm = ClienteValido();
        vm.Nombre = valor;
        Assert.True(Falla(vm, nameof(vm.Nombre)));
    }

    [Theory]
    [InlineData("123456")]    // 6 dígitos
    [InlineData("123456789")] // 9 dígitos
    [InlineData("1234567a")]  // letra
    public void Cliente_DniInvalido(string valor)
    {
        var vm = ClienteValido();
        vm.NumeroDocumento = valor;
        Assert.True(Falla(vm, nameof(vm.NumeroDocumento)));
    }

    [Fact]
    public void Cliente_SueldoYMontosPersonalizados_TopeVeintiMillones()
    {
        var vm = ClienteValido();
        vm.Sueldo = 20_000_000m;
        vm.ConyugeSueldo = 20_000_000.01m;
        vm.MontoMinimoPersonalizado = 25_000_000m;

        Assert.False(Falla(vm, nameof(vm.Sueldo)));
        Assert.True(Falla(vm, nameof(vm.ConyugeSueldo)));
        Assert.True(Falla(vm, nameof(vm.MontoMinimoPersonalizado)));
    }

    [Fact]
    public void Cliente_LocalidadConNumeros_Invalida_YNombreLargoDeCiudad_Valido()
    {
        var vm = ClienteValido();
        vm.Localidad = "Presidencia Roque Sáenz Peña";
        Assert.False(Falla(vm, nameof(vm.Localidad)));

        vm.Localidad = "Córdoba 123";
        Assert.True(Falla(vm, nameof(vm.Localidad)));
    }

    [Fact]
    public void Producto_PreciosFueraDeRango_Invalidos()
    {
        var vm = new ProductoViewModel
        {
            PrecioCompra = 25_000_000m,
            PrecioVenta = 19_999_999.99m,
            CostoEnvio = -1m,
        };

        Assert.True(Falla(vm, nameof(vm.PrecioCompra)));
        Assert.False(Falla(vm, nameof(vm.PrecioVenta)));
        Assert.True(Falla(vm, nameof(vm.CostoEnvio)));
    }

    [Fact]
    public void VentaDetalle_PrecioUnitario_MinimoCeroUno_Y_Tope()
    {
        var vm = new VentaDetalleViewModel { PrecioUnitario = 0m };
        Assert.True(Falla(vm, nameof(vm.PrecioUnitario)));

        vm.PrecioUnitario = 20_000_001m;
        Assert.True(Falla(vm, nameof(vm.PrecioUnitario)));

        vm.PrecioUnitario = 1500.50m;
        Assert.False(Falla(vm, nameof(vm.PrecioUnitario)));
    }

    [Fact]
    public void VentaEnvio_TelefonoCodigoPostalYLocalidad()
    {
        var vm = new VentaEnvioViewModel
        {
            Destinatario = "Cualquier tercero - portería",
            Domicilio = "Calle 123",
            Telefono = "abc",
            CodigoPostal = "12",
            Localidad = "Córdoba 5",
            CostoEnvio = 20_000_001m,
        };

        Assert.True(Falla(vm, nameof(vm.Telefono)));
        Assert.True(Falla(vm, nameof(vm.CodigoPostal)));
        Assert.True(Falla(vm, nameof(vm.Localidad)));
        Assert.True(Falla(vm, nameof(vm.CostoEnvio)));
        // El destinatario puede ser un tercero: no se restringe a letras.
        Assert.False(Falla(vm, nameof(vm.Destinatario)));

        vm.Telefono = "11 4567-8901";
        vm.CodigoPostal = "C1234ABC";
        vm.Localidad = "San Miguel de Tucumán";
        vm.CostoEnvio = 15_000m;
        Assert.False(Falla(vm, nameof(vm.Telefono)));
        Assert.False(Falla(vm, nameof(vm.CodigoPostal)));
        Assert.False(Falla(vm, nameof(vm.Localidad)));
        Assert.False(Falla(vm, nameof(vm.CostoEnvio)));
    }

    [Fact]
    public void PlantillaContrato_DniVendedor_SieteOOchoDigitos()
    {
        var vm = new PlantillaContratoCreditoViewModel { DniVendedor = "1234567" };
        Assert.False(Falla(vm, nameof(vm.DniVendedor)));

        vm.DniVendedor = "123";
        Assert.True(Falla(vm, nameof(vm.DniVendedor)));
    }

    [Fact]
    public void Caja_MontosConTopeYMinimo()
    {
        var apertura = new AbrirCajaViewModel { MontoInicial = 20_000_001m };
        Assert.True(Falla(apertura, nameof(apertura.MontoInicial)));

        var movimiento = new MovimientoCajaViewModel { Monto = 0m };
        Assert.True(Falla(movimiento, nameof(movimiento.Monto)));
    }
}
