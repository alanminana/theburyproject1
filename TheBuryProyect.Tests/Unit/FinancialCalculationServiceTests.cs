using System.Linq;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para FinancialCalculationService.
/// Todas las funciones son puras (sin dependencias).
/// </summary>
public class FinancialCalculationServiceTests
{
    private readonly FinancialCalculationService _sut = new();

    // ---------------------------------------------------------------
    // CalcularCuotaSistemaFrances
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularCuotaFrances_TasaCero_DivideMontoEnCuotas()
    {
        var resultado = _sut.CalcularCuotaSistemaFrances(12000m, 0m, 12);
        Assert.Equal(1000m, resultado);
    }

    [Fact]
    public void CalcularCuotaFrances_ConTasa_CalculaCorrectamente()
    {
        // 10.000 a 5% mensual en 12 cuotas → cuota ≈ 1128.25
        var resultado = _sut.CalcularCuotaSistemaFrances(10000m, 0.05m, 12);
        Assert.InRange(resultado, 1128m, 1129m);
    }

    [Fact]
    public void CalcularCuotaFrances_UnaCuota_DevuelveMontoPlusInteres()
    {
        var resultado = _sut.CalcularCuotaSistemaFrances(1000m, 0.10m, 1);
        Assert.Equal(1100m, resultado);
    }

    [Fact]
    public void CalcularCuotaFrances_MontoNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.CalcularCuotaSistemaFrances(-100m, 0.05m, 12));
    }

    [Fact]
    public void CalcularCuotaFrances_CuotasCero_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.CalcularCuotaSistemaFrances(1000m, 0.05m, 0));
    }

    // ---------------------------------------------------------------
    // CalcularTotalConInteres
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularTotalConInteres_TasaCero_DevuelveMonto()
    {
        var resultado = _sut.CalcularTotalConInteres(5000m, 0m, 6);
        Assert.Equal(5000m, resultado);
    }

    [Fact]
    public void CalcularTotalConInteres_ConTasa_MayorQueMonto()
    {
        var resultado = _sut.CalcularTotalConInteres(10000m, 0.05m, 12);
        Assert.True(resultado > 10000m);
    }

    // ---------------------------------------------------------------
    // CalcularInteresTotal
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularInteresTotal_TasaCero_DevuelveCero()
    {
        var resultado = _sut.CalcularInteresTotal(5000m, 0m, 6);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularInteresTotal_ConTasa_DevuelveDiferenciaPositiva()
    {
        var resultado = _sut.CalcularInteresTotal(10000m, 0.05m, 12);
        Assert.True(resultado > 0m);
    }

    // ---------------------------------------------------------------
    // CalcularCFTEA
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularCFTEA_CuotasCero_DevuelveCero()
    {
        var resultado = _sut.CalcularCFTEA(12000m, 10000m, 0);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularCFTEA_MontoInicialCero_DevuelveCero()
    {
        var resultado = _sut.CalcularCFTEA(12000m, 0m, 12);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularCFTEA_ConDatos_DevuelvePositivo()
    {
        var resultado = _sut.CalcularCFTEA(13539m, 10000m, 12);
        Assert.True(resultado > 0m);
    }

    // ---------------------------------------------------------------
    // CalcularCFTEADesdeTasa
    // ---------------------------------------------------------------

    [Fact]
    public void CalcularCFTEADesdeTasa_TasaCero_DevuelveCero()
    {
        var resultado = _sut.CalcularCFTEADesdeTasa(0m);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void CalcularCFTEADesdeTasa_TasaNegativa_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.CalcularCFTEADesdeTasa(-0.05m));
    }

    [Fact]
    public void CalcularCFTEADesdeTasa_TasaPositiva_DevuelveAnualizado()
    {
        // 5% mensual → CFTEA ≈ 79.59%
        var resultado = _sut.CalcularCFTEADesdeTasa(0.05m);
        Assert.InRange(resultado, 79m, 80m);
    }

    // ---------------------------------------------------------------
    // ComputePmt
    // ---------------------------------------------------------------

    [Fact]
    public void ComputePmt_TasaCero_DivideRedondeado()
    {
        var resultado = _sut.ComputePmt(0m, 3, 1000m);
        Assert.Equal(333.33m, resultado);
    }

    [Fact]
    public void ComputePmt_MontoCero_DevuelveCero()
    {
        var resultado = _sut.ComputePmt(0.05m, 12, 0m);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void ComputePmt_MontoNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputePmt(0.05m, 12, -100m));
    }

    [Fact]
    public void ComputePmt_TasaNegativa_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputePmt(-0.01m, 12, 1000m));
    }

    [Fact]
    public void ComputePmt_CuotasMenorAUno_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputePmt(0.05m, 0, 1000m));
    }

    [Fact]
    public void ComputePmt_ConTasa_DevuelveRedondeadoA2Decimales()
    {
        var resultado = _sut.ComputePmt(0.05m, 12, 10000m);
        Assert.Equal(Math.Round(resultado, 2), resultado);
    }

    // ---------------------------------------------------------------
    // ComputeFinancedAmount
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeFinancedAmount_SinAnticipo_DevuelveTotal()
    {
        var resultado = _sut.ComputeFinancedAmount(10000m, 0m);
        Assert.Equal(10000m, resultado);
    }

    [Fact]
    public void ComputeFinancedAmount_ConAnticipo_RestaDiferencia()
    {
        var resultado = _sut.ComputeFinancedAmount(10000m, 3000m);
        Assert.Equal(7000m, resultado);
    }

    [Fact]
    public void ComputeFinancedAmount_AnticipoIgualTotal_DevuelveCero()
    {
        var resultado = _sut.ComputeFinancedAmount(5000m, 5000m);
        Assert.Equal(0m, resultado);
    }

    [Fact]
    public void ComputeFinancedAmount_TotalNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputeFinancedAmount(-100m, 0m));
    }

    [Fact]
    public void ComputeFinancedAmount_AnticipoNegativo_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputeFinancedAmount(1000m, -100m));
    }

    [Fact]
    public void ComputeFinancedAmount_AnticipoMayorQueTotal_LanzaExcepcion()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.ComputeFinancedAmount(1000m, 2000m));
    }

    // ---------------------------------------------------------------
    // SimularPlanCredito — integra cálculo + semáforo
    // ---------------------------------------------------------------

    private static DateTime FechaPrimera => new(2026, 1, 1);

    [Fact]
    public void SimularPlan_MontoFinanciado_EsTotalMenosAnticipo()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 3_000m, 6, 5m, 0m, FechaPrimera);
        Assert.Equal(7_000m, resultado.MontoFinanciado);
    }

    [Fact]
    public void SimularPlan_TasaAplicada_EsLaMismaQueSeIngresa()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 6, 3.5m, 0m, FechaPrimera);
        Assert.Equal(3.5m, resultado.TasaAplicada);
    }

    [Fact]
    public void SimularPlan_TotalPlan_IncluyeGastosAdministrativos()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 6, 5m, 500m, FechaPrimera);
        Assert.Equal(resultado.TotalAPagar + 500m, resultado.TotalPlan);
    }

    [Fact]
    public void SimularPlan_GastosAdministrativosCero_TotalPlanIgualTotalAPagar()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 6, 5m, 0m, FechaPrimera);
        Assert.Equal(resultado.TotalAPagar, resultado.TotalPlan);
    }

    [Fact]
    public void SimularPlan_FechaPrimerPago_EsLaQueSeIngresa()
    {
        var fecha = new DateTime(2026, 6, 15);
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 6, 5m, 0m, fecha);
        Assert.Equal(fecha, resultado.FechaPrimerPago);
    }

    [Fact]
    public void SimularPlan_ConTasa_InteresTotalPositivo()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 6, 5m, 0m, FechaPrimera);
        Assert.True(resultado.InteresTotal > 0m);
    }

    [Fact]
    public void SimularPlan_TasaCero_InteresTotalCero()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 6, 0m, 0m, FechaPrimera);
        Assert.Equal(0m, resultado.InteresTotal);
    }

    // Semáforo
    [Fact]
    public void SimularPlan_RatioMuyBajo_SemaforoVerde()
    {
        // ratio = cuota/monto ≤ 0.08 → verde
        // tasa=0%, 120 cuotas → cuota ≈ 83.33; ratio=83.33/10000=0.008 → verde
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 120, 0m, 0m, FechaPrimera);
        Assert.Equal("verde", resultado.SemaforoEstado);
        Assert.False(resultado.MostrarMsgIngreso);
    }

    [Fact]
    public void SimularPlan_RatioMedio_SemaforoAmarillo()
    {
        // ratio 0.08 < r ≤ 0.15 → amarillo
        // monto=10.000, cuotas=10, tasa=0 → cuota=1.000; ratio=1000/10000=0.10 → amarillo
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 10, 0m, 0m, FechaPrimera);
        Assert.Equal("amarillo", resultado.SemaforoEstado);
        Assert.True(resultado.MostrarMsgIngreso);
        Assert.False(resultado.MostrarMsgAntiguedad);
    }

    [Fact]
    public void SimularPlan_RatioAlto_SemaforoRojo()
    {
        // ratio > 0.15 → rojo
        // monto=10.000, cuotas=3, tasa=0 → cuota=3.333; ratio=3333/10000=0.33 → rojo
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 3, 0m, 0m, FechaPrimera);
        Assert.Equal("rojo", resultado.SemaforoEstado);
        Assert.True(resultado.MostrarMsgIngreso);
        Assert.True(resultado.MostrarMsgAntiguedad);
    }
    [Fact]
    public void SimularPlan_RatioExactoVerdeMax_SemaforoVerde()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 10, 0m, 0m, FechaPrimera, 0.10m, 0.15m);

        Assert.Equal("verde", resultado.SemaforoEstado);
        Assert.False(resultado.MostrarMsgIngreso);
    }

    [Fact]
    public void SimularPlan_RatioExactoAmarilloMax_SemaforoAmarillo()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 10, 0m, 0m, FechaPrimera, 0.08m, 0.10m);

        Assert.Equal("amarillo", resultado.SemaforoEstado);
        Assert.True(resultado.MostrarMsgIngreso);
        Assert.False(resultado.MostrarMsgAntiguedad);
    }

    [Fact]
    public void SimularPlan_RatioMayorAAmarilloMax_SemaforoRojo()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 10, 0m, 0m, FechaPrimera, 0.05m, 0.09m);

        Assert.Equal("rojo", resultado.SemaforoEstado);
        Assert.True(resultado.MostrarMsgIngreso);
        Assert.True(resultado.MostrarMsgAntiguedad);
    }

    [Fact]
    public void SimularPlan_UmbralesPersonalizados_CambianClasificacion()
    {
        var resultado = _sut.SimularPlanCredito(10_000m, 0m, 10, 0m, 0m, FechaPrimera, 0.12m, 0.20m);

        Assert.Equal("verde", resultado.SemaforoEstado);
    }

    // ---------------------------------------------------------------
    // Cuotas — vector exacto de capital/interés/total (corrección post-ML3:
    // el interés se construye como vector independiente no-negativo, nunca
    // como Total - Capital, para evitar interés negativo en la última
    // cuota cuando los residuos de redondeo de Total y Capital se
    // compensan en direcciones opuestas).
    // ---------------------------------------------------------------

    private static void AssertVectorCoherente(SimulacionPlanCreditoDto resultado)
    {
        Assert.Equal(resultado.MontoFinanciado, resultado.Cuotas.Sum(c => c.Capital));
        Assert.Equal(resultado.InteresTotal, resultado.Cuotas.Sum(c => c.Interes));
        Assert.Equal(resultado.TotalAPagar, resultado.Cuotas.Sum(c => c.Total));
        foreach (var cuota in resultado.Cuotas)
        {
            Assert.True(cuota.Capital >= 0m, $"Capital negativo en cuota {cuota.NumeroCuota}: {cuota.Capital}");
            Assert.True(cuota.Interes >= 0m, $"Interés negativo en cuota {cuota.NumeroCuota}: {cuota.Interes}");
            Assert.True(cuota.Total >= 0m, $"Total negativo en cuota {cuota.NumeroCuota}: {cuota.Total}");
            Assert.Equal(cuota.Total, cuota.Capital + cuota.Interes);
        }
    }

    [Fact]
    public void VectorCuotas_RecargoUnCentavoSobreCienMilTresCuotas_SinNegativosYSumasExactas()
    {
        // Caso reportado por el usuario tras el cierre de ML3: capital 100,00 / recargo
        // 0,01 / 3 cuotas. Con la versión anterior (capital independiente, interés =
        // Total - Capital) la última cuota daba interés -0,01. porcentajeRecargo=0.01%
        // sobre 100 da exactamente un recargo de 0,01.
        var resultado = _sut.SimularPlanCredito(100m, 0m, 3, 0.01m, 0m, FechaPrimera);

        Assert.Equal(100m, resultado.MontoFinanciado);
        Assert.Equal(0.01m, resultado.InteresTotal);
        AssertVectorCoherente(resultado);
    }

    [Fact]
    public void VectorCuotas_RecargoMenorQueLaCantidadDeCuotasEnCentavos_SoloUnaCuotaRecibeElCentavo()
    {
        // Recargo total (0,01) menor que la cantidad de cuotas (5): no alcanza para
        // repartir ni un centavo por cuota — round(0,01/5)=0,00 para las primeras 4,
        // la última absorbe el único centavo entero.
        var resultado = _sut.SimularPlanCredito(1_000m, 0m, 5, 0.001m, 0m, FechaPrimera);

        Assert.Equal(0.01m, resultado.InteresTotal);
        AssertVectorCoherente(resultado);
        Assert.All(resultado.Cuotas.Take(4), c => Assert.Equal(0m, c.Interes));
        Assert.Equal(0.01m, resultado.Cuotas[4].Interes);
    }

    [Fact]
    public void VectorCuotas_RecargoCero_TodosLosInteresesCeroYCapitalIgualATotal()
    {
        var resultado = _sut.SimularPlanCredito(100_000m, 0m, 3, 0m, 0m, FechaPrimera);

        AssertVectorCoherente(resultado);
        Assert.All(resultado.Cuotas, c =>
        {
            Assert.Equal(0m, c.Interes);
            Assert.Equal(c.Total, c.Capital);
        });
    }

    [Fact]
    public void VectorCuotas_ConAnticipoYRecargo_CapitalTotalIgualSaldoPosteriorAlAnticipo()
    {
        // Caso 3 del pedido: 100.000 / anticipo 20.000 / 12 cuotas / 10%.
        var resultado = _sut.SimularPlanCredito(100_000m, 20_000m, 12, 10m, 0m, FechaPrimera);

        Assert.Equal(80_000m, resultado.MontoFinanciado);
        Assert.Equal(8_000m, resultado.InteresTotal);
        AssertVectorCoherente(resultado);
    }

    [Fact]
    public void VectorCuotas_MismosDatosDeEntrada_DevuelveSiempreElMismoVector()
    {
        // VentaService (persistencia) y ContratoVentaCreditoService (contrato) llaman
        // a SimularPlanCredito con exactamente los mismos datos server-side; este test
        // documenta que, dado el mismo input, el vector es siempre idéntico — por eso
        // contrato y cuotas persistidas nunca pueden divergir.
        var primero = _sut.SimularPlanCredito(100_000m, 20_000m, 12, 10m, 0m, FechaPrimera);
        var segundo = _sut.SimularPlanCredito(100_000m, 20_000m, 12, 10m, 0m, FechaPrimera);

        Assert.Equal(primero.Cuotas.Count, segundo.Cuotas.Count);
        for (var i = 0; i < primero.Cuotas.Count; i++)
        {
            Assert.Equal(primero.Cuotas[i].Capital, segundo.Cuotas[i].Capital);
            Assert.Equal(primero.Cuotas[i].Interes, segundo.Cuotas[i].Interes);
            Assert.Equal(primero.Cuotas[i].Total, segundo.Cuotas[i].Total);
        }
    }

    [Fact]
    public void VectorCuotas_RecargoExtremoFrenteAlCapital_FallbackRedistribuyeSinNegativos()
    {
        // Caso extremo (no reportado por el usuario, pero cubierto por la regla "ajustá
        // la distribución sin generar valores negativos"): capital financiado casi nulo
        // (0,01) frente a un recargo enorme (100). Con la distribución por centavos
        // enteros ambos vectores quedan muy parejos entre sí (33,34 de interés <=
        // 33,35 de total en la última cuota), así que este caso puntual no dispara el
        // fallback — pero sigue sirviendo para confirmar que las tres sumatorias
        // cierran exactas incluso con un desbalance extremo. El fallback en sí se
        // ejercita explícitamente en VectorCuotas_RecargoCercanoAlTotal_FallbackRedistribuyeSinSuperarElTotal.
        var resultado = _sut.SimularPlanCredito(0.01m, 0m, 3, 1_000_000m, 0m, FechaPrimera);

        Assert.Equal(0.01m, resultado.MontoFinanciado);
        Assert.Equal(100m, resultado.InteresTotal);
        AssertVectorCoherente(resultado);
    }

    [Fact]
    public void VectorCuotas_TotalFinanciadoMenorQueLaCantidadDeCuotas_NingunaCuotaNegativa()
    {
        // Bug reportado post-ML3: redondear "totalFinanciado/n" por cuota y restar la
        // suma de las anteriores para la última podía darla negativa cuando el importe
        // es chico frente a n. Total financiado 0,05 en 9 cuotas: base redondeada
        // 0,01 × 8 = 0,08, ya supera los 0,05 totales → última cuota = 0,05 - 0,08 =
        // -0,03. Con división entera de centavos (base 0 + resto 5 solo en la última)
        // esto no puede pasar.
        var resultado = _sut.SimularPlanCredito(0.05m, 0m, 9, 0m, 0m, FechaPrimera);

        Assert.Equal(0.05m, resultado.TotalAPagar);
        AssertVectorCoherente(resultado);
        Assert.All(resultado.Cuotas.Take(8), c => Assert.Equal(0m, c.Total));
        Assert.Equal(0.05m, resultado.Cuotas[8].Total);
    }

    [Fact]
    public void VectorCuotas_TotalFinanciadoDosCentavosTresCuotas_ResiduoCompletoEnLaUltima()
    {
        var resultado = _sut.SimularPlanCredito(0.02m, 0m, 3, 0m, 0m, FechaPrimera);

        Assert.Equal(0.02m, resultado.TotalAPagar);
        AssertVectorCoherente(resultado);
        Assert.Equal(0.00m, resultado.Cuotas[0].Total);
        Assert.Equal(0.00m, resultado.Cuotas[1].Total);
        Assert.Equal(0.02m, resultado.Cuotas[2].Total);
    }

    [Fact]
    public void VectorCuotas_RecargoCercanoAlTotal_FallbackRedistribuyeSinSuperarElTotal()
    {
        // Capital financiado 0,03 con un recargo que lo deja en 0,97 sobre un total
        // financiado de 1,00 (7 cuotas): la distribución independiente por centavos da
        // interés crudo [13,13,13,13,13,13,19] contra total [14,14,14,14,14,14,16] —
        // el interés de la última cuota (19) supera su propio total (16), dispara el
        // fallback, que devuelve 3 centavos a las tres cuotas anteriores a la última.
        var resultado = _sut.SimularPlanCredito(0.03m, 0m, 7, 3233m, 0m, FechaPrimera);

        Assert.Equal(0.03m, resultado.MontoFinanciado);
        Assert.Equal(0.97m, resultado.InteresTotal);
        Assert.Equal(1.00m, resultado.TotalAPagar);
        AssertVectorCoherente(resultado);

        Assert.All(resultado.Cuotas, c => Assert.True(c.Interes <= c.Total));

        var interesesEsperados = new[] { 0.13m, 0.13m, 0.13m, 0.14m, 0.14m, 0.14m, 0.16m };
        for (var i = 0; i < interesesEsperados.Length; i++)
            Assert.Equal(interesesEsperados[i], resultado.Cuotas[i].Interes);
    }
}
