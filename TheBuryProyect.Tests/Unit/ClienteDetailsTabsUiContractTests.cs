namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Contrato de UI de la reorganizacion "hibrida" de Cliente/Details (2026-09-14):
/// resumen ejecutivo + alerta principal siempre visibles arriba, y el resto de la
/// ficha organizado en 5 solapas fijas (Resumen/Credito/Documentacion/Datos/
/// Historial) con el patron ARIA completo de ERP-UI-STANDARD.md §5. Cubre que las
/// 5 solapas existan, que Resumen sea la que arranca activa, que cada panel
/// conserve los elementos criticos que ya tenia antes del reorden, y que no se
/// hayan perdido ids/data-* usados por JS o por tests/E2E existentes.
/// </summary>
public class ClienteDetailsTabsUiContractTests
{
    private const string ViewRelativePath = "Views/Cliente/Details_tw.cshtml";

    [Fact]
    public void DetailsView_TieneExactamenteCincoSolapasConPatronAriaCompleto()
    {
        var view = ReadView();

        Assert.Contains("role=\"tablist\"", view);

        var tabs = new[] { "resumen", "credito", "documentacion", "datos", "historial" };
        foreach (var tab in tabs)
        {
            Assert.Contains($"data-cliente-tab=\"{tab}\"", view);
            Assert.Contains($"data-cliente-tab-panel=\"{tab}\"", view);
        }

        // Exactamente 5, ni más ni menos (mockup del usuario: "Máximo 5").
        var countTabs = CountOccurrences(view, "data-cliente-tab=\"");
        var countPanels = CountOccurrences(view, "data-cliente-tab-panel=\"");
        Assert.Equal(5, countTabs);
        Assert.Equal(5, countPanels);

        Assert.Contains("role=\"tab\"", view);
        Assert.Contains("role=\"tabpanel\"", view);
        Assert.Contains("aria-selected=\"true\"", view);
        Assert.Contains("aria-controls=\"panel-resumen\"", view);
        Assert.Contains("aria-labelledby=\"tab-resumen\"", view);
    }

    [Fact]
    public void DetailsView_ResumenEsLaSolapaActivaPorDefecto()
    {
        var view = ReadView();

        // El boton de Resumen es el unico con aria-selected="true" en el markup inicial;
        // los otros 4 arrancan con tabindex="-1" (roving tabindex, sólo el activo enfocable).
        Assert.Contains("id=\"tab-resumen\" class=\"tab-btn is-active\" role=\"tab\" aria-selected=\"true\"", view);
        Assert.Contains("id=\"panel-resumen\" class=\"tab-panel is-active\" data-cliente-tab-panel=\"resumen\"", view);

        foreach (var tab in new[] { "credito", "documentacion", "datos", "historial" })
        {
            Assert.Contains($"aria-controls=\"panel-{tab}\" data-cliente-tab=\"{tab}\"", view);
        }

        // Los otros 4 paneles arrancan con el atributo hidden nativo (progressive
        // enhancement: sin JS quedarían ocultos, igual que el patron ya usado en
        // Views/Venta/Index_tw.cshtml para sus tab-panel).
        Assert.Contains("data-cliente-tab-panel=\"credito\" role=\"tabpanel\" aria-labelledby=\"tab-credito\" hidden", view);
        Assert.Contains("data-cliente-tab-panel=\"documentacion\" role=\"tabpanel\" aria-labelledby=\"tab-documentacion\" hidden", view);
        Assert.Contains("data-cliente-tab-panel=\"datos\" role=\"tabpanel\" aria-labelledby=\"tab-datos\" hidden", view);
        Assert.Contains("data-cliente-tab-panel=\"historial\" role=\"tabpanel\" aria-labelledby=\"tab-historial\" hidden", view);
    }

    [Fact]
    public void DetailsView_ResumenEjecutivoYAlertaPrincipalQuedanFueraDeLasSolapas()
    {
        var view = ReadView();

        var indiceTabs = view.IndexOf("data-cliente-tab=\"resumen\"", StringComparison.Ordinal);
        Assert.True(indiceTabs > 0, "La barra de solapas debe existir.");

        var antesDeTabs = view.Substring(0, indiceTabs);

        // El resumen ejecutivo (4 KPIs) y la alerta principal de aptitud viven antes
        // de la barra de solapas — nunca dentro de un tab-panel que pueda ocultarse.
        Assert.Contains("grid-kpi", antesDeTabs);
        Assert.Contains("Credito disponible", antesDeTabs);
        Assert.Contains("Situacion BCRA", antesDeTabs);
        Assert.Contains("apt-card", antesDeTabs);
        Assert.Contains("Recalcular aptitud", antesDeTabs);
    }

    [Fact]
    public void DetailsView_PanelResumenConservaVentasPendientesBcraMotivosYGarante()
    {
        var view = ReadView();
        var panel = ExtraerPanel(view, "panel-resumen", "panel-credito");

        Assert.Contains("Ventas pendientes de autorización", panel);
        Assert.Contains("id=\"bcra-panel\"", panel);
        Assert.Contains("id=\"bcra-chip\"", panel);
        Assert.Contains("data-cliente-bcra-refresh", panel);
        Assert.Contains("checklist</span>@motivosTitulo", panel);
        Assert.Contains("id=\"garante-panel-card\"", panel);
        Assert.Contains("id=\"garanteModal\"", panel);
        Assert.Contains("data-garante-asignar", panel);
    }

    [Fact]
    public void DetailsView_PanelCreditoConservaAccionesDePuntajeYUltimosCreditos()
    {
        var view = ReadView();
        var panel = ExtraerPanel(view, "panel-credito", "panel-documentacion");

        Assert.Contains("data-cliente-open-nivel-manual", panel);
        Assert.Contains("data-cliente-open-limites", panel);
        Assert.Contains("Scoring de comportamiento", panel);
        Assert.Contains("Historial de puntaje", panel);
        Assert.Contains("Ultimos creditos del cliente", panel);
        Assert.Contains("Model.CreditosActivos?.Take(5)", view); // computado arriba, reusado acá
        Assert.Contains("foreach (var credito in creditosRecientes)", panel);
        Assert.Contains("Ver todos (@totalCreditos)", panel);
        Assert.Contains("Punitorio aplicado pendiente", panel);
    }

    [Fact]
    public void DetailsView_PanelDocumentacionConservaAccionesDeCargaYRevision()
    {
        var view = ReadView();
        var panel = ExtraerPanel(view, "panel-documentacion", "panel-datos");

        Assert.Contains("data-cliente-upload-open", panel);
        Assert.Contains("data-cliente-reject-form", panel);
        Assert.Contains("asp-action=\"Verificar\"", panel);
        Assert.Contains("asp-action=\"Rechazar\"", panel);
        Assert.Contains("Ver toda la documentacion", panel);
    }

    [Fact]
    public void DetailsView_PanelDatosConservaDatosPersonalesContactoYZonaSensible()
    {
        var view = ReadView();
        var panel = ExtraerPanel(view, "panel-datos", "panel-historial");

        Assert.Contains("Datos personales", panel);
        Assert.Contains("Contacto", panel);
        Assert.Contains("Zona sensible", panel);
        Assert.Contains("Dar de baja al cliente lo oculta del listado operativo. Se conserva el historial.", panel);
        Assert.Contains(">Dar de baja cliente</a>", panel);
    }

    [Fact]
    public void DetailsView_PanelHistorialReutilizaListadoDeCreditosExistente()
    {
        var view = ReadView();
        var panel = ExtraerPanel(view, "panel-historial", "id=\"limitesModalContainer\"");

        Assert.Contains("Créditos históricos", panel);
        Assert.Contains("asp-controller=\"Credito\" asp-action=\"Index\" asp-route-clienteId=\"@c.Id\"", panel);
        // No inventa una bitacora nueva: solo el link al listado completo ya existente.
        Assert.DoesNotContain("<table", panel);
    }

    [Fact]
    public void DetailsView_NoPierdeIdsDeModalesGlobalesFueraDeLasSolapas()
    {
        var view = ReadView();

        Assert.Contains("id=\"nivelManualModal\"", view);
        Assert.Contains("id=\"rejectDocModal\"", view);
        Assert.Contains("id=\"uploadDocModal\"", view);
        Assert.Contains("id=\"limitesModalContainer\"", view);
    }

    [Fact]
    public void ClienteDetailsJs_InicializaSolapasConRovingTabindexYTeclado()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-details.js"));

        Assert.Contains("data-cliente-tab", script);
        Assert.Contains("data-cliente-tab-panel", script);
        Assert.Contains("ArrowRight", script);
        Assert.Contains("ArrowLeft", script);
        Assert.Contains("'Home'", script);
        Assert.Contains("'End'", script);
        Assert.Contains("tabIndex", script);
        Assert.Contains("initClienteTabs", script);
    }

    [Fact]
    public void ClienteModuleCss_DefineTabBtnScopeadoALaFichaDeCliente()
    {
        var css = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", "cliente-module.css"));

        Assert.Contains(".shell.cliente-details .tab-btn", css);
        Assert.Contains(".shell.cliente-details .tabs-scroll", css);
        Assert.Contains(".tab-btn.is-active", css);
        Assert.Contains(".tab-btn:focus-visible", css);
    }

    /// <summary>
    /// 10/10 polish (2026-09-14): la solapa activa persiste via hash de URL
    /// (#credito, #documentacion, ...) sin SPA ni backend — replaceState (nunca
    /// pushState) para no ensuciar el boton "Atras", y fallback a "resumen" si el
    /// hash es invalido o no existe.
    /// </summary>
    [Fact]
    public void ClienteDetailsJs_PersisteSolapaActivaPorHashConFallbackAResumen()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-details.js"));

        Assert.Contains("location.hash", script);
        Assert.Contains("history.replaceState", script);
        Assert.Contains("'resumen'", script);

        // Nunca pushState: cambiar de solapa no debe apilar el historial de "Atras".
        Assert.DoesNotContain("history.pushState", script);

        // 'hashchange' cubre la navegacion "en la misma pagina" (un link a otra
        // solapa mientras la ficha ya esta abierta, o editar el hash a mano): el
        // navegador no recarga en ese caso, solo dispara este evento.
        Assert.Contains("hashchange", script);
    }

    /// <summary>
    /// Al volver de Verificar/Rechazar/Subir documento (acciones que redirigen
    /// directo al returnUrl recibido), el hash de la solapa activa se agrega al
    /// campo oculto antes de enviar el form — sin tocar ningun controller ni el
    /// contrato existente de returnUrl.
    /// </summary>
    [Fact]
    public void ClienteDetailsJs_AgregaHashDeSolapaActivaAlReturnUrlAntesDeEnviarForms()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-details.js"));

        Assert.Contains("input[name=\"returnUrl\"]", script);
        Assert.Contains("returnUrlField.value +=", script);
    }

    /// <summary>
    /// PUN-ML / FASE 11D compatible: un fallo de red al actualizar BCRA no debe
    /// pisar el ultimo dato real conocido (bcra-desc) con un texto generico — se
    /// muestra un error propio, reintentable con el mismo boton.
    /// </summary>
    [Fact]
    public void ClienteDetailsJs_ErrorDeBcraNoPisaLaDescripcionRealConocida()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cliente-details.js"));

        Assert.Contains("bcra-error", script);
        Assert.DoesNotContain("descEl.textContent = 'Error al consultar'", script);

        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), ViewRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("id=\"bcra-error\"", view);
    }

    /// <summary>
    /// La solapa Documentación no debe quedar con una lista vacía sin ningún
    /// mensaje cuando el cliente no tiene documentos cargados ni faltantes.
    /// </summary>
    [Fact]
    public void DetailsView_PanelDocumentacionTieneEstadoVacioHonesto()
    {
        var view = ReadView();
        var panel = ExtraerPanel(view, "panel-documentacion", "panel-datos");

        Assert.Contains("No hay documentación pendiente.", panel);
    }

    /// <summary>
    /// El bloque de "Crédito" dentro de Resumen ya no repite el mismo "Crédito
    /// disponible" en formato de card grande (duplicado exacto del KPI superior):
    /// queda reducido a lo que el KPI no muestra (puntaje, mora).
    /// </summary>
    [Fact]
    public void DetailsView_PanelResumenNoDuplicaCreditoDisponibleComoCardGrande()
    {
        var view = ReadView();
        var panel = ExtraerPanel(view, "panel-resumen", "panel-credito");

        Assert.DoesNotContain("Información crediticia</h2>", panel);
        Assert.Contains("Puntaje @nivelFinal/5", panel);
        Assert.Contains("Mora: @moraLabel", panel);
    }

    private static string ReadView() => File.ReadAllText(Path.Combine(FindRepoRoot(), ViewRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ExtraerPanel(string view, string inicioMarcador, string finMarcador)
    {
        var inicio = view.IndexOf($"id=\"{inicioMarcador}\"", StringComparison.Ordinal);
        Assert.True(inicio > 0, $"No se encontro el panel {inicioMarcador}.");

        var fin = view.IndexOf(finMarcador, inicio, StringComparison.Ordinal);
        Assert.True(fin > inicio, $"No se encontro el limite de fin para el panel {inicioMarcador}.");

        return view.Substring(inicio, fin - inicio);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += value.Length;
        }
        return count;
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
