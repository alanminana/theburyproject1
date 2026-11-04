using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

public class DecimalParsingHelperTests
{
    [Fact]
    public void TryParseFlexibleDecimal_ConComaDecimal_ParseaCorrectamente()
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal("12,5", out var value);

        Assert.True(parsed);
        Assert.Equal(12.5m, value);
    }

    [Fact]
    public void TryParseFlexibleDecimal_ConPuntoDecimal_ParseaCorrectamente()
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal("12.5", out var value);

        Assert.True(parsed);
        Assert.Equal(12.5m, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseFlexibleDecimal_Vacio_DevuelveCero(string raw)
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal(raw, out var value);

        Assert.True(parsed);
        Assert.Equal(0m, value);
    }

    [Fact]
    public void TryParseFlexibleDecimal_Invalido_DevuelveFalse()
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal("abc", out var value);

        Assert.False(parsed);
        Assert.Equal(0m, value);
    }

    [Fact]
    public void TryParseFlexibleDecimal_FormatoMixtoEuropeo_ParseaCorrectamente()
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal(
            "1.234,56",
            out var value,
            allowMixedSeparators: true);

        Assert.True(parsed);
        Assert.Equal(1234.56m, value);
    }

    // H11: antes se "caracterizaba" que "1,234.56" valía 1,23456 (el normalizador asumía siempre el orden es-AR y borraba el
    // punto decimal): un importe 1000 veces menor sin aviso. Con ambos separadores el último es el decimal.
    [Theory]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("250,000.00", 250000.00)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("250.000,00", 250000.00)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("-1.234.567,5", -1234567.5)]
    public void TryParseFlexibleDecimal_Mixto_ElUltimoSeparadorEsElDecimal_Y_LosRepetidosSonMiles(string raw, double esperado)
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal(raw, out var value, allowMixedSeparators: true);

        Assert.True(parsed);
        Assert.Equal((decimal)esperado, value);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("12.34.567")]
    [InlineData("1,23,456")]
    public void TryParseFlexibleDecimal_SeparadoresRepetidosSinGruposDeTres_SeRechazan(string raw)
    {
        Assert.False(DecimalParsingHelper.TryParseFlexibleDecimal(raw, out _, allowMixedSeparators: true));
    }

    [Fact]
    public void TryParseFlexibleDecimal_ValorNegativo_ParseaCorrectamente()
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal("-1,5", out var value);

        Assert.True(parsed);
        Assert.Equal(-1.5m, value);
    }

    [Theory]
    [InlineData("1.500", true)]
    [InlineData("1,500", true)]
    [InlineData("12,345", true)]
    [InlineData("-1.500", true)]
    [InlineData("1.50", false)]
    [InlineData("1500", false)]
    [InlineData("0.500", false)]
    [InlineData("1.500,00", false)]
    [InlineData("1.500.000", false)]
    [InlineData("1234.567", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsMontoAmbiguo_SoloUnSeparadorConTresDigitosYEnteroNoCero(string? raw, bool esperado)
    {
        Assert.Equal(esperado, DecimalParsingHelper.EsMontoAmbiguo(raw));
    }
}
