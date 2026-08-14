namespace TheBuryProject.Tests.Unit;

/// <summary>
/// VENTA-CREDITO-REDESIGN-VISUAL-CIERRE-01: cierra el Step Crédito del wizard de Venta en
/// exactamente 2 superficies principales ("Estado del crédito" + "Configurar plan"). Antes
/// de este lote la mitad inferior del paso eran hasta 4 bloques con chrome propio
/// consecutivos: título externo "Configuración de Crédito Personal" (_VentaWizardForm.cshtml)
/// + card "Configurar plan" + aside "Resumen del plan" + card "Ver desglose" — cada uno
/// leyéndose como una superficie principal más, más un CTA "Guardar configuración" duplicado
/// (hero global del wizard + botón real dentro del fragmento). Contrato estático
/// (string-based) sobre vistas/JS/CSS — el comportamiento real (responsive, teclado, guardar
/// → continuar → revisión, sidebar durante Crédito) se validó con Playwright MCP contra la
/// app real y queda documentado en el cierre del micro-lote, no en este archivo.
/// </summary>
public sealed class VentaCreditoRedesignVisualCierreUiContractTests
{
    // ── "Configurar plan" es una única superficie: inputs, recargo, resumen vivo,
    //    desglose y CTA viven dentro de la misma <section class="card card-pad">, sin
    //    cards internas (aside "Resumen del plan" / card "Ver desglose" retiradas). ──────

    [Fact]
    public void ConfigurarVentaEmbebida_ConfigurarPlanEsUnaUnicaSuperficieCardCardPad()
    {
        var view = ReadCredito("_ConfigurarVentaEmbebida.cshtml");

        // Antes había 2 secciones "card card-pad" (Configurar plan + wrapper de Ver
        // desglose); ahora sólo 1. La documentación contractual sigue aparte pero usa
        // "card-2" (clase distinta), nunca visible en Crédito (se reubica a Revisión en
        // runtime, ver venta-credito-embebido.js).
        Assert.Equal(1, CountOccurrences(view, "class=\"card card-pad\""));

        var sectionIdx = view.IndexOf("<section class=\"card card-pad\">", StringComparison.Ordinal);
        Assert.True(sectionIdx >= 0, "No se encontró la superficie única 'Configurar plan'.");
        var closeIdx = view.IndexOf("</section>", sectionIdx, StringComparison.Ordinal);
        Assert.True(closeIdx > sectionIdx);

        // Todo lo que antes vivía repartido en 4 bloques queda dentro de esta única
        // sección: los 4 campos del plan, el resumen vivo, el desglose y el CTA real.
        foreach (var marcador in new[]
        {
            "id=\"txt-cuotas\"", "id=\"txt-anticipo\"", "id=\"txt-gastos\"",
            "id=\"txt-fecha-primera-cuota\"", "id=\"txt-tasa\"",
            "id=\"plan-resumen\"", "id=\"plan-cuota-estimada\"", "id=\"plan-total\"",
            "data-plan-cuotas-detalle-details", "id=\"plan-cuotas-tabla\"",
            "id=\"btn-confirmar-credito\""
        })
        {
            var idx = view.IndexOf(marcador, StringComparison.Ordinal);
            Assert.True(idx > sectionIdx && idx < closeIdx,
                $"'{marcador}' debe vivir dentro de la única superficie 'Configurar plan'.");
        }
    }

    [Fact]
    public void ConfigurarVentaEmbebida_CantidadDeCuotasSeSumaALaGrillaDeValores()
    {
        // VENTA-CREDITO-REDESIGN-VISUAL-CIERRE-01 (§9): "Cantidad de cuotas" ya no ocupa
        // una fila propia a ancho completo — se integra a la misma grilla que
        // Anticipo/Gastos/Vencimiento (antes 3 campos, ahora 4), para que el layout
        // desktop pueda ponerlos en fila usando el ancho recuperado de la sidebar.
        var view = ReadCredito("_ConfigurarVentaEmbebida.cshtml");

        var gridIdx = view.IndexOf("data-credito-config-valores-grid", StringComparison.Ordinal);
        var hintCierreIdx = view.IndexOf(
            "El backend valida los importes finales; esta pantalla solo previsualiza.",
            StringComparison.Ordinal);
        Assert.True(gridIdx >= 0 && hintCierreIdx > gridIdx);

        var idxCuotas = view.IndexOf("id=\"txt-cuotas\"", StringComparison.Ordinal);
        var idxAnticipo = view.IndexOf("id=\"txt-anticipo\"", StringComparison.Ordinal);
        var idxGastos = view.IndexOf("id=\"txt-gastos\"", StringComparison.Ordinal);
        var idxFecha = view.IndexOf("id=\"txt-fecha-primera-cuota\"", StringComparison.Ordinal);

        Assert.True(gridIdx < idxCuotas && idxCuotas < idxAnticipo && idxAnticipo < idxGastos
            && idxGastos < idxFecha && idxFecha < hintCierreIdx,
            "Cuotas/Anticipo/Gastos/Vencimiento deben vivir en orden dentro de la misma grilla.");
    }

    [Fact]
    public void ConfigurarVentaEmbebida_ResumenDejaDeSerCardIndependiente()
    {
        // Antes: <aside><div class="card card-pad"><h3>Resumen del plan</h3>...</div></aside>,
        // columna propia junto a "Configurar plan". Ahora el resultado vivo (cuota/total
        // financiado/vencimiento) se integra al cierre de la misma card — sin su propio
        // wrapper con chrome (border/bg/rounded), sin encabezado h3 "Resumen del plan"
        // aparte, y sin la caja verde .result-card.is-ok ("card dentro de card") alrededor
        // del monto de la cuota.
        var view = ReadCredito("_ConfigurarVentaEmbebida.cshtml");

        Assert.DoesNotContain("class=\"result-card is-ok\"", view);
        Assert.DoesNotContain(
            "<h3 class=\"font-semibold text-white mb-3 flex items-center gap-2\">", view);

        // plan-resumen sigue existiendo (mismo id/JS) pero como cierre de la única
        // superficie "Configurar plan" (ver ConfigurarVentaEmbebida_ConfigurarPlanEsUnaUnicaSuperficieCardCardPad).
        Assert.Contains("id=\"plan-resumen\"", view);
        Assert.Contains("id=\"plan-cuota-estimada\"", view);
    }

    [Fact]
    public void ConfigurarVentaEmbebida_VerDesgloseSinWrapperDeCardPropia()
    {
        // Antes: <section class="card card-pad mt-4" data-plan-cuotas-detalle"> envolvía
        // el <details> — una tercera card. El <details> (mismo id/summary/contenido,
        // protegido por VentaCreditoRedesignVisualUiContractTests.ConfigurarVentaEmbebida_UnicoDetailsVerDesglose)
        // ya no tiene ese wrapper: vive directo dentro del cierre de "Configurar plan".
        var view = ReadCredito("_ConfigurarVentaEmbebida.cshtml");

        Assert.DoesNotContain("class=\"card card-pad mt-4\" data-plan-cuotas-detalle>", view);
        Assert.DoesNotContain("data-plan-cuotas-detalle>", view);
        Assert.Contains("<details data-plan-cuotas-detalle-details>", view);
    }

    [Fact]
    public void ConfigurarVentaEmbebida_CtaGuardarSinWrapperDeCierrePropio()
    {
        // Antes: <div class="mt-4 credito-embebido-cierre"> alineaba el CTA a una columna
        // angosta (2/3 del split Configuración/Resumen que ya no existe). El botón real
        // (mismo id/data-hook/comportamiento) queda como cierre de la única superficie.
        var view = ReadCredito("_ConfigurarVentaEmbebida.cshtml");

        Assert.DoesNotContain("class=\"mt-4 credito-embebido-cierre\"", view);
        Assert.Contains("id=\"btn-confirmar-credito\"", view);
        Assert.Contains("data-credito-embebido-continuar-revision", view);
    }

    [Fact]
    public void ConfigurarVentaEmbebida_DocumentacionContractualQuedaFueraDeConfigurarPlan()
    {
        // No es configuración del plan — es una acción posterior (y nunca es visible en
        // Crédito: venta-credito-embebido.js la reubica a Revisión en runtime). Se
        // preserva como su propia sección (class distinta, "card-2"), fuera de la card
        // única de "Configurar plan".
        var view = ReadCredito("_ConfigurarVentaEmbebida.cshtml");

        var closeConfigurarPlanIdx = view.IndexOf("</section>", StringComparison.Ordinal);
        var contratoIdx = view.IndexOf("data-credito-embebido-contrato-seccion", StringComparison.Ordinal);
        Assert.True(closeConfigurarPlanIdx > 0 && contratoIdx > closeConfigurarPlanIdx,
            "La sección de documentación contractual debe quedar fuera (después) de 'Configurar plan'.");
        Assert.Contains("class=\"card-2\"", view);
    }

    // ── Título duplicado ("Configuración de Crédito Personal" + "Configurar plan") ─────

    [Fact]
    public void VentaWizardForm_RetiraTituloDuplicadoDeConfiguracionDeCreditoPersonal()
    {
        var view = ReadVenta("_VentaWizardForm.cshtml");

        Assert.DoesNotContain("Configuración de Crédito Personal", view);

        // El wrapper y sus 3 hooks de estado siguen intactos (mismo id/DOM, sólo se
        // retiró el título+descripción propios).
        Assert.Contains("id=\"panel-configuracion-credito\"", view);
        Assert.Contains("id=\"credito-embebido-cargando\"", view);
        Assert.Contains("id=\"credito-embebido-error\"", view);
        Assert.Contains("id=\"credito-embebido-contenedor\"", view);
    }

    // ── CTA único: el hero global del wizard no compite con el botón real del Plan ─────

    [Fact]
    public void VentaPageWizardJs_OcultaCtaGlobalCuandoElPlanTieneBotonEquivalente()
    {
        var js = ReadJs("venta-page-wizard.js");

        // Sólo "Verificar crédito" (SCORE todavía no corrió) no tiene botón equivalente
        // dentro del Plan — en 'guardar-configuracion' y 'continuar-revision' el CTA
        // global se oculta para no duplicar el CTA primario visible.
        Assert.Contains("const ocultarCtaGlobal = esCredito && accion !== 'verify-credit';", js);
        Assert.Contains("button.classList.toggle('hidden', ocultarCtaGlobal);", js);
        Assert.Contains("button.hidden = ocultarCtaGlobal;", js);
    }

    // ── Sidebar contextual: sin columna dedicada durante Crédito (desktop) ─────────────

    [Fact]
    public void VentaPageWizardCss_SidebarSinColumnaDedicadaDuranteCredito()
    {
        var css = ReadCss("venta-page-wizard.css");

        Assert.Contains("#venta-create-page[data-paso-activo=\"credito\"] #venta-form>.grid,", css);
        Assert.Contains("#venta-edit-page[data-paso-activo=\"credito\"] #venta-form>.grid {", css);
        Assert.Contains("#venta-create-page[data-paso-activo=\"credito\"] .vm-sidebar,", css);
        Assert.Contains("#venta-edit-page[data-paso-activo=\"credito\"] .vm-sidebar {", css);

        // El bloque de reglas de Crédito debe forzar 1 columna + sidebar no-sticky, sin
        // depender de un breakpoint de viewport (aplica también en desktop ≥1280px,
        // donde el resto de los pasos sí reserva columna de sidebar).
        var idx = css.IndexOf("[data-paso-activo=\"credito\"] #venta-form>.grid,", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var bloque = css.Substring(idx, Math.Min(400, css.Length - idx));
        Assert.Contains("grid-template-columns: 1fr;", bloque);

        var idxSidebar = css.IndexOf("[data-paso-activo=\"credito\"] .vm-sidebar,", StringComparison.Ordinal);
        Assert.True(idxSidebar >= 0);
        var bloqueSidebar = css.Substring(idxSidebar, Math.Min(200, css.Length - idxSidebar));
        Assert.Contains("position: static;", bloqueSidebar);
    }

    [Fact]
    public void VentaPageWizardCss_EliminaCssMuertoDeCierreAnguloDelCta()
    {
        // .credito-embebido-cierre ya no tiene consumidor en el markup (ver
        // ConfigurarVentaEmbebida_CtaGuardarSinWrapperDeCierrePropio) — la regla de
        // ancho angosto/margin-left:auto que sólo aplicaba a esa clase se retira.
        var css = ReadCss("venta-page-wizard.css");

        Assert.DoesNotContain(".credito-embebido-cierre {", css);
        Assert.DoesNotContain("max-width: 26rem;", css);
    }

    // ── Layout desktop: 4 columnas cuando el ancho recuperado alcanza ──────────────────

    [Fact]
    public void CreditoModuleCss_ContainerQuerySumaUmbralDeCuatroColumnas()
    {
        var css = ReadCss("credito-module.css");

        Assert.Contains("@container (min-width: 16.25rem) {", css);
        Assert.Contains("@container (min-width: 26.25rem) {", css);
        Assert.Contains("@container (min-width: 40rem) {", css);

        var idx = css.IndexOf("@container (min-width: 40rem) {", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        var bloque = css.Substring(idx, Math.Min(150, css.Length - idx));
        Assert.Contains("[data-credito-config-valores-grid]", bloque);
        Assert.Contains("repeat(4, minmax(0, 1fr));", bloque);
    }

    // ── Regresión evitada: el <summary> de "Ver desglose" no pierde su estilo al perder
    //    el wrapper data-plan-cuotas-detalle (ahora vive directo en el <details>) ───────

    [Fact]
    public void CreditoModuleCss_SummaryDeVerDesgloseSigueEstilizadoSinElWrapperRetirado()
    {
        var css = ReadCss("credito-module.css");

        Assert.Contains("[data-plan-cuotas-detalle-details] summary {", css);
        Assert.Contains("[data-plan-cuotas-detalle-details] summary::marker {", css);
        Assert.Contains("[data-plan-cuotas-detalle-details] summary:focus-visible {", css);

        // El bloque original (standalone, ConfigurarVenta_tw.cshtml) se conserva intacto,
        // regla aparte (no combinada), para no tocar el contrato ya protegido por
        // CreditoVisualDensidadUiContractTests.CreditoModuleCss_EstilaSummaryNativoSinReemplazarComportamiento.
        Assert.Contains("[data-plan-cuotas-detalle] summary {", css);
    }

    // ── Standalone ConfigurarVenta_tw: no tocado por este lote ─────────────────────────

    [Fact]
    public void ConfigurarVentaTw_NoFueTocadoPorElCierreVisual()
    {
        var view = ReadCredito("ConfigurarVenta_tw.cshtml");

        // Conserva su propio split Configuración/Resumen (markup original, con "lg:"/"md:"
        // de Tailwind) — nunca tuvo el wrapper .credito-embebido-cierre ni el <details>
        // fusionado "Ver desglose ▸" (ese texto es exclusivo del fragmento embebido).
        Assert.DoesNotContain("credito-embebido-cierre", view);
        Assert.DoesNotContain("Ver desglose", view);
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
