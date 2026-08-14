namespace TheBuryProject.Tests.Unit;

/// <summary>
/// VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01: reduce el paso Crédito del wizard de
/// Venta a 2 superficies ("Estado del crédito" + "Configurar plan"), fusiona 5
/// &lt;details&gt; en 1 ("Ver desglose ▸"), y corrige la incoherencia de CTA (el submit
/// persistente del sidebar compartía copy "Verificar crédito"/"Guardar configuración" con
/// el CTA contextual sin ejecutar esa acción al clickear). Contrato estático (string-based)
/// sobre vistas/JS — el comportamiento real se validó con Playwright MCP contra la app real
/// y queda documentado en el cierre del micro-lote, no en este archivo.
/// </summary>
public sealed class VentaCreditoRedesignVisualUiContractTests
{
    // ── Hallazgos confirmados en vivo con Playwright MCP ────────────────────────────────

    [Fact]
    public void VentaPageWizardCss_CreditoEstadoRowGanaSobreHiddenDeTailwind()
    {
        // CSS Cascade Layers: .hidden de Tailwind v4 vive dentro de @layer utilities, así
        // que una regla sin capa (.credito-estado-row, display:flex) le gana siempre,
        // regardless de especificidad. Confirmado en vivo: #estado-riesgo-row aparecía
        // visible con "Riesgo ·" (sin dot/label/tag) antes de que el semáforo tuviera datos.
        // Mismo patrón que credito-module.css ya documentaba para .ph (CreditoModuleCss_
        // FuerzaHiddenSobreCualquierOtraClaseDeDisplay).
        var css = ReadCss("venta-page-wizard.css");

        Assert.Contains(".credito-estado-row.hidden", css);

        var idxRow = css.IndexOf(".credito-estado-row,", StringComparison.Ordinal);
        var idxHiddenOverride = css.IndexOf(".credito-estado-row.hidden", StringComparison.Ordinal);
        Assert.True(idxRow >= 0 && idxHiddenOverride > idxRow,
            "El override de .hidden debe declararse después de la regla base .credito-estado-row.");
    }

    [Fact]
    public void VentaCreateJs_OtrosMotivosNuncaDuplicaCupoConLaFilaSiempreVisible()
    {
        // Hallazgo en vivo: aplicar la excepción documental oculta #panel-cupo-insuficiente
        // (mismo hide() que Documentación, ver mostrarPanelExcepcion/activarExcepcionConfirmada)
        // — con la categoría 2 todavía filtrada condicionalmente, "Cupo insuficiente.
        // Disponible: $0" reaparecía duplicado en Otros motivos, repitiendo el mismo $0 que ya
        // muestra la fila "Cupo disponible" (siempre visible en Estado del crédito, a
        // diferencia del panel condicional que tenía antes del redesign).
        var js = ReadJs("venta-create.js");

        var idx = js.IndexOf("const autoridadDedicadaVisible", StringComparison.Ordinal);
        Assert.True(idx >= 0, "No se encontró autoridadDedicadaVisible.");
        var bloque = js.Substring(idx, 250);

        Assert.Contains("2: true", bloque);
    }

    // ── Estado del crédito: una sola superficie, sin las cards/wrappers eliminados ─────

    [Fact]
    public void VentaWizardForm_EstadoDelCreditoReemplazaVerificacionCrediticiaComoTitulo()
    {
        var view = ReadVenta("_VentaWizardForm.cshtml");

        Assert.Contains("Estado del crédito", view);
        Assert.Contains("id=\"panel-verificacion-crediticia\"", view);
    }

    [Fact]
    public void VentaWizardForm_EliminaWrappersDeZonaYCupoSuficiente()
    {
        var view = ReadVenta("_VentaWizardForm.cshtml");

        Assert.DoesNotContain("id=\"credito-zona-bloqueantes\"", view);
        Assert.DoesNotContain("id=\"credito-zona-otras-condiciones\"", view);
        Assert.DoesNotContain("credito-zona-label", view);
        Assert.DoesNotContain("id=\"panel-cupo-suficiente\"", view);
    }

    [Fact]
    public void VentaWizardForm_EstadoDelCreditoNoTieneDetailsPropios()
    {
        // "Ver límite y utilizado" y "Ver motivo" se eliminaron de Crédito (compactados a
        // fila siempre visible / movidos a Revisión sin duplicar). Los únicos <details>
        // que deben sobrevivir en todo el archivo son los de Revisión (detalle financiero +
        // evaluación, sin tocar) y el de Observaciones en el sidebar.
        var view = ReadVenta("_VentaWizardForm.cshtml");

        var inicioCredito = view.IndexOf("id=\"panel-verificacion-crediticia\"", StringComparison.Ordinal);
        var inicioRevision = view.IndexOf("id=\"step-panel-revision\"", StringComparison.Ordinal);
        Assert.True(inicioCredito >= 0 && inicioRevision > inicioCredito);

        var bloqueCredito = view[inicioCredito..inicioRevision];
        Assert.DoesNotContain("<details", bloqueCredito);
    }

    [Fact]
    public void VentaWizardForm_PreservaIdsDeEstadoDelCredito()
    {
        var view = ReadVenta("_VentaWizardForm.cshtml");

        foreach (var id in new[]
        {
            "panel-resultado-verificacion", "verificacion-badge", "verificacion-estado",
            "verificacion-saldo", "verificacion-limite", "verificacion-utilizado",
            "verificacion-barra", "panel-cupo-insuficiente", "cupo-insuficiente-detalle",
            "panel-documentacion-faltante", "lista-docs-faltantes", "btn-cargar-documentacion",
            "panel-excepcion-crediticia", "panel-alerta-mora", "alerta-mora-titulo",
            "alerta-mora-texto", "panel-motivos", "lista-motivos",
            "estado-riesgo-row", "estado-riesgo-dot", "estado-riesgo-label", "estado-riesgo-tag"
        })
        {
            Assert.Contains($"id=\"{id}\"", view);
        }
    }

    [Fact]
    public void VentaWizardForm_ExcepcionMotivoResumenViveEnRevisionBajoElMismoGate()
    {
        // El motivo completo de la excepción documental (antes en un <details>Ver motivo▸
        // dentro de Crédito) se reubicó a Revisión sin duplicarse: mismo id, mismo gate de
        // permiso server-side.
        var view = ReadVenta("_VentaWizardForm.cshtml");

        Assert.Equal(1, CountOccurrences(view, "id=\"excepcion-motivo-resumen\""));

        var idxMotivo = view.IndexOf("id=\"excepcion-motivo-resumen\"", StringComparison.Ordinal);
        var idxRevisionPersonal = view.IndexOf("id=\"revision-credito-personal\"", StringComparison.Ordinal);
        var idxContratoSlot = view.IndexOf("id=\"revision-credito-contrato-slot\"", StringComparison.Ordinal);
        Assert.True(idxRevisionPersonal >= 0 && idxMotivo > idxRevisionPersonal && idxMotivo < idxContratoSlot,
            "excepcion-motivo-resumen debe vivir dentro de revision-credito-personal.");

        var idxIf = view.LastIndexOf("User.TienePermiso(\"ventas\", \"authorize\")", idxMotivo, StringComparison.Ordinal);
        Assert.True(idxIf >= 0 && idxIf < idxMotivo && idxIf > idxRevisionPersonal,
            "El párrafo del motivo debe seguir gateado por el mismo permiso que el control de excepción.");
    }

    // ── Configurar plan: "Ver desglose" fusiona detalle financiero + tabla por cuota ───

    [Fact]
    public void ConfigurarVentaEmbebida_UnicoDetailsVerDesglose()
    {
        var view = ReadCredito("_ConfigurarVentaEmbebida.cshtml");

        Assert.Contains("<details data-plan-cuotas-detalle-details>", view);
        Assert.Contains("<summary class=\"sec-title\" data-plan-cuotas-detalle-summary>Ver desglose ▸</summary>", view);
        Assert.DoesNotContain("data-plan-detalle-financiero", view);

        var detailsIdx = view.IndexOf("<details data-plan-cuotas-detalle-details>", StringComparison.Ordinal);
        var precioFinalIdx = view.IndexOf("id=\"plan-precio-final\"", StringComparison.Ordinal);
        var tablaIdx = view.IndexOf("id=\"plan-cuotas-tabla\"", StringComparison.Ordinal);
        var cierreIdx = view.IndexOf("</details>", tablaIdx, StringComparison.Ordinal);

        Assert.True(detailsIdx >= 0 && precioFinalIdx > detailsIdx && tablaIdx > precioFinalIdx && cierreIdx > tablaIdx,
            "El detalle financiero y la tabla por cuota deben vivir dentro del mismo <details>, sin accordions anidados.");

        // Un solo <details> real en la sección de "Ver desglose" — no dos fusionados que
        // dejaron un cierre huérfano.
        Assert.Equal(1, CountOccurrences(view, "data-plan-cuotas-detalle-details"));
    }

    [Fact]
    public void ConfigurarVentaEmbebida_EliminaEvaluacionPreliminarComoCardSeparada()
    {
        // La card "Evaluación preliminar" (badge + <details>Ver evaluación▸) se retiró del
        // fragmento embebido — el resumen de una línea vive en Estado del crédito. La
        // página standalone (ConfigurarVenta_tw) conserva su propia card íntegra: no se
        // toca, valida standalone intacto.
        var embebida = ReadCredito("_ConfigurarVentaEmbebida.cshtml");
        var standalone = ReadCredito("ConfigurarVenta_tw.cshtml");

        // Marcadores funcionales reales (ids), no prosa de comentario — el comentario que
        // documenta el retiro de esta card menciona su nombre viejo a propósito.
        Assert.DoesNotContain("id=\"semaforo-panel\"", embebida);
        Assert.DoesNotContain("id=\"semaforo-badge\"", embebida);
        Assert.DoesNotContain("data-semaforo-detalle", embebida);

        Assert.Contains("id=\"semaforo-panel\"", standalone);
        Assert.Contains("id=\"semaforo-badge\"", standalone);
    }

    [Fact]
    public void ConfigurarVentaCreditoJs_GuardaSemaforoConNodosOpcionalesYEspejaAEstado()
    {
        var js = ReadJs("configurar-venta-credito.js");

        // Los nodos primarios del semáforo dejaron de ser obligatorios (ya no existen en
        // el fragmento embebido) — sus asignaciones deben estar guardadas, igual que ya
        // lo estaban los espejos de Revisión.
        Assert.Contains("if (semaforoDot) semaforoDot.className = `size-4 rounded-full animate-pulse", js);
        Assert.Contains("if (semaforoBadge) semaforoBadge.className", js);
        Assert.Contains("if (semaforoMensaje) semaforoMensaje.textContent", js);

        // Nuevo espejo hacia "Estado del crédito", mismo patrón que los de Revisión.
        Assert.Contains("estadoRiesgoDot", js);
        Assert.Contains("estadoRiesgoLabel", js);
        Assert.Contains("estadoRiesgoTag", js);
        Assert.Contains("document.getElementById('estado-riesgo-row')", js);
    }

    // ── CTA: sidebar no compite con el CTA contextual durante Crédito ──────────────────

    [Fact]
    public void VentaWizardForm_SidebarTotalesTieneIdParaOcultarseSoloEnCredito()
    {
        var view = ReadVenta("_VentaWizardForm.cshtml");

        Assert.Contains("id=\"venta-sidebar-totales\"", view);
        Assert.Contains("class=\"vm-totals", view);
    }

    [Fact]
    public void VentaPageWizardJs_StateMachineDeCreditoNoComparteCopyConSidebarDuranteElPaso()
    {
        var js = ReadJs("venta-page-wizard.js");

        Assert.Contains("scoreDisponible", js);
        Assert.Contains("'guardar-configuracion'", js);
        Assert.Contains("'continuar-revision'", js);
        Assert.Contains("sidebarTotales.hidden = esCredito", js);
        Assert.Contains("if (btnConfirmarLabel && !esCredito)", js);

        // Delegan en los botones reales que sí ejecutan la acción (no reimplementan lógica).
        Assert.Contains("document.querySelector('[data-credito-embebido-confirmar]')", js);
        Assert.Contains("document.querySelector('[data-credito-embebido-continuar-revision]')", js);

        // Hallazgo en vivo (Playwright, cliente con documentación+mora+cupo bloqueados): con
        // SCORE ya corrido pero el cliente todavía bloqueado, el fragmento embebido responde
        // con error y nunca renderiza data-credito-embebido-confirmar — sin fallback, el CTA
        // "Guardar configuración" quedaba en un no-op silencioso al clickear.
        Assert.Contains("mostrarRequisitoCredito(", js);
        Assert.Contains("Resolvé los bloqueantes de crédito", js);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string ReadVenta(string archivo) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", archivo));

    private static string ReadCredito(string archivo) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", archivo));

    private static string ReadJs(string archivo) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", archivo));

    private static string ReadCss(string archivo) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "css", archivo));

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
