using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// C10 (2026-11-20, opción B aprobada por el propietario): gracia REAL. <c>DiasPunitorio = Max(0, DiasAtraso - DiasGracia)</c>; el día del
/// vencimiento es el día 0 y los primeros <c>DiasGracia</c> días no generan punitorio ni se acumulan retroactivamente.
/// Saldo 1.000, 10 % cada 20 días: cada día punitorio vale $5,00. Todos los importes están calculados a mano.
/// </summary>
public class PunitorioGraciaRealTests
{
    private readonly PunitorioCalculator _sut = new();

    private static readonly DateOnly Venc = new(2026, 11, 1);

    private static ConfiguracionPunitorioEntrada Config(int diasGracia, int id = 1, DateOnly? vigenteDesde = null) => new()
    {
        Id = id,
        VigenteDesde = vigenteDesde ?? new DateOnly(2020, 1, 1),
        Porcentaje = 10m,
        PeriodoDias = 20,
        DiasGracia = diasGracia,
        Activa = true,
        ProrrateoDiario = true
    };

    private PunitorioCalculoResultado Calcular(
        DateOnly vencimiento, DateOnly calculo, int diasGracia, decimal monto = 1_000m,
        IEnumerable<PagoAplicadoPunitorioEntrada>? pagos = null, IEnumerable<ConfiguracionPunitorioEntrada>? configs = null) =>
        _sut.Calcular(new PunitorioCalculoEntrada
        {
            MontoOriginalCuota = monto,
            FechaVencimiento = vencimiento,
            FechaCalculo = calculo,
            PagosAplicados = pagos?.ToList() ?? new List<PagoAplicadoPunitorioEntrada>(),
            Configuraciones = configs?.ToList() ?? new List<ConfiguracionPunitorioEntrada> { Config(diasGracia) }
        });

    private static PagoAplicadoPunitorioEntrada Pago(DateOnly fecha, decimal importe) => new()
    {
        FechaPagoComercial = fecha,
        ImporteAplicadoCuota = importe,
        Estado = EstadoPagoCuota.Aplicado,
        HistorialCompleto = true
    };

    // ---------------------------------------------------------------- tabla del propietario (gracia 12)

    [Theory]
    [InlineData(0, 0, 0.00)]
    [InlineData(1, 0, 0.00)]
    [InlineData(11, 0, 0.00)]
    [InlineData(12, 0, 0.00)]
    [InlineData(13, 1, 5.00)]
    [InlineData(14, 2, 10.00)]
    [InlineData(15, 3, 15.00)]
    [InlineData(20, 8, 40.00)]
    [InlineData(30, 18, 90.00)]
    public void Gracia12_DiasDeAtrasoYDiasQueGeneranPunitorio(int atraso, int diasPunitorio, double importe)
    {
        var r = Calcular(Venc, Venc.AddDays(atraso), diasGracia: 12);

        Assert.Equal(atraso, r.DiasTranscurridos);
        Assert.Equal((decimal)importe, r.PunitorioRedondeado ?? 0m);
        var dias = r.Segmentos.Sum(s => s.DiasPunitorio);
        Assert.Equal(diasPunitorio, dias);
        Assert.Equal(Math.Max(0, atraso - 12), dias);   // la fórmula del propietario, tal cual
        Assert.Equal(atraso <= 12 ? EstadoResultadoPunitorio.DentroDeGracia : EstadoResultadoPunitorio.Calculado, r.EstadoResultado);
    }

    // ---------------------------------------------------------------- la gracia es configuración, no una constante

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(0, 10, 10)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 4, 1)]
    [InlineData(30, 30, 0)]
    [InlineData(30, 31, 1)]
    [InlineData(30, 45, 15)]
    public void GraciaDistintaDe12_UsaLaGraciaConfigurada(int gracia, int atraso, int diasPunitorio)
    {
        var r = Calcular(Venc, Venc.AddDays(atraso), gracia);

        Assert.Equal(diasPunitorio, r.Segmentos.Sum(s => s.DiasPunitorio));
        Assert.Equal(diasPunitorio * 5m, r.PunitorioRedondeado ?? 0m);
    }

    // ---------------------------------------------------------------- calendario: mes, año, bisiesto

    [Fact]
    public void CambioDeMesYDeAnio_ContaDiasCorridos()
    {
        var venc = new DateOnly(2026, 12, 25);

        var r = Calcular(venc, new DateOnly(2027, 1, 14), diasGracia: 12);   // 20 días de atraso

        Assert.Equal(20, r.DiasTranscurridos);
        Assert.Equal(8, r.Segmentos.Sum(s => s.DiasPunitorio));
        Assert.Equal(40.00m, r.PunitorioRedondeado);
    }

    [Fact]
    public void AnioBisiesto_29DeFebreroCuentaComoDia_YElAnioComunNo()
    {
        // 2028 es bisiesto: del 20/02 al 05/03 hay 14 días (febrero tiene 29). 2027 no: del 20/02 al 05/03 hay 13.
        var bisiesto = Calcular(new DateOnly(2028, 2, 20), new DateOnly(2028, 3, 5), diasGracia: 12);
        var comun = Calcular(new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 5), diasGracia: 12);

        Assert.Equal(14, bisiesto.DiasTranscurridos);
        Assert.Equal(2, bisiesto.Segmentos.Sum(s => s.DiasPunitorio));
        Assert.Equal(10.00m, bisiesto.PunitorioRedondeado);
        Assert.Equal(13, comun.DiasTranscurridos);
        Assert.Equal(1, comun.Segmentos.Sum(s => s.DiasPunitorio));
        Assert.Equal(5.00m, comun.PunitorioRedondeado);
    }

    // ---------------------------------------------------------------- cuotas parcialmente pagadas

    [Fact]
    public void PagoParcialDentroDeLaGracia_ElPunitorioDevengaSoloSobreElSaldoRestante()
    {
        // Pago de 400 el día 5 (gracia); cálculo día 20. Tramo [0,5): 0 días. Tramo [5,20) saldo 600: devengan los días 12..19 = 8 días.
        var r = Calcular(Venc, Venc.AddDays(20), 12, pagos: new[] { Pago(Venc.AddDays(5), 400m) });

        Assert.Equal(2, r.Segmentos.Count);
        Assert.Equal(0, r.Segmentos[0].DiasPunitorio);
        Assert.Equal(8, r.Segmentos[1].DiasPunitorio);
        Assert.Equal(600m, r.Segmentos[1].SaldoBase);
        Assert.Equal(24.00m, r.PunitorioRedondeado);   // 600 × 10 % × 8 / 20
    }

    [Fact]
    public void PagoParcialDespuesDeLaGracia_CadaTramoDevengaConSuPropioSaldo()
    {
        // Pago de 400 el día 15; cálculo día 20. Tramo [0,15) saldo 1.000: días 12..14 = 3 → 15,00. Tramo [15,20) saldo 600: 5 días → 15,00.
        var r = Calcular(Venc, Venc.AddDays(20), 12, pagos: new[] { Pago(Venc.AddDays(15), 400m) });

        Assert.Equal(3, r.Segmentos[0].DiasPunitorio);
        Assert.Equal(5, r.Segmentos[1].DiasPunitorio);
        Assert.Equal(30.00m, r.PunitorioRedondeado);
    }

    [Fact]
    public void PagoTotalDentroDeLaGracia_NoDejaPunitorio()
    {
        var r = Calcular(Venc, Venc.AddDays(40), 12, pagos: new[] { Pago(Venc.AddDays(10), 1_000m) });

        // Pagada dentro de la gracia: nada devenga (con la regla anterior, un cálculo posterior a la gracia cobraba retroactivo 10 días).
        Assert.Equal(0m, r.PunitorioRedondeado ?? 0m);
        Assert.Equal(0, r.Segmentos.Sum(s => s.DiasPunitorio));
    }

    // ---------------------------------------------------------------- cuota no vencida / adelantada

    [Fact]
    public void CuotaNoVencida_NoEsCalculable_SinPunitorio()
    {
        var r = Calcular(Venc, Venc.AddDays(-3), 12);

        Assert.Equal(EstadoResultadoPunitorio.EntradaInvalida, r.EstadoResultado);
        Assert.Null(r.PunitorioRedondeado);
    }

    [Fact]
    public void CuotaAdelantadaYSaldadaAntesDelVencimiento_NoDevengaNada()
    {
        var r = Calcular(Venc, Venc.AddDays(30), 12, pagos: new[] { Pago(Venc.AddDays(-5), 1_000m) });

        Assert.Equal(EstadoResultadoPunitorio.SinSaldo, r.EstadoResultado);
        Assert.Equal(0m, r.PunitorioRedondeado ?? 0m);
    }

    // ---------------------------------------------------------------- cambio de configuración con otra gracia

    [Fact]
    public void ConfiguracionNuevaConOtraGracia_CadaTramoUsaLaGraciaDeSuConfiguracion()
    {
        // Config 1 (gracia 12) hasta el día 10; config 2 (gracia 0) desde el día 10. Cálculo día 20.
        // Tramo [0,10) con gracia 12 → 0 días. Tramo [10,20) con gracia 0 → desde el día 10: 10 días.
        var configs = new[] { Config(12, id: 1), Config(0, id: 2, vigenteDesde: Venc.AddDays(10)) };

        var r = Calcular(Venc, Venc.AddDays(20), 12, configs: configs);

        Assert.Equal(new[] { 0, 10 }, r.Segmentos.Select(s => s.DiasPunitorio).ToArray());
        Assert.Equal(50.00m, r.PunitorioRedondeado);
    }

    // ---------------------------------------------------------------- invariantes

    [Fact]
    public void ConfiguracionInactiva_NoDevengaAunPasadaLaGracia()
    {
        var inactiva = new ConfiguracionPunitorioEntrada
        {
            Id = 1, VigenteDesde = new DateOnly(2020, 1, 1), Porcentaje = 10m, PeriodoDias = 20, DiasGracia = 12, Activa = false, ProrrateoDiario = true
        };

        var r = Calcular(Venc, Venc.AddDays(30), 12, configs: new[] { inactiva });

        Assert.Equal(EstadoResultadoPunitorio.ConfiguracionInactiva, r.EstadoResultado);
    }

    [Fact]
    public void GraciaRetroactiva_SoloConservaLaReglaAnterior_NoCambiaLasTasas()
    {
        var retro = _sut.Calcular(new PunitorioCalculoEntrada
        {
            MontoOriginalCuota = 1_000m, FechaVencimiento = Venc, FechaCalculo = Venc.AddDays(13), GraciaRetroactiva = true,
            Configuraciones = new[] { Config(12) }
        });

        Assert.Equal(65.00m, retro.PunitorioRedondeado);   // regla anterior: 13 días × $5
    }

    /// <summary>
    /// P-02 / Agente 3: parámetros iniciales de la puesta en marcha (0 % cada 30 días, 12 de gracia). Con tasa 0 el punitorio es $0 en
    /// cualquier atraso (antes, dentro y mucho después de la gracia), sin error ni importe negativo, y sin
    /// IVA (el punitorio no tiene concepto de IVA). Así la tasa 0 % no distorsiona nada fiscal hasta que el propietario la fije.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(45)]
    [InlineData(400)]
    public void ParametrosIniciales_Tasa0_30Dias_Gracia12_NoGeneranPunitorio(int atraso)
    {
        var config = new ConfiguracionPunitorioEntrada
        {
            Id = 1, VigenteDesde = new DateOnly(2020, 1, 1), Porcentaje = 0m, PeriodoDias = 30, DiasGracia = 12,
            Activa = true, ProrrateoDiario = true
        };

        var r = Calcular(Venc, Venc.AddDays(atraso), 12, configs: new[] { config });

        // Un resultado indeterminado (sin configuración / historial incompleto) tiene importe null: acá debe ser un $0 calculado.
        Assert.NotEqual(EstadoResultadoPunitorio.SinConfiguracion, r.EstadoResultado);
        Assert.NotEqual(EstadoResultadoPunitorio.HistorialIncompleto, r.EstadoResultado);
        Assert.Equal(atraso, r.DiasTranscurridos);
        Assert.NotNull(r.PunitorioRedondeado);
        Assert.Equal(0m, r.PunitorioRedondeado);
    }
}
