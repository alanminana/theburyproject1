namespace TheBuryProject.Tests.Unit;

public class VentaCreateUiContractTests
{
    [Fact]
    public void CreateView_PosteaCamposSnapshotDeTarjeta()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("name=\"DatosTarjeta.ConfiguracionTarjetaId\"", view);
        Assert.Contains("name=\"DatosTarjeta.NombreTarjeta\"", view);
        Assert.Contains("name=\"DatosTarjeta.TipoTarjeta\"", view);
        Assert.Contains("id=\"hdn-tarjeta-nombre\"", view);
        Assert.Contains("id=\"hdn-tarjeta-tipo\"", view);
    }

    [Fact]
    public void CreateView_TieneSelectorTipoPagoGeneralVisible()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"select-tipo-pago\"", view);
        Assert.Contains("<label class=\"venta-label\">Tipo de pago</label>", view);
        Assert.Contains("Selecciona el medio principal de cobro para toda la venta.", view);
        Assert.Contains("tipo de pago principal", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<div class=\"hidden\">\r\n                        <select asp-for=\"TipoPago\"", view);
    }

    [Fact]
    public void CreateView_NoMuestraAccionPagoPorItemEnTabla()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.DoesNotContain("<th class=\"py-3 px-2 text-[10px] font-bold text-slate-500 uppercase\">Tipo de pago</th>", view);
        Assert.DoesNotContain("btn-configurar-pago-item", view);
        Assert.DoesNotContain("id=\"modal-pago-item\"", view);
        Assert.DoesNotContain("id=\"select-tipo-pago-item\"", view);
        Assert.DoesNotContain("id=\"btn-guardar-pago-item\"", view);
        Assert.DoesNotContain("El tipo de pago se configura producto por producto", view);
    }

    [Fact]
    public void CreateView_MantienePanelesDePagoGeneral()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"panel-tarjeta\"", view);
        Assert.Contains("id=\"panel-cheque\"", view);
        Assert.Contains("id=\"panel-credito-personal\"", view);
        Assert.Contains("name=\"DatosTarjeta.ProductoCondicionPagoPlanId\"", view);
        Assert.Contains("id=\"hdn-plan-pago-id\"", view);
    }

    [Fact]
    public void CreateView_NoExponePanelDiagnosticoCondicionesPagoProductoEnNuevaVenta()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.DoesNotContain("id=\"panel-diagnostico-condiciones-pago\"", view);
        Assert.DoesNotContain("data-diagnostico-condiciones-pago", view);
        Assert.DoesNotContain("id=\"diagnostico-condiciones-pago-bloqueo\"", view);
        Assert.DoesNotContain("Las cuotas disponibles fueron restringidas por condiciones del producto", view);
    }

    [Fact]
    public void VentaCreateJs_PueblaSnapshotDeTarjetaDesdeTarjetaActiva()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("hdnTarjetaNombre.value = info.nombre", script);
        Assert.Contains("hdnTarjetaTipo.value = info.tipo", script);
        Assert.Contains("await cargarTarjetasActivas()", script);
        Assert.Contains("ventaForm.requestSubmit()", script);
    }

    [Fact]
    public void VentaCreateJs_MantienePreviewRecargoDebitoComoMontoYPorcentaje()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("recargoDebitoPreview", script);
        Assert.Contains("function formatRecargoDebitoPreview", script);
        Assert.Contains("formatCurrency(recargo.monto)", script);
        Assert.Contains("formatPercent(recargo.porcentaje)", script);
        Assert.Contains("if (recargoDebitoPreview?.monto > 0)", script);
    }

    [Fact]
    public void VentaCreateJs_SelectorGlobalSigueFuncionandoComoPagoPrincipal()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var tipoPago = ExtractFunction(script, "function onTipoPagoChange");

        Assert.Contains("selectTipoPago?.addEventListener('change', onTipoPagoChange)", script);
        Assert.Contains("const val = selectTipoPago.value", tipoPago);
        Assert.Contains("const isTarjeta = esTipoPagoTarjeta(val)", tipoPago);
        Assert.Contains("isTarjeta ? show(panelTarjeta) : hide(panelTarjeta)", tipoPago);
        Assert.Contains("isCredito ? show(panelCreditoPersonal) : hide(panelCreditoPersonal)", tipoPago);
        Assert.Contains("renderDetalles()", tipoPago);
    }

    [Fact]
    public void VentaCreateJs_NoRenderizaBotonPagoPorItem()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        Assert.DoesNotContain("btn-configurar-pago-item", render);
        Assert.DoesNotContain("openModalPagoItem(parseInt", render);
        Assert.DoesNotContain("Tipo de pago:", render);
    }

    [Fact]
    public void VentaCreateJs_ModalPagoPorItemQuedaSinEntradaDesdeNuevaVenta()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.DoesNotContain("async function openModalPagoItem", script);
        Assert.DoesNotContain("function guardarPagoItem", script);
        Assert.DoesNotContain("function closeModalPagoItem", script);
        Assert.DoesNotContain("modal-pago-item", script);
        Assert.DoesNotContain("btnGuardarPagoItem?.addEventListener", script);
        Assert.DoesNotContain("selectTipoPagoItem?.addEventListener", script);
    }

    [Fact]
    public void VentaCreateJs_NoLlamaMediosPagoPorProductoDesdeNuevaVenta()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.DoesNotContain("GetMediosPagoPorProducto", script);
        Assert.DoesNotContain("async function cargarMediosPagoPorProducto", script);
        Assert.DoesNotContain("function poblarSelectTipoPagoItem", script);
        Assert.DoesNotContain("function obtenerPlanesParaItem", script);
    }

    [Fact]
    public void VentaCreateJs_NoLlamaDiagnosticoCondicionesPagoDesdeNuevaVenta()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var programar = ExtractFunction(script, "function programarDiagnosticoCondicionesPago");

        Assert.DoesNotContain("DiagnosticarCondicionesPagoCarrito", script);
        Assert.DoesNotContain("function diagnosticarCondicionesPagoCarrito", script);
        Assert.Contains("clearTimeout(diagnosticoCondicionesTimer)", programar);
        Assert.DoesNotContain("setTimeout", programar);
    }

    [Fact]
    public void VentaCreateJs_NoGeneraHiddenInputsPagoPorDetalle()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        Assert.DoesNotContain("Detalles[${i}].TipoPago", render);
        Assert.DoesNotContain("Detalles[${i}].ProductoCondicionPagoPlanId", render);
        Assert.DoesNotContain("if (d.tipoPago != null)", render);
        Assert.DoesNotContain("if (d.planId != null)", render);
    }

    [Fact]
    public void VentaCreateJs_PreviewNoEnviaPagoPorDetalle()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var recalcular = ExtractFunction(script, "async function recalcularTotales");

        Assert.Contains("await postJson('/api/ventas/CalcularTotalesVenta', body)", recalcular);
        Assert.DoesNotContain("tipoPago: d.tipoPago", recalcular);
        Assert.DoesNotContain("productoCondicionPagoPlanId", recalcular);
        Assert.Contains("tarjetaId: tarjetaId", recalcular);
    }

    [Fact]
    public void VentaCreateJs_DetalleEstadoYaNoInicializaPagoPorItem()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.DoesNotContain("tipoPago: null", script);
        Assert.DoesNotContain("planId: null", script);
        Assert.DoesNotContain("planCuotas: null", script);
        Assert.DoesNotContain("planAjustePct: null", script);
    }

    [Fact]
    public void VentaCreate_ConfirmacionBackendSigueSinResolverCondicionesPagoPorProductoDesdeController()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "VentaController.cs"));
        var createPost = ExtractFunction(controller, "public async Task<IActionResult> Create(VentaViewModel viewModel, string? DatosCreditoPersonallJson)");

        Assert.Contains("var venta = await _ventaService.CreateAsync(viewModel);", createPost);
        Assert.DoesNotContain("DiagnosticarCondicionesPagoCarrito", createPost);
        Assert.DoesNotContain("ICondicionesPagoCarritoResolver", createPost);
    }

    [Fact]
    public void VentaCreateJs_NoConservaHelpersDelModalPagoPorItem()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.DoesNotContain("function formatearEtiquetaPlanConSubtotal", script);
        Assert.DoesNotContain("function actualizarResumenPlanSeleccionado", script);
        Assert.DoesNotContain("plan-pago-item-btn", script);
    }

    [Fact]
    public void VentaApiController_ConservaEndpointLegacyGetMediosPagoPorProducto()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "VentaApiController.cs"));

        Assert.Contains("GetMediosPagoPorProducto", controller);
        Assert.Contains("ObtenerMediosPorProductoAsync", controller);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }

    private static string ExtractFunction(string script, string signature)
    {
        var start = script.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontro la funcion {signature}.");

        var nextFunction = script.IndexOf("\n    function ", start + signature.Length, StringComparison.Ordinal);
        var nextAsyncFunction = script.IndexOf("\n    async function ", start + signature.Length, StringComparison.Ordinal);
        var candidates = new[] { nextFunction, nextAsyncFunction }.Where(i => i > start).ToArray();
        var end = candidates.Length > 0 ? candidates.Min() : script.Length;

        return script[start..end];
    }
}
