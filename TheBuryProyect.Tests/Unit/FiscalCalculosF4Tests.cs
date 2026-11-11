using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// P-02 / F4.2: tests de CONSISTENCIA y CARACTERIZACIÓN de cálculos fiscales/financieros puros.
///
/// Regla de este frente: no se implementa ningún criterio nuevo. Cada valor esperado está calculado a mano y escrito
/// en el test (no se deriva del código bajo prueba). Los tests "Caracteriza_" fijan el comportamiento VIGENTE
/// de un criterio pendiente de respuesta del contador: si cambian, es una decisión fiscal, no un refactor.
/// Ver docs/iva-matriz-diferencias.md y docs/iva-decisiones-contador.md.
/// </summary>
public class FiscalCalculosF4Tests
{
    // Línea (porcentaje, bruto, neto, iva) con la misma convención que VentaDetalle: neto = bruto / (1 + alícuota) a 2 decimales.
    private static (decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva) Linea(decimal pct, decimal bruto)
    {
        var neto = pct > 0m ? Math.Round(bruto / (1m + pct / 100m), 2, MidpointRounding.AwayFromZero) : bruto;
        return (pct, bruto, neto, bruto - neto);
    }

    // ───────────────────────── neto + IVA = total por alícuota (valores a mano) ─────────────────────────

    [Theory]
    // alícuota, total de ítems, total cobrado, neto esperado, IVA esperado
    // 21 %: 1000 → neto 826,45 / IVA 173,55. Recargo 30 → 24,79 / 5,21. Total 1030 → 851,24 / 178,76.
    [InlineData(21, 1000, 1030, 851.24, 178.76)]
    // 10,5 %: 1000 → 904,98 / 95,02. Descuento 50 → 45,25 / 4,75. Total 950 → 859,73 / 90,27.
    [InlineData(10.5, 1000, 950, 859.73, 90.27)]
    // 0 %: exento. Recargo 30 → neto 30, IVA 0. Total 1030 → 1030 / 0.
    [InlineData(0, 1000, 1030, 1030.00, 0.00)]
    // 21 % sin ajuste: el total de ítems es el total cobrado.
    [InlineData(21, 1000, 1000, 826.45, 173.55)]
    public void Discriminar_UnaAlicuota_NetoEIvaCoincidenConElCalculoManualYCierranContraElTotal(
        double pct, double items, double cobrado, double netoEsperado, double ivaEsperado)
    {
        var d = RecargoMedioPagoIva.Discriminar((decimal)cobrado, new[] { Linea((decimal)pct, (decimal)items) });

        Assert.Equal((decimal)netoEsperado, d.Neto);
        Assert.Equal((decimal)ivaEsperado, d.Iva);
        Assert.Equal((decimal)cobrado, d.Neto + d.Iva);
    }

    [Fact]
    public void Discriminar_MezclaDeAlicuotas_RecargoDe60_SeProrrateaPorBrutoYCierraExacto()
    {
        // A 1000 @21 %, B 500 @10,5 %, C 500 @0 %; Transferencia +3 % → total 2060 (ajuste 60).
        // Reparto por bruto (1000/500/500 de 2000): 30 / 15 / 15.
        //   A: ítems 826,45 + 173,55; ajuste 30 → 24,79 + 5,21
        //   B: ítems 452,49 +  47,51; ajuste 15 → 13,57 + 1,43
        //   C: ítems 500,00 +   0,00; ajuste 15 → 15,00 + 0,00
        // Neto 1832,30; IVA 227,70; total 2060,00.
        var lineas = new[] { Linea(21m, 1000m), Linea(10.5m, 500m), Linea(0m, 500m) };

        var d = RecargoMedioPagoIva.Discriminar(2060m, lineas);

        Assert.Equal(1832.30m, d.Neto);
        Assert.Equal(227.70m, d.Iva);
        Assert.Equal(2060m, d.Neto + d.Iva);
        Assert.Equal(3, d.Partes.Count);
        Assert.Equal(60m, d.Partes.Sum(p => p.Bruto));
    }

    [Fact]
    public void ResumenPorAlicuota_MezclaDeAlicuotas_TresBucketsQueSumanNetoIvaYTotalDelComprobante()
    {
        var lineas = new[] { Linea(21m, 1000m), Linea(10.5m, 500m), Linea(0m, 500m) };

        var resumen = RecargoMedioPagoIva.ResumenPorAlicuota(2060m, lineas);

        Assert.Equal(new[] { 21m, 10.5m, 0m }, resumen.Select(r => r.Porcentaje).ToArray());
        var a = resumen.Single(r => r.Porcentaje == 21m);
        Assert.Equal((1030m, 851.24m, 178.76m), (a.Bruto, a.Neto, a.Iva));
        var b = resumen.Single(r => r.Porcentaje == 10.5m);
        Assert.Equal((515m, 466.06m, 48.94m), (b.Bruto, b.Neto, b.Iva));
        var c = resumen.Single(r => r.Porcentaje == 0m);
        Assert.Equal((515m, 515m, 0m), (c.Bruto, c.Neto, c.Iva));
        Assert.Equal(2060m, resumen.Sum(r => r.Bruto));
        Assert.Equal(1832.30m, resumen.Sum(r => r.Neto));
        Assert.Equal(227.70m, resumen.Sum(r => r.Iva));
    }

    // ───────────────────────── redondeo y truncado a pesos ─────────────────────────

    [Theory]
    // Los ítems suman un importe con centavos; el total cobrado es el piso a pesos (VentaService.TruncarAPesos = Math.Floor).
    // El centavo truncado se absorbe primero del IVA y luego del neto, y neto + IVA == total entero.
    // Línea 1000,xx @21 %: neto = ROUND(bruto / 1,21, 2); IVA = bruto - neto; truncado = xx.
    // 1000,00 → neto 826,45  IVA 173,55  | truncado 0,00 → 826,45 / 173,55
    // 1000,49 → neto 826,85  IVA 173,64  | truncado 0,49 → IVA 173,15
    // 1000,50 → neto 826,86  IVA 173,64  | truncado 0,50 → IVA 173,14
    // 1000,51 → neto 826,87  IVA 173,64  | truncado 0,51 → IVA 173,13
    // 1000,99 → neto 827,26  IVA 173,73  | truncado 0,99 → IVA 172,74
    [InlineData(1000.00, 1000, 826.45, 173.55)]
    [InlineData(1000.49, 1000, 826.85, 173.15)]
    [InlineData(1000.50, 1000, 826.86, 173.14)]
    [InlineData(1000.51, 1000, 826.87, 173.13)]
    [InlineData(1000.99, 1000, 827.26, 172.74)]
    public void TruncadoAPesos_CasosBorde_SeTruncaSiempreHaciaAbajoYElCentavoSeAbsorbeEnElIva(
        double brutoItems, double totalEsperado, double netoEsperado, double ivaEsperado)
    {
        var linea = Linea(21m, (decimal)brutoItems);
        var total = Math.Floor((decimal)brutoItems);

        var d = RecargoMedioPagoIva.Discriminar(total, new[] { linea });

        Assert.Equal((decimal)totalEsperado, total);
        Assert.Equal((decimal)netoEsperado, d.Neto);
        Assert.Equal((decimal)ivaEsperado, d.Iva);
        Assert.Equal(total, d.Neto + d.Iva);
    }

    [Theory]
    // CARACTERIZACIÓN (decisión pendiente, contador respuesta 8 / matriz 2.8): el criterio vigente es TRUNCAR (piso).
    // El criterio descrito por el contador es «a pesos, hacia arriba si supera 0,50 y hacia abajo si es inferior».
    // Delta exacto por caso (vigente vs contador):
    //   ,00 → 1000 vs 1000 (igual)      ,49 → 1000 vs 1000 (igual)
    //   ,50 → 1000 vs 1000 o 1001: «supera 0,50» no incluye 0,50 → 1000 (igual si se toma literal; la frase es ambigua)
    //   ,51 → 1000 vs 1001 (DIFIERE en +1,00)      ,99 → 1000 vs 1001 (DIFIERE en +1,00)
    [InlineData(1000.00, 1000, 1000)]
    [InlineData(1000.49, 1000, 1000)]
    [InlineData(1000.50, 1000, 1000)]
    [InlineData(1000.51, 1000, 1001)]
    [InlineData(1000.99, 1000, 1001)]
    public void Caracteriza_RedondeoVigenteVsDescritoPorElContador(double bruto, double vigente, double descritoContador)
    {
        var b = (decimal)bruto;
        // Vigente: Math.Floor (VentaService.TruncarAPesos).
        Assert.Equal((decimal)vigente, Math.Floor(b));
        // Lo descrito por el contador, solo para medir el delta (no es código de producción):
        var fraccion = b - Math.Floor(b);
        var contador = fraccion > 0.50m ? Math.Floor(b) + 1m : Math.Floor(b);
        Assert.Equal((decimal)descritoContador, contador);
    }

    [Fact]
    public void ServiciosVentaIva_Separar_ArmadosYEnvio_ValoresExactosAl21()
    {
        // Precio final con IVA incluido (ProductoIvaResolver.PorcentajeDefault = 21 en los tests).
        // 200 → 165,29 + 34,71 | 300 → 247,93 + 52,07 | 450 → 371,90 + 78,10 | 500 → 413,22 + 86,78
        Assert.Equal((165.29m, 34.71m), ServiciosVentaIva.Separar(200m));
        Assert.Equal((247.93m, 52.07m), ServiciosVentaIva.Separar(300m));
        Assert.Equal((371.90m, 78.10m), ServiciosVentaIva.Separar(450m));
        Assert.Equal((413.22m, 86.78m), ServiciosVentaIva.Separar(500m));
        Assert.Equal((0m, 0m), ServiciosVentaIva.Separar(0m));
    }

    [Fact]
    public void Prorratear_RepartoDelCentavo_MayorRestoYSumaExacta()
    {
        // 100,00 entre tres pesos iguales: 33,33 × 3 = 99,99 → el centavo que falta va al primero (desempate por índice).
        var r = RecargoMedioPagoIva.Prorratear(new[] { 1m, 1m, 1m }, 100m);
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, r);
        Assert.Equal(100m, r.Sum());

        // 0,01 entre tres: un solo centavo, al primero.
        var uno = RecargoMedioPagoIva.Prorratear(new[] { 1m, 1m, 1m }, 0.01m);
        Assert.Equal(new[] { 0.01m, 0m, 0m }, uno);

        // El mayor resto gana: 10,00 entre 2 y 1 → 6,67 / 3,33.
        Assert.Equal(new[] { 6.67m, 3.33m }, RecargoMedioPagoIva.Prorratear(new[] { 2m, 1m }, 10m));
    }

    [Fact]
    public void PrecioIvaCalculator_QuitarIVA_CoincideConElRedondeoComercialEnTodoElRangoDePreciosCon2Decimales()
    {
        // QuitarIVA usa Math.Round(x, 2) (a-par) mientras que el resto del sistema usa AwayFromZero.
        // Con precios de 2 decimales y alícuotas 21 / 10,5 no existe un empate exacto en el tercer decimal
        // (el desarrollo en el comentario de la matriz lo demuestra: 200·p = 121·(2k+1) y 400·p = 221·(2k+1) no tienen
        // solución entera), así que ambos modos coinciden. El test lo comprueba por exhaustividad 0,01..2.000,00.
        for (var centavos = 1; centavos <= 200_000; centavos++)
        {
            var precio = centavos / 100m;
            foreach (var pct in new[] { 21m, 10.5m })
            {
                var aPar = PrecioIvaCalculator.QuitarIVA(precio, pct);
                var comercial = Math.Round(precio / (1m + pct / 100m), 2, MidpointRounding.AwayFromZero);
                Assert.True(aPar == comercial, $"precio {precio} @ {pct} %: a-par {aPar} vs comercial {comercial}");
            }
        }
    }

    // ───────────────────────── total a cobrar vs total facturable ─────────────────────────

    [Theory]
    // total, envío fuera del total (legacy), total a cobrar esperado, total facturable esperado
    [InlineData(1000, 200, 1200, 1000)]      // venta legacy con envío aparte
    [InlineData(1000, 0, 1000, 1000)]        // venta nueva: el envío ya está en Total
    [InlineData(1000, -50, 1000, 1000)]      // un envío nunca resta
    [InlineData(1000, 200.005, 1200.01, 1000)] // el envío legacy se redondea a 2 decimales (AwayFromZero)
    public void VentaMontos_TotalACobrarMasEnvioLegacy_TotalFacturableSoloElTotal(
        double total, double envio, double aCobrar, double facturable)
    {
        Assert.Equal((decimal)aCobrar, VentaMontos.CalcularTotalACobrar((decimal)total, (decimal)envio));
        Assert.Equal((decimal)facturable, VentaMontos.CalcularTotalFacturable((decimal)total));
    }

    [Fact]
    public void VentaMontos_EnvioNulo_CuentaCero()
    {
        Assert.Equal(1000m, VentaMontos.CalcularTotalACobrar(1000m, null));
    }

    // ───────────────────────── venta con servicios: el IVA de envío y armado ─────────────────────────

    [Fact]
    public void Discriminar_VentaConEnvioYArmado_ElTotalCierraConLosServiciosComoLineaAl21()
    {
        // 2 × 1000 @21 % (bruto 2000: 1652,89 + 347,11) + armado 450 + envío 200 → servicios 650 = 537,19 + 112,81. Total 2650.
        var productos = Linea(21m, 2000m);
        var (netoSrv, ivaSrv) = ServiciosVentaIva.Separar(650m);
        var servicios = (21m, 650m, netoSrv, ivaSrv);

        var d = RecargoMedioPagoIva.Discriminar(2650m, new[] { productos, servicios });

        Assert.Equal((537.19m, 112.81m), (netoSrv, ivaSrv));
        Assert.Equal(2190.08m, d.Neto);
        Assert.Equal(459.92m, d.Iva);
        Assert.Equal(2650m, d.Neto + d.Iva);
    }

    // ───────────────────────── reintegro de devoluciones: nunca más de lo cobrado ─────────────────────────

    [Fact]
    public void RecargoPorLinea_TotalTruncadoSobreItemsConCentavos_DevolverTodoReintegraExactamenteElTotal()
    {
        // 3 × 333,33 = 999,99; total cobrado 999 (truncado a pesos). Reintegro de las 3 líneas = 999,00 (no 999,99).
        var cobrado = new[] { 333.33m, 333.33m, 333.33m };
        var ajuste = DevolucionService.RecargoPorLinea(cobrado, 0m, 999m);

        Assert.Equal(-0.99m, ajuste.Sum());
        Assert.Equal(999m, cobrado.Sum() + ajuste.Sum());
    }

    [Fact]
    public void RecargoPorLinea_RecargoConTruncado_ReintegraElTotalCobradoNoMas()
    {
        // 999,99 + recargo 3 % → 1.029,9897 → total cobrado 1.029 (truncado). Reintegro de todo = 1.029,00.
        var cobrado = new[] { 999.99m };
        var ajuste = DevolucionService.RecargoPorLinea(cobrado, 0m, 1029m);

        Assert.Equal(29.01m, ajuste.Sum());
        Assert.Equal(1029m, cobrado.Sum() + ajuste.Sum());
    }

    [Fact]
    public void RecargoPorLinea_TotalConCentavosIgualAlBruto_NoInventaUnRecargo()
    {
        // Venta legacy no truncada: total 999,99 == ítems 999,99. Antes se leía 0,99 de «recargo» (total − piso de ítems).
        Assert.All(DevolucionService.RecargoPorLinea(new[] { 999.99m }, 0m, 999.99m), r => Assert.Equal(0m, r));
    }

    [Fact]
    public void RecargoPorLinea_ConServiciosYCentavos_SoloLaParteDeProductosAbsorbeElTruncado()
    {
        // productos 900,50 + servicios 100 = 1.000,50; total 1.000 → ajuste neto −0,50; a productos 0,50 × 900,50 / 1.000,50 = 0,45.
        var ajuste = DevolucionService.RecargoPorLinea(new[] { 900.50m }, 100m, 1000m);

        Assert.Equal(-0.45m, ajuste.Sum());
    }
}
