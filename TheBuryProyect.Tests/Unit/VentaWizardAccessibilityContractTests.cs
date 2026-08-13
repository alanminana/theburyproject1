namespace TheBuryProject.Tests.Unit;

public class VentaWizardAccessibilityContractTests
{
    [Fact]
    public void CreateRendersTheSharedWizardWithoutEmbeddingTheQuotationPage()
    {
        var view = ReadView("Create_tw.cshtml");

        Assert.Contains("<partial name=\"_VentaWizardForm\" model=\"Model\" />", view);
        // El cotizador entra como parcial reutilizable, nunca como vista completa.
        Assert.DoesNotContain("Index_tw", view);
        Assert.Contains("cotizacion-simulador.css", view);
        Assert.Contains("cotizacion-simulador-ui.js", view);
        Assert.Contains("cotizacion-simulador.js", view);
        // El enlace circular a Cotizacion/Index (que redirige a esta misma pantalla)
        // fue reemplazado por la pestaña Cotizar.
        Assert.DoesNotContain("asp-controller=\"Cotizacion\" asp-action=\"Index\"", view);
    }

    [Fact]
    public void CreateShowsCotizarStepBeforeClienteUsingTheReusableQuotationPartial()
    {
        var partial = ReadView("_VentaWizardForm.cshtml");

        Assert.Contains("id=\"step-btn-cotizar\"", partial);
        Assert.Contains("id=\"step-panel-cotizar\"", partial);
        Assert.Contains("aria-controls=\"step-panel-cotizar\"", partial);
        Assert.Contains("aria-labelledby=\"step-btn-cotizar\"", partial);
        Assert.Contains("data-step=\"cotizar\"", partial);
        // Reutiliza el flujo canónico de Cotización, no una copia del markup.
        Assert.Contains("<partial name=\"~/Views/Cotizacion/_CotizadorForm.cshtml\" />", partial);

        // Cotizar se renderiza antes que Cliente, tanto la pestaña como el panel.
        Assert.True(
            partial.IndexOf("id=\"step-btn-cotizar\"", StringComparison.Ordinal)
            < partial.IndexOf("id=\"step-btn-cliente\"", StringComparison.Ordinal),
            "La pestaña Cotizar debe renderizarse antes que Cliente.");
        Assert.True(
            partial.IndexOf("id=\"step-panel-cotizar\"", StringComparison.Ordinal)
            < partial.IndexOf("id=\"step-panel-cliente\"", StringComparison.Ordinal),
            "El panel Cotizar debe renderizarse antes que el de Cliente.");
    }

    [Fact]
    public void CotizarStepIsRenderedOnlyWhenCreatingTheOperation()
    {
        var partial = ReadView("_VentaWizardForm.cshtml");

        // El parcial es compartido con Edit: Cotizar depende de `pasoCotizar`,
        // derivado de `esEdicion` (Model.Id > 0).
        Assert.Contains("var esEdicion = Model.Id > 0;", partial);
        Assert.Contains("var pasoCotizar = !esEdicion;", partial);
        Assert.Contains("@if (pasoCotizar)", partial);

        // Edit no trae los assets del cotizador.
        var edit = ReadView("Edit_tw.cshtml");
        Assert.DoesNotContain("cotizacion-simulador", edit);
        Assert.DoesNotContain("_CotizadorForm", edit);
    }

    [Fact]
    public void SharedWizardDropsTheSelfReferencingQuotationLink()
    {
        var partial = ReadView("_VentaWizardForm.cshtml");

        Assert.DoesNotContain("Ir al cotizador", partial);
        Assert.DoesNotContain("<a asp-controller=\"Venta\" asp-action=\"Create\"", partial);
    }

    [Fact]
    public void ReusableQuotationPartialAvoidsFullViewArtifacts()
    {
        var partial = ReadCotizacionView("_CotizadorForm.cshtml");

        Assert.Contains("data-cotizacion-simulador", partial);
        // Sin layout ni secciones: es un parcial, no una vista completa.
        Assert.DoesNotContain("@section", partial);
        Assert.DoesNotContain("ViewData[\"Title\"]", partial);
        Assert.DoesNotContain("<html", partial);
        // Sin <form> propio (evita anidar dentro de #venta-form) ni antiforgery
        // duplicado: el host lo aporta y cotizacion-simulador.js lo resuelve.
        Assert.DoesNotContain("<form", partial);
        Assert.DoesNotContain("Html.AntiForgeryToken()", partial);
        // Los scripts los carga el host, no el parcial.
        Assert.DoesNotContain("<script", partial);
    }

    [Fact]
    public void StandaloneQuotationViewReusesTheSamePartial()
    {
        var view = ReadCotizacionView("Index_tw.cshtml");

        // Un único origen del markup del cotizador: la vista de pantalla completa
        // también consume el parcial en vez de duplicarlo.
        Assert.Contains("<partial name=\"_CotizadorForm\" />", view);
        Assert.Contains("Html.AntiForgeryToken()", view);
        Assert.Contains("@section Styles", view);
        Assert.Contains("@section Scripts", view);
        Assert.DoesNotContain("data-cotizacion-simulador", view);
    }

    [Fact]
    public void WizardScriptKeepsCotizarStepEnabledAndDelegatesItsPrimaryAction()
    {
        var wizardScript = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-page-wizard.js"));

        Assert.Contains("cotizar: () => true", wizardScript);
        Assert.Contains("simular-cotizacion", wizardScript);
        Assert.Contains("cotizacion-simular", wizardScript);
        // El resumen de la venta no debe pisar los totales del cotizador.
        Assert.Contains("COTIZADOR_SELECTOR", wizardScript);
    }

    [Fact]
    public void SharedWizardHasAccessibleDocumentationModalAndTabList()
    {
        var partial = ReadView("_VentaWizardForm.cshtml");

        Assert.Contains("class=\"venta-wizard-tablist", partial);
        Assert.Contains("role=\"dialog\"", partial);
        Assert.Contains("aria-modal=\"true\"", partial);
        Assert.Contains("aria-labelledby=\"modal-documentacion-titulo\"", partial);
        Assert.Contains("id=\"modal-documentacion-titulo\"", partial);
    }

    [Fact]
    public void SharedScriptsMaintainModalFocusAndTabKeyboardNavigation()
    {
        var root = FindRepoRoot();
        var modalScript = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "venta-module.js"));
        var wizardScript = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "venta-page-wizard.js"));

        Assert.Contains("triggerElement.focus()", modalScript);
        Assert.Contains("event.key !== 'Tab'", modalScript);
        Assert.Contains("actualizarNumeracionPasos", wizardScript);
        Assert.Contains("event.key === 'ArrowRight'", wizardScript);
        Assert.Contains("event.key === 'Home'", wizardScript);
    }

    [Fact]
    public void SharedModalFocusableSelectorExcludesHiddenInputs()
    {
        // VENTA-MODAL-FOCUS-TRAP-01: input[type="hidden"] (p.ej. el antiforgery
        // token que el FormTagHelper agrega al final del <form>) no debe contarse
        // como focusable. Si se cuela, el trap calcula un "last" invisible que el
        // navegador nunca enfoca y Tab escapa del modal en vez de volver al primero.
        var root = FindRepoRoot();
        var modalScript = File.ReadAllText(Path.Combine(root, "wwwroot", "js", "venta-module.js"));

        Assert.Contains("input:not([disabled]):not([type=\"hidden\"])", modalScript);
    }

    private static string ReadView(string name) => File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", name));

    private static string ReadCotizacionView(string name) => File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", name));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
