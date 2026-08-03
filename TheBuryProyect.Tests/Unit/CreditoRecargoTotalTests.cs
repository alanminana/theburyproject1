using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// ML1 (auditoría Crédito Personal, 2026-07-29): caracteriza la regla de negocio real —
/// recargo TOTAL del plan sobre el saldo posterior al anticipo, no interés compuesto
/// mensual — usando los 4 casos de referencia del pedido. Corren contra
/// <see cref="FinancialCalculationService.SimularPlanCredito"/>, que hoy implementa
/// amortización francesa ((1+i)^n): los casos 2 y 3 (tasa &gt; 0) deben fallar hasta
/// ML2, donde el núcleo pasa a calcular recargo total. El caso 1 (tasa 0%) ya es
/// idéntico bajo ambas fórmulas y por eso pasa desde ahora — se deja como referencia,
/// no como regresión esperada.
///
/// Fórmula esperada (ver plan, §5):
///   SaldoAFinanciar = totalVenta - anticipo
///   RecargoTotal    = round2(SaldoAFinanciar * porcentaje / 100)
///   TotalFinanciado = SaldoAFinanciar + RecargoTotal
///   ValorCuotaBase  = round2(TotalFinanciado / cuotas)
/// </summary>
public class CreditoRecargoTotalTests
{
    private readonly FinancialCalculationService _sut = new();

    private static DateTime FechaPrimera => new(2026, 1, 1);

    /// <summary>
    /// Caso 1 del pedido: 100.000 / anticipo 0 / 1 cuota / 0% → sin recargo.
    /// Tasa 0% es idéntica bajo interés compuesto y bajo recargo total (ambas
    /// fórmulas colapsan a monto/cuotas): este caso ya pasa hoy.
    /// </summary>
    [Fact]
    public void Caso1_SinAnticipoUnaCuotaSinRecargo_TotalIgualAlPrecio()
    {
        var resultado = _sut.SimularPlanCredito(100_000m, 0m, 1, 0m, 0m, FechaPrimera);

        Assert.Equal(100_000m, resultado.MontoFinanciado);
        Assert.Equal(0m, resultado.InteresTotal);
        Assert.Equal(100_000m, resultado.CuotaEstimada);
        Assert.Equal(100_000m, resultado.TotalAPagar);
    }

    /// <summary>
    /// Caso 2 del pedido: 100.000 / anticipo 0 / 12 cuotas / 10% recargo total.
    /// Con interés compuesto mensual (código actual) la cuota da ≈14.676,33 y el
    /// total ≈176.116 — muy por encima de lo esperado. RED hasta ML2.
    /// </summary>
    [Fact]
    public void Caso2_DocePagosDiezPorCientoRecargoTotal_NoInteresCompuestoMensual()
    {
        var resultado = _sut.SimularPlanCredito(100_000m, 0m, 12, 10m, 0m, FechaPrimera);

        Assert.Equal(100_000m, resultado.MontoFinanciado);
        Assert.Equal(10_000m, resultado.InteresTotal);
        Assert.Equal(9_166.67m, resultado.CuotaEstimada);
        // TotalAPagar debe cerrar EXACTO contra el total financiado (110.000), no
        // contra CuotaEstimada * 12 (110.000,04) — ese es el ajuste de última cuota.
        Assert.Equal(110_000m, resultado.TotalAPagar);
    }

    /// <summary>
    /// Caso 3 del pedido: 100.000 / anticipo 20.000 / 12 cuotas / 10% — el recargo
    /// se aplica solo sobre el saldo posterior al anticipo (80.000), nunca sobre el
    /// precio bruto ni sobre el anticipo. RED hasta ML2.
    /// </summary>
    [Fact]
    public void Caso3_ConAnticipo_RecargoSoloSobreSaldoPosteriorAlAnticipo()
    {
        var resultado = _sut.SimularPlanCredito(100_000m, 20_000m, 12, 10m, 0m, FechaPrimera);

        Assert.Equal(80_000m, resultado.MontoFinanciado);
        Assert.Equal(8_000m, resultado.InteresTotal);
        Assert.Equal(7_333.33m, resultado.CuotaEstimada);
        Assert.Equal(88_000m, resultado.TotalAPagar);
    }
}
