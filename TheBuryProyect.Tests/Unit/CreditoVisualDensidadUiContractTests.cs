namespace TheBuryProject.Tests.Unit;

/// <summary>
/// CREDITO-VISUAL-02B: reduce la densidad visual del configurador de Crédito embebido
/// (wrapper <c>#panel-configuracion-credito</c>, subgrupo "Primera cuota") y convierte
/// "Detalle por cuota" en &lt;details&gt;/&lt;summary&gt; nativo colapsado por defecto.
/// Contrato estático (string-based) sobre vistas/JS/CSS — no recalcula la fórmula
/// financiera acá. El comportamiento real (colapsar/expandir, teclado, recalculación con
/// el &lt;details&gt; abierto preservado, responsive) se validó con Playwright MCP contra
/// la app real y queda documentado en el cierre del micro-lote, no en este archivo.
/// </summary>
public sealed class CreditoVisualDensidadUiContractTests
{
    private const string WizardForm = "_VentaWizardForm.cshtml";
    private const string ConfigurarEmbebida = "_ConfigurarVentaEmbebida.cshtml";

    // ── Wrapper #panel-configuracion-credito: id/DOM/selectores JS preservados ─────────

    [Fact]
    public void VentaWizardForm_PreservaIdYSelectoresDelWrapperDeCredito()
    {
        var view = ReadVenta(WizardForm);

        Assert.Contains("id=\"panel-configuracion-credito\"", view);
        Assert.Contains("id=\"credito-embebido-cargando\"", view);
        Assert.Contains("id=\"credito-embebido-error\"", view);
        Assert.Contains("id=\"credito-embebido-contenedor\"", view);
    }

    [Fact]
    public void VentaWizardForm_WrapperDeCreditoYaNoDuplicaChromeDeCard()
    {
        // CREDITO-VISUAL-02B: el div interno ya no lleva su propio border/bg/rounded — las
        // secciones inyectadas (.card.card-pad) ya aportan esa jerarquía; duplicarla acá
        // producía "card que contiene cards". El wrapper en sí (id) se preserva intacto.
        var view = ReadVenta(WizardForm);

        var wrapperIndex = view.IndexOf("id=\"panel-configuracion-credito\"", StringComparison.Ordinal);
        Assert.True(wrapperIndex >= 0, "No se encontró #panel-configuracion-credito.");
        var siguiente = view.Substring(wrapperIndex, 400);

        Assert.DoesNotContain("rounded-xl border border-slate-700 bg-slate-900/40 p-4", siguiente);
    }

    // ── Primera cuota: subgrupo nivel 3 (separador liviano), ya no card completa ───────

    [Fact]
    public void ConfigurarVentaEmbebida_PrimeraCuotaPreservaIdYContenido()
    {
        var view = ReadCredito(ConfigurarEmbebida);

        Assert.Contains("id=\"primera-cuota-panel\"", view);
        Assert.Contains("data-primera-cuota-panel", view);
        Assert.Contains("data-primera-cuota-no-aplica", view);
        Assert.Contains("data-primera-cuota-aplica", view);
        Assert.Contains("data-primera-cuota-cobrar", view);
        Assert.Contains("data-primera-cuota-medio-container", view);
        Assert.Contains("data-primera-cuota-medio", view);
        Assert.Contains("id=\"chk-cobrar-primera-cuota\"", view);
        Assert.Contains("id=\"select-medio-primera-cuota\"", view);
    }

    [Fact]
    public void ConfigurarVentaEmbebida_PrimeraCuotaYaNoEsCardCompleta()
    {
        var view = ReadCredito(ConfigurarEmbebida);

        var idx = view.IndexOf("id=\"primera-cuota-panel\"", StringComparison.Ordinal);
        Assert.True(idx >= 0, "No se encontró #primera-cuota-panel.");
        var bloqueApertura = view.Substring(Math.Max(0, idx - 250), 250);

        // Ya no repite el mismo chrome de card completa que usaba el wrapper original
        // (border+rounded+fondo) — pasa a un separador superior liviano, mismo patrón que
        // "Porcentaje de recargo total del plan" en la misma card.
        Assert.DoesNotContain("rounded-xl border border-slate-700 bg-slate-900/40 p-4", bloqueApertura);
        Assert.Contains("border-top:1px solid var(--line)", bloqueApertura);
    }

    [Fact]
    public void ConfigurarVentaEmbebida_PrimeraCuotaQuedaVisualmenteSubordinadaALaCard()
    {
        // El heading "Primera cuota" no debe competir con "Cantidad de cuotas"/"Valores del
        // credito" (.sec-title, nivel 2): pasa a un label pequeño en mayúsculas, no a un h3
        // del mismo peso que el título del wrapper "Configuración de Crédito Personal".
        var view = ReadCredito(ConfigurarEmbebida);

        Assert.Contains("<h3 class=\"text-[11px] font-bold text-slate-400 uppercase tracking-wide\">Primera cuota</h3>", view);
        Assert.DoesNotContain("<h3 class=\"font-semibold text-white\">Primera cuota</h3>", view);
    }

    // ── Detalle por cuota: details/summary nativo, colapsado por defecto ───────────────

    [Fact]
    public void ConfigurarVentaEmbebida_DetalleUsaDetailsSummaryNativoColapsado()
    {
        var view = ReadCredito(ConfigurarEmbebida);

        Assert.Contains("<details data-plan-cuotas-detalle-details>", view);
        Assert.Contains("<summary class=\"sec-title\" data-plan-cuotas-detalle-summary>", view);
        // Sin atributo "open": colapsado por defecto tanto en Create como en Edit.
        Assert.DoesNotContain("<details open", view);
        Assert.DoesNotContain("open data-plan-cuotas-detalle-details", view);
        Assert.DoesNotContain("data-plan-cuotas-detalle-details open", view);
    }

    [Fact]
    public void ConfigurarVentaEmbebida_DetalleNoAgregaAccordionNiAriaExpandedManual()
    {
        // Comportamiento nativo del navegador (Enter/Espacio, foco, marcador abierto/cerrado):
        // nada de aria-expanded manual ni role="button" sobre el <summary>.
        var view = ReadCredito(ConfigurarEmbebida);

        Assert.DoesNotContain("aria-expanded", view);

        var summaryIdx = view.IndexOf("data-plan-cuotas-detalle-summary", StringComparison.Ordinal);
        Assert.True(summaryIdx >= 0);
        var largo = Math.Min(80, view.Length - summaryIdx);
        Assert.DoesNotContain("role=\"button\"", view.Substring(summaryIdx, largo));
    }

    [Fact]
    public void ConfigurarVentaEmbebida_TablaSigueIntactaDentroDelDetails()
    {
        var view = ReadCredito(ConfigurarEmbebida);

        Assert.Contains("id=\"plan-cuotas-tabla\"", view);
        Assert.Contains("<tbody id=\"plan-cuotas-tabla-body\"></tbody>", view);
        Assert.Contains(">N°<", view);
        Assert.Contains(">Capital<", view);
        Assert.Contains(">Recargo<", view);
        Assert.Contains(">Total<", view);

        // La tabla vive dentro del <details>, no fuera.
        // Carryover pre-existente (ya roto en HEAD b6660e8, ajeno a este micro-lote):
        // cierreIdx buscaba el primer "</details>" de TODO el archivo, pero "Ver detalle
        // financiero" (data-plan-detalle-financiero) abre y cierra su propio <details>
        // antes de que este bloque exista en el documento — el primer cierre real nunca
        // es el de data-plan-cuotas-detalle-details. Se busca el cierre a partir de la
        // tabla, no desde el principio del archivo.
        var detailsIdx = view.IndexOf("<details data-plan-cuotas-detalle-details>", StringComparison.Ordinal);
        var tablaIdx = view.IndexOf("id=\"plan-cuotas-tabla\"", StringComparison.Ordinal);
        var cierreIdx = view.IndexOf("</details>", tablaIdx, StringComparison.Ordinal);
        Assert.True(detailsIdx >= 0 && tablaIdx > detailsIdx && cierreIdx > tablaIdx);
    }

    // ── JS: summary dinámico alimentado por la misma autoridad que el Resumen ──────────

    [Fact]
    public void ConfigurarVentaJs_SummaryDeDetalleUsaMismaAutoridadQueElResumen()
    {
        var js = ReadJs("configurar-venta-credito.js");

        Assert.Contains("const planCuotasDetalleSummary = $('[data-plan-cuotas-detalle-summary]');", js);
        Assert.Contains("function formatearResumenDetalleCuotas(cantidad, cuotaEstimada)", js);
        // Reusa data.cuotaEstimada (misma fuente que #plan-cuota-estimada) y el parámetro
        // "cuotas" ya calculado — nunca un cálculo financiero propio.
        Assert.Contains("planCuotasDetalleSummary.textContent = formatearResumenDetalleCuotas(cuotas, data.cuotaEstimada);", js);
    }

    // VENTA-CREDITO-REDESIGN-VISUAL-IMPLEMENTACION-01: "Detalle por cuota" y "Ver detalle
    // financiero" se fusionaron en un único <details> "Ver desglose ▸" (ver
    // ConfigurarVentaEmbebida_UnicoDetailsVerDesglose más abajo) — el fallback y el texto
    // dinámico del <summary> se renombraron para no quedar desalineados con el nuevo
    // encabezado.
    [Fact]
    public void ConfigurarVentaJs_SummaryTieneFallbackNeutralSinSimulacionValida()
    {
        var js = ReadJs("configurar-venta-credito.js");

        Assert.Contains("if (!cantidad || cantidad <= 0 || !Number.isFinite(cuotaEstimada)) return 'Ver desglose ▸';", js);
        Assert.DoesNotContain("0 cuotas de $0", js);
    }

    [Fact]
    public void ConfigurarVentaJs_ResetPlanResumenLimpiaElSummaryDeDetalle()
    {
        var js = ReadJs("configurar-venta-credito.js");

        var resetIdx = js.IndexOf("function resetPlanResumen()", StringComparison.Ordinal);
        Assert.True(resetIdx >= 0, "No se encontró resetPlanResumen().");
        var cuerpoReset = js.Substring(resetIdx, 1100);

        Assert.Contains("planCuotasDetalleSummary.textContent = 'Ver desglose ▸';", cuerpoReset);
    }

    [Fact]
    public void ConfigurarVentaJs_NoReemplazaElNodoDetailsAlActualizarElPlan()
    {
        // CREDITO-VISUAL-02B (item 12 del micro-lote): si el <details> se reemplazara por
        // innerHTML en cada recalculación, el estado "open" que dejó el usuario se
        // perdería. Sólo debe tocarse el texto del summary (arriba) y el tbody de la tabla
        // (renderTablaCuotas), nunca un innerHTML del propio <details> ni su atributo open.
        var js = ReadJs("configurar-venta-credito.js");

        Assert.DoesNotContain("detalle-details'].innerHTML", js);
        Assert.DoesNotContain(".removeAttribute('open')", js);
        Assert.DoesNotContain(".setAttribute('open'", js);
        Assert.DoesNotContain(".open = false", js);
        Assert.DoesNotContain(".open = true", js);
    }

    // ── CSS: sólo foco/cursor/marcador nativos, sin reemplazar el comportamiento ───────

    [Fact]
    public void CreditoModuleCss_EstilaSummaryNativoSinReemplazarComportamiento()
    {
        var css = ReadCss("credito-module.css");

        Assert.Contains("[data-plan-cuotas-detalle] summary {", css);
        Assert.Contains("cursor: pointer;", css);
        Assert.Contains("[data-plan-cuotas-detalle] summary:focus-visible {", css);
        // Nada de accordion JS: el marcador y su rotación quedan a cargo del navegador.
        Assert.DoesNotContain(".chevron-detalle-cuota", css);
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
