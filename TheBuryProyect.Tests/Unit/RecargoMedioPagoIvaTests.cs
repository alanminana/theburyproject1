using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>H5-I: el recargo del medio de pago se discrimina con IVA, se reparte exacto y se devuelve proporcionalmente.</summary>
public class RecargoMedioPagoIvaTests
{
    [Fact]
    public void Repartir_UnaAlicuota_SeparaNetoEIvaYSumaExacto()
    {
        var partes = RecargoMedioPagoIva.Repartir(new[] { (21m, 12_752m) }, 382m);

        var parte = Assert.Single(partes);
        Assert.Equal(382m, parte.Bruto);
        Assert.Equal(315.70m, parte.Neto);
        Assert.Equal(66.30m, parte.Iva);
        Assert.Equal(parte.Bruto, parte.Neto + parte.Iva);
    }

    [Fact]
    public void Repartir_MultiAlicuota_ProporcionalAlBrutoYSumaExacta()
    {
        var partes = RecargoMedioPagoIva.Repartir(new[] { (21m, 7_000m), (10.5m, 3_000m), (0m, 1_000m) }, 330m);

        Assert.Equal(330m, partes.Sum(p => p.Bruto));
        Assert.All(partes, p => Assert.Equal(p.Bruto, p.Neto + p.Iva));
        Assert.Equal(210m, partes.Single(p => p.Porcentaje == 21m).Bruto);
        Assert.Equal(90m, partes.Single(p => p.Porcentaje == 10.5m).Bruto);
        Assert.Equal(30m, partes.Single(p => p.Porcentaje == 0m).Bruto);
        Assert.Equal(0m, partes.Single(p => p.Porcentaje == 0m).Iva);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void Repartir_BordeDeUnPeso_NoPierdeNiInventaCentavos(int recargo)
    {
        var partes = RecargoMedioPagoIva.Repartir(new[] { (21m, 3_333m), (10.5m, 3_333m), (0m, 3_334m) }, recargo);

        Assert.Equal(recargo, partes.Sum(p => p.Bruto));
        Assert.Equal(recargo, partes.Sum(p => p.Neto + p.Iva));
    }

    [Fact]
    public void Repartir_Propiedades_SumaExactaParaMuchasCombinaciones()
    {
        var rnd = new Random(20261029);
        var alicuotas = new[] { 21m, 10.5m, 27m, 0m };
        for (var i = 0; i < 2_000; i++)
        {
            var n = rnd.Next(1, 5);
            var bases = Enumerable.Range(0, n)
                .Select(_ => (alicuotas[rnd.Next(alicuotas.Length)], Math.Round((decimal)rnd.NextDouble() * 50_000m + 0.01m, 2)))
                .ToList();
            var recargo = rnd.Next(1, 20_000);

            var partes = RecargoMedioPagoIva.Repartir(bases, recargo);

            Assert.Equal(recargo, partes.Sum(p => p.Bruto));
            Assert.All(partes, p =>
            {
                Assert.Equal(p.Bruto, p.Neto + p.Iva);
                Assert.True(p.Neto >= 0m && p.Iva >= 0m);
            });
        }
    }

    [Fact]
    public void CalcularRecargo_DescuentoOSinRecargo_EsCero()
    {
        Assert.Equal(382m, RecargoMedioPagoIva.CalcularRecargo(13_134m, 12_752.60m));
        Assert.Equal(0m, RecargoMedioPagoIva.CalcularRecargo(950m, 1_000m));
        Assert.Equal(0m, RecargoMedioPagoIva.CalcularRecargo(1_000m, 1_000.60m));
    }

    [Fact]
    public void AbsorberTruncado_DescuentaDelIvaPrimeroYLuegoDelNeto()
    {
        Assert.Equal((100m, 20.40m), RecargoMedioPagoIva.AbsorberTruncado(100m, 21m, 0.60m));
        Assert.Equal((99.50m, 0m), RecargoMedioPagoIva.AbsorberTruncado(100m, 0.10m, 0.60m));
    }

    [Fact]
    public void RecargoPorLinea_SumaExactaYDevolverTodoReintegraLoCobrado()
    {
        // Venta del hallazgo: productos 12.752 (dos líneas), total cobrado 13.134 con débito +3%.
        var cobrado = new[] { 7_000m, 5_752m };
        var recargo = DevolucionService.RecargoPorLinea(cobrado, 0m, 13_134m);

        Assert.Equal(382m, recargo.Sum());
        Assert.Equal(13_134m, cobrado.Sum() + recargo.Sum());
    }

    [Fact]
    public void RecargoPorLinea_ConServicios_SoloLaParteDeProductosSeDevuelve()
    {
        var recargo = DevolucionService.RecargoPorLinea(new[] { 900m }, 100m, 1_030m);

        Assert.Equal(27m, recargo.Sum());   // 30 * 900 / 1000
    }

    [Fact]
    public void RecargoPorLinea_SinRecargo_Ceros()
    {
        Assert.All(DevolucionService.RecargoPorLinea(new[] { 500m, 500m }, 0m, 1_000m), r => Assert.Equal(0m, r));
    }

    [Fact]
    public void RecargoPorLinea_AjusteNegativo_DevolverTodoReintegraExactamenteLoCobrado()
    {
        // Efectivo -5%: productos 885.218 (3 lineas), total cobrado 840.957 (truncado).
        var cobrado = new[] { 500_000m, 300_000.40m, 85_217.60m };
        var ajuste = DevolucionService.RecargoPorLinea(cobrado, 0m, 840_957m);

        Assert.Equal(840_957m, cobrado.Sum() + ajuste.Sum());
        Assert.All(ajuste, r => Assert.True(r <= 0m));
    }

    [Fact]
    public void RecargoPorLinea_AjusteNegativoConServicios_SoloDescuentaLaParteDeProductos()
    {
        // productos 900 + servicios 100, total 950 (-5%): descuento 50 => 45 a productos.
        var ajuste = DevolucionService.RecargoPorLinea(new[] { 900m }, 100m, 950m);

        Assert.Equal(-45m, ajuste.Sum());
    }

    [Fact]
    public void DiscriminarRecargo_VentaConDebito_NetoMasIvaIgualTotal_EsIdempotente()
    {
        var venta = VentaWizard(total: 13_134m, out var detalles);

        VentaService.DiscriminarRecargoEnNetoEIva(venta, detalles);

        Assert.Equal(13_134m, venta.Subtotal + venta.IVA);
        var subtotal = venta.Subtotal;
        var iva = venta.IVA;
        VentaService.DiscriminarRecargoEnNetoEIva(venta, detalles);
        Assert.Equal((subtotal, iva), (venta.Subtotal, venta.IVA));
    }

    [Fact]
    public void DiscriminarRecargo_SinRecargoODescuento_NoTocaLaVenta()
    {
        foreach (var total in new[] { 12_752m, 12_000m })
        {
            var venta = VentaWizard(total, out var detalles);
            var antes = (venta.Subtotal, venta.IVA);

            VentaService.DiscriminarRecargoEnNetoEIva(venta, detalles);

            Assert.Equal(antes, (venta.Subtotal, venta.IVA));
        }
    }

    [Fact]
    public void DiscriminarRecargo_VentaDeCotizacionConSubtotalBruto_NoReinterpreta()
    {
        var venta = VentaWizard(total: 13_134m, out var detalles);
        venta.Subtotal = 12_752m; // convención de cotización: Subtotal con IVA incluido
        var antes = (venta.Subtotal, venta.IVA);

        VentaService.DiscriminarRecargoEnNetoEIva(venta, detalles);

        Assert.Equal(antes, (venta.Subtotal, venta.IVA));
    }

    // Dos alícuotas: 8.000 al 21 % y 4.752 al 10,5 % = 12.752 de ítems (Subtotal + IVA).
    internal static Venta VentaWizard(decimal total, out List<VentaDetalle> detalles)
    {
        detalles = new List<VentaDetalle>
        {
            Linea(8_000m, 21m),
            Linea(4_752m, 10.5m)
        };
        return new Venta
        {
            Total = total,
            Subtotal = detalles.Sum(d => d.SubtotalFinalNeto),
            IVA = detalles.Sum(d => d.SubtotalFinalIVA)
        };
    }

    internal static VentaDetalle Linea(decimal bruto, decimal pct)
    {
        var neto = Math.Round(bruto / (1m + pct / 100m), 2, MidpointRounding.AwayFromZero);
        return new VentaDetalle
        {
            Cantidad = 1, PrecioUnitario = bruto, Subtotal = bruto, SubtotalFinal = bruto, PorcentajeIVA = pct,
            SubtotalNeto = neto, SubtotalIVA = bruto - neto, SubtotalFinalNeto = neto, SubtotalFinalIVA = bruto - neto
        };
    }

    [Fact]
    public void Comprobante_DebitoConRecargo_NetoMasIvaIgualTotalYResumenCierra()
    {
        var comprobante = ComprobanteDebito(total: 13_134m, factura: 13_134m);

        var t = comprobante.Totales;
        Assert.Equal(13_134m, t.Total);
        Assert.Equal(13_134m, t.SubtotalNeto + t.IVA);
        Assert.Equal(382m, t.RecargoIncluidoNeto + t.RecargoIncluidoIVA);
        Assert.True(t.RecargoIncluido);
        Assert.Equal(13_134m, comprobante.ResumenAlicuotas.Sum(r => r.BaseImponible + r.IVA));
        Assert.Equal(t.IVA, comprobante.ResumenAlicuotas.Sum(r => r.IVA));
    }

    [Fact]
    public void Comprobante_SinRecargo_NoCambiaRespectoDeLasLineas()
    {
        var comprobante = ComprobanteDebito(total: 12_752m, factura: 12_752m);

        var t = comprobante.Totales;
        Assert.False(t.RecargoIncluido);
        Assert.Equal(comprobante.Lineas.Sum(l => l.SubtotalNeto), t.SubtotalNeto);
        Assert.Equal(comprobante.Lineas.Sum(l => l.IVA), t.IVA);
    }

    [Fact]
    public void Comprobante_EfectivoConDescuento_SigueIgual()
    {
        var comprobante = ComprobanteDebito(total: 12_115m, factura: 12_115m);

        Assert.False(comprobante.Totales.RecargoIncluido);
        Assert.Equal(12_752m, comprobante.Totales.SubtotalNeto + comprobante.Totales.IVA);
        Assert.Equal(12_115m, comprobante.Totales.Total);
    }

    private static TheBuryProject.ViewModels.FacturaComprobanteViewModel ComprobanteDebito(decimal total, decimal factura)
    {
        var venta = VentaWizard(total, out var detalles);
        venta.Numero = "V-H5I";
        venta.TipoPago = TipoPago.TarjetaDebito;
        venta.Cliente = new Cliente { Nombre = "Ana", Apellido = "C", NumeroDocumento = "1" };
        foreach (var d in detalles) d.Producto = new Producto { Codigo = "P", Nombre = "P" };
        venta.Detalles = detalles;
        venta.DatosTarjeta = new DatosTarjeta { TipoTarjeta = TipoTarjeta.Debito, RecargoAplicado = total > 12_752m ? total - 12_752m : null };
        var f = new Factura { Numero = "FC-H5I", Tipo = TipoFactura.A, FechaEmision = new DateTime(2026, 10, 29), Total = factura, Venta = venta };
        venta.Facturas.Add(f);
        return FacturaComprobanteBuilder.Build(f);
    }

    [Fact]
    public void VentaDeCotizacion_DebitoConRecargo_SumaElIvaDelRecargoYMantieneSubtotalIgualTotal()
    {
        var detalles = new List<VentaDetalle> { Linea(8_000m, 21m), Linea(4_752m, 10.5m) };
        var venta = new Venta { Subtotal = 12_752m, IVA = detalles.Sum(d => d.SubtotalFinalIVA), Total = 13_134m };
        var ivaAntes = venta.IVA;

        VentaService.DiscriminarRecargoEnIvaVentaCotizacion(venta, detalles);

        Assert.Equal(13_134m, venta.Subtotal);
        Assert.True(venta.IVA > ivaAntes);
        var iva = venta.IVA;
        VentaService.DiscriminarRecargoEnIvaVentaCotizacion(venta, detalles);
        Assert.Equal(iva, venta.IVA);   // idempotente
    }

    [Fact]
    public void VentaDeCotizacion_SinRecargoOConDescuento_NoCambia()
    {
        foreach (var total in new[] { 12_752m, 12_115m })
        {
            var detalles = new List<VentaDetalle> { Linea(12_752m, 21m) };
            var venta = new Venta { Subtotal = 12_752m, IVA = 2_213.16m, Total = total };

            VentaService.DiscriminarRecargoEnIvaVentaCotizacion(venta, detalles);

            Assert.Equal((12_752m, 2_213.16m), (venta.Subtotal, venta.IVA));
        }
    }
}
