using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para CreditoService.GenerarPlanAmortizacionFrances.
///
/// Documenta el contrato del loop de amortización francesa extraído de
/// SimularCreditoAsync. Todos los valores esperados fueron calculados
/// manualmente contra el bloque inline original antes de la extracción.
///
/// Comportamiento documentado:
/// - El saldo final puede ser un residuo positivo pequeño por acumulación
///   de redondeos intermedios. Math.Max(0, saldo) lo fuerza a 0 si va negativo.
/// - La tasa 0 produce cuotas de capital lineal puro (capital = montoCuota,
///   interés = 0) sin división por cero porque el loop no divide por tasa.
///
/// No requiere DB ni infraestructura — función pura con fechaInicio como parámetro.
/// </summary>
public class CreditoServiceAmortizacionTests
{
    private static readonly DateTime FechaBase = new DateTime(2026, 1, 1);

    // ---------------------------------------------------------------------------
    // Cantidad de cuotas generadas = cantidadCuotas solicitadas
    // ---------------------------------------------------------------------------

    [Fact]
    public void CantidadCuotas_IgualAlParametro()
    {
        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: 10_000m,
            tasa: 0.05m,
            cantidadCuotas: 12,
            montoCuota: 1_128.25m,   // cuota francesa aproximada para esos parámetros
            fechaInicio: FechaBase);

        Assert.Equal(12, plan.Count);
    }

    // ---------------------------------------------------------------------------
    // Números de cuota son consecutivos desde 1
    // ---------------------------------------------------------------------------

    [Fact]
    public void NumerosCuota_SonConsecutivosDesde1()
    {
        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: 1_000m,
            tasa: 0.03m,
            cantidadCuotas: 3,
            montoCuota: 353.53m,
            fechaInicio: FechaBase);

        Assert.Equal(1, plan[0].NumeroCuota);
        Assert.Equal(2, plan[1].NumeroCuota);
        Assert.Equal(3, plan[2].NumeroCuota);
    }

    // ---------------------------------------------------------------------------
    // Tasa 0% → interés = 0 en todas las cuotas, capital = montoCuota
    // ---------------------------------------------------------------------------

    [Fact]
    public void Tasa0_InteresEsCeroEnTodasLasCuotas()
    {
        const decimal monto = 1_200m;
        const int cuotas = 4;
        const decimal montoCuota = 300m; // monto / cuotas, exacto

        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: monto,
            tasa: 0m,
            cantidadCuotas: cuotas,
            montoCuota: montoCuota,
            fechaInicio: FechaBase);

        Assert.All(plan, c => Assert.Equal(0m, c.MontoInteres));
        Assert.All(plan, c => Assert.Equal(montoCuota, c.MontoCapital));
    }

    // ---------------------------------------------------------------------------
    // Tasa 0% → saldo final = 0
    // ---------------------------------------------------------------------------

    [Fact]
    public void Tasa0_SaldoFinalEsCero()
    {
        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: 1_200m,
            tasa: 0m,
            cantidadCuotas: 4,
            montoCuota: 300m,
            fechaInicio: FechaBase);

        Assert.Equal(0m, plan.Last().SaldoCapital);
    }

    // ---------------------------------------------------------------------------
    // 1 cuota → plan de 1 elemento con valores correctos
    // ---------------------------------------------------------------------------

    [Fact]
    public void UnaCuota_ProducePlanDeUnElemento()
    {
        const decimal monto = 500m;
        const decimal tasa = 0.02m;
        // 1 cuota: montoCuota = monto * (1 + tasa) = 500 * 1.02 = 510
        const decimal montoCuota = 510m;

        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: monto,
            tasa: tasa,
            cantidadCuotas: 1,
            montoCuota: montoCuota,
            fechaInicio: FechaBase);

        Assert.Single(plan);
        Assert.Equal(1, plan[0].NumeroCuota);
        Assert.Equal(Math.Round(monto * tasa, 2), plan[0].MontoInteres);   // 10.00
        Assert.Equal(Math.Round(montoCuota - monto * tasa, 2), plan[0].MontoCapital); // 500.00
        Assert.Equal(0m, plan[0].SaldoCapital); // saldo = 0 al final
    }

    // ---------------------------------------------------------------------------
    // Interés decrece cuota a cuota (amortización francesa)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Interes_DecrececuotaACuota()
    {
        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: 10_000m,
            tasa: 0.05m,
            cantidadCuotas: 6,
            montoCuota: 1_970.04m,
            fechaInicio: FechaBase);

        for (int i = 1; i < plan.Count; i++)
            Assert.True(plan[i].MontoInteres <= plan[i - 1].MontoInteres,
                $"El interés de cuota {i + 1} ({plan[i].MontoInteres}) no es <= que el de cuota {i} ({plan[i - 1].MontoInteres})");
    }

    // ---------------------------------------------------------------------------
    // Suma de capital amortizado ≈ monto financiado (tolerancia por redondeos)
    // ---------------------------------------------------------------------------

    [Fact]
    public void SumaCapital_AproximaAlMontoFinanciado()
    {
        const decimal monto = 10_000m;

        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: monto,
            tasa: 0.05m,
            cantidadCuotas: 6,
            montoCuota: 1_970.04m,
            fechaInicio: FechaBase);

        var sumaCapital = plan.Sum(c => c.MontoCapital);

        // Tolerancia de 1 unidad monetaria por acumulación de redondeos intermedios
        Assert.True(Math.Abs(sumaCapital - monto) <= 1m,
            $"Suma de capital ({sumaCapital}) difiere del monto ({monto}) en más de $1");
    }

    // ---------------------------------------------------------------------------
    // Fechas de vencimiento avanzan mes a mes desde fechaInicio
    // ---------------------------------------------------------------------------

    [Fact]
    public void Fechas_AvanzanUnMesPorCuota()
    {
        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: 3_000m,
            tasa: 0.02m,
            cantidadCuotas: 3,
            montoCuota: 1_060.40m,
            fechaInicio: FechaBase);

        Assert.Equal(FechaBase, plan[0].FechaVencimiento);
        Assert.Equal(FechaBase.AddMonths(1), plan[1].FechaVencimiento);
        Assert.Equal(FechaBase.AddMonths(2), plan[2].FechaVencimiento);
    }

    // ---------------------------------------------------------------------------
    // SaldoCapital nunca es negativo (Math.Max(0, ...) garantizado)
    // ---------------------------------------------------------------------------

    [Fact]
    public void SaldoCapital_NuncaEsNegativo()
    {
        var plan = CreditoService.GenerarPlanAmortizacionFrances(
            monto: 1_000m,
            tasa: 0.05m,
            cantidadCuotas: 3,
            montoCuota: 367.21m,
            fechaInicio: FechaBase);

        Assert.All(plan, c => Assert.True(c.SaldoCapital >= 0m,
            $"SaldoCapital negativo en cuota {c.NumeroCuota}: {c.SaldoCapital}"));
    }
}
