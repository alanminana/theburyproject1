namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contrato de UI del módulo de envío (serie ENVIO-ML): verifica que los hooks DOM que
/// el JS necesita existen en las vistas reales, y que el JS los referencia con los
/// mismos nombres — mismo patrón que ConfigurarVentaUiContractTests.
/// </summary>
public class VentaEnvioUiContractTests
{
    private static string LeerVista(params string[] segments)
    {
        var partes = new[] { "Views" }.Concat(segments).ToArray();
        return File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(partes).ToArray()));
    }

    private static string LeerJs(string archivo) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", archivo));

    // -------------------------------------------------------------------------
    // Simular (Cotización): checkbox "tiene envío"
    // -------------------------------------------------------------------------

    [Fact]
    public void CotizadorForm_ExponeCheckboxTieneEnvio()
    {
        var view = LeerVista("Cotizacion", "_CotizadorForm.cshtml");
        Assert.Contains("id=\"cotizacion-tiene-envio\"", view);
    }

    [Fact]
    public void CotizacionSimuladorJs_EnviaTieneEnvioEnElPayloadDeGuardar()
    {
        var js = LeerJs("cotizacion-simulador.js");
        Assert.Contains("#cotizacion-tiene-envio", js);
        Assert.Contains("tieneEnvio: hayEnvioGuardado()", js);
    }

    // -------------------------------------------------------------------------
    // Wizard de Venta: paso Envío
    // -------------------------------------------------------------------------

    [Fact]
    public void VentaWizardForm_ExponeTabYPanelDeEnvio()
    {
        var view = LeerVista("Venta", "_VentaWizardForm.cshtml");
        Assert.Contains("id=\"step-btn-envio\"", view);
        Assert.Contains("id=\"step-panel-envio\"", view);
        Assert.Contains("aria-controls=\"step-panel-envio\"", view);
    }

    [Fact]
    public void VentaWizardForm_ExponeCheckboxYBloqueDeDatosDeEnvio()
    {
        var view = LeerVista("Venta", "_VentaWizardForm.cshtml");
        Assert.Contains("asp-for=\"TieneEnvio\"", view);
        Assert.Contains("id=\"chk-tiene-envio\"", view);
        Assert.Contains("id=\"envio-datos\"", view);
        Assert.Contains("name=\"Envio.Destinatario\"", view);
        Assert.Contains("name=\"Envio.Domicilio\"", view);
    }

    [Fact]
    public void VentaPageWizardJs_IncluyeEnvioEnElGatingDePasos()
    {
        var js = LeerJs("venta-page-wizard.js");
        Assert.Contains("envio: () => clienteListo && productosListo", js);
    }

    [Fact]
    public void VentaEnvioJs_ReferenciaLosHooksDelPaso()
    {
        var js = LeerJs("venta-envio.js");
        Assert.Contains("#chk-tiene-envio", js);
        Assert.Contains("#envio-datos", js);
        Assert.Contains("#btn-envio-usar-cliente", js);
    }

    [Fact]
    public void CreateYEditTw_CarganVentaEnvioJs()
    {
        var create = LeerVista("Venta", "Create_tw.cshtml");
        var edit = LeerVista("Venta", "Edit_tw.cshtml");
        Assert.Contains("venta-envio.js", create);
        Assert.Contains("venta-envio.js", edit);
    }

    // -------------------------------------------------------------------------
    // Details: card + modal de cambio de estado
    // -------------------------------------------------------------------------

    [Fact]
    public void DetailsTw_ExponeCardYModalDeEnvio()
    {
        var view = LeerVista("Venta", "Details_tw.cshtml");
        Assert.Contains("data-venta-modal=\"actualizar-envio\"", view);
        Assert.Contains("data-venta-modal-target=\"actualizar-envio\"", view);
        Assert.Contains("asp-action=\"CambiarEstadoEnvio\"", view);
        Assert.Contains("id=\"envio-nuevo-estado\"", view);
        Assert.Contains("id=\"envio-motivo-bloque\"", view);
    }

    [Fact]
    public void DetailsVentaJs_ManejaElModalDeActualizarEnvio()
    {
        var js = LeerJs("details-venta.js");
        Assert.Contains("bindModal('actualizar-envio'", js);
        Assert.Contains("envio-nuevo-estado", js);
        Assert.Contains("envio-motivo-bloque", js);
    }

    // -------------------------------------------------------------------------
    // Index: tab "Envíos pendientes"
    // -------------------------------------------------------------------------

    [Fact]
    public void IndexTw_ExponeTabYPanelDeEnviosPendientes()
    {
        var view = LeerVista("Venta", "Index_tw.cshtml");
        Assert.Contains("id=\"tab-envios\"", view);
        Assert.Contains("data-venta-tab=\"envios\"", view);
        Assert.Contains("id=\"panel-envios\"", view);
        Assert.Contains("data-venta-tab-panel=\"envios\"", view);
    }

    // -------------------------------------------------------------------------
    // VENTA-ENVIO-TOTAL-01: el envío se cobra — Productos / Envío / TOTAL A COBRAR
    // -------------------------------------------------------------------------

    [Fact]
    public void VentaDetails_MuestraProductosEnvioYTotalACobrar_SinLlamarloInformativo()
    {
        var view = LeerVista("Venta", "Details_tw.cshtml");
        Assert.Contains("id=\"venta-total-a-cobrar\"", view);
        Assert.Contains("id=\"venta-total-productos\"", view);
        Assert.Contains("id=\"venta-importe-envio\"", view);
        Assert.Contains("Model.TotalACobrar", view);
        Assert.Contains("Costo de envío", view);
        Assert.DoesNotContain("Costo (informativo)", view);
        Assert.DoesNotContain("Sólo informativo", view);
    }

    [Fact]
    public void VentaDetails_ExplicaQueElEnvioNoIntegraElComprobante()
    {
        var view = LeerVista("Venta", "Details_tw.cshtml");
        Assert.Contains("id=\"venta-comprobante-nota\"", view);
        Assert.Contains("Model.TotalFacturable", view);
        Assert.Contains("id=\"venta-envio-no-financiado\"", view);
    }

    [Fact]
    public void VentaWizard_RevisionDesglosaProductosArmadosEnvioYRecargo()
    {
        var view = LeerVista("Venta", "_VentaWizardForm.cshtml");
        Assert.Contains("data-rev-desglose", view);
        Assert.Contains("data-rev-total-productos", view);
        Assert.Contains("data-rev-armados-row", view);
        Assert.Contains("data-rev-armados-lista", view);
        Assert.Contains("data-rev-envio-row", view);
        Assert.Contains("data-rev-recargo-row", view);
        Assert.Contains("data-rev-total-label", view);
        Assert.DoesNotContain("data-rev-envio-lines", view);
        Assert.DoesNotContain("data-rev-envio-credito-nota", view);
        Assert.DoesNotContain("Costo de envío (informativo)", view);
    }

    [Fact]
    public void VentaWizard_EnvioSeEligePorTipoConPrecioGlobal_SinCostoLibre()
    {
        var view = LeerVista("Venta", "_VentaWizardForm.cshtml");
        Assert.Contains("name=\"Envio.TipoEnvio\"", view);
        Assert.Contains("id=\"envio-tipo\"", view);
        Assert.Contains("id=\"venta-servicios-json\"", view);
        Assert.DoesNotContain("name=\"Envio.CostoEnvio\"", view);
        Assert.DoesNotContain("id=\"envio-costo\"", view);
    }

    [Fact]
    public void VentaPageWizardJs_DesglosaConLosImportesDelBackend_SinSumarElEnvioEnElCliente()
    {
        var js = LeerJs("venta-page-wizard.js");
        Assert.Contains("venta:totales", js);
        Assert.Contains("renderDesglose", js);
        Assert.DoesNotContain("leerImporteEnvio", js);
        // El total ya incluye envío, armados y recargo: viene del backend, no se suma acá.
        Assert.Contains("setText('[data-side-total]', totalProductos)", js);
        Assert.Contains("setText('[data-rev-total], [data-mobile-total]', total)", js);

        var create = LeerJs("venta-create.js");
        Assert.Contains("tipoEnvio: leerTipoEnvioSeleccionado()", create);
        Assert.Contains("tipoArmado: d.tipoArmado ?? null", create);
        Assert.Contains("entregaCajaCerrada", create);
        Assert.Contains("Detalles[${i}].TipoArmado", create);
        Assert.Contains("Detalles[${i}].EntregaCajaCerrada", create);
        Assert.Contains("data-armado-select", create);
    }

    [Fact]
    public void Cotizador_SeparaEnvioYTotalACobrar_YLoEnviaAlGuardar()
    {
        var view = LeerVista("Cotizacion", "_CotizadorForm.cshtml");
        Assert.Contains("id=\"cotizacion-seg-envio\"", view);
        Assert.Contains("id=\"cotizacion-seg-total-a-cobrar\"", view);
        Assert.DoesNotContain("Informativo: no modifica el total de la venta", view);

        var js = LeerJs("cotizacion-simulador.js");
        // VENTA-SERVICIOS-01: el cliente manda sólo el tipo (Ciudad/Rural) y el armado por línea; el
        // importe lo fija el servidor y ya está dentro del total (no viaja ni se suma en el navegador).
        Assert.Contains("tipoEnvio: hayEnvioGuardado()", js);
        Assert.Contains("tipoArmado: p.tipoArmado || null", js);
        Assert.DoesNotContain("costoEnvio:", js);
        Assert.Contains("id=\"cotizacion-seg-armados\"", view);
        Assert.Contains("id=\"cotizacion-envio-tipo\"", view);
        Assert.DoesNotContain("id=\"cotizacion-envio-costo\"", view);
    }

    // Edit de una venta con tarjeta: el preview del wizard debe llevar el mismo recargo que el total
    // guardado, así que la tarjeta y el plan persistidos se rehidratan al abrir la edición.
    [Fact]
    public void VentaEdit_RehidrataTarjetaYPlanGuardados()
    {
        var view = LeerVista("Venta", "_VentaWizardForm.cshtml");
        Assert.Contains("selected=\"@(Model.DatosTarjeta?.ConfiguracionTarjetaId?.ToString() == t.Value)\"", view);
        Assert.Contains("value=\"@(Model.DatosTarjeta?.ConfiguracionPagoPlanId)\"", view);

        var js = LeerJs("venta-create.js");
        Assert.Contains("let planGuardadoPendiente = hdnConfiguracionPagoPlanId?.value || '';", js);
        Assert.Contains("buscarPlan(planGuardadoPendiente)", js);
    }

    [Fact]
    public void CotizacionDetalles_MuestraEnvioYTotalACobrar()
    {
        var view = LeerVista("Cotizacion", "Detalles_tw.cshtml");
        Assert.Contains("id=\"cotizacion-envio-totales\"", view);
        Assert.Contains("Model.TotalACobrar", view);
    }

    [Fact]
    public void CajaVentasDelTurno_MuestraProductosYEnvioDentroDeLaCeldaDeTotal()
    {
        var view = LeerVista("Caja", "_ConciliacionVentasTab.cshtml");
        Assert.Contains("data-venta-productos", view);
        Assert.Contains("data-venta-envio", view);
        Assert.Contains("v.ImporteEnvio", view);
    }

    [Fact]
    public void ModalFacturar_ExplicaResumenComercialFrenteAlComprobante()
    {
        var partial = LeerVista("Shared", "_FacturaCamposEmision.cshtml");
        Assert.Contains("resumen-comercial", partial);
        Assert.Contains("comercial-envio", partial);
        Assert.Contains("no integra este comprobante", partial);

        var facturarPagina = LeerVista("Venta", "Facturar_tw.cshtml");
        Assert.Contains("facturar-resumen-comercial", facturarPagina);
    }

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
