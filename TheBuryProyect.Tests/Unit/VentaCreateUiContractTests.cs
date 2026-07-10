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
        Assert.Contains("<label class=\"venta-label\" for=\"select-tipo-pago\">Tipo de pago principal</label>", view);
        Assert.Contains("Selecciona el medio principal de cobro para toda la venta.", view);
        Assert.Contains("tipo de pago principal", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<div class=\"hidden\">\r\n                        <select asp-for=\"TipoPago\"", view);
    }

    [Fact]
    public void VentaCreate_View_ConservaTipoPagoPrincipalVisible()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("<label class=\"venta-label\" for=\"select-tipo-pago\">Tipo de pago principal</label>", view);
        Assert.Contains("id=\"select-tipo-pago\"", view);
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
        Assert.Contains("id=\"panel-planes-pago\"", view);
        Assert.Contains("id=\"configuracion-pagos-global-estado\"", view);
        Assert.DoesNotContain("name=\"DatosTarjeta.ProductoCondicionPagoPlanId\"", view);
        Assert.DoesNotContain("id=\"hdn-plan-pago-id\"", view);
    }

    [Fact]
    public void CreateView_TieneSoporteSelectorProductoUnidad()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"hdn-producto-requiere-numero-serie\"", view);
        Assert.Contains("id=\"panel-selector-unidad\"", view);
        Assert.Contains("id=\"select-producto-unidad\"", view);
        Assert.Contains("id=\"producto-unidad-ayuda\"", view);
        Assert.Contains("unidad física", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateView_TieneAvisoSinUnidadesConLinkAGestionarUnidades()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"aviso-sin-unidades\"", view);
        Assert.Contains("id=\"link-gestionar-unidades\"", view);
        Assert.Contains("Gestionar unidades", view);
        Assert.Contains("antes de vender", view);
    }

    [Fact]
    public void CreateView_PanelDiagnosticoCondicionesPagoExisteOcultoEnNuevaVenta()
    {
        // El panel existe en el DOM para que los refs JS resuelvan, pero empieza oculto.
        // El diagnóstico no dispara en nueva venta (programarDiagnosticoCondicionesPago es stub),
        // por lo que el panel permanece hidden durante toda la sesión de Create.
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"panel-diagnostico-condiciones-pago\"", view);
        Assert.Contains("id=\"diagnostico-condiciones-pago-bloqueo\"", view);
        Assert.DoesNotContain("data-diagnostico-condiciones-pago", view);
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
        Assert.Contains("cargarConfiguracionPagosGlobal()", script);
        Assert.Contains("fetchJson('/api/ventas/configuracion-pagos-global')", script);
        Assert.Contains("aplicarMediosGlobalesAlSelector(medios)", script);
        Assert.Contains("const val = selectTipoPago.value", tipoPago);
        Assert.Contains("const isTarjeta = esTipoPagoTarjeta(val)", tipoPago);
        Assert.Contains("isTarjeta ? show(panelTarjeta) : hide(panelTarjeta)", tipoPago);
        Assert.Contains("const isCredito = esTipoPagoCredito(val)", tipoPago);
        Assert.Contains("isCredito ? show(panelCreditoPersonal) : hide(panelCreditoPersonal)", tipoPago);
        Assert.Contains("renderDetalles()", tipoPago);
    }

    [Fact]
    public void VentaCreateJs_SigueUsandoSelectTipoPago()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("select-tipo-pago", script);
        Assert.Contains("selectTipoPago?.addEventListener('change', onTipoPagoChange)", script);
    }

    [Fact]
    public void VentaCreateJs_ManejaMediosTarjetasPlanesGlobales()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("function normalizarMedioGlobal", script);
        Assert.Contains("planesGenerales", script);
        Assert.Contains("planesEspecificos", script);
        Assert.Contains("function getPlanesGlobalesDisponibles", script);
        Assert.Contains("function renderPlanesGlobalesSeleccionados", script);
        Assert.Contains("No hay planes activos configurados para el medio seleccionado.", script);
    }

    [Fact]
    public void VentaCreateJs_ContemplaEndpointGlobalVacioOFallido()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var cargar = ExtractFunction(script, "async function cargarConfiguracionPagosGlobal");

        Assert.Contains("No hay medios activos en la configuracion global.", cargar);
        Assert.Contains("No se pudo cargar la configuracion global. Se conserva el selector actual.", cargar);
        Assert.Contains("catch", cargar);
    }

    [Fact]
    public void VentaCreateJs_AplicarMediosGlobalesConservaFallbackCuandoListaVacia()
    {
        // Cuando la API devuelve medios vacíos, el selector no debe vaciarse.
        // El guard medios.length === 0 preserva las opciones renderizadas por Razor.
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var fn = ExtractFunction(script, "function aplicarMediosGlobalesAlSelector");

        Assert.Contains("medios.length === 0", fn);
    }

    [Fact]
    public void VentaCreateJs_ConfiguracionPagosGlobalDisponibleSoloCuandoHayMediosActivos()
    {
        // configuracionPagosGlobalDisponible debe ser false cuando medios es vacío.
        // Así las rutas gated por el flag (selector de tarjeta, planes, límites de cuotas)
        // no ejecutan con datos vacíos y el fallback a Razor/API propia funciona correctamente.
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var cargar = ExtractFunction(script, "async function cargarConfiguracionPagosGlobal");

        Assert.Contains("configuracionPagosGlobalDisponible = medios.length > 0", cargar);
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
    public void VentaCreateJs_CargaUnidadesDisponiblesParaProductoTrazable()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("data-requiere-numero-serie", script);
        Assert.Contains("async function cargarUnidadesDisponibles", script);
        Assert.Contains("/api/productos/${productoId}/unidades-disponibles", script);
        Assert.Contains("cargarUnidadesDisponibles(parseInt(hdnProductoId?.value))", script);
    }

    [Fact]
    public void VentaCreateJs_MuestraAvisoConLinkCuandoNoHayUnidades()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var cargar = ExtractFunction(script, "async function cargarUnidadesDisponibles");
        var agregar = ExtractFunction(script, "btnAgregarProducto?.addEventListener");

        // DOM refs presentes
        Assert.Contains("avisoSinUnidades", script);
        Assert.Contains("linkGestionarUnidades", script);

        // cargarUnidadesDisponibles muestra aviso con link cuando lista vacía
        Assert.Contains("disponibles.length === 0", cargar);
        Assert.Contains("/Producto/Unidades/${productoId}", cargar);
        Assert.Contains("show(avisoSinUnidades)", cargar);
        Assert.Contains("No hay unidades disponibles para este producto", cargar);

        // limpiarSelectorUnidad oculta el aviso al cambiar producto
        Assert.Contains("hide(avisoSinUnidades)", script);

        // btnAgregarProducto también muestra aviso cuando selector está deshabilitado
        Assert.Contains("show(avisoSinUnidades)", agregar);
        Assert.Contains("/Producto/Unidades/", agregar);
        Assert.Contains("No hay unidades disponibles para este producto", agregar);
    }

    [Fact]
    public void VentaCreateJs_RenderizaYValidaSelectorProductoUnidad()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var agregar = ExtractFunction(script, "btnAgregarProducto?.addEventListener");
        var render = ExtractFunction(script, "function renderDetalles");
        var submit = ExtractFunction(script, "ventaForm.addEventListener");

        Assert.Contains("const productoUnidadId = origenEsUnidad", agregar);
        Assert.Contains("unidad física", agregar, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No hay unidades disponibles para este producto", agregar);
        Assert.Contains("unidadesSeleccionadasExcepto(productoUnidadId)", agregar);
        Assert.Contains("cantidad !== 1", agregar);
        Assert.Contains("Detalles[${i}].ProductoUnidadId", render);
        Assert.Contains("Unidad física:", render);
        Assert.Contains("trazableSinUnidad", submit);
        Assert.Contains("unidadesDuplicadas", submit);
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
        Assert.DoesNotContain("ProductoCondicionPagoPlanId", script);
        Assert.DoesNotContain("hdn-plan-pago-id", script);
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
    public void VentaApiController_NoConservaEndpointLegacyGetMediosPagoPorProducto()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "VentaApiController.cs"));

        Assert.DoesNotContain("GetMediosPagoPorProducto", controller);
        Assert.DoesNotContain("public async Task<IActionResult> GetMediosPagoPorProducto", controller);
        Assert.DoesNotContain("ObtenerMediosPorProductoAsync", controller);
    }

    [Fact]
    public void VentaApiController_NoConservaEndpointLegacyDiagnosticarCondicionesPagoCarrito()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "VentaApiController.cs"));

        Assert.DoesNotContain("DiagnosticarCondicionesPagoCarrito", controller);
        Assert.DoesNotContain("ICondicionesPagoCarritoResolver", controller);
    }

    [Fact]
    public void VentaCreateJs_RenderizaStockBreakdown()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var fn = ExtractFunction(script, "function renderStockInfo");

        Assert.Contains("Stock total:", fn);
        Assert.Contains("Identificadas:", fn);
        Assert.Contains("Sin identificar:", fn);
        Assert.Contains("unidadesEnStock", fn);
        Assert.Contains("stockSinIdentificar", fn);
    }

    [Fact]
    public void VentaCreateJs_MuestraAdvertenciaConciliacionSiStockSinIdentificarNegativo()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var fn = ExtractFunction(script, "function renderStockInfo");

        Assert.Contains("stockSinIdentificar < 0", fn);
        Assert.Contains("Revisar conciliación", fn);
    }

    // ── VENTAS-UX-1F — mobile sticky footer y barra de resumen ─────────────

    [Fact]
    public void CreateView_StickyFooterMobile_ExisteEnFormulario()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("vm-mobile-summary-bar", view);
        Assert.Contains("id=\"vm-modal-sticky-total\"", view);
    }

    [Fact]
    public void CreateView_StickyFooterMobile_BtnEsTypeButton()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        var idx = view.IndexOf("class=\"vm-mobile-summary-bar\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Se esperaba class=\"vm-mobile-summary-bar\" en Create_tw.cshtml.");
        var footerBlock = view[idx..(Math.Min(idx + 600, view.Length))];
        Assert.Contains("type=\"button\"", footerBlock);
        Assert.DoesNotContain("type=\"submit\"", footerBlock);
    }

    [Fact]
    public void CreateView_StickyFooterMobile_BtnEsOperableComoProxyDeConfirmar()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        var idx = view.IndexOf("class=\"vm-mobile-summary-bar\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Se esperaba class=\"vm-mobile-summary-bar\" en Create_tw.cshtml.");
        var footerBlock = view[idx..(Math.Min(idx + 1000, view.Length))];
        Assert.Contains("data-wizard-submit-proxy=\"btn-confirmar\"", footerBlock);
        Assert.DoesNotContain("aria-hidden=\"true\"", footerBlock);
        Assert.DoesNotContain("tabindex=\"-1\"", footerBlock);
    }

    [Fact]
    public void CreateView_TotalesConservanSusIds()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"total-final\"", view);
        Assert.Contains("id=\"total-subtotal\"", view);
        Assert.Contains("id=\"total-descuento\"", view);
        Assert.Contains("id=\"total-iva\"", view);
    }

    [Fact]
    public void CreateView_HiddenInputsTotalesConservados()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"hdn-subtotal\"", view);
        Assert.Contains("id=\"hdn-descuento\"", view);
        Assert.Contains("id=\"hdn-iva\"", view);
        Assert.Contains("id=\"hdn-total\"", view);
    }

    [Fact]
    public void CreateView_BtnConfirmarPrincipalConservado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"btn-confirmar\"", view);
        Assert.Contains("type=\"submit\"", view);
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

    // ── Fase 7.1B — MercadoPago DatosTarjeta UI contract ────────────────

    [Fact]
    public void VentaCreateJs_TrataMercadoPagoComoMedioConPlanes_PeroNoPanelTarjeta()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        var esTarjeta = ExtractFunction(script, "function esTipoPagoTarjeta");
        var conPlanes = ExtractFunction(script, "function esTipoPagoConPlanes");

        // MercadoPago included in esTipoPagoConPlanes → puede tener planes globales
        Assert.Contains("TIPO_PAGO.MercadoPago", conPlanes);

        // MercadoPago NOT included in esTipoPagoTarjeta → no usa selector de tarjeta del catálogo
        Assert.DoesNotContain("MercadoPago", esTarjeta);
    }

    [Fact]
    public void VentaCreateJs_MercadoPago_SetNombreTarjetaYTipoDebitoEnHiddenInputs()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var onTipoPagoChange = ExtractFunction(script, "function onTipoPagoChange");

        // MercadoPago no usa ConfiguracionTarjeta; TipoTarjeta queda como Debito para no identificarlo como credito.
        Assert.Contains("TIPO_PAGO.MercadoPago", onTipoPagoChange);
        Assert.Contains("hdnTarjetaNombre.value = 'Mercado Pago'", onTipoPagoChange);
        Assert.Contains("hdnTarjetaTipo.value = TIPO_TARJETA.Debito", onTipoPagoChange);
        Assert.DoesNotContain("hdnTarjetaTipo.value = '1'", onTipoPagoChange);
        Assert.Contains("Debito: '0'", script);
    }

    [Fact]
    public void VentaCreateView_MercadoPago_TieneHiddenInputsParaDatosTarjeta()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        // Los hidden inputs que envían DatosTarjeta (incluso para MercadoPago) deben estar presentes
        Assert.Contains("name=\"DatosTarjeta.NombreTarjeta\"", view);
        Assert.Contains("name=\"DatosTarjeta.TipoTarjeta\"", view);
        Assert.Contains("name=\"DatosTarjeta.ConfiguracionPagoPlanId\"", view);
        Assert.Contains("id=\"hdn-configuracion-pago-plan-id\"", view);
    }

    [Fact]
    public void VentaCreateJs_MercadoPago_RenderizaPlanesGlobales_CuandoConfigDisponible()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var onTipoPagoChange = ExtractFunction(script, "function onTipoPagoChange");

        // Los medios no-tarjeta (incluido MercadoPago) disparan renderPlanesGlobalesSeleccionados
        Assert.Contains("if (!isTarjeta && configuracionPagosGlobalDisponible)", onTipoPagoChange);
        Assert.Contains("renderPlanesGlobalesSeleccionados()", onTipoPagoChange);
    }

    [Fact]
    public void VentaCreateJs_Efectivo_Transferencia_NoActivanPlanesGlobales()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var conPlanes = ExtractFunction(script, "function esTipoPagoConPlanes");

        // Efectivo y Transferencia no están en esTipoPagoConPlanes → no renderizan planes
        Assert.DoesNotContain("TIPO_PAGO.Efectivo", conPlanes);
        Assert.DoesNotContain("TIPO_PAGO.Transferencia", conPlanes);
    }

    [Fact]
    public void VentaCreateJs_TarjetaCredito_Debito_UsanEsTipoPagoTarjeta()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var esTarjeta = ExtractFunction(script, "function esTipoPagoTarjeta");

        Assert.Contains("TIPO_PAGO.TarjetaCredito", esTarjeta);
        Assert.Contains("TIPO_PAGO.TarjetaDebito", esTarjeta);
        Assert.Contains(
            "return tipoPago === TIPO_PAGO.TarjetaCredito || tipoPago === TIPO_PAGO.TarjetaDebito;",
            esTarjeta);
    }

    [Fact]
    public void VentaViewBagBuilder_NoExponeTipoPagoTarjetaEnSelectorFallbackNuevo()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Helpers", "VentaViewBagBuilder.cs"));

        Assert.Contains("CrearTiposPagoParaVenta", source);
        Assert.Contains("TipoPago.Tarjeta", source);
        Assert.Contains("tipoPagoSeleccionado != TipoPago.Tarjeta", source);
        Assert.Contains("Tarjeta (historico)", source);
    }

    [Fact]
    public void LimpiarModelState_TrataMercadoPagoComoEsTarjeta_ConservaDatosTarjeta()
    {
        var controller = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controllers", "VentaController.cs"));
        var limpiar = ExtractFunction(controller, "private void LimpiarModelStateSegunTipoPago");

        // MercadoPago se trata como esTarjeta → no limpia DatosTarjeta del ModelState
        Assert.Contains("TipoPago.MercadoPago", limpiar);
    }

    [Fact]
    public void VentaCreate_View_TieneContenedorDetalleCobro()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));
        Assert.Contains("Detalle de cobro", view);
        Assert.Contains("id=\"panel-tarjeta\"", view);
        Assert.Contains("id=\"panel-cheque\"", view);
    }

    [Fact]
    public void VentaCreate_View_TieneBotonOAccionAgregarProducto()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));
        Assert.True(
            view.Contains("id=\"btn-agregar-produto\"") || view.Contains("id=\"btn-agregar-producto\""),
            "Se esperaba id btn-agregar-produto o btn-agregar-producto en Create_tw.cshtml");
    }

    [Fact]
    public void VentaCreateJs_ContieneHandlerAgregarProducto()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        Assert.Contains("btnAgregarProducto?.addEventListener", script);
        Assert.Contains("renderDetalles()", script);
        Assert.Contains("recalcularTotales()", script);
    }

    // ── Carlos cleanup: defensive JS + tabla modal ──────────────────────

    [Fact]
    public void VentaCreateJs_UsaNullGuardParaHdnProductoRequiereNumeroSerie()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        Assert.Contains("if (hdnProductoRequiereNumeroSerie)", script);
        Assert.Contains("hdnProductoRequiereNumeroSerie.value = requiereNumeroSerie ? 'true' : 'false';", script);
    }

    // ── Fase Kira — Advertencia stock sin identificar ────────────────────

    [Fact]
    public void VentaCreate_View_ContieneAdvertenciaStockSinIdentificar()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"advertencia-stock-sin-identificar\"", view);
    }

    [Fact]
    public void VentaCreateJs_ContieneFuncionActualizarAdvertenciaStockSinIdentificar()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("function actualizarAdvertenciaStockSinIdentificar", script);
        Assert.Contains("advertenciaStockSinIdentificar", script);
    }

    [Fact]
    public void VentaCreateJs_AdvertenciaEvaluaCantidadMayorAStockSinIdentificar()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var fn = ExtractFunction(script, "function actualizarAdvertenciaStockSinIdentificar");

        Assert.Contains("cantidad > productoActualStockSinIdentificar", fn);
        Assert.Contains("productoActualStockSinIdentificar < 0", fn);
    }

    [Fact]
    public void VentaCreateJs_AdvertenciaSoloParaNoTrazablesConUnidades()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var fn = ExtractFunction(script, "function actualizarAdvertenciaStockSinIdentificar");

        Assert.Contains("requiereNumeroSerie", fn);
        Assert.Contains("productoActualUnidadesEnStock", fn);
    }

    [Fact]
    public void VentaCreateJs_AdvertenciaNoBloqueaAgregarProducto()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var submit = ExtractFunction(script, "ventaForm.addEventListener");

        // El submit/confirm no bloquea por la advertencia de stock sin identificar
        Assert.DoesNotContain("advertenciaStockSinIdentificar", submit);
        Assert.DoesNotContain("productoActualStockSinIdentificar", submit);
    }

    // ── VENTAS-UX-1C — copy y accesibilidad ─────────────────────────────

    [Fact]
    public void CreateView_LabelTipoPagoPrincipalTieneForYTextoArmonizado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("<label class=\"venta-label\" for=\"select-tipo-pago\">Tipo de pago principal</label>", view);
        Assert.DoesNotContain("<label class=\"venta-label\">Tipo de pago</label>", view);
    }

    [Fact]
    public void CreateView_PanelAlertaMoraTieneRoleAlert()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        var idx = view.IndexOf("id=\"panel-alerta-mora\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Se esperaba id panel-alerta-mora en la vista.");
        var context = view[idx..(idx + 200)];
        Assert.Contains("role=\"alert\"", context);
    }

    [Fact]
    public void CreateView_PanelCupoInsuficienteTieneRoleAlert()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        var idx = view.IndexOf("id=\"panel-cupo-insuficiente\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Se esperaba id panel-cupo-insuficiente en la vista.");
        var context = view[idx..(idx + 200)];
        Assert.Contains("role=\"alert\"", context);
    }

    [Fact]
    public void CreateView_LabelesTarjetaCuotasAutorizacionTienenFor()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("for=\"select-tarjeta\"", view);
        Assert.Contains("for=\"select-cuotas-tarjeta\"", view);
        Assert.Contains("for=\"txt-num-autorizacion-tarjeta\"", view);
    }

    // ── VENTAS-UX-1E-A — accesibilidad botones dinámicos en renderDetalles ─────────────

    [Fact]
    public void VentaCreateJs_RenderDetalles_BtnEliminarTieneAriaLabel()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        Assert.Contains("btn-eliminar-detalle", render);
        Assert.Contains("aria-label=", render);
    }

    [Fact]
    public void VentaCreateJs_RenderDetalles_BtnEliminarAriaLabelUsaEscParaNombre()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        // aria-label usa esc() para escapar el nombre — protege contra XSS en atributo
        Assert.Contains("esc(d.nombre)", render);
        Assert.Contains("aria-label=", render);
    }

    [Fact]
    public void VentaCreateJs_RenderDetalles_BtnEliminarConservaClasesYDataIndex()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        Assert.Contains("btn-eliminar-detalle", render);
        Assert.Contains("data-index=", render);
        Assert.Contains("type=\"button\"", render);
    }

    [Fact]
    public void VentaCreateJs_FuncionEscExiste()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("function esc(", script);
        Assert.Contains(".replaceAll('\"', '&quot;')", script);
    }

    // ── VENTAS-UX-1E-B — escape seguro en celdas dinámicas de renderDetalles ─────────────

    [Fact]
    public void VentaCreateJs_RenderDetalles_CeldaCodigoUsaEsc()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        // d.codigo se escapa antes de interpolarse en HTML — protege contra XSS en celda
        Assert.Contains("esc(d.codigo)", render);
    }

    [Fact]
    public void VentaCreateJs_RenderDetalles_CeldaNombreUsaEscEnContenido()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var render = ExtractFunction(script, "function renderDetalles");

        // esc(d.nombre) debe aparecer >= 2 veces: en contenido de celda y en aria-label del botón
        var count = render.Split("esc(d.nombre)").Length - 1;
        Assert.True(count >= 2, "esc(d.nombre) debe usarse en la celda de nombre y en el aria-label del botón eliminar.");
    }

    // ── VENTAS-UX-1G — resumen pre-confirmación ─────────────────────────

    [Fact]
    public void CreateView_PanelDocumentacionFaltanteTieneRoleAlert()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        var idx = view.IndexOf("id=\"panel-documentacion-faltante\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Se esperaba id panel-documentacion-faltante en la vista.");
        var context = view[idx..(idx + 120)];
        Assert.Contains("role=\"alert\"", context);
    }

    [Fact]
    public void CreateView_TieneRecordatorioPreConfirmacion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("Verificá cliente, tipo de pago y total", view);
        var reminderIdx = view.IndexOf("Verificá cliente, tipo de pago y total", StringComparison.Ordinal);
        var btnIdx = view.IndexOf("id=\"btn-confirmar\"", StringComparison.Ordinal);
        Assert.True(reminderIdx < btnIdx, "El recordatorio pre-confirmación debe aparecer antes del btn-confirmar.");
    }

    [Fact]
    public void CreateView_RecordatorioPreConfirmacion_TieneRoleNote()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("Verificá cliente, tipo de pago y total", view);
        var idx = view.IndexOf("Verificá cliente, tipo de pago y total", StringComparison.Ordinal);
        var contextStart = Math.Max(0, idx - 300);
        var context = view[contextStart..idx];
        Assert.Contains("role=\"note\"", context);
    }

    // ── VENTAS-UX-MAINT-1 — labels accesibles en Create_tw ─────────────

    [Fact]
    public void CreateView_LabelBuscarClienteTieneFor()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("for=\"input-buscar-cliente\"", view);
        Assert.Contains("id=\"input-buscar-cliente\"", view);
    }

    [Fact]
    public void CreateView_LabelFechaOperacionTieneFor()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("for=\"FechaVenta\"", view);
        Assert.Contains("asp-for=\"FechaVenta\"", view);
    }

    // ── Vendedor fijo = usuario logueado (sin selector ni display en la UI) ──
    // El vendedor ya no se selecciona ni se muestra en el form; siempre es el
    // usuario logueado y el backend lo resuelve (ResolverVendedorAsync).

    [Fact]
    public void CreateView_VendedorEsUsuarioLogueado_SinSelectorDeDelegacion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.DoesNotContain("asp-for=\"VendedorUserId\"", view);
        Assert.DoesNotContain("id=\"VendedorUserId\"", view);
        Assert.DoesNotContain("Seleccione vendedor", view);
    }

    [Fact]
    public void IndexView_NuevaVentaNavegaACreateSinHookModal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Index_tw.cshtml"));

        Assert.Contains("asp-controller=\"Venta\" asp-action=\"Create\"", view);
        Assert.Contains("Nueva Venta", view);
        Assert.DoesNotContain("id=\"btn-abrir-modal-crear-venta\"", view);
        Assert.DoesNotContain("VentaCrearModal.open()", view);
        Assert.DoesNotContain("data-venta-modal-target=\"crear-venta\"", view);
    }

    [Fact]
    public void IndexView_NoRenderizaCrearVentaModalNiCargaScriptsDelModal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Index_tw.cshtml"));

        Assert.DoesNotContain("<partial name=\"_VentaCrearModal\" />", view);
        Assert.DoesNotContain("venta-crear-modal.js", view);
        Assert.DoesNotContain("venta-modal-rework.js", view);
        Assert.DoesNotContain("venta-create.js", view);
    }

    [Fact]
    public void VentaCreateJs_ActualizarResumenOperacion_ActualizaDetalleBadgeConNullGuardEnHero()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var fn     = ExtractFunction(script, "function actualizarResumenOperacion");

        // El badge del wizard (siempre visible) se actualiza directamente
        Assert.Contains("detalleItemsBadge.innerHTML", fn);
        Assert.Contains("shopping_bag", fn);

        // Los elementos hero (solo existen en Create_tw, no en el modal) usan guarda null
        // para que la función no lance errores cuando no se encuentra el elemento
        Assert.Contains("if (heroDetallesCount)", fn);
        Assert.Contains("if (heroTipoPago)", fn);
        Assert.Contains("if (heroCliente && heroClienteDetalle)", fn);
    }

    // ── KIRA-VENTAS-PAGE-REWORK-1A — pagina wizard /Venta/Create ─────────────

    [Fact]
    public void CreateView_EsPaginaWizardSinRootModalPrincipal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"venta-create-page\"", view);
        Assert.Contains("id=\"step-btn-cliente\"", view);
        Assert.Contains("id=\"step-panel-cliente\"", view);
        Assert.DoesNotContain("id=\"modal-crear-venta\"", view);
        Assert.DoesNotContain("id=\"modal-crear-venta-backdrop\"", view);
        Assert.DoesNotContain("id=\"btn-cerrar-modal-crear-venta\"", view);
        Assert.DoesNotContain("aria-modal=\"true\"", view);
        Assert.DoesNotContain("role=\"dialog\"", view);
        Assert.DoesNotContain("id=\"modal-confirmar-operacion\"", view);
    }

    [Fact]
    public void CreateView_FormSiguePosteandoCreateNativo()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("<form id=\"venta-form\" asp-action=\"Create\" method=\"post\">", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.DoesNotContain("CreateAjax", view);
        Assert.DoesNotContain("VentaCrearModal.submit()", view);
    }

    [Fact]
    public void CreateView_WizardTieneCuatroPasosYPaneles()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        foreach (var step in new[] { "cliente", "productos", "pago", "revision" })
        {
            Assert.Contains($"id=\"step-btn-{step}\"", view);
            Assert.Contains($"id=\"step-panel-{step}\"", view);
            Assert.Contains($"aria-controls=\"step-panel-{step}\"", view);
            Assert.Contains($"aria-labelledby=\"step-btn-{step}\"", view);
        }
    }

    [Fact]
    public void CreateView_ConservaContratosCriticosDeVenta()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        foreach (var id in new[]
        {
            "venta-form", "btn-confirmar", "input-buscar-cliente", "dropdown-clientes",
            "hdn-cliente-id", "info-cliente", "input-buscar-producto", "dropdown-productos",
            "panel-agregar-producto", "hdn-producto-id", "txt-cantidad", "txt-descuento-item",
            "btn-agregar-producto", "tbody-detalles", "detalles-hidden-inputs",
            "select-tipo-pago", "total-subtotal", "total-descuento", "total-iva",
            "total-final", "hdn-subtotal", "hdn-descuento", "hdn-iva", "hdn-total",
            "panel-alerta-mora", "panel-cupo-insuficiente", "panel-documentacion-faltante",
            "Observaciones"
        })
        {
            Assert.Contains($"id=\"{id}\"", view);
        }

        Assert.Contains("asp-for=\"AplicarExcepcionDocumental\"", view);
        Assert.Contains("asp-for=\"MotivoExcepcionDocumentalCreate\"", view);
    }

    [Fact]
    public void CreateView_ConservaHooksDeSidebarRevisionYMobileSummary()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        foreach (var hook in new[]
        {
            "data-side-cliente", "data-side-items", "data-side-pago",
            "data-side-subtotal", "data-side-descuento", "data-side-iva", "data-side-total",
            "data-rev-cliente", "data-rev-fecha", "data-rev-pago", "data-rev-items",
            "data-rev-subtotal", "data-rev-descuento", "data-rev-iva", "data-rev-total",
            "data-conf-cliente", "data-conf-items", "data-conf-pago", "data-conf-total",
            "data-conf-credito", "data-mobile-total"
        })
        {
            Assert.Contains(hook, view);
        }
    }

    [Fact]
    public void CreateView_CargaJsWizardDePagina()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("venta-page-wizard.js", view);
        Assert.DoesNotContain("venta-modal-rework.js", view);
        Assert.DoesNotContain("venta-crear-modal.js", view);
    }

    // KIRA-VENTAS-PAGE-REWORK-1E - pago, credito, documentacion y excepcion

    [Fact]
    public void CreateView_ConservaPanelesPagoCreditoDocumentacionYExcepcion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        foreach (var id in new[]
        {
            "panel-tarjeta", "panel-cheque", "panel-mercadopago", "panel-credito-personal",
            "panel-planes-pago", "lista-planes-pago", "configuracion-pagos-global-estado",
            "hdn-configuracion-pago-plan-id", "panel-diagnostico-condiciones-pago",
            "panel-verificacion-crediticia", "panel-verificacion-crediticia-auto",
            "panel-resultado-verificacion", "panel-cupo-suficiente", "panel-cupo-insuficiente",
            "panel-alerta-mora", "panel-documentacion-faltante", "lista-docs-faltantes",
            "btn-cargar-documentacion", "modal-documentacion", "btn-subir-documento",
            "panel-excepcion-crediticia", "hdn-aplicar-excepcion", "btn-aplicar-excepcion",
            "panel-excepcion-inactiva", "panel-excepcion-activa", "txt-excepcion-documental",
            "btn-cancelar-excepcion", "btn-confirmar-excepcion"
        })
        {
            Assert.Contains($"id=\"{id}\"", view);
        }

        Assert.Contains("asp-for=\"AplicarExcepcionDocumental\"", view);
        Assert.Contains("asp-for=\"MotivoExcepcionDocumentalCreate\"", view);
    }

    [Fact]
    public void CreateView_UnificaPagoYCreditoEnUnSoloPaso()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("id=\"step-btn-pago\"", view);
        Assert.Contains("id=\"step-panel-pago\"", view);
        Assert.DoesNotContain("id=\"step-btn-credito\"", view);
        Assert.DoesNotContain("id=\"step-panel-credito\"", view);

        var panelPago = view.IndexOf("id=\"step-panel-pago\"", StringComparison.Ordinal);
        var verificacionCredito = view.IndexOf("id=\"panel-verificacion-crediticia\"", StringComparison.Ordinal);
        var panelRevision = view.IndexOf("id=\"step-panel-revision\"", StringComparison.Ordinal);

        Assert.True(panelPago >= 0, "step-panel-pago debe existir.");
        Assert.True(verificacionCredito > panelPago, "La verificacion crediticia debe vivir dentro del paso de pago.");
        Assert.True(panelRevision > verificacionCredito, "Revision debe venir despues del bloque unificado de pago/credito.");
    }

    [Fact]
    public void CreateView_NoReintroduceModalLegacyNiConfirmacionExtra()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("<form id=\"venta-form\" asp-action=\"Create\" method=\"post\">", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("id=\"venta-create-page\"", view);
        Assert.Contains("venta-page-wizard.js", view);
        Assert.Contains("id=\"btn-confirmar\"", view);
        Assert.Contains("id=\"hdn-subtotal\"", view);
        Assert.Contains("id=\"hdn-descuento\"", view);
        Assert.Contains("id=\"hdn-iva\"", view);
        Assert.Contains("id=\"hdn-total\"", view);
        Assert.Contains("id=\"panel-documentacion-faltante\"", view);
        Assert.Contains("id=\"panel-excepcion-crediticia\"", view);
        Assert.DoesNotContain("id=\"modal-crear-venta\"", view);
        Assert.DoesNotContain("#modal-crear-venta", view);
        Assert.DoesNotContain("id=\"modal-confirmar-operacion\"", view);
        Assert.DoesNotContain("#modal-confirmar-operacion", view);
        Assert.DoesNotContain("role=\"dialog\"", view);
        Assert.DoesNotContain("aria-modal=\"true\"", view);
        Assert.DoesNotContain("action=\"/Venta/CreateAjax\"", view);
        Assert.DoesNotContain("CreateAjax", view);
        Assert.DoesNotContain("VentaCrearModal.submit()", view);
        Assert.DoesNotContain("btn-cerrar-modal-crear-venta", view);
        Assert.DoesNotContain("onclick=", view);
    }

    [Fact]
    public void EditView_EsPaginaWizardYConservaContratoDeEdicion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Edit_tw.cshtml"));

        Assert.Contains("id=\"venta-edit-page\"", view);
        Assert.Contains("<form id=\"venta-form\" asp-action=\"Edit\" method=\"post\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("asp-for=\"Id\"", view);
        Assert.Contains("asp-for=\"Estado\"", view);
        Assert.Contains("asp-for=\"RowVersion\"", view);
        Assert.Contains("id=\"venta-inicial-json\"", view);
        Assert.Contains("id=\"step-btn-cliente\"", view);
        Assert.Contains("id=\"step-panel-cliente\"", view);
        Assert.Contains("id=\"step-panel-productos\"", view);
        Assert.Contains("id=\"step-panel-pago\"", view);
        Assert.Contains("id=\"step-panel-credito\"", view);
        Assert.Contains("id=\"step-panel-revision\"", view);
        Assert.Contains("id=\"select-tipo-pago\"", view);
        Assert.Contains("id=\"btn-confirmar\"", view);
        Assert.Contains("asp-for=\"Observaciones\"", view);
        Assert.Contains("venta-page-wizard.js", view);
        Assert.DoesNotContain("id=\"modal-crear-venta\"", view);
        Assert.DoesNotContain("#modal-crear-venta", view);
        Assert.DoesNotContain("id=\"modal-confirmar-operacion\"", view);
        Assert.DoesNotContain("#modal-confirmar-operacion", view);
        Assert.DoesNotContain("VentaCrearModal.submit()", view);
        Assert.DoesNotContain("CreateAjax", view);
        Assert.DoesNotContain("venta-crear-modal.js", view);
        Assert.DoesNotContain("venta-modal-rework.js", view);
        Assert.DoesNotContain("<script>window.ventaInicial", view);
        Assert.DoesNotContain("onclick=", view);
    }

    [Fact]
    public void VentaCreateJs_ContemplaMercadoPagoCreditoYCuentaCorrienteEnPagina()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));
        var tipoPago = ExtractFunction(script, "function onTipoPagoChange");
        var esCredito = ExtractFunction(script, "function esTipoPagoCredito");
        var verificar = ExtractFunction(script, "async function verificarElegibilidadAuto");

        Assert.Contains("panel-mercadopago", script);
        Assert.Contains("const isMercadoPago = val === TIPO_PAGO.MercadoPago", tipoPago);
        Assert.Contains("isMercadoPago ? show(panelMercadoPago) : hide(panelMercadoPago)", tipoPago);
        Assert.Contains("tipoPago === TIPO_PAGO.CreditoPersonal", esCredito);
        Assert.Contains("tipoPago === TIPO_PAGO.CuentaCorriente", esCredito);
        Assert.Contains("!esTipoPagoCredito(selectTipoPago?.value)", verificar);
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
