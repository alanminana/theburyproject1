using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para CuilHelper: armado (Componer) y desarmado (Descomponer) del CUIL
/// en sus partes XX-DNI-X. Funciones puras — no requieren base de datos.
/// </summary>
public class CuilHelperTests
{
    // ---------- Componer ----------

    [Fact]
    public void Componer_PartesVacias_ConDni_CompletaConCeros()
    {
        // __-12345678-_  →  00-12345678-0
        Assert.Equal("00123456780", CuilHelper.Componer(null, "12345678", null));
        Assert.Equal("00123456780", CuilHelper.Componer("", "12345678", ""));
    }

    [Fact]
    public void Componer_PartesCompletas_ArmaCuil()
    {
        Assert.Equal("20123456783", CuilHelper.Componer("20", "12345678", "3"));
        Assert.Equal("27123456789", CuilHelper.Componer("27", "12345678", "9"));
    }

    [Fact]
    public void Componer_PrefijoDeUnDigito_RellenaConCeroAdelante()
    {
        // "2" → "02"
        Assert.Equal("02123456780", CuilHelper.Componer("2", "12345678", ""));
    }

    [Fact]
    public void Componer_IgnoraGuionesYSimbolos()
    {
        Assert.Equal("20123456783", CuilHelper.Componer("2-0", "12.345.678", "-3"));
    }

    [Theory]
    [InlineData("2033")]   // prefijo con más de 2 dígitos → toma los primeros 2
    public void Componer_PrefijoLargo_TomaPrimerosDosDigitos(string prefijo)
    {
        Assert.Equal("20123456780", CuilHelper.Componer(prefijo, "12345678", null));
    }

    [Fact]
    public void Componer_VerificadorConVariosDigitos_TomaElPrimero()
    {
        Assert.Equal("20123456783", CuilHelper.Componer("20", "12345678", "34"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]        // menos de 8
    [InlineData("123456789")]  // más de 8
    [InlineData("abcdefgh")]   // sin dígitos
    public void Componer_SinDniValido_DevuelveNull(string? numeroDocumento)
    {
        Assert.Null(CuilHelper.Componer("20", numeroDocumento, "3"));
    }

    // ---------- Descomponer ----------

    [Fact]
    public void Descomponer_CuilDe11Digitos_SeparaPrefijoYVerificador()
    {
        var (prefijo, verificador) = CuilHelper.Descomponer("27123456784");
        Assert.Equal("27", prefijo);
        Assert.Equal("4", verificador);
    }

    [Fact]
    public void Descomponer_AceptaGuiones()
    {
        var (prefijo, verificador) = CuilHelper.Descomponer("20-12345678-3");
        Assert.Equal("20", prefijo);
        Assert.Equal("3", verificador);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2012345678")]    // 10 dígitos
    [InlineData("201234567890")]  // 12 dígitos
    public void Descomponer_ValorInvalido_DevuelveNulls(string? cuil)
    {
        var (prefijo, verificador) = CuilHelper.Descomponer(cuil);
        Assert.Null(prefijo);
        Assert.Null(verificador);
    }

    [Fact]
    public void ComponerDescomponer_Roundtrip()
    {
        var original = "23123456781";
        var (prefijo, verificador) = CuilHelper.Descomponer(original);
        Assert.Equal(original, CuilHelper.Componer(prefijo, "12345678", verificador));
    }
}
