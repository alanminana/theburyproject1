using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Fase 15.8.C + Fase 16.5 — Desglose visual del ajuste por plan aplicado.
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

    // ── Fase 16.5: desglose por ítem y resumen agrupado ────────────────

    [Fact]
    public void DetailsView_MuestraFormaPagoPorItem()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("forma-pago-item", view);
        Assert.Contains("item.TipoPago", view);
        Assert.Contains("item.ProductoCondicionPagoPlanId.HasValue", view);
    }

    [Fact]
    public void DetailsView_MuestraPlanSinAjusteCuandoSnapshotCero()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Plan sin ajuste", view);
    }

    [Fact]
    public void DetailsView_OcultaBloqueAjusteSiItemSinPlan()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        // El bloque por ítem solo se renderiza si hay plan y monto snapshot
        Assert.Contains("tieneAjustePlanItem", view);
        Assert.Contains("item.ProductoCondicionPagoPlanId.HasValue", view);
    }

    [Fact]
    public void DetailsView_UsaMontoAjustePlanAplicadoPorItem_NoRecalcula()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("item.MontoAjustePlanAplicado", view);
        // Razor no recalcula: no hay producto de SubtotalFinal * Porcentaje
        Assert.DoesNotContain("SubtotalFinal * item.Porcentaje", view);
        Assert.DoesNotContain("item.SubtotalFinal * item.PorcentajeAjuste", view);
    }

    [Fact]
    public void DetailsView_MuestraResumenAgrupadoPorGrupo()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("resumen-ajuste-por-grupo", view);
        Assert.Contains("Sum(d => d.MontoAjustePlanAplicado", view);
        Assert.Contains("gruposAjustePlan", view);
    }

    [Fact]
    public void DetailsView_TotalFinalEsModelTotal_NoSumaGrupos()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        Assert.Contains("Model.Total.ToString(\"C2\")", view);
        // El total no se recalcula sumando grupos sobre Model.Total
        Assert.DoesNotContain("gruposAjustePlan.Sum", view);
    }

    [Fact]
    public void DetailsView_CreditoPersonalNoMuestraAjustePorPlan()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Details_tw.cshtml"));

        // La condición de ajuste por ítem excluye CreditoPersonal
        Assert.Contains("TipoPago.CreditoPersonal", view);
        Assert.Contains("!= TipoPago.CreditoPersonal", view);
    }

    [Fact]
    public void VentaDetalleViewModel_ExponeResumenFormaPago()
    {
        var vm = new VentaDetalleViewModel();
        // La propiedad existe y por defecto es null (sin tipo de pago ni plan)
        Assert.Null(vm.ResumenFormaPago);
    }

    [Fact]
    public void AutoMapper_VentaDetalle_ConPlanPositivo_PopulaResumenFormaPago()
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance)
            .CreateMapper();

        var detalle = new VentaDetalle
        {
            TipoPago = TipoPago.TarjetaCredito,
            ProductoCondicionPagoPlanId = 1,
            PorcentajeAjustePlanAplicado = 10m,
            MontoAjustePlanAplicado = 50m,
            Producto = new Producto { Nombre = "Test", Codigo = "T1", RowVersion = new byte[8] }
        };

        var vm = mapper.Map<VentaDetalleViewModel>(detalle);

        Assert.NotNull(vm.ResumenFormaPago);
        Assert.Contains("Tarjeta Crédito", vm.ResumenFormaPago);
        Assert.Contains("Recargo", vm.ResumenFormaPago);
    }

    [Fact]
    public void AutoMapper_VentaDetalle_ConPlanNegativo_PopulaDescuentoEnResumen()
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance)
            .CreateMapper();

        var detalle = new VentaDetalle
        {
            TipoPago = TipoPago.TarjetaDebito,
            ProductoCondicionPagoPlanId = 2,
            PorcentajeAjustePlanAplicado = -5m,
            MontoAjustePlanAplicado = -25m,
            Producto = new Producto { Nombre = "Test", Codigo = "T2", RowVersion = new byte[8] }
        };

        var vm = mapper.Map<VentaDetalleViewModel>(detalle);

        Assert.NotNull(vm.ResumenFormaPago);
        Assert.Contains("Descuento", vm.ResumenFormaPago);
    }

    [Fact]
    public void AutoMapper_VentaDetalle_SinPlanNiTipoPago_ResumenEsNull()
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance)
            .CreateMapper();

        var detalle = new VentaDetalle
        {
            TipoPago = null,
            ProductoCondicionPagoPlanId = null,
            Producto = new Producto { Nombre = "Test", Codigo = "T3", RowVersion = new byte[8] }
        };

        var vm = mapper.Map<VentaDetalleViewModel>(detalle);

        Assert.Null(vm.ResumenFormaPago);
    }

    // ── Fase 16.6: comprobante con pagos por ítem ───────────────────────

    [Fact]
    public void ComprobanteView_ConPagosPorItem_MuestraSeccionDesgloseGrupo()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "ComprobanteFactura_tw.cshtml"));

        Assert.Contains("desglose-pago-por-item", view);
        Assert.Contains("GruposPagoPorItem", view);
        Assert.Contains("GruposPagoPorItem.Any()", view);
    }

    [Fact]
    public void ComprobanteView_ConPagosPorItem_IteraGruposYMuestraLabelsDeAjuste()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "ComprobanteFactura_tw.cshtml"));

        Assert.Contains("grupo.TipoPagoLabel", view);
        Assert.Contains("grupo.Subtotal", view);
        Assert.Contains("grupo.AjusteMonto", view);
        Assert.Contains("grupo.Total", view);
        Assert.Contains("Recargo por plan", view);
        Assert.Contains("Descuento por plan", view);
    }

    [Fact]
    public void ComprobanteView_ConPagosPorItem_TotalFinalNoModificado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "ComprobanteFactura_tw.cshtml"));

        // El total del comprobante sigue siendo Model.Totales.Total (sin sumar grupos encima)
        Assert.Contains("Model.Totales.Total.ToString(\"C2\")", view);
        Assert.DoesNotContain("grupo.Total + Model.Totales", view);
    }

    [Fact]
    public void Builder_ConPagosPorItem_GeneraGruposYNulaAjusteTotalesLegacy()
    {
        // Un detalle con TipoPago por ítem + plan → builder debe generar grupos y nullificar ajuste legacy
        var factura = BuildFacturaConDetallesPorItem(new[]
        {
            (TipoPago.TarjetaCredito, planId: 1, pct: 10m, monto: 50m, subtotal: 500m)
        }, tipoPagoGlobal: TipoPago.TarjetaCredito);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.NotEmpty(vm.GruposPagoPorItem);
        // Cuando hay pagos por ítem, el ajuste legacy de Totales queda nulo
        Assert.Null(vm.Totales.MontoAjustePlanAplicado);
        Assert.Null(vm.Totales.PorcentajeAjustePlanAplicado);
    }

    [Fact]
    public void Builder_ConPagosPorItem_GrupoTieneSubtotalAjusteYTotalCorrectos()
    {
        var factura = BuildFacturaConDetallesPorItem(new[]
        {
            (TipoPago.TarjetaCredito, planId: 1, pct: 10m, monto: 50m, subtotal: 500m)
        }, tipoPagoGlobal: TipoPago.TarjetaCredito);

        var vm = FacturaComprobanteBuilder.Build(factura);

        var grupo = Assert.Single(vm.GruposPagoPorItem);
        Assert.Equal("Tarjeta Crédito", grupo.TipoPagoLabel);
        Assert.Equal(10m, grupo.PorcentajeAjuste);
        Assert.Equal(500m, grupo.Subtotal);
        Assert.Equal(50m, grupo.AjusteMonto);
        Assert.Equal(550m, grupo.Total);
    }

    [Fact]
    public void Builder_SinPagosPorItem_MantieneAjusteTotalesDesdesDatosTarjeta()
    {
        // Sin TipoPago por ítem → comportamiento anterior: ajuste viene de DatosTarjeta
        var factura = BuildFacturaConTarjeta(
            tipoTarjeta: TipoTarjeta.Credito,
            montoAjuste: 100m,
            porcentajeAjuste: 5m);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Empty(vm.GruposPagoPorItem);
        Assert.Equal(100m, vm.Totales.MontoAjustePlanAplicado);
        Assert.Equal(5m, vm.Totales.PorcentajeAjustePlanAplicado);
    }

    [Fact]
    public void Builder_CreditoPersonalExcluidoDeGruposPagoPorItem()
    {
        // Detalle con TipoPago = CreditoPersonal → no entra en grupos
        var factura = BuildFacturaConDetallesPorItem(new[]
        {
            (TipoPago.CreditoPersonal, planId: 1, pct: 0m, monto: 0m, subtotal: 300m)
        }, tipoPagoGlobal: TipoPago.CreditoPersonal);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Empty(vm.GruposPagoPorItem);
    }

    [Fact]
    public void Builder_DosGruposDistintosMedio_PropagaAmbosGrupos()
    {
        var factura = BuildFacturaConDetallesPorItem(new[]
        {
            (TipoPago.TarjetaCredito, planId: 1, pct: 10m, monto: 50m, subtotal: 500m),
            (TipoPago.TarjetaDebito,  planId: 2, pct: -5m, monto: -10m, subtotal: 200m)
        }, tipoPagoGlobal: TipoPago.TarjetaCredito);

        var vm = FacturaComprobanteBuilder.Build(factura);

        Assert.Equal(2, vm.GruposPagoPorItem.Count);
        Assert.Contains(vm.GruposPagoPorItem, g => g.TipoPagoLabel == "Tarjeta Crédito" && g.AjusteMonto == 50m);
        Assert.Contains(vm.GruposPagoPorItem, g => g.TipoPagoLabel == "Tarjeta Débito"  && g.AjusteMonto == -10m);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static Factura BuildFacturaConDetallesPorItem(
        IEnumerable<(TipoPago TipoPago, int PlanId, decimal Pct, decimal Monto, decimal Subtotal)> items,
        TipoPago tipoPagoGlobal,
        decimal totalFactura = 1000m)
    {
        var detalles = items.Select(i => new VentaDetalle
        {
            TipoPago = i.TipoPago,
            ProductoCondicionPagoPlanId = i.PlanId,
            PorcentajeAjustePlanAplicado = i.Pct,
            MontoAjustePlanAplicado = i.Monto,
            SubtotalFinal = i.Subtotal,
            Producto = new Producto { Nombre = "Test", Codigo = "T", RowVersion = new byte[8] }
        }).ToList();

        return new Factura
        {
            Numero = "FC-ITEM",
            Tipo = TipoFactura.B,
            FechaEmision = new DateTime(2026, 5, 8),
            Total = totalFactura,
            Venta = new Venta
            {
                Numero = "V-ITEM",
                Total = totalFactura,
                TipoPago = tipoPagoGlobal,
                Cliente = BuildCliente(),
                DatosTarjeta = null,
                Detalles = detalles
            }
        };
    }

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
