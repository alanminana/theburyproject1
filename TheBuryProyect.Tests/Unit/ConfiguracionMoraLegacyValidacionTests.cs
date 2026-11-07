using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H-29: el endpoint legacy POST /Mora/GuardarConfiguracion persistía días de gracia y recargos negativos o fuera de rango
/// (el formulario expandido sí los rechaza). Ahora comparte los mismos rangos.
/// </summary>
public class ConfiguracionMoraLegacyValidacionTests
{
    private static bool Falla(ConfiguracionMoraViewModel vm, string propiedad)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(vm, new ValidationContext(vm), resultados, validateAllProperties: true);
        return resultados.Any(r => r.MemberNames.Contains(propiedad));
    }

    [Theory]
    [InlineData(-5, true)]
    [InlineData(91, true)]
    [InlineData(0, false)]
    [InlineData(10, false)]
    [InlineData(90, false)]
    public void DiasGracia_FueraDeRango_Falla(int dias, bool falla) =>
        Assert.Equal(falla, Falla(new ConfiguracionMoraViewModel { DiasGracia = dias }, nameof(ConfiguracionMoraViewModel.DiasGracia)));

    [Theory]
    [InlineData("-9", true)]
    [InlineData("100.01", true)]
    [InlineData("0", false)]
    [InlineData("5", false)]
    [InlineData("100", false)]
    public void PorcentajeRecargo_FueraDeRango_Falla(string valor, bool falla) =>
        Assert.Equal(falla, Falla(new ConfiguracionMoraViewModel { PorcentajeRecargo = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture) },
            nameof(ConfiguracionMoraViewModel.PorcentajeRecargo)));
}
