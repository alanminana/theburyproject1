using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Fase 15.8.C — Desglose visual del ajuste por plan aplicado.
///
/// Verifica:
/// - Details_tw y ComprobanteFactura_tw leen snapshots sin recalcular.
/// - FacturaComprobanteBuilder propaga los snapshots de DatosTarjeta al ViewModel.
/// - Ventas sin plan no muestran bloque de ajuste.
/// - Total final no cambia.
/// </summary>
public class VentaDetailsAjustePlanUiContractTests
{
    // ── UI contract: Details_tw.cshtml ──────────────────────────────────

    [Fact]
    public void DetailsView_LeeMontoPlanDesdeSnapshot_NoRecalcula()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("ajustePlanMonto", view);
        Assert.Contains("ajustePlanMonto.HasValue", view);
        Assert.Contains("MontoAjustePlanAplicado", view);
    }

    [Fact]
    public void DetailsView_MuestraLabelRecargoCuandoMontoPositivo()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Recargo por plan", view);
    }

    [Fact]
    public void DetailsView_MuestraLabelDescuentoCuandoMontoNegativo()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Descuento por plan", view);
    }

    [Fact]
    public void DetailsView_MuestraPorcentajeSiEstaPresente()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("ajustePlanPct", view);
        Assert.Contains("PorcentajeAjustePlanAplicado", view);
    }

    [Fact]
    public void DetailsView_BloqueAjusteCondicionalNoRompeVentasSinPlan()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        // El bloque solo se renderiza si HasValue — ventas sin plan (null) no lo muestran
        Assert.Contains("ajustePlanMonto.HasValue", view);
        Assert.DoesNotContain("ajustePlanMonto.Value > 0 ? \"Recargo", view); // lógica en Razor, no en JS
    }

    [Fact]
    public void DetailsView_TotalNoEsModificadoEnRazor()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        // El total se lee de Model.Total que ya incluye el ajuste persistido — no se suma nada en Razor
        Assert.Contains("Model.Total.ToString(\"C2\")", view);
        Assert.DoesNotContain("ajustePlanMonto.Value + Model.Total", view);
        Assert.DoesNotContain("Total + ajuste", view);
    }

    [Fact]
    public void DetailsView_AjustePlanAparaceEnAmbasSecciones_TablayPanel()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        // Hay dos secciones de totales; el sufijo de porcentaje aparece en ambas con nombres distintos
        Assert.Contains("sufijoPct", view);   // bloque de tabla
        Assert.Contains("sufijoPctC", view);  // bloque del panel compacto
    }

    // ── UI contract: ComprobanteFactura_tw.cshtml ───────────────────────

    [Fact]
    public void ComprobanteView_LeeMontoPlanDesdeSnapshot()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "ComprobanteFactura_tw.cshtml"));

        Assert.Contains("Model.Totales.MontoAjustePlanAplicado", view);
        Assert.Contains("HasValue", view);
    }

    [Fact]
    public void ComprobanteView_MuestraLabelRecargoyDescuentoSegunSigno()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "ComprobanteFactura_tw.cshtml"));

        Assert.Contains("Recargo por plan", view);
        Assert.Contains("Descuento por plan", view);
    }

    [Fact]
    public void ComprobanteView_TotalNoCambiaPorAjustePlan()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "ComprobanteFactura_tw.cshtml"));

        Assert.Contains("Model.Totales.Total.ToString(\"C2\")", view);
        Assert.DoesNotContain("montoAjuste + Model.Totales.Total", view);
    }

    // ── Builder: FacturaComprobanteBuilder ─────────────────────────────

    [Fact]
    public void Builder_ConPlanPositivo_PropagaSnapshotRecargo()
    {
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Credito,
            montoAjuste: 150m,
            porcentajeAjuste: 5m);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(150m, vm.Totales.MontoAjustePlanAplicado);
        Assert.Equal(5m, vm.Totales.PorcentajeAjustePlanAplicado);
    }

    [Fact]
    public void Builder_ConPlanNegativo_PropagaSnapshotDescuento()
    {
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Credito,
            montoAjuste: -80m,
            porcentajeAjuste: -3m);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(-80m, vm.Totales.MontoAjustePlanAplicado);
        Assert.Equal(-3m, vm.Totales.PorcentajeAjustePlanAplicado);
    }

    [Fact]
    public void Builder_ConPlanCeroPorciento_PropagaSnapshotCero()
    {
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Debito,
            montoAjuste: 0m,
            porcentajeAjuste: 0m);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(0m, vm.Totales.MontoAjustePlanAplicado);
        Assert.Equal(0m, vm.Totales.PorcentajeAjustePlanAplicado);
    }

    [Fact]
    public void Builder_SinPlan_TotalesMontoAjusteEsNull()
    {
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Credito,
            montoAjuste: null,
            porcentajeAjuste: null);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Null(vm.Totales.MontoAjustePlanAplicado);
        Assert.Null(vm.Totales.PorcentajeAjustePlanAplicado);
    }

    [Fact]
    public void Builder_SinDatosTarjeta_TotalesMontoAjusteEsNull()
    {
        var factura = new Factura
        {
            Numero = "FC-001",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 5, 8),
            Total = 500m,
            Venta = new Venta
            {
                Numero = "V-001",
                Total = 500m,
                Cliente = BuildCliente(),
                DatosTarjeta = null
            }
        };

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Null(vm.Totales.MontoAjustePlanAplicado);
    }

    [Fact]
    public void Builder_ConPlanPositivo_TotalPersistidoNoSeCambia()
    {
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Credito,
            montoAjuste: 200m,
            porcentajeAjuste: 10m,
            totalFactura: 2200m);

        var vm = FacturaComprobanteBuilder.Build(factura);

        // El total viene del snapshot de factura/venta, no de productos + ajuste recalculado
        Assert.Equal(2200m, vm.Totales.Total);
    }

    [Fact]
    public void Builder_TarjetaDebito_PropagaAjustePlan()
    {
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Debito,
            montoAjuste: 50m,
            porcentajeAjuste: 2m);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(50m, vm.Totales.MontoAjustePlanAplicado);
    }

    [Fact]
    public void Builder_MercadoPago_PropagaAjustePlan()
    {
        // MercadoPago persiste DatosTarjeta con TipoTarjeta.Credito — el builder propaga el snapshot igual
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Credito,
            montoAjuste: -30m,
            porcentajeAjuste: -1.5m);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(-30m, vm.Totales.MontoAjustePlanAplicado);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static Factura BuildFacturaConTarjeta(
        TipoTarjeta tipoTarjeta,
        decimal? montoAjuste,
        decimal? porcentajeAjuste,
        decimal totalFactura = 1000m)
    {
        return new Factura
        {
            Numero = "FC-TEST",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 5, 8),
            Total = totalFactura,
            Venta = new Venta
            {
                Numero = "V-TEST",
                Total = totalFactura,
                Cliente = BuildCliente(),
                DatosTarjeta = new DatosTarjeta
                {
                    TipoTarjeta = tipoTarjeta,
                    NombreTarjeta = "Test",
                    MontoAjustePlanAplicado = montoAjuste,
                    PorcentajeAjustePlanAplicado = porcentajeAjuste
                }
            }
        };
    }

    private static Cliente BuildCliente() => new()
    {
        Nombre = "Test",
        Apellido = "Cliente",
        NumeroDocumento = "99999999",
        Telefono = "000",
        Domicilio = "Calle 0"
    };

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
