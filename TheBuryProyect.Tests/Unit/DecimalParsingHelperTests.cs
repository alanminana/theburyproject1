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

    [Fact]
    public void TryParseFlexibleDecimal_FormatoMixtoIngles_CaracterizaComportamientoActual()
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal(
            "1,234.56",
            out var value,
            allowMixedSeparators: true);

        Assert.True(parsed);
        Assert.Equal(1.23456m, value);
    }

    [Fact]
    public void TryParseFlexibleDecimal_ValorNegativo_ParseaCorrectamente()
    {
        var parsed = DecimalParsingHelper.TryParseFlexibleDecimal("-1,5", out var value);

        Assert.True(parsed);
        Assert.Equal(-1.5m, value);
    }
}
