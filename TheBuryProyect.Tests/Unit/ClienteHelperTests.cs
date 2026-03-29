using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para ClienteHelper.
/// Función pura — no requiere base de datos ni infraestructura.
/// Cubre CalcularEdad con fechas reales.
/// </summary>
public class ClienteHelperTests
{
    [Fact]
    public void CalcularEdad_FechaNula_RetornaNull()
    {
        var resultado = ClienteHelper.CalcularEdad(null);
        Assert.Null(resultado);
    }

    [Fact]
    public void CalcularEdad_CumpleaniosYaPaso_RetornaEdadCorrecta()
    {
        // Use a fixed reference point: born 30 years ago (birthday already passed)
        var hoy = DateTime.Today;
        var nacimiento = hoy.AddYears(-30).AddDays(-1); // birthday was yesterday

        var edad = ClienteHelper.CalcularEdad(nacimiento);

        Assert.Equal(30, edad);
    }

    [Fact]
    public void CalcularEdad_CumpleaniosMañana_RetornaEdadMenos1()
    {
        var hoy = DateTime.Today;
        var nacimiento = hoy.AddYears(-30).AddDays(1); // birthday is tomorrow

        var edad = ClienteHelper.CalcularEdad(nacimiento);

        Assert.Equal(29, edad);
    }

    [Fact]
    public void CalcularEdad_CumpleaniosHoy_RetornaEdadCorrecta()
    {
        var hoy = DateTime.Today;
        var nacimiento = hoy.AddYears(-25); // birthday is today

        var edad = ClienteHelper.CalcularEdad(nacimiento);

        Assert.Equal(25, edad);
    }

    [Fact]
    public void CalcularEdad_RecienNacido_RetornaCero()
    {
        var nacimiento = DateTime.Today;

        var edad = ClienteHelper.CalcularEdad(nacimiento);

        Assert.Equal(0, edad);
    }
}
