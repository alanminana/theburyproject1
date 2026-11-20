using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// P-02: <see cref="CriterioFiscalV2"/> (redondeo comercial C1/C2). V2 está registrada pero NO vigente: V1 sigue siendo
/// el comportamiento de producción hasta que el propietario autorice la activación (P6).
/// </summary>
public class FiscalCriterioV2P02Tests
{
    private static readonly ICriterioFiscal V1 = CriterioFiscalRegistry.Resolver(1);
    private static readonly ICriterioFiscal V2 = CriterioFiscalRegistry.Resolver(2);

    [Fact]
    public void ProduccionNoActivaV2_LaVigenteSigueSiendoV1()
    {
        Assert.Equal(1, CriterioFiscalRegistry.VersionVigente);
        Assert.IsType<CriterioFiscalV1>(CriterioFiscalRegistry.Vigente);
        Assert.IsType<CriterioFiscalV2>(V2);
    }

    [Theory]
    [InlineData("1000.00", "1000")]
    [InlineData("1000.49", "1000")]
    [InlineData("1000.50", "1000")] // C1: $0,50 o menos → hacia abajo
    [InlineData("1000.51", "1001")] // C2: más de $0,50 → hacia arriba
    [InlineData("1000.99", "1001")]
    [InlineData("0.50", "0")]
    [InlineData("0.51", "1")]
    [InlineData("0.00", "0")]
    public void V2_RedondeoComercial_C1C2(string valor, string esperado) =>
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture),
            V2.TruncarAPesos(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("1000.50", "1000")]
    [InlineData("1000.51", "1000")] // V1 sigue truncando: la operación histórica no cambia
    [InlineData("1000.99", "1000")]
    public void V1_SigueTruncando(string valor, string esperado) =>
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture),
            V1.TruncarAPesos(decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Para los 100 valores de centavos posibles: Neto + IVA == total cobrado, sin diferencia inexplicable (C3).</summary>
    [Fact]
    public void V2_Discriminar_NetoMasIvaIgualTotal_ParaTodosLosCentavos()
    {
        for (var c = 0; c < 100; c++)
        {
            var bruto = 1000m + c / 100m;
            var neto = Math.Round(bruto / 1.21m, 2, MidpointRounding.AwayFromZero);
            var iva = bruto - neto;
            var total = V2.TruncarAPesos(bruto);

            var d = V2.Discriminar(total, new[] { (21m, bruto, neto, iva) });

            Assert.Equal(total, d.Neto + d.Iva);
            Assert.True(d.Iva > 0m);
            var resumen = V2.ResumenPorAlicuota(total, new[] { (21m, bruto, neto, iva) });
            Assert.Equal(total, resumen.Sum(r => r.Neto + r.Iva));
            Assert.Equal(total, resumen.Sum(r => r.Bruto));
        }
    }

    [Fact]
    public void V2_RedondeoHaciaArriba_EnVentaExenta_NoCreaIva()
    {
        var d = V2.Discriminar(1001m, new[] { (0m, 1000.60m, 1000.60m, 0m) });
        Assert.Equal(0m, d.Iva);
        Assert.Equal(1001m, d.Neto);
    }

    [Fact]
    public void V2_ConRecargo_NoInventaUnRecargoPorElRedondeo()
    {
        // Ítems 1.000,51 → total sin recargo 1.001: el recargo calculado es 0, no 1.
        Assert.Equal(0m, V2.CalcularRecargo(1001m, 1000.51m));
        Assert.Equal(1m, V1.CalcularRecargo(1001m, 1000.51m)); // V1 interpreta 1.001 como recargo; ya no ocurre en V1 porque nunca sube
        // Recargo 3 % sobre 1.000,51 = 1.030,51 → 1.031; recargo medido 30.
        Assert.Equal(30m, V2.CalcularRecargo(1031m, 1000.51m));
    }

    [Fact]
    public void V2_ResiduoNegativo_AbsorberTruncado_SumaAlIva()
    {
        Assert.Equal((826.45m, 173.55m + 0.49m), V2.AbsorberTruncado(826.45m, 173.55m, -0.49m));
        Assert.Equal((826.45m, 173.55m - 0.30m), V2.AbsorberTruncado(826.45m, 173.55m, 0.30m)); // positivo: igual que V1
        Assert.Equal(V1.AbsorberTruncado(826.45m, 173.55m, 0.30m), V2.AbsorberTruncado(826.45m, 173.55m, 0.30m));
    }

    /// <summary>Devolver todo reintegra exactamente lo cobrado: con V2 el total sube a 1.000 y el reintegro suma 1.000 (V1: 999).</summary>
    [Fact]
    public void V2_DevolverTodo_ReintegraExactamenteLoCobrado()
    {
        var cobrado = new[] { 333.33m, 333.33m, 333.33m };   // 999,99 de ítems
        var total2 = V2.TruncarAPesos(cobrado.Sum());          // 1.000
        var ajuste2 = TheBuryProject.Services.DevolucionService.RecargoPorLinea(cobrado, 0m, total2, V2);
        Assert.Equal(total2, cobrado.Sum() + ajuste2.Sum());

        var total1 = V1.TruncarAPesos(cobrado.Sum());          // 999
        var ajuste1 = TheBuryProject.Services.DevolucionService.RecargoPorLinea(cobrado, 0m, total1, V1);
        Assert.Equal(total1, cobrado.Sum() + ajuste1.Sum());
    }
}
