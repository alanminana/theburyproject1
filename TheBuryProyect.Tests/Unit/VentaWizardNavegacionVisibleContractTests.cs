using System.Text.RegularExpressions;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Decisión del dueño (PR #73): la barra superior de pasos del wizard de Venta permanece OCULTA y el usuario
/// navega con controles visibles. Estos contratos fijan las dos mitades: la barra sigue oculta (el DOM se conserva
/// porque el JS sincroniza estado sobre los tabs) y existen los controles que la reemplazan, sin los cuales
/// Cotizar -> Cliente (Create) y volver a un paso completado quedaban inaccesibles.
/// </summary>
public class VentaWizardNavegacionVisibleContractTests
{
    [Fact]
    public void BarraDePasos_PermaneceOcultaPorCss_PeroConservaSuDom()
    {
        var css = Read("wwwroot", "css", "venta-page-wizard.css");
        Assert.Matches(
            new Regex(@":is\(#venta-create-page, #venta-edit-page\) \.vm-hero__steps \{\s*display: none !important;\s*\}", RegexOptions.CultureInvariant),
            css);

        var parcial = Read("Views", "Venta", "_VentaWizardForm.cshtml");
        Assert.Contains("class=\"vm-hero__steps", parcial);
        Assert.Contains("role=\"tablist\"", parcial);
        Assert.Contains("id=\"step-btn-cliente\"", parcial);
    }

    [Fact]
    public void PasoAnterior_ExisteEnElHero_ComienzaOcultoYSeApagaEnElPrimerPaso()
    {
        var parcial = Read("Views", "Venta", "_VentaWizardForm.cshtml");
        var boton = Regex.Match(parcial, @"<button[^>]*data-wizard-back[^>]*>", RegexOptions.Singleline);
        Assert.True(boton.Success, "Falta el botón 'Paso anterior' (data-wizard-back).");
        Assert.Contains("hidden", boton.Value);
        Assert.Contains("Paso anterior", parcial);

        var css = Read("wwwroot", "css", "venta-page-wizard.css");
        Assert.Contains("[data-wizard-back][hidden]", css);

        var js = Read("wwwroot", "js", "venta-page-wizard.js");
        Assert.Contains("function retroceder()", js);
        Assert.Contains("querySelectorAll('[data-wizard-back]')", js);
        // El primer paso visible (Cotizar en Create, Cliente en Edit) no tiene "anterior".
        Assert.Contains("const esPrimerPaso = pasosVisibles()[0]?.getAttribute('data-step') === step;", js);
    }

    [Fact]
    public void SiguienteDeCotizar_SoloExisteEnCreate_DentroDelPanelCotizar()
    {
        var parcial = Read("Views", "Venta", "_VentaWizardForm.cshtml");
        var bloqueCotizar = Regex.Match(
            parcial,
            @"@if \(pasoCotizar\)\s*\{\s*<section id=""step-panel-cotizar"".*?</section>\s*\}",
            RegexOptions.Singleline);
        Assert.True(bloqueCotizar.Success, "No se encontró el panel Cotizar condicionado a Create.");
        Assert.Contains("data-wizard-next", bloqueCotizar.Value);
        Assert.Contains("Siguiente: Cliente", bloqueCotizar.Value);
        Assert.Single(Regex.Matches(parcial, "data-wizard-next"));
    }

    [Fact]
    public void ElFoco_AcompañaAlPanel_PorqueLosTabsOcultosNoPuedenRecibirlo()
    {
        var js = Read("wwwroot", "js", "venta-page-wizard.js");
        Assert.Contains("function enfocarPaso(step)", js);
        Assert.Contains("tab.offsetParent !== null", js);
        Assert.Contains("setAttribute('tabindex', '-1')", js);
        // avanzar() y retroceder() pasan por enfocarPaso en lugar de enfocar el tab oculto.
        Assert.DoesNotContain("siguiente.focus({ preventScroll: true })", js);
        Assert.Equal(2, Regex.Matches(js, @"enfocarPaso\(step\);").Count);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

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
