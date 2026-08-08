using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// ML1 — Micro-lote "Contrato del porcentaje por plan": <c>FinancialCalculationService.SimularPlanCredito</c>
/// es el punto final donde el backend aplica el porcentaje de recargo TOTAL de un plan de cuotas
/// (no una tasa mensual compuesta: ver el comentario de <c>SimularPlanCredito</c>). Esta suite fija,
/// como contrato puro, que:
/// <list type="bullet">
///   <item>el porcentaje que entra es el que sale, explícito y sin alteración (T1);</item>
///   <item>0 % es un recargo válido, nunca reinterpretado como "usar otra cosa" (T2);</item>
///   <item>metadatos ajenos al plan (fecha de la 1ª cuota, gastos administrativos) no alteran el
///         cálculo financiero — invariancia matemática (T9);</item>
///   <item>el vector de cuotas redondea exacto: la suma de las cuotas es siempre el total, sin
///         perder ni un centavo (T10).</item>
/// </list>
/// No cubre CÓMO se resuelve el porcentaje de un plan (eso son los tests de resolución en
/// <c>ConfiguracionCreditoPersonalPlanesGlobalesTests</c> y <c>CreditoConfiguracionVentaServiceTests</c>):
/// acá el porcentaje siempre llega explícito por parámetro, como corresponde a la autoridad única
/// congelada (plan de cuotas seleccionado).
/// </summary>
public class SimularPlanCreditoPorcentajePlanTests
{
    private static readonly DateTime FechaPrimeraCuota = new(2026, 8, 10);

    private static FinancialCalculationService CrearService() => new();

    // =========================================================================================
    // T1 — porcentaje explícito del plan: Plan 6 cuotas / 8% → resultado 8%.
    // =========================================================================================

    [Fact]
    public void T1_PlanConOchoPorcientoExplicito_ResuelveElRecargoAlOchoPorciento()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 120_000m,
            anticipo: 0m,
            cuotas: 6,
            porcentajeRecargo: 8m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(8m, resultado.TasaAplicada);
        Assert.Equal(120_000m, resultado.MontoFinanciado);
        Assert.Equal(9_600m, resultado.InteresTotal); // 120000 * 8% = 9600, recargo total, no compuesto
        Assert.Equal(129_600m, resultado.TotalAPagar);
    }

    // =========================================================================================
    // T2 — 0 % explícito: Plan 6 cuotas / 0% → resultado 0%, nunca fallback.
    // =========================================================================================

    [Fact]
    public void T2_PlanConCeroPorcientoExplicito_ResuelveCeroRecargoSinFallback()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 120_000m,
            anticipo: 0m,
            cuotas: 6,
            porcentajeRecargo: 0m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(0m, resultado.TasaAplicada);
        Assert.Equal(0m, resultado.InteresTotal);
        Assert.Equal(resultado.MontoFinanciado, resultado.TotalAPagar); // sin recargo: total = financiado
    }

    // =========================================================================================
    // T9 — invariancia matemática: mismo precio + anticipo + plan (recargo) ⇒ mismo resultado
    // financiero, sin importar qué metadatos ajenos al plan (fecha de 1ª cuota, gastos
    // administrativos) acompañen la simulación. Fixture congelado en la spec de ML1:
    //   Precio 100000, anticipo 30000, plan 12 cuotas, recargo 10%
    //   → saldo 70000, recargo 7000, total 77000.
    // =========================================================================================

    [Theory]
    [InlineData(0)]   // "contexto A": sin gastos administrativos
    [InlineData(500)] // "contexto B": con gastos administrativos — no debe alterar el financiero
    public void T9_InvarianciaMatematica_MismoPrecioAnticipoYPlan_MismoResultadoFinanciero(
        decimal gastosAdministrativos)
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 30_000m,
            cuotas: 12,
            porcentajeRecargo: 10m,
            gastosAdministrativos: gastosAdministrativos,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(70_000m, resultado.MontoFinanciado);
        Assert.Equal(7_000m, resultado.InteresTotal);
        Assert.Equal(77_000m, resultado.TotalAPagar);
    }

    [Fact]
    public void T9_InvarianciaMatematica_DistintaFechaDePrimeraCuota_MismoResultadoFinanciero()
    {
        var service = CrearService();

        var resultadoA = service.SimularPlanCredito(
            totalVenta: 100_000m, anticipo: 30_000m, cuotas: 12, porcentajeRecargo: 10m,
            gastosAdministrativos: 0m, fechaPrimeraCuota: new DateTime(2026, 8, 10));

        var resultadoB = service.SimularPlanCredito(
            totalVenta: 100_000m, anticipo: 30_000m, cuotas: 12, porcentajeRecargo: 10m,
            gastosAdministrativos: 0m, fechaPrimeraCuota: new DateTime(2027, 3, 1));

        Assert.Equal(resultadoA.MontoFinanciado, resultadoB.MontoFinanciado);
        Assert.Equal(resultadoA.InteresTotal, resultadoB.InteresTotal);
        Assert.Equal(resultadoA.TotalAPagar, resultadoB.TotalAPagar);
        Assert.Equal(70_000m, resultadoA.MontoFinanciado);
        Assert.Equal(7_000m, resultadoA.InteresTotal);
        Assert.Equal(77_000m, resultadoA.TotalAPagar);
    }

    // =========================================================================================
    // T10 — redondeo exacto: Total 100000 en 3 cuotas → 33333.33 / 33333.33 / 33333.34, suma
    // exactamente 100000 (sin recargo, para aislar el vector de cuotas del cálculo de interés).
    // =========================================================================================

    [Fact]
    public void T10_RedondeoExacto_TresCuotas_SumaExactamenteElTotal()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 3,
            porcentajeRecargo: 0m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(3, resultado.Cuotas.Count);
        Assert.Equal(33_333.33m, resultado.Cuotas[0].Total);
        Assert.Equal(33_333.33m, resultado.Cuotas[1].Total);
        Assert.Equal(33_333.34m, resultado.Cuotas[2].Total);

        var suma = resultado.Cuotas.Sum(c => c.Total);
        Assert.Equal(100_000m, suma);
    }
}
