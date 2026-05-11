public class ProductoCondicionesPagoModalUiContractTests
{
    private static string RepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(new[] { current!.FullName }.Concat(parts).ToArray());
    }

    private static string Script => File.ReadAllText(RepoFile("wwwroot", "js", "producto-condiciones-pago-modal.js"));

    private static string View => File.ReadAllText(RepoFile("Views", "Catalogo", "Index_tw.cshtml"));

    private static string Css => File.ReadAllText(RepoFile("wwwroot", "css", "catalogo-module.css"));

    [Theory]
    [InlineData("Efectivo", "direct")]
    [InlineData("Transferencia", "direct")]
    [InlineData("Cheque", "direct")]
    [InlineData("Cuenta Corriente", "direct")]
    public void MediosDirectos_NoMuestranCamposDeCuotas(string label, string mode)
    {
        Assert.Contains("{ value:", Script);
        Assert.Contains($"label: '{label}', mode: '{mode}'", Script);
        Assert.Contains("if (tipo.mode === 'card')", Script);
        Assert.Contains("if (tipo.mode === 'credit')", Script);
        Assert.Contains("return hidden(prefix + '.maxCuotasSinInteres'", Script);
        Assert.Contains("hidden(prefix + '.maxCuotasConInteres'", Script);
        Assert.Contains("hidden(prefix + '.maxCuotasCredito'", Script);
    }

    [Theory]
    [InlineData("Tarjeta Debito")]
    [InlineData("Tarjeta Credito")]
    [InlineData("Mercado Pago")]
    public void MediosTipoTarjeta_MuestranCuotasSinYConInteres(string label)
    {
        Assert.Contains($"label: '{label}', mode: 'card'", Script);
        Assert.Contains("field('Cuotas sin interes'", Script);
        Assert.Contains("field('Cuotas con interes'", Script);
        Assert.Contains("Reglas por tarjeta", Script);
    }

    [Fact]
    public void CreditoPersonal_MuestraSoloCuotasCredito()
    {
        Assert.Contains("label: 'Credito Personal', mode: 'credit'", Script);
        Assert.Contains("field('Cuotas credito'", Script);
        Assert.Contains("Credito Personal se configura separado de tarjetas.", Script);
    }

    [Fact]
    public void LabelsYAyuda_ContextualizanEstadoHeredarActivaYBloqueado()
    {
        Assert.DoesNotContain("field('Estado'", Script);
        Assert.Contains("field('Disponibilidad'", Script);
        Assert.Contains("Usar configuracion general", Script);
        Assert.Contains("Bloquear para este producto", Script);
        Assert.Contains("La regla participa en venta/diagnostico.", Script);
    }

    [Fact]
    public void Payload_MantieneNullTrueFalseYActivoActuales()
    {
        Assert.Contains("return String(value).toLowerCase() === 'true';", Script);
        Assert.Contains("if (value == null || value === '') return null;", Script);
        Assert.Contains("activo: data.has(prefix + 'activo')", Script);
        Assert.Contains("permitido: toBoolNullable(data.get(prefix + 'permitido'))", Script);
    }

    [Fact]
    public void Modal_TieneScrollInternoFooterAccesibleYFocusTrap()
    {
        Assert.Contains("condiciones-modal-body min-h-0 flex-1 overflow-y-auto", View);
        Assert.Contains("shrink-0 border-t border-slate-800", View);
        Assert.Contains("function trapFocus(event)", Script);
        Assert.Contains("focusInitialControl", Script);
        Assert.Contains(".condiciones-modal-shell", Css);
        Assert.Contains(".condiciones-modal-body", Css);
    }

    [Fact]
    public void ContratoFuturo_CuotaInactivaDocumentadaSinImplementacionBackend()
    {
        var doc = File.ReadAllText(RepoFile("docs", "fase-14-2-condiciones-pago-modal.md"));
        var fase151 = File.ReadAllText(RepoFile("docs", "fase-15.1-diseno-cuotas-por-plan.md"));

        Assert.Contains("cuota inactiva no se muestra al vendedor", doc);
        Assert.Contains("Plan inactivo no aparece y no se puede seleccionar", fase151);
        Assert.Contains("No cambia endpoints, DTOs, persistencia", doc);
        Assert.DoesNotContain("database update", doc, StringComparison.OrdinalIgnoreCase);
    }

    // --- Fase 17.12: resumen de medio basado en planes activos ---

    [Fact]
    public void MediosTipoCard_ResumenMuestraPlanesActivosNoEscalares()
    {
        Assert.Contains("Sin planes activos", Script);
        Assert.Contains("plan activo", Script);
        Assert.Contains("planes activos", Script);
        Assert.DoesNotContain("ctas s/int", Script);
        Assert.DoesNotContain("ctas c/int", Script);
    }

    // --- Fase 15.5: sección de planes de cuotas en el modal ---

    [Theory]
    [InlineData(0)]  // Efectivo
    [InlineData(1)]  // Transferencia
    [InlineData(4)]  // Cheque
    [InlineData(7)]  // Cuenta Corriente
    public void MediosDirectos_NoMuestranSeccionDePlanes(int tipoPago)
    {
        _ = tipoPago;
        Assert.Contains("if (tipo.mode === 'direct') return '';", Script);
        Assert.DoesNotContain("data-plan-cuota", View, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"Planes", View, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Tarjeta Credito", "card")]
    [InlineData("Tarjeta Debito", "card")]
    [InlineData("Mercado Pago", "card")]
    public void MediosTipoTarjeta_MuestranSeccionDePlanes(string label, string mode)
    {
        Assert.Contains($"label: '{label}', mode: '{mode}'", Script);
        Assert.Contains("Planes de cuotas", Script);
        Assert.Contains("condiciones-planes-section", Script);
        Assert.Contains("data-planes-section", Script);
        Assert.Contains("data-condiciones-add-plan", Script);
    }

    [Fact]
    public void CreditoPersonal_MuestraSeccionDePlanesPropia()
    {
        Assert.Contains("Planes de cuotas (Credito Personal)", Script);
        Assert.Contains("tipo.mode === 'credit'", Script);
    }

    [Fact]
    public void SeccionPlanes_TieneColumnasCuotasActivaAjusteTipoObservaciones()
    {
        Assert.Contains("condiciones-planes-table", Script);
        Assert.Contains(">Plan de pago<", Script);
        Assert.Contains(">Activa<", Script);
        Assert.Contains(">Ajuste al precio<", Script);
        Assert.Contains(">Tipo ajuste<", Script);
        Assert.Contains(">Observaciones<", Script);
        Assert.Contains("condiciones-planes-th", Script);
    }

    [Fact]
    public void SeccionPlanes_TieneAyudaFallbackMaximosEscalares()
    {
        Assert.Contains("Sin planes activos: se usan los maximos escalares actuales.", Script);
        Assert.Contains("data-planes-fallback", Script);
    }

    [Fact]
    public void SeccionPlanes_TieneAyudaAjusteSemantica()
    {
        Assert.Contains("Ajuste negativo descuenta precio, cero no cambia, positivo aplica recargo.", Script);
    }

    [Fact]
    public void SeccionPlanes_TieneAyudaCuotaInactivaNoVisibleEnVenta()
    {
        Assert.Contains("Cuota inactiva no se mostrara al vendedor en venta futura.", Script);
    }

    [Fact]
    public void PayloadFase155_IncluyePlanesYPreservaContratosExistentes()
    {
        Assert.Contains("planes: readPlanes(data, prefix)", Script);
        Assert.Contains("function readPlanes(data, prefix)", Script);
        Assert.Contains("tarjetas: readTarjetas(data, prefix),", Script);
        Assert.Contains("return String(value).toLowerCase() === 'true';", Script);
        Assert.Contains("activo: data.has(prefix + 'activo')", Script);
        Assert.Contains("permitido: toBoolNullable(data.get(prefix + 'permitido'))", Script);
    }

    [Fact]
    public void ModalFase155_LeeCondicionPlanesParaRenderizar()
    {
        Assert.Contains("condicion?.planes", Script);
        Assert.Contains("ajustePorcentaje", Script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".planes[", Script);
        Assert.Contains("renderPlanRow", Script);
        Assert.Contains("handleAddPlan", Script);
    }

    [Fact]
    public void ModalFase155_UsaMaximosEscalaresComoFallbackCuandoNoHayPlanes()
    {
        Assert.Contains("maxCuotasSinInteres", Script);
        Assert.Contains("maxCuotasConInteres", Script);
        Assert.Contains("maxCuotasCredito", Script);
        Assert.Contains("field('Cuotas sin interes'", Script);
        Assert.Contains("field('Cuotas con interes'", Script);
        Assert.Contains("field('Cuotas credito'", Script);
    }
}
