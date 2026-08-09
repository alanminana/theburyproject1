using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// CSR-ML6 — Configurar Venta + Cotización: tabla completa por cuota (Número/Capital/Recargo/Total)
/// y metadata "Cuotas sin recargo". Mismo estilo que <see cref="ConfigurarVentaUiContractTests"/> y
/// <see cref="CotizacionControllerUiTests"/>: contrato estático (string-based) sobre vistas y JS —
/// no recalcula la fórmula financiera acá. El comportamiento real (render, badges, paridad visual
/// en pantalla, 0%, no consecutivas, última cuota, responsive) se valida con Playwright y queda
/// documentado en el reporte de cierre de CSR-ML6, no en este archivo.
/// </summary>
public sealed class CreditoPersonalCuotasTablaUiContractTests
{
    // ── Configurar Venta (standalone + embebida en el wizard) ──────────────────────────────

    [Theory]
    [InlineData("ConfigurarVenta_tw.cshtml")]
    [InlineData("_ConfigurarVentaEmbebida.cshtml")]
    public void ConfigurarVentaView_TieneTablaCompletaPorCuota(string archivo)
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", archivo));

        Assert.Contains("id=\"plan-cuotas-tabla\"", view);
        Assert.Contains(">Capital<", view);
        Assert.Contains(">Recargo<", view);
        Assert.Contains(">Total<", view);
        // La tabla arranca vacía en el markup: sólo JS la puebla desde el vector del servidor, no
        // hay filas ni importes pre-renderizados server-side que puedan quedar desactualizados.
        Assert.Contains("<tbody id=\"plan-cuotas-tabla-body\"></tbody>", view);
    }

    [Theory]
    [InlineData("ConfigurarVenta_tw.cshtml")]
    [InlineData("_ConfigurarVentaEmbebida.cshtml")]
    public void ConfigurarVentaView_MuestraMetadataDeCuotasSinRecargo(string archivo)
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", archivo));

        Assert.Contains("Cuotas sin recargo", view);
        Assert.Contains("id=\"plan-cuotas-sin-recargo\"", view);
    }

    [Theory]
    [InlineData("ConfigurarVenta_tw.cshtml")]
    [InlineData("_ConfigurarVentaEmbebida.cshtml")]
    public void ConfigurarVentaView_NoOfreceEdicionDeCuotasSinRecargoNiDePorcentajePorCuota(string archivo)
    {
        // La única pantalla editable de "cuotas sin recargo" es /ConfiguracionPago/CreditoPersonal
        // (ver CreditoPersonalConfigUiContractTests). Acá sólo se renderiza el vector resuelto.
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", archivo));

        Assert.DoesNotContain("asp-for=\"CuotasSinRecargo\"", view);
        Assert.DoesNotContain("name=\"CuotasSinRecargo\"", view);
        Assert.DoesNotContain("name=\"cuotasSinRecargo\"", view);
    }

    [Fact]
    public void ConfigurarVentaJs_ConsumeElVectorYLaMetadataDelServidor()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("data.cuotasSinRecargo", js);
        Assert.Contains("function renderTablaCuotas(cuotas)", js);
        Assert.Contains("function formatearCuotasSinRecargo(lista)", js);
        Assert.Contains("c.capital", js);
        Assert.Contains("c.interes", js);
        Assert.Contains("c.total", js);
    }

    [Fact]
    public void ConfigurarVentaJs_NoAsumeNMenosUnoIgualesMasUltimaDistinta()
    {
        // CSR-ML1: el resumen legado comparaba sólo cuotas[0] (primera) contra cuotas[length-1]
        // (última), lo que dejaba de ser válido con cuotas específicas sin recargo (no
        // necesariamente al final). El criterio nuevo compara TODAS las cuotas entre sí antes de
        // resumir "N cuotas de $X"; si hay importes distintos, remite a la tabla completa.
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("cuotas.every((c)", js);
        Assert.Contains("Ver detalle por cuota", js);
        Assert.DoesNotContain("cuotas.length - 1} cuotas de", js);
        Assert.DoesNotContain("+ última cuota de ${formatCurrency(ultima)}", js);
        // CV7: con colección vacía, el fallback histórico se conserva tal cual.
        Assert.Contains("`${cantidad} cuotas`", js);
    }

    [Fact]
    public void ConfigurarVentaJs_BadgeSinRecargoEsSobrioNoSoloColor()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "configurar-venta-credito.js"));

        Assert.Contains("Sin recargo", js);
        // .chip-neutral (credito-module.css) es gris/neutro: el badge no depende únicamente de un
        // color semántico (verde/rojo) para transmitir la distinción.
        Assert.Contains("chip-neutral", js);
    }

    // ── Cotización ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void CotizadorFormView_TieneTablaCompletaPorCuota()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));

        Assert.Contains("id=\"plan-credito-cuotas-tabla\"", view);
        Assert.Contains(">Capital<", view);
        Assert.Contains(">Recargo<", view);
        Assert.Contains(">Total<", view);
        Assert.Contains("<tbody id=\"plan-credito-cuotas-tabla-body\"></tbody>", view);
    }

    [Fact]
    public void CotizadorFormView_MuestraMetadataDeCuotasSinRecargo()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));

        Assert.Contains("Cuotas sin recargo", view);
        Assert.Contains("id=\"plan-credito-cuotas-sin-recargo\"", view);
    }

    [Fact]
    public void CotizadorFormView_NoOfreceInputsEditablesDeCuotasSinRecargoNiDePorcentaje()
    {
        // CT5: la UI de Cotización no debe exponer ningún input editable de CuotasSinRecargo,
        // Porcentaje ni Recargo por cuota — el único input de crédito personal es el anticipo
        // (ya existente antes de CSR-ML6).
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "_CotizadorForm.cshtml"));

        Assert.DoesNotContain("cuotasSinRecargo\" type=\"", view);
        Assert.DoesNotContain("id=\"cotizacion-cuotas-sin-recargo\"", view);
        Assert.DoesNotContain("id=\"cotizacion-tasa\"", view);
        Assert.DoesNotContain("id=\"cotizacion-porcentaje\"", view);
        Assert.DoesNotContain("id=\"cotizacion-recargo-cuota\"", view);
    }

    [Fact]
    public void CotizacionSimuladorJs_ConsumeElVectorYLaMetadataDelServidor()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        Assert.Contains("plan.cuotasSinRecargo", js);
        Assert.Contains("function renderTablaCuotasCredito(cuotas)", js);
        Assert.Contains("function formatearCuotasSinRecargo(lista)", js);
        Assert.Contains("c.capital", js);
        Assert.Contains("c.interes", js);
        Assert.Contains("c.total", js);
    }

    [Fact]
    public void CotizacionSimuladorJs_NoAsumeNMenosUnoIgualesMasUltimaDistinta()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        Assert.Contains("cuotas.every(c =>", js);
        Assert.Contains("Ver detalle por cuota", js);
        Assert.DoesNotContain("regulares.length} cuotas de", js);
        Assert.DoesNotContain("+ última cuota de ${formatCurrency(ultima.total)}", js);
    }

    [Fact]
    public void CotizacionSimuladorJs_BadgeSinRecargoEsSobrioNoSoloColor()
    {
        var js = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        Assert.Contains("Sin recargo", js);
        // .pill-slate (cotizacion-simulador.css) es gris/neutro, no un color semántico.
        Assert.Contains("pill-slate", js);
    }

    // ── DTO / contrato de superficie de cliente ─────────────────────────────────────────────

    [Fact]
    public void CreditoSimulacionVentaJson_ExponeCuotasSinRecargoComoMetadataDelPlan()
    {
        var propiedad = typeof(CreditoSimulacionVentaJson).GetProperty("cuotasSinRecargo");

        Assert.NotNull(propiedad);
        Assert.True(typeof(IReadOnlyList<int>).IsAssignableFrom(propiedad!.PropertyType));
    }

    [Fact]
    public void CotizacionPlanPagoResultado_ExponeCuotasSinRecargoComoMetadataDelPlan()
    {
        var propiedad = typeof(CotizacionPlanPagoResultado).GetProperty("CuotasSinRecargo");

        Assert.NotNull(propiedad);
        Assert.True(typeof(IReadOnlyList<int>).IsAssignableFrom(propiedad!.PropertyType));
    }

    // Complementa a P10 (CotizacionVentaParidadCreditoPersonalTests, que cubre
    // CreditoSimulacionVentaRequest): tampoco el request de simulación de Cotización transporta
    // CuotasSinRecargo como superficie de cliente — el backend la resuelve siempre server-side
    // desde el plan.
    [Fact]
    public void CotizacionSimulacionRequest_NoExponeCuotasSinRecargoComoSuperficieDeCliente()
    {
        var propiedades = typeof(CotizacionSimulacionRequest).GetProperties();
        Assert.DoesNotContain(propiedades, p => p.Name.Contains("CuotasSinRecargo", StringComparison.Ordinal));
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
