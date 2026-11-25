using TheBuryProject.Helpers;
using Xunit;

namespace TheBuryProject.Tests.Unit;

/// <summary>Reparto del IVA de una NC fiscal externa entre alícuotas (neteo informativo; criterio interno del propietario 2026-11-25).</summary>
public class IvaNotaCreditoFiscalTests
{
    [Fact]
    public void AlicuotasMixtas_SumaExactaYNetoMasIvaIgualTotal()
    {
        var partes = IvaNotaCreditoFiscal.Repartir(1545.00m, new[] { (21m, 1030m), (10.5m, 515m) });

        Assert.Equal(1545.00m, partes.Sum(p => p.Total));
        Assert.All(partes, p => Assert.Equal(p.Total, p.Neto + p.Iva));
        var a21 = partes.Single(p => p.Porcentaje == 21m);
        Assert.Equal((851.24m, 178.76m), (a21.Neto, a21.Iva));
    }

    [Theory]
    [InlineData("100.01")]
    [InlineData("0.01")]
    [InlineData("999.99")]
    [InlineData("1000.50")]
    [InlineData("12345.67")]
    public void Redondeos_TresAlicuotasIgualesNuncaPierdenNiInventanCentavos(string importeTxt)
    {
        var importe = decimal.Parse(importeTxt, System.Globalization.CultureInfo.InvariantCulture);
        var partes = IvaNotaCreditoFiscal.Repartir(importe, new[] { (21m, 1m), (10.5m, 1m), (0m, 1m) });

        Assert.Equal(importe, partes.Sum(p => p.Total));
        Assert.All(partes, p => Assert.Equal(p.Total, p.Neto + p.Iva));
        Assert.All(partes, p => Assert.True(p.Iva >= 0m && p.Neto >= 0m));
    }

    [Fact]
    public void AlicuotaCero_NoGeneraIva()
    {
        var partes = IvaNotaCreditoFiscal.Repartir(500m, new[] { (0m, 10m) });

        var unica = Assert.Single(partes);
        Assert.Equal((500m, 0m), (unica.Neto, unica.Iva));
    }

    [Fact]
    public void MismaAlicuotaEnVariasLineas_SeAgrupa()
    {
        var partes = IvaNotaCreditoFiscal.Repartir(242m, new[] { (21m, 100m), (21m, 50m) });

        var unica = Assert.Single(partes);
        Assert.Equal((200m, 42m, 242m), (unica.Neto, unica.Iva, unica.Total));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void ImporteNoPositivo_NoRepartePor(string importeTxt)
    {
        Assert.Empty(IvaNotaCreditoFiscal.Repartir(decimal.Parse(importeTxt), new[] { (21m, 1m) }));
    }

    [Fact]
    public void SinPesosPositivos_NoInventaUnDesglose()
    {
        Assert.Empty(IvaNotaCreditoFiscal.Repartir(100m, new[] { (21m, 0m) }));
        Assert.Empty(IvaNotaCreditoFiscal.Repartir(100m, Array.Empty<(decimal, decimal)>()));
    }

    [Fact]
    public void ImporteChicoConMuchasAlicuotas_NingunaParteEsNegativa()
    {
        var partes = IvaNotaCreditoFiscal.Repartir(0.02m, new[] { (0m, 1m), (10.5m, 1m), (21m, 1m), (27m, 1m) });

        Assert.Equal(0.02m, partes.Sum(p => p.Total));
        Assert.All(partes, p => Assert.True(p.Total >= 0m && p.Neto >= 0m && p.Iva >= 0m));
        Assert.All(partes, p => Assert.Equal(p.Total, p.Neto + p.Iva));
    }
}
