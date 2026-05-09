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
    public void VentaCreateJs_PueblaSnapshotDeTarjetaDesdeTarjetaActiva()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("hdnTarjetaNombre.value = info.nombre", script);
        Assert.Contains("hdnTarjetaTipo.value = info.tipo", script);
        Assert.Contains("await cargarTarjetasActivas()", script);
        Assert.Contains("ventaForm.requestSubmit()", script);
    }

    [Fact]
    public void CreateController_ArmonizaErroresBackendDeCondicionesPago()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "VentaController.cs"));
        var createPost = ExtractFunction(controller, "public async Task<IActionResult> Create(VentaViewModel viewModel, string? DatosCreditoPersonallJson)");
        var createAjax = ExtractFunction(controller, "public async Task<IActionResult> CreateAjax(VentaViewModel viewModel)");
        var confirmar = ExtractFunction(controller, "public async Task<IActionResult> Confirmar(int id, bool aplicarExcepcionDocumental = false, string? motivoExcepcionDocumental = null)");

        Assert.Contains("catch (CondicionesPagoVentaException ex)", createPost);
        Assert.Contains("CrearMensajePresentacionCondicionesPago(ex.Message)", createPost);
        Assert.Contains("ModelState.AddModelError(\"\", mensaje)", createPost);
        Assert.Contains("message = mensaje", createAjax);
        Assert.Contains("errors = new Dictionary<string, string[]>", createAjax);
        Assert.Contains("catch (CondicionesPagoVentaException ex)", confirmar);
        Assert.Contains("TempData[\"Error\"] = CrearMensajePresentacionCondicionesPago(ex.Message)", confirmar);
    }

    [Fact]
    public void VentaCrearModalJs_UsaMensajeJsonControladoSiNoHayErrors()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-crear-modal.js"));

        Assert.Contains("showErrors(data.errors || { '': [data.message || 'Error al crear la venta.'] });", script);
        Assert.DoesNotContain("DiagnosticarCondicionesPagoCarrito", script);
        Assert.DoesNotContain("MaxCuotasSinInteres", script);
        Assert.DoesNotContain("MaxCuotasConInteres", script);
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
    public void CreateView_TienePanelDiagnosticoCondicionesPagoConBloqueoUi()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"panel-diagnostico-condiciones-pago\"", view);
        Assert.Contains("data-diagnostico-condiciones-pago", view);
        Assert.Contains("id=\"diagnostico-condiciones-pago-bloqueo\"", view);
        Assert.Contains("No se puede confirmar con el medio de pago seleccionado", view);
        Assert.Contains("esta informacion no modifica totales, no aplica ajustes", view);
        Assert.Contains("solo limita cuotas disponibles del medio seleccionado", view);
        Assert.Contains("Las cuotas disponibles fueron restringidas por condiciones del producto", view);
    }

    [Fact]
    public void VentaCreateJs_LlamaEndpointDiagnosticoConProductoIdsYTipoPago()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("function diagnosticarCondicionesPagoCarrito()", script);
        Assert.Contains("postJson('/api/ventas/DiagnosticarCondicionesPagoCarrito', body)", script);
        Assert.Contains("productoIds: detalles.map(d => d.productoId)", script);
        Assert.Contains("tipoPago: Number(selectTipoPago.value)", script);
        Assert.Contains("configuracionTarjetaId: tarjetaId", script);
        Assert.Contains("tipoTarjeta: tipoTarjeta", script);
    }

    [Fact]
    public void VentaCreateJs_DiagnosticoBloqueadoMuestraBloqueoVisual()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("renderDiagnosticoCondicionesPago", script);
        Assert.Contains("permitido ? 'ok' : 'blocked'", script);
        Assert.Contains("permitido ? 'Permitido' : 'Bloqueado'", script);
        Assert.Contains("obtenerMensajeBloqueoCondicionesPago", script);
        Assert.Contains("if (!permitido && diagnosticoCondicionesPagoBloqueo)", script);
        Assert.Contains("diagnosticoCondicionesPagoBloqueo.textContent = mensajeBloqueo", script);
    }

    [Fact]
    public void VentaCreateJs_DiagnosticoBloqueadoImpideConfirmarConEseMedio()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("let diagnosticoCondicionesBloqueaContinuidad = false", script);
        Assert.Contains("actualizarBloqueoContinuidadCondicionesPago(!permitido)", script);
        Assert.Contains("btnConfirmarVenta.disabled = diagnosticoCondicionesBloqueaContinuidad", script);
        Assert.Contains("if (diagnosticoCondicionesBloqueaContinuidad)", script);
        Assert.Contains("e.preventDefault()", script);
    }

    [Fact]
    public void VentaCreateJs_DiagnosticoPermitidoYErroresLiberanContinuidad()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var diagnostico = ExtractFunction(script, "async function diagnosticarCondicionesPagoCarrito");
        var bloqueo = ExtractFunction(script, "function actualizarBloqueoContinuidadCondicionesPago");

        Assert.Contains("actualizarBloqueoContinuidadCondicionesPago(!permitido)", script);
        Assert.Contains("actualizarBloqueoContinuidadCondicionesPago(false)", diagnostico);
        Assert.Contains("diagnosticoCondicionesPagoBloqueo.textContent = ''", bloqueo);
        Assert.Contains("hide(diagnosticoCondicionesPagoBloqueo)", bloqueo);
        Assert.Contains("No se pudo consultar el diagnostico", diagnostico);
    }

    [Fact]
    public void VentaCreateJs_MuestraProductosBloqueantesYMotivoAlVendedor()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("crearBloqueDiagnostico('Productos bloqueantes'", script);
        Assert.Contains("crearBloqueDiagnostico('Bloqueos detallados'", script);
        Assert.Contains("const motivo = getProp(b, 'motivo', 'Motivo')", script);
        Assert.Contains("return `${textoProductoDiagnostico(productoId)}: ${motivo}`", script);
        Assert.Contains("return `No se puede confirmar con el medio seleccionado. ${producto}: ${motivo}`", script);
    }

    [Fact]
    public void VentaCreateJs_NoOcultaNiEliminaMediosDePagoGlobalmente()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var bloqueo = ExtractFunction(script, "function actualizarBloqueoContinuidadCondicionesPago");

        Assert.Contains("btnConfirmarVenta.disabled", bloqueo);
        Assert.DoesNotContain("selectTipoPago.remove", bloqueo);
        Assert.DoesNotContain("selectTipoPago.disabled", bloqueo);
        Assert.DoesNotContain("option.remove", bloqueo);
    }

    [Fact]
    public void VentaCreateJs_ErrorDiagnosticoNoBloqueaVenta()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var diagnostico = ExtractFunction(script, "async function diagnosticarCondicionesPagoCarrito");

        Assert.Contains("catch", diagnostico);
        Assert.Contains("actualizarBloqueoContinuidadCondicionesPago(false)", diagnostico);
        Assert.Contains("El calculo normal y la carga de la venta continuan sin cambios", diagnostico);
    }

    [Fact]
    public void VentaCreateJs_DiagnosticoNoModificaTotales()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDiagnosticoCondicionesPago");

        Assert.Contains("totalReferencia", script);
        Assert.DoesNotContain("actualizarTotalesUI", render);
        Assert.DoesNotContain("hdnTotal.value", render);
        Assert.Contains("await postJson('/api/ventas/CalcularTotalesVenta', body)", script);
    }

    [Fact]
    public void VentaCreateJs_DiagnosticoMaxCuotasSinInteresLimitaDropdownSinTocarTotales()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var diagnostico = ExtractFunction(script, "function obtenerLimiteDiagnosticoCuotas");
        var aplicar = ExtractFunction(script, "function aplicarLimiteCuotasTarjeta");
        var render = ExtractFunction(script, "function renderDiagnosticoCondicionesPago");

        Assert.Contains("Maximos efectivos informativos", render);
        Assert.Contains("maxCuotasSinInteres", diagnostico);
        Assert.Contains("tipoCuota === TIPO_CUOTA_TARJETA.SinInteres", diagnostico);
        Assert.Contains("return maxSinInteres", diagnostico);
        Assert.Contains("selectCuotasTarjeta.remove(i)", aplicar);
        Assert.DoesNotContain("actualizarTotalesUI", aplicar);
        Assert.DoesNotContain("hdnTotal.value", aplicar);
    }

    [Fact]
    public void VentaCreateJs_MaxCuotasEscalaresSiguenFuncionando()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var diagnostico = ExtractFunction(script, "function obtenerLimiteDiagnosticoCuotas");

        Assert.Contains("maxCuotasSinInteres", diagnostico);
        Assert.Contains("maxCuotasConInteres", diagnostico);
        Assert.Contains("maxCuotasCredito", diagnostico);
    }

    [Fact]
    public void VentaCreateJs_DiagnosticoMaxCuotasConInteresLimitaDropdownSinTocarTotales()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var diagnostico = ExtractFunction(script, "function obtenerLimiteDiagnosticoCuotas");
        var aplicar = ExtractFunction(script, "function aplicarLimiteCuotasTarjeta");

        Assert.Contains("maxCuotasConInteres", diagnostico);
        Assert.Contains("tipoCuota === TIPO_CUOTA_TARJETA.ConInteres", diagnostico);
        Assert.Contains("return maxConInteres", diagnostico);
        Assert.Contains("selectCuotasTarjeta.remove(i)", aplicar);
        Assert.DoesNotContain("actualizarTotalesUI", aplicar);
    }

    [Fact]
    public void VentaCreateJs_DiagnosticoMaxCuotasCreditoQuedaInformativoSiNoHaySelectorCredito()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var diagnostico = ExtractFunction(script, "function obtenerLimiteDiagnosticoCuotas");

        Assert.Contains("selectTipoPago?.value === TIPO_PAGO.CreditoPersonal", diagnostico);
        Assert.Contains("return maxCredito", diagnostico);
        Assert.DoesNotContain("select-cuotas-credito", view);
        Assert.Contains("if (maxCredito != null) maximos.push(`Credito personal: hasta ${maxCredito}`)", script);
    }

    [Fact]
    public void VentaCreateJs_AplicaReglaMasRestrictivaEntreLimiteExistenteYDiagnostico()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var aplicar = ExtractFunction(script, "function aplicarLimiteCuotasTarjeta");

        Assert.Contains("const limites = [limiteCuotasExistente, limiteCuotasDiagnostico]", aplicar);
        Assert.Contains("Math.min(...limites)", aplicar);
        Assert.Contains("if (parseInt(selectCuotasTarjeta.options[i].value) > limiteEfectivo)", aplicar);
    }

    [Fact]
    public void VentaCreateJs_CambiarMedioOTarjetaLiberaORecalculaCuotas()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var tipoPago = ExtractFunction(script, "function onTipoPagoChange");
        var tarjetaChangeStart = script.IndexOf("selectTarjeta?.addEventListener('change'", StringComparison.Ordinal);
        Assert.True(tarjetaChangeStart >= 0, "No se encontro el handler de cambio de tarjeta.");
        var tarjetaChange = script[tarjetaChangeStart..script.IndexOf("selectCuotasTarjeta?.addEventListener", tarjetaChangeStart, StringComparison.Ordinal)];

        Assert.Contains("limiteCuotasDiagnostico = null", tipoPago);
        Assert.Contains("cuotasLimitadasPorDiagnostico = false", tipoPago);
        Assert.Contains("aplicarLimiteCuotasTarjeta()", tipoPago);
        Assert.Contains("limiteCuotasDiagnostico = null", tarjetaChange);
        Assert.Contains("repoblarCuotasTarjeta(info)", tarjetaChange);
        Assert.Contains("await recalcularTotales()", tarjetaChange);
    }

    [Fact]
    public void VentaCreateJs_ErrorDiagnosticoNoLimitaCuotas()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var diagnostico = ExtractFunction(script, "async function diagnosticarCondicionesPagoCarrito");

        Assert.Contains("catch", diagnostico);
        Assert.Contains("limiteCuotasDiagnostico = null", diagnostico);
        Assert.Contains("cuotasLimitadasPorDiagnostico = false", diagnostico);
        Assert.Contains("aplicarLimiteCuotasTarjeta()", diagnostico);
    }

    [Fact]
    public void VentaCreateJs_RecargosYDescuentosSiguenInformativos()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDiagnosticoCondicionesPago");

        Assert.Contains("Ajustes configurados informativos", render);
        Assert.Contains("(no aplicado al total)", render);
        Assert.DoesNotContain("recargoDebitoPreview =", render);
        Assert.DoesNotContain("totalFinal.textContent", render);
    }

    [Fact]
    public void VentaCreateJs_MuestraMaximosYAjustesSinAplicarlosATotales()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDiagnosticoCondicionesPago");

        Assert.Contains("Maximos efectivos informativos", render);
        Assert.Contains("Ajustes configurados informativos", render);
        Assert.Contains("(no aplicado al total)", render);
        Assert.DoesNotContain("actualizarTotalesUI", render);
        Assert.Contains("function aplicarLimiteCuotasSinInteres", script);
    }

    [Fact]
    public void VentaCreateJs_ConservaPreviewRecargoDebitoYCalculoTotalesSeparados()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("La venta sigue sin cambios productivos", script);
        Assert.Contains("await postJson('/api/ventas/CalcularTotalesVenta', body)", script);
        Assert.Contains("recargoDebitoPreview", script);
    }

    [Fact]
    public void VentaCreate_ConfirmacionBackendSigueSinValidarCondicionesPago()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "VentaController.cs"));
        var createPost = ExtractFunction(controller, "public async Task<IActionResult> Create(VentaViewModel viewModel, string? DatosCreditoPersonallJson)");

        Assert.Contains("var venta = await _ventaService.CreateAsync(viewModel);", createPost);
        Assert.DoesNotContain("DiagnosticarCondicionesPagoCarrito", createPost);
        Assert.DoesNotContain("ICondicionesPagoCarritoResolver", createPost);
        Assert.DoesNotContain("MaxCuotasSinInteres", createPost);
        Assert.DoesNotContain("MaxCuotasConInteres", createPost);
        Assert.DoesNotContain("MaxCuotasCredito", createPost);
    }

    // ── Fase 15.7.B: selector de plan de pago ──────────────────────────

    [Fact]
    public void CreateView_TienePanelPlanesPagoConInputHiddenYLista()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"panel-planes-pago\"", view);
        Assert.Contains("id=\"lista-planes-pago\"", view);
        Assert.Contains("name=\"DatosTarjeta.ProductoCondicionPagoPlanId\"", view);
        Assert.Contains("id=\"hdn-plan-pago-id\"", view);
    }

    [Fact]
    public void VentaCreateJs_ConsumePlanesDisponiblesDelDiagnostico()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDiagnosticoCondicionesPago");

        Assert.Contains("planesDisponibles", render, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PlanesDisponibles", render);
        Assert.Contains("renderSelectorPlanesPago(planesRaw)", render);
    }

    [Fact]
    public void VentaCreateJs_TieneFuncionEsTipoPagoConPlanes()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("function esTipoPagoConPlanes(tipoPago)", script);
        Assert.Contains("TIPO_PAGO.TarjetaCredito", script);
        Assert.Contains("TIPO_PAGO.TarjetaDebito", script);
        Assert.Contains("TIPO_PAGO.MercadoPago", script);
    }

    [Fact]
    public void VentaCreateJs_SelectorPlanNoAparece_ParaMediosDirectos()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var renderSelector = ExtractFunction(script, "function renderSelectorPlanesPago");

        Assert.Contains("esTipoPagoConPlanes(tipoPago)", renderSelector);
        Assert.Contains("limpiarSelectorPlanesPago()", renderSelector);
    }

    [Fact]
    public void VentaCreateJs_SelectorPlanAparece_ParaTarjetaDebitoYMercadoPago()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("function esTipoPagoConPlanes", script);
        Assert.Contains("TIPO_PAGO.MercadoPago", script.Substring(script.IndexOf("function esTipoPagoConPlanes", StringComparison.Ordinal)));
    }

    [Fact]
    public void VentaCreateJs_CambioDeTipoPagoLimpiaPlanSeleccionado()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var tipoPago = ExtractFunction(script, "function onTipoPagoChange");

        Assert.Contains("limpiarSelectorPlanesPago()", tipoPago);
    }

    [Fact]
    public void VentaCreateJs_AjustePlanMostradoComoInformativo()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var formatear = ExtractFunction(script, "function formatearEtiquetaPlan");

        Assert.Contains("informativo", formatear);
        Assert.Contains("ajustePorcentaje", formatear, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sin ajuste", formatear);
    }

    [Fact]
    public void VentaCreateJs_SeleccionarPlanEscribeEnHiddenInput()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("hdnPlanPagoId", script);
        Assert.Contains("hdnPlanPagoId.value = planId", script);
    }

    [Fact]
    public void VentaCreateJs_LimpiarSelectorPlanesPagoLimpiaPlanId()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var limpiar = ExtractFunction(script, "function limpiarSelectorPlanesPago");

        Assert.Contains("hdnPlanPagoId.value = ''", limpiar);
        Assert.Contains("hide(panelPlanesPago)", limpiar);
    }

    // ── Fase 16.3: UI pago por ítem ───────────────────────────────────

    [Fact]
    public void CreateView_TieneModalPagoItem()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"modal-pago-item\"", view);
        Assert.Contains("id=\"select-tipo-pago-item\"", view);
        Assert.Contains("id=\"modal-pago-item-planes\"", view);
        Assert.Contains("id=\"btn-guardar-pago-item\"", view);
        Assert.Contains("id=\"modal-pago-item-titulo\"", view);
        Assert.Contains("btn-cerrar-pago-item", view);
    }

    [Fact]
    public void CreateView_TieneColumnaPagoEnHeaderTabla()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains(">Pago<", view);
    }

    [Fact]
    public void VentaCreateJs_TieneFuncionesModalPagoItem()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("function openModalPagoItem", script);
        Assert.Contains("function guardarPagoItem", script);
        Assert.Contains("function closeModalPagoItem", script);
        Assert.Contains("function actualizarPlanesItem", script);
    }

    [Fact]
    public void VentaCreateJs_HiddenInputsIncluyenTipoPagoYPlanPorLinea()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("Detalles[${i}].TipoPago", script);
        Assert.Contains("Detalles[${i}].ProductoCondicionPagoPlanId", script);
    }

    [Fact]
    public void VentaCreateJs_DetalleEstadoTieneTipoPagoYPlanIdNullable()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("tipoPago: null", script);
        Assert.Contains("planId: null", script);
    }

    [Fact]
    public void VentaCreateJs_MuestraBadgeUsaPagoPrincipalConBotonPorLinea()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("Usa pago principal", script);
        Assert.Contains("btn-configurar-pago-item", script);
    }

    [Fact]
    public void VentaCreateJs_GuardarPagoItemActualizaDetalleYRerenderiza()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var guardar = ExtractFunction(script, "function guardarPagoItem");

        Assert.Contains("detalles[pagoItemModalIndex].tipoPago", guardar);
        Assert.Contains("detalles[pagoItemModalIndex].planId", guardar);
        Assert.Contains("renderDetalles()", guardar);
        Assert.Contains("closeModalPagoItem()", guardar);
    }

    [Fact]
    public void VentaCreateJs_GuardarPagoItemNoModificaTotales()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var guardar = ExtractFunction(script, "function guardarPagoItem");

        Assert.DoesNotContain("recalcularTotales()", guardar);
        Assert.DoesNotContain("actualizarTotalesUI", guardar);
        Assert.DoesNotContain("postJson('/api/ventas/CalcularTotalesVenta'", guardar);
    }

    [Fact]
    public void VentaCreateJs_HiddenInputsTipoPagoSoloSiDefinido()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("if (d.tipoPago != null)", script);
        Assert.Contains("if (d.planId != null)", script);
    }

    [Fact]
    public void VentaCreateJs_SelectorGlobalSigueFuncionandoComoFallback()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("selectTipoPago", script);
        Assert.Contains("function onTipoPagoChange", script);
    }

    // ── Fase 17.2: badge heredado muestra pago principal ──────────────

    [Fact]
    public void VentaCreateJs_BadgeHeredadoMuestraLabelDelPagoPrincipalGlobal()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        Assert.Contains("globalLabel", render);
        Assert.Contains("Usa pago principal:", render);
        Assert.Contains("selectedOptions", render);
    }

    [Fact]
    public void VentaCreateJs_CambioTipoPagoGlobalRerenderizaBadgesItems()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var tipoPago = ExtractFunction(script, "function onTipoPagoChange");

        Assert.Contains("renderDetalles()", tipoPago);
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
