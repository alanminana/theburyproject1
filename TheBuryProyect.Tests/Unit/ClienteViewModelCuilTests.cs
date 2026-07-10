using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests del CUIL parcialmente editable en ClienteViewModel: CuilCuit se arma desde el
/// prefijo + DNI (NumeroDocumento) + verificador, y al asignarlo se descompone en sus partes.
/// </summary>
public class ClienteViewModelCuilTests
{
    private static IList<ValidationResult> ValidarPropiedad(ClienteViewModel vm, string propiedad, object? valor)
    {
        var resultados = new List<ValidationResult>();
        var ctx = new ValidationContext(vm) { MemberName = propiedad };
        Validator.TryValidateProperty(valor, ctx, resultados);
        return resultados;
    }

    [Fact]
    public void CuilCuit_PartesVacias_ConDni_SeArmaConCeros()
    {
        var vm = new ClienteViewModel { NumeroDocumento = "12345678" };

        // __-12345678-_  →  00-12345678-0
        Assert.Equal("00123456780", vm.CuilCuit);
    }

    [Fact]
    public void CuilCuit_PartesCargadas_SeArmaCompleto()
    {
        var vm = new ClienteViewModel
        {
            NumeroDocumento = "12345678",
            CuilPrefijo = "20",
            CuilVerificador = "3"
        };

        Assert.Equal("20123456783", vm.CuilCuit);
    }

    [Fact]
    public void CuilCuit_SinDni_EsNull()
    {
        var vm = new ClienteViewModel
        {
            NumeroDocumento = "",
            CuilPrefijo = "20",
            CuilVerificador = "3"
        };

        Assert.Null(vm.CuilCuit);
    }

    [Fact]
    public void SetCuilCuit_DescomponeEnPartes()
    {
        var vm = new ClienteViewModel { NumeroDocumento = "12345678" };

        vm.CuilCuit = "27123456784";

        Assert.Equal("27", vm.CuilPrefijo);
        Assert.Equal("4", vm.CuilVerificador);
    }

    [Fact]
    public void PartesEditables_NormalizanADigitos()
    {
        var vm = new ClienteViewModel
        {
            NumeroDocumento = "12345678",
            CuilPrefijo = "2-0",
            CuilVerificador = "x3"
        };

        Assert.Equal("20", vm.CuilPrefijo);
        Assert.Equal("3", vm.CuilVerificador);
        Assert.Equal("20123456783", vm.CuilCuit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("20")]
    public void CuilPrefijo_HastaDosDigitos_EsValido(string? prefijo)
    {
        var vm = new ClienteViewModel { NumeroDocumento = "12345678", CuilPrefijo = prefijo };
        Assert.Empty(ValidarPropiedad(vm, nameof(ClienteViewModel.CuilPrefijo), vm.CuilPrefijo));
    }

    [Fact]
    public void CuilPrefijo_TresDigitos_EsInvalido()
    {
        var vm = new ClienteViewModel { NumeroDocumento = "12345678" };
        // Se fuerza un valor de 3 dígitos evitando el normalizador de la propiedad.
        Assert.NotEmpty(ValidarPropiedad(vm, nameof(ClienteViewModel.CuilPrefijo), "203"));
    }

    [Fact]
    public void CuilVerificador_DosDigitos_EsInvalido()
    {
        var vm = new ClienteViewModel { NumeroDocumento = "12345678" };
        Assert.NotEmpty(ValidarPropiedad(vm, nameof(ClienteViewModel.CuilVerificador), "34"));
    }
}
