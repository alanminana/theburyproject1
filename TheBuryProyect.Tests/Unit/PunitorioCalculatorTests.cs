using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios de <see cref="PunitorioCalculator"/> (PUN-ML4). Cubren exactamente los casos
/// obligatorios del pedido. Todos son puros: sin base de datos, sin reloj, sin mocks.
/// </summary>
public class PunitorioCalculatorTests
{
    private readonly PunitorioCalculator _sut = new();

    private static readonly DateOnly Vencimiento = new(2026, 1, 1);

    private static PunitorioCalculoEntrada Entrada(
        decimal monto,
        DateOnly calculo,
        DateOnly? vencimiento = null,
        IEnumerable<PagoAplicadoPunitorioEntrada>? pagos = null,
        IEnumerable<ConfiguracionPunitorioEntrada>? configuraciones = null) => new()
    {
        MontoOriginalCuota = monto,
        FechaVencimiento = vencimiento ?? Vencimiento,
        FechaCalculo = calculo,
        PagosAplicados = pagos?.ToList() ?? new List<PagoAplicadoPunitorioEntrada>(),
        Configuraciones = configuraciones?.ToList() ?? new List<ConfiguracionPunitorioEntrada>()
    };

    private static PagoAplicadoPunitorioEntrada Pago(
        DateOnly fecha, decimal? importe, EstadoPagoCuota estado = EstadoPagoCuota.Aplicado, bool historialCompleto = true) => new()
    {
        FechaPagoComercial = fecha,
        ImporteAplicadoCuota = importe,
        Estado = estado,
        HistorialCompleto = historialCompleto
    };

    private static ConfiguracionPunitorioEntrada Config(
        int id, DateOnly vigenteDesde, decimal porcentaje, int periodoDias, int diasGracia,
        bool activa = true, bool prorrateoDiario = true, bool aplicacionRetroactiva = false) => new()
    {
        Id = id,
        VigenteDesde = vigenteDesde,
        Porcentaje = porcentaje,
        PeriodoDias = periodoDias,
        DiasGracia = diasGracia,
        Activa = activa,
        ProrrateoDiario = prorrateoDiario,
        AplicacionRetroactiva = aplicacionRetroactiva
    };

    // =====================================================================
    // Gracia — ejemplo obligatorio del pedido (vencimiento día 1, gracia 5)
    // =====================================================================

    [Fact]
    public void DentroDeLaGracia_PunitorioCero()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(5),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 5) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.DentroDeGracia, r.EstadoResultado);
        Assert.Equal(5, r.DiasTranscurridos);
        Assert.Equal(0m, r.PunitorioRedondeado);
        Assert.Equal(0m, r.PunitorioExacto);
    }

    [Fact]
    public void PrimerDiaPosteriorALaGracia_CuentaRetroactivoDesdeElVencimiento()
    {
        // Ejemplo del pedido: vencimiento día 1, gracia 5, cálculo día 7 → 6 días desde el
        // vencimiento (no 1 día desde el fin de la gracia). Saldo 1.000, 10% cada 20 días.
        var entrada = Entrada(1_000m, Vencimiento.AddDays(6),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 5) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(6, r.DiasTranscurridos);
        var esperado = 1_000m * (10m / 100m) * 6 / 20; // 30.00
        Assert.Equal(esperado, r.PunitorioExacto);
        Assert.Equal(30.00m, r.PunitorioRedondeado);

        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(Vencimiento, segmento.Desde);
        Assert.Equal(Vencimiento.AddDays(6), segmento.Hasta);
        Assert.Equal(6, segmento.Dias);
    }

    // =====================================================================
    // 10% cada 20 días — prorrateo lineal
    // =====================================================================

    // 10% cada 20 días sobre una base de 1.000: 10 días → 5% del saldo ($50); 20 días → 10% ($100);
    // 30 días → 15% ($150). El pedido expresa el resultado como porcentaje del saldo — el importe en
    // pesos es ese porcentaje aplicado a la base de la prueba.
    [Theory]
    [InlineData(10, 50.00)]
    [InlineData(20, 100.00)]
    [InlineData(30, 150.00)]
    public void ProrrateoLineal_10PorcientoCada20Dias(int dias, decimal esperado)
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(dias),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(esperado, r.PunitorioRedondeado);
    }

    // =====================================================================
    // Ausencia / inactiva / 0% activo — no deben confundirse entre sí
    // =====================================================================

    [Fact]
    public void ConfiguracionActivaConCeroPorciento_EsCalculadoCero_NoAusencia()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 0m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(0m, r.PunitorioRedondeado);
        Assert.NotNull(r.PunitorioExacto);
    }

    [Fact]
    public void SinConfiguracion_NoHayTotalAutoritativo()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30));

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.SinConfiguracion, r.EstadoResultado);
        Assert.Null(r.PunitorioRedondeado);
        Assert.Null(r.PunitorioExacto);
    }

    [Fact]
    public void ConfiguracionInactiva_EsDistintaDeAusenciaYDeCalculadoCero()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0, activa: false) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.ConfiguracionInactiva, r.EstadoResultado);
        Assert.Equal(0m, r.PunitorioRedondeado); // calculable (0), no ausente (null)
    }

    [Fact]
    public void ConfiguracionFutura_TodaviaNoVigente_EsSinConfiguracion()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[] { Config(1, Vencimiento.AddDays(20), porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.SinConfiguracion, r.EstadoResultado);
    }

    // =====================================================================
    // Cambio de configuración durante la mora
    // =====================================================================

    [Fact]
    public void CambioDeConfiguracion_CortaElTramo_CadaSegmentoUsaSuPropioPorcentajeYPeriodo()
    {
        var cambioVigencia = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(20),
            configuraciones: new[]
            {
                Config(1, Vencimiento, porcentaje: 5m, periodoDias: 15, diasGracia: 0),
                Config(2, cambioVigencia, porcentaje: 8m, periodoDias: 15, diasGracia: 0)
            });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(2, r.Segmentos.Count);

        var seg1 = r.Segmentos[0];
        Assert.Equal(Vencimiento, seg1.Desde);
        Assert.Equal(cambioVigencia, seg1.Hasta);
        Assert.Equal(10, seg1.Dias);
        Assert.Equal(1, seg1.ConfiguracionPunitorioId);
        Assert.Equal(1_000m * 5m / 100m * 10 / 15, seg1.ImporteExacto);
        Assert.Equal(MotivoLimiteSegmento.Vencimiento, seg1.MotivoDeInicio);
        Assert.Equal(MotivoLimiteSegmento.NuevaConfiguracion, seg1.MotivoDeFin);

        var seg2 = r.Segmentos[1];
        Assert.Equal(cambioVigencia, seg2.Desde);
        Assert.Equal(Vencimiento.AddDays(20), seg2.Hasta);
        Assert.Equal(10, seg2.Dias);
        Assert.Equal(2, seg2.ConfiguracionPunitorioId);
        Assert.Equal(1_000m * 8m / 100m * 10 / 15, seg2.ImporteExacto);
        Assert.Equal(MotivoLimiteSegmento.NuevaConfiguracion, seg2.MotivoDeInicio);
        Assert.Equal(MotivoLimiteSegmento.FechaCalculo, seg2.MotivoDeFin);

        var esperado = Math.Round(seg1.ImporteExacto!.Value + seg2.ImporteExacto!.Value, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(esperado, r.PunitorioRedondeado);
    }

    // =====================================================================
    // Pagos parciales — antes, el día del vencimiento, durante y después de la gracia
    // =====================================================================

    [Fact]
    public void PagoParcial_AntesDelVencimiento_ReduceElSaldoDeApertura()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            pagos: new[] { Pago(Vencimiento.AddDays(-5), 400m) },
            configuraciones: new[] { Config(1, Vencimiento.AddDays(-100), porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(600m, r.SaldoInicial);
        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(600m, segmento.SaldoBase);
        Assert.Equal(600m * 10m / 100m * 30 / 20, segmento.ImporteExacto);
    }

    [Fact]
    public void PagoParcial_ElDiaDelVencimiento_ReduceElSaldoAntesDeQueSeDevenguenDias()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(20),
            pagos: new[] { Pago(Vencimiento, 300m) },
            configuraciones: new[] { Config(1, Vencimiento.AddDays(-1), porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(700m, r.SaldoInicial);
        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(700m, segmento.SaldoBase);
        Assert.Equal(20, segmento.Dias);
    }

    [Fact]
    public void PagoParcial_DuranteLaGracia_ReduceLaBaseQueSeUsaSiLuegoSeSuperaLaGracia()
    {
        var pagoEnGracia = Vencimiento.AddDays(3);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(10),
            pagos: new[] { Pago(pagoEnGracia, 400m) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 5) });

        var r = _sut.Calcular(entrada);

        // Gracia=5, transcurridos=10 → se supera; ambos tramos (antes y después del pago) se
        // calculan, cada uno con su propio saldo — nada se salta por haber caído en gracia.
        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(2, r.Segmentos.Count);

        var antes = r.Segmentos[0];
        Assert.Equal(Vencimiento, antes.Desde);
        Assert.Equal(pagoEnGracia, antes.Hasta);
        Assert.Equal(1_000m, antes.SaldoBase);

        var despues = r.Segmentos[1];
        Assert.Equal(pagoEnGracia, despues.Desde);
        Assert.Equal(600m, despues.SaldoBase);

        var esperado = (1_000m * 10m / 100m * 3 / 20) + (600m * 10m / 100m * 7 / 20);
        Assert.Equal(Math.Round(esperado, 2, MidpointRounding.AwayFromZero), r.PunitorioRedondeado);
    }

    [Fact]
    public void PagoParcial_DespuesDeLaGracia_CreaUnNuevoSegmentoDesdeSuFecha()
    {
        var pagoPostGracia = Vencimiento.AddDays(8);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(15),
            pagos: new[] { Pago(pagoPostGracia, 400m) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 5) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(2, r.Segmentos.Count);
        Assert.Equal(MotivoLimiteSegmento.Pago, r.Segmentos[0].MotivoDeFin);
        Assert.Equal(MotivoLimiteSegmento.Pago, r.Segmentos[1].MotivoDeInicio);
        Assert.Equal(600m, r.Segmentos[1].SaldoBase);
    }

    // =====================================================================
    // Varios pagos
    // =====================================================================

    [Fact]
    public void DosPagos_EnFechasDiferentes_GeneranDosCortes()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            pagos: new[]
            {
                Pago(Vencimiento.AddDays(10), 300m),
                Pago(Vencimiento.AddDays(20), 200m)
            },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(3, r.Segmentos.Count);
        Assert.Equal(1_000m, r.Segmentos[0].SaldoBase);
        Assert.Equal(700m, r.Segmentos[1].SaldoBase);
        Assert.Equal(500m, r.Segmentos[2].SaldoBase);
        Assert.Equal(500m, r.SaldoFinal);
    }

    [Fact]
    public void DosPagos_ElMismoDia_SeAgrupanDeterministicamente()
    {
        var fechaPago = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(20),
            pagos: new[]
            {
                Pago(fechaPago, 200m),
                Pago(fechaPago, 150m)
            },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(2, r.Segmentos.Count);
        Assert.Equal(650m, r.Segmentos[1].SaldoBase); // 1000 - 200 - 150
        Assert.Equal(650m, r.SaldoFinal);
    }

    // =====================================================================
    // Saldo cero — antes del vencimiento y durante la mora
    // =====================================================================

    [Fact]
    public void PagoTotal_AntesDelVencimiento_EsSinSaldo()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            pagos: new[] { Pago(Vencimiento.AddDays(-1), 1_000m) },
            configuraciones: new[] { Config(1, Vencimiento.AddDays(-10), porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.SinSaldo, r.EstadoResultado);
        Assert.Equal(0m, r.SaldoInicial);
        Assert.Equal(0m, r.SaldoFinal);
        Assert.Equal(0m, r.PunitorioRedondeado);
        Assert.Empty(r.Segmentos);
    }

    [Fact]
    public void PagoTotal_DuranteLaMora_NoDevengaDespuesDeLaCancelacion()
    {
        var fechaCancelacion = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            pagos: new[] { Pago(fechaCancelacion, 1_000m) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(0m, r.SaldoFinal);

        var ultimo = r.Segmentos[^1];
        Assert.Equal(fechaCancelacion, ultimo.Hasta);
        Assert.Equal(MotivoLimiteSegmento.SaldoCancelado, ultimo.MotivoDeFin);

        // Solo se devengó hasta la cancelación (10 días), nada de los 20 posteriores:
        // 1.000 * 10% * 10/20 = 50.00
        Assert.Equal(50.00m, r.PunitorioRedondeado);
    }

    [Fact]
    public void PagoAplicado_ElMismoDiaDeCalculo_ReduceSaldoFinalSinGenerarDiasPosteriores()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(15),
            pagos: new[] { Pago(Vencimiento.AddDays(15), 1_000m) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        // El pago del día de cálculo no agrega un segmento propio (0 días), pero sí baja el saldo final.
        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(15, segmento.Dias);
        Assert.Equal(1_000m, segmento.SaldoBase);
        Assert.Equal(0m, r.SaldoFinal);

        // Sí se devengaron los 15 días previos: 1.000 * 10% * 15/20 = 75.00
        Assert.Equal(75.00m, r.PunitorioRedondeado);
    }

    // =====================================================================
    // Pagos no efectivos
    // =====================================================================

    [Fact]
    public void PagoAnulado_NoReduceSaldo()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(20),
            pagos: new[] { Pago(Vencimiento.AddDays(5), 500m, EstadoPagoCuota.Anulado) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(1_000m, segmento.SaldoBase);
        Assert.Equal(1_000m, r.SaldoFinal);
    }

    [Fact]
    public void PagoRevertido_NoReduceSaldo()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(20),
            pagos: new[] { Pago(Vencimiento.AddDays(5), 500m, EstadoPagoCuota.Revertido) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(1_000m, segmento.SaldoBase);
        Assert.Equal(1_000m, r.SaldoFinal);
    }

    // =====================================================================
    // Historial incompleto
    // =====================================================================

    [Fact]
    public void HistorialIncompleto_NoInventaComposicion_NoDevuelveTotalCobrable()
    {
        var fechaAmbigua = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            pagos: new[] { Pago(fechaAmbigua, importe: null, historialCompleto: false) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.HistorialIncompleto, r.EstadoResultado);
        Assert.Null(r.PunitorioExacto);
        Assert.Null(r.PunitorioRedondeado);
        Assert.NotNull(r.Motivo);
        Assert.Contains(fechaAmbigua.ToString("yyyy-MM-dd"), r.Motivo);
    }

    [Fact]
    public void HistorialIncompleto_ConservaSegmentosConfiablesAnterioresParaDiagnostico()
    {
        var fechaAmbigua = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            pagos: new[]
            {
                Pago(Vencimiento.AddDays(5), 100m), // confiable, antes del punto ambiguo
                Pago(fechaAmbigua, importe: null, historialCompleto: false)
            },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.HistorialIncompleto, r.EstadoResultado);
        Assert.Null(r.PunitorioRedondeado);
        // Los tramos confiables antes del punto ambiguo (cortados a su vez por el pago del día 5)
        // quedan disponibles para diagnóstico; el último señala por qué no se siguió más allá.
        Assert.Equal(2, r.Segmentos.Count);
        Assert.Equal(Vencimiento, r.Segmentos[0].Desde);
        Assert.Equal(Vencimiento.AddDays(5), r.Segmentos[0].Hasta);
        Assert.Equal(MotivoLimiteSegmento.Pago, r.Segmentos[0].MotivoDeFin);
        Assert.Equal(Vencimiento.AddDays(5), r.Segmentos[1].Desde);
        Assert.Equal(fechaAmbigua, r.Segmentos[1].Hasta);
        Assert.Equal(MotivoLimiteSegmento.HistorialIncompleto, r.Segmentos[1].MotivoDeFin);
    }

    [Fact]
    public void HistorialIncompleto_SiCaeAntesOEnElVencimiento_NiElSaldoInicialEsConfiable()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(30),
            pagos: new[] { Pago(Vencimiento.AddDays(-2), importe: null, historialCompleto: false) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.HistorialIncompleto, r.EstadoResultado);
        Assert.Empty(r.Segmentos);
    }

    // =====================================================================
    // Entrada inválida
    // =====================================================================

    [Fact]
    public void PagoSuperiorAlSaldo_EsEntradaInvalida()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(10),
            pagos: new[] { Pago(Vencimiento.AddDays(1), 1_500m) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.EntradaInvalida, r.EstadoResultado);
        Assert.Null(r.PunitorioRedondeado);
    }

    [Fact]
    public void ConfiguracionesConVigenciaDuplicada_EsEntradaInvalida_RechazoDeterministico()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[]
            {
                Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0),
                Config(2, Vencimiento, porcentaje: 15m, periodoDias: 20, diasGracia: 0)
            });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.EntradaInvalida, r.EstadoResultado);
    }

    [Fact]
    public void ProrrateoDiarioFalse_EsConfiguracionNoSoportada_EntradaInvalida()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0, prorrateoDiario: false) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.EntradaInvalida, r.EstadoResultado);
        Assert.NotNull(r.Motivo);
    }

    [Fact]
    public void MontoOriginalCuotaNoPositivo_EsEntradaInvalida()
    {
        var r = _sut.Calcular(Entrada(0m, Vencimiento.AddDays(10)));

        Assert.Equal(EstadoResultadoPunitorio.EntradaInvalida, r.EstadoResultado);
    }

    [Fact]
    public void FechaCalculoAnteriorAlVencimiento_EsEntradaInvalida()
    {
        var r = _sut.Calcular(Entrada(1_000m, Vencimiento.AddDays(-1)));

        Assert.Equal(EstadoResultadoPunitorio.EntradaInvalida, r.EstadoResultado);
    }

    [Fact]
    public void EntradaNula_LanzaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Calcular(null!));
    }

    // =====================================================================
    // El punitorio no depende del recargo original de la cuota (Credito.TasaInteres)
    // =====================================================================

    [Fact]
    public void ElPunitorioDependeExclusivamenteDeLaConfiguracion_NoExisteCampoParaLaTasaDelCredito()
    {
        // La entrada no tiene forma de recibir Credito.TasaInteres: dos cuotas con el mismo saldo
        // impago y la misma configuración de punitorio SIEMPRE dan el mismo resultado, sin
        // importar de qué crédito (0% o 45% de recargo) provenga ese saldo.
        var configuraciones = new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) };

        var deCreditoSinRecargo = _sut.Calcular(Entrada(1_000m, Vencimiento.AddDays(30), configuraciones: configuraciones));
        var deCreditoConRecargo = _sut.Calcular(Entrada(1_000m, Vencimiento.AddDays(30), configuraciones: configuraciones));

        Assert.Equal(deCreditoSinRecargo.PunitorioRedondeado, deCreditoConRecargo.PunitorioRedondeado);
    }

    // =====================================================================
    // Determinismo / pureza
    // =====================================================================

    [Fact]
    public void DosCuotasConLosMismosDatos_ResultadosIndependientesYDeterministas()
    {
        var configuraciones = new[] { Config(1, Vencimiento, porcentaje: 7m, periodoDias: 30, diasGracia: 3) };
        var pagos = new[] { Pago(Vencimiento.AddDays(4), 250m) };

        var cuotaA = Entrada(2_000m, Vencimiento.AddDays(40), pagos: pagos, configuraciones: configuraciones);
        var cuotaB = Entrada(2_000m, Vencimiento.AddDays(40), pagos: pagos, configuraciones: configuraciones);

        var r1 = _sut.Calcular(cuotaA);
        var r2 = _sut.Calcular(cuotaB);

        Assert.Equal(r1.EstadoResultado, r2.EstadoResultado);
        Assert.Equal(r1.PunitorioRedondeado, r2.PunitorioRedondeado);
        Assert.Equal(r1.Segmentos.Count, r2.Segmentos.Count);
    }

    [Fact]
    public void LlamadaRepetida_ProduceElMismoResultadoYNoModificaLaEntrada()
    {
        var configuraciones = new[] { Config(1, Vencimiento, porcentaje: 7m, periodoDias: 30, diasGracia: 3) };
        var pagos = new[] { Pago(Vencimiento.AddDays(4), 250m) };
        var entrada = Entrada(2_000m, Vencimiento.AddDays(40), pagos: pagos, configuraciones: configuraciones);

        var r1 = _sut.Calcular(entrada);
        var r2 = _sut.Calcular(entrada);
        var r3 = _sut.Calcular(entrada);

        Assert.Equal(r1.PunitorioRedondeado, r2.PunitorioRedondeado);
        Assert.Equal(r2.PunitorioRedondeado, r3.PunitorioRedondeado);
        Assert.Equal(2_000m, entrada.MontoOriginalCuota); // la entrada no fue mutada
        Assert.Single(entrada.PagosAplicados);
        Assert.Single(entrada.Configuraciones);
    }

    [Fact]
    public void AplicacionRetroactiva_NoTieneEfectoEnElCalculo()
    {
        var conFlagFalse = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0, aplicacionRetroactiva: false) });
        var conFlagTrue = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0, aplicacionRetroactiva: true) });

        var r1 = _sut.Calcular(conFlagFalse);
        var r2 = _sut.Calcular(conFlagTrue);

        Assert.Equal(r1.PunitorioRedondeado, r2.PunitorioRedondeado);
        Assert.Equal(r1.EstadoResultado, r2.EstadoResultado);
    }

    // =====================================================================
    // Invariantes generales
    // =====================================================================

    [Fact]
    public void Invariantes_SaldosNoNegativos_SegmentosOrdenadosSinSuperposicion_DiasCoincidenConLaVentana()
    {
        var entrada = Entrada(10_000m, Vencimiento.AddDays(50),
            pagos: new[]
            {
                Pago(Vencimiento.AddDays(10), 1_000m),
                Pago(Vencimiento.AddDays(25), 2_000m)
            },
            configuraciones: new[]
            {
                Config(1, Vencimiento, porcentaje: 5m, periodoDias: 30, diasGracia: 0),
                Config(2, Vencimiento.AddDays(20), porcentaje: 6m, periodoDias: 30, diasGracia: 0)
            });

        var r = _sut.Calcular(entrada);

        Assert.True(r.SaldoInicial >= 0m);
        Assert.True(r.SaldoFinal >= 0m);
        Assert.True(r.PunitorioRedondeado is null or >= 0m);

        for (var i = 0; i < r.Segmentos.Count; i++)
        {
            var s = r.Segmentos[i];
            Assert.True(s.Dias >= 0);
            Assert.True(s.SaldoBase >= 0m);
            Assert.True(s.ImporteExacto is null or >= 0m);
            Assert.True(s.Desde < s.Hasta);
            if (i > 0)
                Assert.Equal(r.Segmentos[i - 1].Hasta, s.Desde); // contiguos, sin huecos de fecha ni superposición
        }

        Assert.Equal(Vencimiento, r.Segmentos[0].Desde);
        Assert.Equal(entrada.FechaCalculo, r.Segmentos[^1].Hasta);

        var sumaDias = r.Segmentos.Sum(s => s.Dias);
        Assert.Equal(entrada.FechaCalculo.DayNumber - Vencimiento.DayNumber, sumaDias);
    }

    // =====================================================================
    // Residuos de redondeo y período extenso — sin overflow, sin límite artificial
    // =====================================================================

    [Fact]
    public void ValoresConResiduos_RedondeaUnaSolaVezElTotalConAwayFromZero()
    {
        // 100 a un porcentaje que produce infinitas cifras decimales al dividir por 3 días de período.
        var entrada = Entrada(100m, Vencimiento.AddDays(1),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 3, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        // Exacto sin redondear: 100 * 0.10 * 1 / 3 = 3.3333...
        Assert.True(r.PunitorioExacto is > 3.333m and < 3.334m);
        Assert.Equal(3.33m, r.PunitorioRedondeado);
    }

    [Fact]
    public void PeriodoGrandeYMoraExtensa_SinOverflowNiLimiteArtificial()
    {
        var entrada = Entrada(50_000_000m, Vencimiento.AddDays(20_000),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 25m, periodoDias: 30, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(20_000, r.DiasTranscurridos);
        var esperado = Math.Round(50_000_000m * 25m / 100m * 20_000 / 30, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(esperado, r.PunitorioRedondeado);
        Assert.True(r.PunitorioRedondeado > 0m);
    }

    // =====================================================================
    // PUN-ML4 (corrección) — ventana temporal autoritativa: [FechaVencimiento, FechaCalculo).
    // El caller pasa Cuota.Pagos y el historial completo de ConfiguracionPunitorio tal cual
    // están; datos posteriores a FechaCalculo nunca deben alterar ni invalidar un cálculo
    // histórico.
    // =====================================================================

    [Fact]
    public void ConfiguracionFutura_ProrrateoDiarioFalse_NoInvalidaUnCalculoAnterior()
    {
        var entrada = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[]
            {
                Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0),
                // VigenteDesde >= FechaCalculo: no gobierna ningún tramo de este cálculo.
                Config(2, Vencimiento.AddDays(50), porcentaje: 8m, periodoDias: 20, diasGracia: 0, prorrateoDiario: false)
            });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(50.00m, r.PunitorioRedondeado);
    }

    [Fact]
    public void ConfiguracionFutura_Inactiva_NoModificaUnResultadoHistoricoActivo()
    {
        var sinFutura = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });
        var conFutura = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[]
            {
                Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0),
                Config(2, Vencimiento.AddDays(50), porcentaje: 10m, periodoDias: 20, diasGracia: 0, activa: false)
            });

        var r1 = _sut.Calcular(sinFutura);
        var r2 = _sut.Calcular(conFutura);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r2.EstadoResultado);
        Assert.Equal(r1.EstadoResultado, r2.EstadoResultado);
        Assert.Equal(r1.PunitorioRedondeado, r2.PunitorioRedondeado);
        Assert.Equal(r1.Segmentos.Count, r2.Segmentos.Count);
    }

    [Fact]
    public void ConfiguracionConVigenteDesdeIgualAFechaCalculo_QuedaFueraDeLaVentana()
    {
        var fechaCalculo = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, fechaCalculo,
            configuraciones: new[]
            {
                Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0),
                // VigenteDesde == FechaCalculo: fuera del intervalo semiabierto, no crea segmento.
                Config(2, fechaCalculo, porcentaje: 999m, periodoDias: 20, diasGracia: 0)
            });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(1, segmento.ConfiguracionPunitorioId);
        Assert.Equal(50.00m, r.PunitorioRedondeado);
    }

    [Fact]
    public void CambioDeConfiguracion_DentroDeLaVentana_SiCortaElTramo()
    {
        var cambio = Vencimiento.AddDays(4);
        var entrada = Entrada(1_000m, Vencimiento.AddDays(10),
            configuraciones: new[]
            {
                Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0),
                Config(2, cambio, porcentaje: 20m, periodoDias: 20, diasGracia: 0)
            });

        var r = _sut.Calcular(entrada);

        Assert.Equal(2, r.Segmentos.Count);
        Assert.Equal(1, r.Segmentos[0].ConfiguracionPunitorioId);
        Assert.Equal(2, r.Segmentos[1].ConfiguracionPunitorioId);
        Assert.Equal(cambio, r.Segmentos[0].Hasta);
        Assert.Equal(cambio, r.Segmentos[1].Desde);
    }

    [Fact]
    public void PagoPosteriorAFechaCalculo_NoReduceSaldoNiPunitorioHistorico()
    {
        var fechaCalculo = Vencimiento.AddDays(10);
        var sinPagoFuturo = Entrada(1_000m, fechaCalculo,
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });
        var conPagoFuturo = Entrada(1_000m, fechaCalculo,
            pagos: new[] { Pago(fechaCalculo.AddDays(5), 400m) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r1 = _sut.Calcular(sinPagoFuturo);
        var r2 = _sut.Calcular(conPagoFuturo);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r2.EstadoResultado);
        Assert.Equal(r1.SaldoFinal, r2.SaldoFinal);
        Assert.Equal(r1.PunitorioRedondeado, r2.PunitorioRedondeado);
        Assert.Equal(r1.Segmentos.Count, r2.Segmentos.Count);
    }

    [Fact]
    public void PagoPosteriorAFechaCalculo_Incompleto_NoConvierteEnHistorialIncompleto()
    {
        var fechaCalculo = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, fechaCalculo,
            pagos: new[] { Pago(fechaCalculo.AddDays(5), importe: null, historialCompleto: false) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        Assert.Equal(EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
        Assert.Equal(50.00m, r.PunitorioRedondeado);
    }

    [Fact]
    public void PagoParcial_ElDiaDeCalculo_ReduceSaldoFinalPeroNoLosDiasDevengados()
    {
        var fechaCalculo = Vencimiento.AddDays(10);
        var entrada = Entrada(1_000m, fechaCalculo,
            pagos: new[] { Pago(fechaCalculo, 300m) },
            configuraciones: new[] { Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0) });

        var r = _sut.Calcular(entrada);

        var segmento = Assert.Single(r.Segmentos);
        Assert.Equal(10, segmento.Dias);
        Assert.Equal(1_000m, segmento.SaldoBase); // el pago del día de cálculo no reduce la base ya devengada
        Assert.Equal(700m, r.SaldoFinal); // pero sí el saldo final reportado
        Assert.Equal(50.00m, r.PunitorioRedondeado); // 1000 * 10% * 10/20, sin descontar el pago del mismo día
    }

    [Fact]
    public void RepetirCalculoHistorico_TrasAgregarPagoYConfiguracionPosteriores_DevuelveElMismoResultado()
    {
        var fechaCalculo = Vencimiento.AddDays(10);
        var configuracionBase = Config(1, Vencimiento, porcentaje: 10m, periodoDias: 20, diasGracia: 0);

        var original = Entrada(1_000m, fechaCalculo, configuraciones: new[] { configuracionBase });
        var conEventosPosteriores = Entrada(1_000m, fechaCalculo,
            pagos: new[] { Pago(fechaCalculo.AddDays(20), 1_000m) },
            configuraciones: new[]
            {
                configuracionBase,
                Config(2, fechaCalculo.AddDays(30), porcentaje: 50m, periodoDias: 5, diasGracia: 0, activa: false, prorrateoDiario: false)
            });

        var r1 = _sut.Calcular(original);
        var r2 = _sut.Calcular(conEventosPosteriores);

        Assert.Equal(r1.EstadoResultado, r2.EstadoResultado);
        Assert.Equal(r1.SaldoInicial, r2.SaldoInicial);
        Assert.Equal(r1.SaldoFinal, r2.SaldoFinal);
        Assert.Equal(r1.DiasTranscurridos, r2.DiasTranscurridos);
        Assert.Equal(r1.PunitorioExacto, r2.PunitorioExacto);
        Assert.Equal(r1.PunitorioRedondeado, r2.PunitorioRedondeado);
        Assert.Equal(r1.Segmentos.Count, r2.Segmentos.Count);
        for (var i = 0; i < r1.Segmentos.Count; i++)
        {
            Assert.Equal(r1.Segmentos[i].Desde, r2.Segmentos[i].Desde);
            Assert.Equal(r1.Segmentos[i].Hasta, r2.Segmentos[i].Hasta);
            Assert.Equal(r1.Segmentos[i].ImporteExacto, r2.Segmentos[i].ImporteExacto);
            Assert.Equal(r1.Segmentos[i].ConfiguracionPunitorioId, r2.Segmentos[i].ConfiguracionPunitorioId);
        }
    }
}
