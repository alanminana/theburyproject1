using TheBuryProject.Helpers;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// P-02 — contraste de las decisiones funcionales del propietario del 2026-11-20 contra la implementación. Fijan como
/// contrato lo que ya coincidía (C3, C6, C10): los demás criterios tienen su cobertura en FiscalAlicuotasMixtasP02Tests,
/// RecargoMedioPagoIvaTests, ReporteDevolucionesNetasTests (C13) y los tests de crédito/recibo.
/// </summary>
public class CriteriosPropietario20261120Tests
{
    // ------------------------------------------------------------------ C3: residuo de redondeo absorbido en el IVA

    [Theory]
    [InlineData("0.40", "100.00", "20.60")]   // total redondeado hacia abajo: el residuo sale del IVA primero
    [InlineData("0.00", "100.00", "21.00")]
    public void C3_ResiduoPositivo_SeAbsorbeEnElIva_ElNetoNoSeToca(string residuo, string netoEsperado, string ivaEsperado)
    {
        var (neto, iva) = RecargoMedioPagoIva.AbsorberRedondeo(100m, 21m, decimal.Parse(residuo, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(netoEsperado, System.Globalization.CultureInfo.InvariantCulture), neto);
        Assert.Equal(decimal.Parse(ivaEsperado, System.Globalization.CultureInfo.InvariantCulture), iva);
    }

    [Fact]
    public void C3_ResiduoNegativoDeV2_SeSumaAlIva_YEnVentaExentaAlNeto_SinInventarIva()
    {
        var conIva = RecargoMedioPagoIva.AbsorberRedondeo(100m, 21m, -0.40m);
        Assert.Equal((100m, 21.40m), conIva);

        var exenta = RecargoMedioPagoIva.AbsorberRedondeo(100m, 0m, -0.40m);
        Assert.Equal((100.40m, 0m), exenta);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void C3_NetoMasIvaIgualTotalCobrado_ParaTodosLosCentavos_EnV1YV2(int version)
    {
        var criterio = CriterioFiscalRegistry.Resolver(version);
        for (var centavos = 0; centavos < 100; centavos++)
        {
            var bruto = 1000m + centavos / 100m;
            var neto = Math.Round(bruto / 1.21m, 2, MidpointRounding.AwayFromZero);
            var total = criterio.TruncarAPesos(bruto);

            var d = criterio.Discriminar(total, new[] { (21m, bruto, neto, bruto - neto) });

            Assert.Equal(total, d.Neto + d.Iva);
        }
    }

    // ------------------------------------------------------------------ C6: envío y armado con IVA 21 %

    [Fact]
    public void C6_ServiciosTributanAl21PorDefecto_EnV1YV2_YSeparanNetoEIvaExactos()
    {
        foreach (var version in new[] { 1, 2 })
        {
            var criterio = CriterioFiscalRegistry.Resolver(version);
            Assert.Equal(21m, criterio.AlicuotaServicios);
            Assert.Equal("IVA 21%", criterio.NombreAlicuotaServicios);
            Assert.Equal((1000m, 210m), criterio.SepararServicios(1210m));
            Assert.Equal((100m, 21m), criterio.SepararServicios(121m));
        }
    }

    [Fact]
    public void C6_LaAlicuotaDeUnaOperacionYaCongeladaNoCambiaConLaConfiguracionPosterior()
    {
        var congelada = CriterioFiscalRegistry.Resolver(2, 21m);
        using var _ = ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m);

        Assert.Equal(21m, congelada.AlicuotaServicios);
        Assert.Equal((1000m, 210m), congelada.SepararServicios(1210m));
    }

    // ------------------------------------------------------------------ C10: mora desde el día 13

    private static PunitorioCalculoEntrada Entrada(DateOnly vencimiento, int diasTranscurridos, int diasGracia) => new()
    {
        MontoOriginalCuota = 1_000m,
        FechaVencimiento = vencimiento,
        FechaCalculo = vencimiento.AddDays(diasTranscurridos),
        PagosAplicados = new List<PagoAplicadoPunitorioEntrada>(),
        Configuraciones = new List<ConfiguracionPunitorioEntrada>
        {
            new()
            {
                Id = 1, VigenteDesde = vencimiento.AddDays(-30), Porcentaje = 10m, PeriodoDias = 20, DiasGracia = diasGracia,
                Activa = true, ProrrateoDiario = true
            }
        }
    };

    [Fact]
    public void C10_ConGracia12_ElDia12NoDevengaYElDia13SiLoHace_ContadoDesdeElVencimiento()
    {
        var venc = new DateOnly(2026, 11, 1);
        var calc = new PunitorioCalculator();

        var dia12 = calc.Calcular(Entrada(venc, 12, diasGracia: 12));
        var dia13 = calc.Calcular(Entrada(venc, 13, diasGracia: 12));

        Assert.Equal(EstadoResultadoPunitorio.DentroDeGracia, dia12.EstadoResultado);
        Assert.Equal(0m, dia12.PunitorioRedondeado);
        Assert.Equal(EstadoResultadoPunitorio.Calculado, dia13.EstadoResultado);
        Assert.Equal(65m, dia13.PunitorioRedondeado);   // 1.000 × 10 % × 13 / 20: regla vigente (PUN-ML4), devengo desde el vencimiento
    }

    [Fact]
    public void C10_ElPunitorioNoTieneConceptoDeIva_NiPorElCalculoNiPorLaAplicacion()
    {
        // El recargo por mora es un importe único sin discriminación: no hay IVA que repartir entre alícuotas de la venta
        // (decisión pendiente del propietario para ventas con alícuotas distintas; nada se inventa).
        var propiedades = typeof(PunitorioCalculoResultado).GetProperties().Select(p => p.Name)
            .Concat(typeof(TheBuryProject.Models.Entities.PunitorioAplicado).GetProperties().Select(p => p.Name));

        Assert.DoesNotContain(propiedades, n => n.Contains("Iva", StringComparison.OrdinalIgnoreCase));
    }
}
