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

    [Fact]
    public void Cuit_OnceDigitos_NoExigeDigitoVerificadorAfip()
    {
        var model = new ProveedorViewModel
        {
            Cuit = "30714421092",
            RazonSocial = "Proveedor Test"
        };

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
