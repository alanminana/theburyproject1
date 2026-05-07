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
        Assert.Contains("Heredar - usa configuracion global", Script);
        Assert.Contains("La regla participa en venta/diagnostico.", Script);
        Assert.Contains("Bloqueado impide usar este medio para el producto.", Script);
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

        Assert.Contains("cuota inactiva no se muestra al vendedor", doc);
        Assert.Contains("No cambia endpoints, DTOs, persistencia", doc);
        Assert.DoesNotContain("database update", doc, StringComparison.OrdinalIgnoreCase);
    }
}
