using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

// ---------------------------------------------------------------------------
// CSR-ML1 — Caracterización + contrato congelado de "cuotas específicas sin recargo,
// configurables por plan" (Crédito Personal).
//
// Este archivo NO modifica código productivo. Sigue el mismo patrón que
// TheBuryProyect.Tests/Integration/PunitorioCaracterizacionTests.cs (PUN-ML1):
//
//   BASELINE (verde)         llama a FinancialCalculationService.SimularPlanCredito TAL CUAL
//                             existe hoy (sin parámetro de "cuotas sin recargo": no existe
//                             superficie para pasarlo). Cubre los escenarios de la spec donde el
//                             conjunto de cuotas sin recargo es vacío o el % es 0 — casos en los
//                             que el resultado esperado coincide con el actual por construcción.
//                             Es red de seguridad: si CSR-ML2 rompe alguno de estos números al
//                             tocar ConstruirVectorCuotas, esta mitad se pone roja.
//
//   CONTRATO NUEVO (rojo)    llama al MISMO método real de hoy (sigue sin existir forma de
//                             pasarle qué cuotas van sin recargo) pero afirma los valores por
//                             cuota que exige la spec nueva. Está roja a propósito: hoy
//                             SimularPlanCredito reparte el recargo entre TODAS las cuotas por
//                             igual, sin noción de exclusión. El mensaje de assertion de xUnit
//                             (valor esperado vs. real) documenta la causa exacta.
//
// Ningún assert de BASELINE debe leerse como "ya soporta cuotas sin recargo": simplemente el
// resultado nuevo y el actual coinciden en esos escenarios concretos. CONTRATO NUEVO es el que
// fija la regla correcta para CSR-ML2.
// ---------------------------------------------------------------------------
public class CreditoPersonalCuotaSinRecargoContratoTests
{
    private static readonly DateTime FechaPrimeraCuota = new(2026, 9, 10);

    private static FinancialCalculationService CrearService() => new();

    // =========================================================================================
    // BASELINE (verde) — conjunto de cuotas sin recargo vacío, o 0 %: el resultado nuevo debe
    // coincidir exactamente con el actual.
    // =========================================================================================

    [Fact]
    public void T1_SinPromociones_DiezCuotasIguales_ComoHoy()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(110_000m, resultado.TotalAPagar);
        Assert.All(resultado.Cuotas, c => Assert.Equal(11_000m, c.Total));
        Assert.Equal(110_000m, resultado.Cuotas.Sum(c => c.Total));
    }

    [Fact]
    public void T6_CeroPorciento_SinRecargoDeclaradoEsIrrelevante_DiezCuotasDeDiezMil()
    {
        // Sin recargo: 1,2,3 declarado en la spec, pero al 0% no hay recargo que excluir de
        // ningún lado: el resultado es idéntico a "sin promociones" a 0%. No hace falta (ni
        // existe forma de) pasar la exclusión para que este caso ya dé el número correcto hoy.
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 0m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(0m, resultado.InteresTotal);
        Assert.All(resultado.Cuotas, c =>
        {
            Assert.Equal(10_000m, c.Capital);
            Assert.Equal(0m, c.Interes);
            Assert.Equal(10_000m, c.Total);
        });
    }

    [Fact]
    public void T7_ConAnticipo_AgregadosSaldoRecargoTotal_SinRecargoDeclaradoNoAlteraLosAgregados()
    {
        // Sin recargo: 1,2,3 declarado en la spec, pero los agregados (saldo, recargo total,
        // total financiado) se calculan ANTES de repartir por cuota — la exclusión por cuota
        // nunca los altera (spec: "el porcentaje sigue siendo el recargo total del plan"). Estos
        // 3 números ya son correctos hoy, con la superficie actual.
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 20_000m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(80_000m, resultado.MontoFinanciado);
        Assert.Equal(8_000m, resultado.InteresTotal);
        Assert.Equal(88_000m, resultado.TotalAPagar);
    }

    [Fact]
    public void T8_ResiduoDeCapital_SumaCapitalSiempreIgualAlSaldo_InvarianteYaVigenteHoy()
    {
        // Saldo que no divide exacto entre las cuotas (100000 / 3). El invariante Σ Capital ==
        // montoFinanciado ya lo garantiza ConstruirVectorCuotas hoy (ML3) y es independiente de
        // cuáles cuotas queden sin recargo: CSR-ML2 no debe romperlo al introducir exclusiones.
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 3,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(100_000m, resultado.Cuotas.Sum(c => c.Capital));
    }

    [Fact]
    public void T9_ResiduoDeRecargo_SumaInteresSiempreIgualAlRecargoTotal_InvarianteYaVigenteHoy()
    {
        // Recargo que no divide exacto entre cuotas (100005 * 10% = 10000.50 / 10). El invariante
        // Σ Interes == interesTotal ya lo garantiza ConstruirVectorCuotas hoy y debe seguir
        // valiendo cuando el recargo se reparta solo entre las cuotas elegibles (CSR-ML2).
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_005m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(10_000.50m, resultado.InteresTotal);
        Assert.Equal(10_000.50m, resultado.Cuotas.Sum(c => c.Interes));
    }

    [Fact]
    public void T10_Invariantes_CapitalRecargoYTotalCierranExacto_YaVigenteHoy()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota);

        Assert.Equal(resultado.MontoFinanciado, resultado.Cuotas.Sum(c => c.Capital));
        Assert.Equal(resultado.InteresTotal, resultado.Cuotas.Sum(c => c.Interes));
        Assert.Equal(resultado.TotalAPagar, resultado.Cuotas.Sum(c => c.Total));
    }

    // =========================================================================================
    // CONTRATO NUEVO — cuotas sin recargo declaradas. Desde CSR-ML3, SimularPlanCredito acepta
    // el parámetro cuotasSinRecargo: estos tests ya llaman a la superficie nueva. Los valores
    // esperados abajo son los de la spec (CSR-ML1), calculados a mano una única vez por el propio
    // pedido (ejemplo canónico y variantes T3/T4/T5) y no se tocaron al implementar CSR-ML3: son
    // el contrato que la implementación debe cumplir.
    // =========================================================================================

    [Fact]
    public void T2_UnoDosTresSinRecargo_EjemploCanonicoDeLaSpec()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: new[] { 1, 2, 3 });

        Assert.Equal(10_000m, resultado.InteresTotal);

        var esperado = new (decimal Capital, decimal Interes, decimal Total)[]
        {
            (10_000m,     0m, 10_000m),
            (10_000m,     0m, 10_000m),
            (10_000m,     0m, 10_000m),
            (10_000m, 1_428.57m, 11_428.57m),
            (10_000m, 1_428.57m, 11_428.57m),
            (10_000m, 1_428.57m, 11_428.57m),
            (10_000m, 1_428.57m, 11_428.57m),
            (10_000m, 1_428.57m, 11_428.57m),
            (10_000m, 1_428.57m, 11_428.57m),
            (10_000m, 1_428.58m, 11_428.58m),
        };

        for (var i = 0; i < esperado.Length; i++)
        {
            Assert.Equal(esperado[i].Capital, resultado.Cuotas[i].Capital);
            Assert.Equal(esperado[i].Interes, resultado.Cuotas[i].Interes);
            Assert.Equal(esperado[i].Total, resultado.Cuotas[i].Total);
        }

        Assert.Equal(110_000m, esperado.Sum(e => e.Total));
    }

    [Fact]
    public void T3_UnoDosSinRecargo_DosDeDiezMilMasOchoDeOnceMilDoscientosCincuenta()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: new[] { 1, 2 });

        // Esperado (sin recargo: 1,2): 8 cuotas con recargo dividen 10000 exacto (1250 c/u).
        Assert.Equal(10_000m, resultado.Cuotas[0].Total);
        Assert.Equal(10_000m, resultado.Cuotas[1].Total);
        for (var i = 2; i < 10; i++)
            Assert.Equal(11_250m, resultado.Cuotas[i].Total);

        Assert.Equal(110_000m, resultado.Cuotas.Sum(c => c.Total));
    }

    [Fact]
    public void T4_NoConsecutivas_UnoTresCincoSinRecargo_SoloEsasQuedanEnCero()
    {
        var service = CrearService();

        var sinRecargoEsperado = new[] { 1, 3, 5 };
        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: sinRecargoEsperado);

        // Esperado (sin recargo: 1,3,5): 7 cuotas con recargo, 10000/7 = 1428.57 x6 + 1428.58
        // (residuo en la última cuota que sí tiene recargo, cuota 10).
        foreach (var numero in sinRecargoEsperado)
            Assert.Equal(0m, resultado.Cuotas[numero - 1].Interes);

        var conRecargo = resultado.Cuotas.Where(c => !sinRecargoEsperado.Contains(c.NumeroCuota)).ToList();
        Assert.All(conRecargo, c => Assert.True(c.Interes > 0m));
        Assert.Equal(10_000m, resultado.Cuotas.Sum(c => c.Interes));
        Assert.Equal(100_000m, resultado.Cuotas.Sum(c => c.Capital));
    }

    [Fact]
    public void T5_PrimeraYUltimaSinRecargo_LaCuotaDiezQuedaEnCero_ResiduoVaALaUltimaConRecargo()
    {
        var service = CrearService();

        // Saldo 100005 (no 100000): fuerza un residuo de recargo que no cae exacto entre las 8
        // cuotas elegibles, para poder distinguir "cuota 10 sin recargo" de "cuota 9 (última con
        // recargo) absorbe el residuo" sin ambigüedad con el residuo de capital (que sí divide
        // exacto: 100005/10 = 10000.50 c/u).
        var resultado = service.SimularPlanCredito(
            totalVenta: 100_005m,
            anticipo: 0m,
            cuotas: 10,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: new[] { 1, 10 });

        Assert.Equal(10_000.50m, resultado.InteresTotal);

        // Esperado (sin recargo: 1,10): cuota 10 en 0, residuo (0.02) en cuota 9 (última con
        // recargo), el resto de las elegibles (2-8) en 1250.06 exactos.
        Assert.Equal(0m, resultado.Cuotas[0].Interes);   // cuota 1 — sin recargo
        Assert.Equal(0m, resultado.Cuotas[9].Interes);   // cuota 10 — sin recargo
        Assert.Equal(1_250.08m, resultado.Cuotas[8].Interes); // cuota 9, absorbe el residuo
        for (var i = 1; i < 8; i++) // cuotas 2..8
            Assert.Equal(1_250.06m, resultado.Cuotas[i].Interes);

        Assert.Equal(10_000.50m, resultado.Cuotas.Sum(c => c.Interes));
    }

    // =========================================================================================
    // T11 — paridad (parcial): precondición matemática. SimularPlanCredito es puro y
    // determinista: mismos inputs ⇒ mismo vector, sin importar cuántas veces o desde qué caller
    // se invoque. Hoy Cotización (CotizacionPagoCalculator → CreditoSimulacionVentaService) y
    // Configurar Venta (CreditoController.SimularPlanVenta → CreditoSimulacionVentaService)
    // comparten el mismo call site de CreditoSimulacionVentaService.SimularAsync, y la
    // confirmación real (VentaService.GenerarCuotasCreditoAsync) y la proyección de contrato
    // (ContratoVentaCreditoService.ConstruirPlanCuotas, cuando no hay cuotas persistidas) llaman
    // directamente a este mismo método — ver informe CSR-ML1. La paridad de INTEGRACIÓN con
    // cuotas sin recargo no es testeable hasta CSR-ML2 (no existe forma de pasar la exclusión a
    // ningún endpoint todavía); este test fija la precondición que CSR-ML2 no puede romper: si
    // extiende la firma de SimularPlanCredito, todos los call sites deben seguir recibiendo
    // exactamente los mismos argumentos para no divergir.
    // =========================================================================================

    [Fact]
    public void T11_Paridad_MismosInputs_MismoVectorSinImportarElCaller()
    {
        var service = CrearService();

        var a = service.SimularPlanCredito(100_000m, 20_000m, 10, 10m, 0m, FechaPrimeraCuota);
        var b = service.SimularPlanCredito(100_000m, 20_000m, 10, 10m, 0m, FechaPrimeraCuota);

        Assert.Equal(a.MontoFinanciado, b.MontoFinanciado);
        Assert.Equal(a.InteresTotal, b.InteresTotal);
        Assert.Equal(a.TotalAPagar, b.TotalAPagar);
        Assert.Equal(a.Cuotas.Count, b.Cuotas.Count);
        for (var i = 0; i < a.Cuotas.Count; i++)
        {
            Assert.Equal(a.Cuotas[i].Capital, b.Cuotas[i].Capital);
            Assert.Equal(a.Cuotas[i].Interes, b.Cuotas[i].Interes);
            Assert.Equal(a.Cuotas[i].Total, b.Cuotas[i].Total);
        }
    }

    // =========================================================================================
    // T12 — mora/punitorio: una cuota "sin recargo" (Interes = 0, Total = Capital) vencida e
    // impaga debe poder seguir generando punitorio. PunitorioCalculator (PUN-ML4) es puro y solo
    // recibe MontoOriginalCuota (el Total, sin desglose) — nunca lee Interes/Capital por
    // separado. Ya es 100% agnóstico a cómo se compuso el Total: este test lo confirma
    // directamente contra el calculador real, sin tocar producción, y debe seguir verde después
    // de CSR-ML2.
    // =========================================================================================

    [Fact]
    public void T12_CuotaSinRecargoVencidaEImpaga_SigueGenerandoPunitorio()
    {
        var calculator = new PunitorioCalculator();

        // Cuota "sin recargo": su MontoTotal es exactamente su capital (10000), sin componente
        // de interés — igual que cualquier cuota de un plan al 0%, que ya hoy genera punitorio.
        var entrada = new PunitorioCalculoEntrada
        {
            MontoOriginalCuota = 10_000m,
            FechaVencimiento = new DateOnly(2026, 8, 1),
            FechaCalculo = new DateOnly(2026, 8, 21), // 20 días de atraso
            PagosAplicados = Array.Empty<PagoAplicadoPunitorioEntrada>(),
            Configuraciones = new[]
            {
                new ConfiguracionPunitorioEntrada
                {
                    Id = 1,
                    VigenteDesde = new DateOnly(2026, 1, 1),
                    Porcentaje = 10m,
                    PeriodoDias = 30,
                    DiasGracia = 5,
                    ProrrateoDiario = true,
                    Activa = true
                }
            }
        };

        var resultado = calculator.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, resultado.EstadoResultado);
        Assert.NotNull(resultado.PunitorioRedondeado);
        Assert.True(resultado.PunitorioRedondeado > 0m);
    }

    // =========================================================================================
    // CSR-ML2 — Validaciones de negocio (antes [Skip] en CSR-ML1): ahora ejercitan el validador
    // real y puro `ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar` (sin DB), que es el
    // mismo que usa `ConfiguracionPagoService.GuardarCuotasSinRecargoCreditoPersonalAsync`. Cubre
    // V1-V6 del spec CSR-ML2. Las pruebas de persistencia (V7-V9, con DB) viven en
    // `ConfiguracionCreditoPersonalCuotaSinRecargoPersistenciaTests` (Integration).
    // =========================================================================================

    [Theory]
    [InlineData(10, 10, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, true)]        // V5 — 9 de 10 sin recargo, 10% -> válido (queda 1 cuota con recargo)
    [InlineData(10, 10, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, false)]   // V4 — 10 de 10 sin recargo, 10% -> inválido (ninguna cuota lleva el recargo)
    [InlineData(10, 0, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, true)]     // V6 — 10 de 10 sin recargo, 0% -> válido (no hay recargo que exigir)
    public void Validacion_RecargoPositivoRequiereAlMenosUnaCuotaConRecargo(
        int cantidadCuotas, decimal porcentajeRecargo, int[] cuotasSinRecargo, bool esperadoValido)
    {
        var errores = ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar(
            cantidadCuotas, porcentajeRecargo, cuotasSinRecargo);

        Assert.Equal(esperadoValido, errores.Count == 0);
    }

    [Fact]
    public void V1_NumeroCuotaCero_FueraDeRangoInferior_EsInvalida()
    {
        // Regla congelada: NumeroCuota debe estar en [1, CantidadCuotas]. 0 debe rechazarse.
        var errores = ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar(
            cantidadCuotas: 10, tasaMensual: 10m, numerosCuota: new[] { 0 });

        Assert.NotEmpty(errores);
    }

    [Fact]
    public void V2_NumeroCuotaOnce_FueraDeRangoSuperiorDeUnPlanDeDiez_EsInvalida()
    {
        // Regla congelada: plan de 10 cuotas, NumeroCuota = 11 (> CantidadCuotas) debe rechazarse.
        var errores = ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar(
            cantidadCuotas: 10, tasaMensual: 10m, numerosCuota: new[] { 11 });

        Assert.NotEmpty(errores);
    }

    [Fact]
    public void V3_NumeroCuotaDuplicado_UnoTresTres_EsInvalida()
    {
        // Regla congelada: (ConfiguracionCreditoPersonalCuotaId, NumeroCuota) es único — un mismo
        // número de cuota no puede declararse "sin recargo" dos veces para el mismo plan.
        var errores = ConfiguracionCreditoPersonalCuotaSinRecargoRules.Validar(
            cantidadCuotas: 10, tasaMensual: 10m, numerosCuota: new[] { 1, 3, 3 });

        Assert.NotEmpty(errores);
    }

    // =========================================================================================
    // CSR-ML3 — cobertura matemática obligatoria adicional (M1-M6) sobre la matemática canónica
    // de SimularPlanCredito con cuotasSinRecargo.
    // =========================================================================================

    [Fact]
    public void M1_ColeccionVacia_ReproduceExactamenteElAlgoritmoAnterior_VectorCuotaPorCuota()
    {
        // Caso elegido a propósito porque diverge si la implementación distribuyera Capital y
        // Recargo de forma SIEMPRE independiente (incluso sin exclusiones): saldo 80.000 / 12
        // cuotas dejan residuo 8 tanto en la división de capital como en la de recargo (8+8=16 >=
        // 12), así que "capitalBase + recargoBase" por cuota (733.332) NO coincide con
        // "totalBase" (733.333) del algoritmo vigente si se dividen por separado. La rama de
        // compatibilidad (cuotasSinRecargo vacío) debe seguir usando el algoritmo anterior
        // (Total y Recargo distribuidos, Capital = Total - Recargo) para que este caso cierre
        // igual que hoy.
        var service = CrearService();

        var sinParametro = service.SimularPlanCredito(100_000m, 20_000m, 12, 10m, 0m, FechaPrimeraCuota);
        var conColeccionVacia = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 20_000m,
            cuotas: 12,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: Array.Empty<int>());

        Assert.Equal(sinParametro.Cuotas.Count, conColeccionVacia.Cuotas.Count);
        for (var i = 0; i < sinParametro.Cuotas.Count; i++)
        {
            Assert.Equal(sinParametro.Cuotas[i].Capital, conColeccionVacia.Cuotas[i].Capital);
            Assert.Equal(sinParametro.Cuotas[i].Interes, conColeccionVacia.Cuotas[i].Interes);
            Assert.Equal(sinParametro.Cuotas[i].Total, conColeccionVacia.Cuotas[i].Total);
        }
    }

    [Fact]
    public void M2_OrdenDeEntrada_UnoTresCinco_IgualQueCincoUnoTres()
    {
        var service = CrearService();

        var resultadoA = service.SimularPlanCredito(
            100_000m, 0m, 10, 10m, 0m, FechaPrimeraCuota, cuotasSinRecargo: new[] { 1, 3, 5 });
        var resultadoB = service.SimularPlanCredito(
            100_000m, 0m, 10, 10m, 0m, FechaPrimeraCuota, cuotasSinRecargo: new[] { 5, 1, 3 });

        Assert.Equal(resultadoA.Cuotas.Count, resultadoB.Cuotas.Count);
        for (var i = 0; i < resultadoA.Cuotas.Count; i++)
        {
            Assert.Equal(resultadoA.Cuotas[i].Capital, resultadoB.Cuotas[i].Capital);
            Assert.Equal(resultadoA.Cuotas[i].Interes, resultadoB.Cuotas[i].Interes);
            Assert.Equal(resultadoA.Cuotas[i].Total, resultadoB.Cuotas[i].Total);
        }
    }

    [Fact]
    public void M3_Duplicados_UnoTresTres_SeRechaza()
    {
        var service = CrearService();

        Assert.Throws<ArgumentException>(() => service.SimularPlanCredito(
            100_000m, 0m, 10, 10m, 0m, FechaPrimeraCuota, cuotasSinRecargo: new[] { 1, 3, 3 }));
    }

    [Fact]
    public void M4_ResiduoSimultaneo_CapitalYRecargoNoDivideExacto_UltimaCuotaSinRecargo()
    {
        // 5 cuotas, cuota 5 sin recargo. Ni el saldo (10001.01) ni el recargo (1000.10 entre 4
        // cuotas elegibles) dividen exacto: permite verificar residuoCapital (cae en la cuota 5,
        // la última del plan) y residuoRecargo (cae en la cuota 4, la última CON recargo) de
        // forma independiente, sin que uno contamine al otro.
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 10_001.01m,
            anticipo: 0m,
            cuotas: 5,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: new[] { 5 });

        Assert.Equal(10_001.01m, resultado.MontoFinanciado);
        Assert.Equal(1_000.10m, resultado.InteresTotal);

        // Residuo de capital: cuota 5 (última del plan) absorbe el centavo sobrante.
        Assert.Equal(2_000.20m, resultado.Cuotas[0].Capital);
        Assert.Equal(2_000.20m, resultado.Cuotas[1].Capital);
        Assert.Equal(2_000.20m, resultado.Cuotas[2].Capital);
        Assert.Equal(2_000.20m, resultado.Cuotas[3].Capital);
        Assert.Equal(2_000.21m, resultado.Cuotas[4].Capital);

        // Cuota 5 sin recargo, siempre.
        Assert.Equal(0m, resultado.Cuotas[4].Interes);

        // Residuo de recargo: cuota 4 (última CON recargo) absorbe los 2 centavos sobrantes,
        // no la cuota 5.
        Assert.Equal(250.02m, resultado.Cuotas[0].Interes);
        Assert.Equal(250.02m, resultado.Cuotas[1].Interes);
        Assert.Equal(250.02m, resultado.Cuotas[2].Interes);
        Assert.Equal(250.04m, resultado.Cuotas[3].Interes);

        Assert.Equal(10_001.01m, resultado.Cuotas.Sum(c => c.Capital));
        Assert.Equal(1_000.10m, resultado.Cuotas.Sum(c => c.Interes));
        Assert.Equal(11_001.11m, resultado.Cuotas.Sum(c => c.Total));
    }

    [Fact]
    public void M5_TodasSinRecargo_CeroPorciento_EsValido()
    {
        var service = CrearService();

        var resultado = service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 5,
            porcentajeRecargo: 0m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: new[] { 1, 2, 3, 4, 5 });

        Assert.Equal(0m, resultado.InteresTotal);
        Assert.All(resultado.Cuotas, c =>
        {
            Assert.Equal(0m, c.Interes);
            Assert.Equal(20_000m, c.Capital);
            Assert.Equal(20_000m, c.Total);
        });
    }

    [Fact]
    public void M6_TodasSinRecargo_ConRecargoMayorACero_EsInvalido()
    {
        var service = CrearService();

        Assert.Throws<ArgumentException>(() => service.SimularPlanCredito(
            totalVenta: 100_000m,
            anticipo: 0m,
            cuotas: 5,
            porcentajeRecargo: 10m,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: FechaPrimeraCuota,
            cuotasSinRecargo: new[] { 1, 2, 3, 4, 5 }));
    }
}
