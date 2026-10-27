using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class ProveedorViewModelValidationTests
{
    private static List<ValidationResult> Validate(ProveedorViewModel model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    // VALID-L4 (cambio de criterio): antes este test exigía aceptar un CUIT con verificador incorrecto
    // ("30714421092"). Todo CUIT real cumple el módulo 11 (los proveedores reales de la base lo cumplen), así que un
    // verificador erróneo es un error de tipeo y el mensaje indica el dígito correcto. El prefijo no se restringe.
    [Fact]
    public void Cuit_OnceDigitos_ConVerificadorValido_SeAcepta()
    {
        var model = new ProveedorViewModel
        {
            Cuit = "30714421099",
            RazonSocial = "Proveedor Test"
        };

        var results = Validate(model);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(ProveedorViewModel.Cuit)));
    }

    [Fact]
    public void Cuit_OnceDigitos_ConVerificadorIncorrecto_SeRechaza_IndicandoElCorrecto()
    {
        var model = new ProveedorViewModel
        {
            Cuit = "30714421092",
            RazonSocial = "Proveedor Test"
        };

        var results = Validate(model);

        var error = Assert.Single(results, r => r.MemberNames.Contains(nameof(ProveedorViewModel.Cuit)));
        Assert.Contains("9", error.ErrorMessage);
    }

    [Fact]
    public void Cuit_ConPrefijoDeProveedorDelExterior_NoSeRestringePorPrefijo()
    {
        // 55 es un prefijo de entidades del exterior; mientras el verificador cierre, se acepta.
        var model = new ProveedorViewModel { Cuit = "55000000009", RazonSocial = "Proveedor Exterior" };

        var results = Validate(model);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(ProveedorViewModel.Cuit)));
    }

    [Theory]
    [InlineData("3071442109")]
    [InlineData("307144210921")]
    [InlineData("3071442109A")]
    [InlineData("30-71442109-2")]
    public void Cuit_FormatoNoNumericoOConLongitudIncorrecta_Invalido(string cuit)
    {
        var model = new ProveedorViewModel
        {
            Cuit = cuit,
            RazonSocial = "Proveedor Test"
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(ProveedorViewModel.Cuit)));
    }
}
