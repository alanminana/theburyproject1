namespace TheBuryProject.Tests.Unit;

// -------------------------------------------------------------------------
// PUN-ML8: UI administrativa de ConfiguracionPunitorio (pestaña "s7 — Punitorios
// por mora" dentro de Views/ConfiguracionPago/CreditoPersonal_tw.cshtml).
//
// Contrato congelado del spec: la UI administra versiones, no parámetros
// mutables. Ninguna versión existente se edita, ninguna autorización se
// confía al navegador y ninguna fórmula financiera se duplica en JavaScript.
// -------------------------------------------------------------------------

public class ConfiguracionPunitorioUiContractTests
{
    private static string LeerVista() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "ConfiguracionPago", "CreditoPersonal_tw.cshtml"));

    [Fact]
    public void Vista_ExponeLaPestanaDePunitorios()
    {
        var view = LeerVista();

        Assert.Contains("data-target=\"s7\"", view);
        Assert.Contains("id=\"s7\"", view);
        Assert.Contains("Punitorios por mora", view);
    }

    [Fact]
    public void Vista_PestanaDePunitoriosEstaGateadaPorViewpunitorio()
    {
        var view = LeerVista();

        Assert.Contains("User.TienePermiso(\"configuracion\", \"viewpunitorio\")", view);
        Assert.Contains("User.TienePermiso(\"configuracion\", \"managepunitorio\")", view);
    }

    [Fact]
    public void Vista_NoExponeCredito_TasaInteresParaPunitorios()
    {
        var view = LeerVista();

        Assert.DoesNotContain("Credito.TasaInteres", view);
        Assert.DoesNotContain("Credito!.TasaInteres", view);
    }

    [Fact]
    public void Vista_NoExponeConfiguracionMora_TasaMoraBaseComoSiFueraPunitorio()
    {
        var view = LeerVista();

        Assert.DoesNotContain("TasaMoraBase", view);
    }

    [Fact]
    public void Vista_NoLeeCuota_MontoPunitorioLegacy()
    {
        var view = LeerVista();

        Assert.DoesNotContain("MontoPunitorio", view);
    }

    [Fact]
    public void Vista_NoExponeAutorizadoParaRetroactivoComoCampoDeFormulario()
    {
        var view = LeerVista();

        // No debe existir un input/campo bindable con ese nombre; la única mención
        // aceptable sería en un comentario, y ni siquiera eso está presente hoy.
        Assert.DoesNotContain("AutorizadoParaRetroactivo", view);
        Assert.DoesNotContain("name=\"AplicacionRetroactiva\"", view);
    }

    [Fact]
    public void Vista_ProrrateoDiarioNoEsUnInputEditable()
    {
        var view = LeerVista();

        // Se muestra como texto fijo ("Prorrateo diario: Si"), nunca como
        // <input ... ProrrateoDiario ...> bindable.
        Assert.DoesNotContain("name=\"ProrrateoDiario\"", view);
        Assert.DoesNotContain("for=\"ProrrateoDiario\"", view);
        Assert.DoesNotContain("CrearForm.ProrrateoDiario", view);
    }

    [Fact]
    public void Vista_HistorialDePunitorios_NoOfreceEditarNiEliminar()
    {
        var view = LeerVista();

        Assert.DoesNotContain("asp-action=\"EditarVersionPunitorio\"", view);
        Assert.DoesNotContain("asp-action=\"EliminarVersionPunitorio\"", view);
        Assert.DoesNotContain("EditarConfiguracionPunitorio", view);
        Assert.DoesNotContain("EliminarConfiguracionPunitorio", view);
    }

    [Fact]
    public void Vista_NoReutilizaElIdBaneadoMHistorial()
    {
        // Ya prohibido por CreditoPersonalConfigUiContractTests.Vista_NoUsaHandlersInline
        // para el resto de la página; se reafirma acá porque la sección nueva agrega
        // su propio historial y sería un nombre natural (pero incorrecto) a reusar.
        var view = LeerVista();

        Assert.DoesNotContain("m-historial", view);
    }

    [Fact]
    public void Vista_NoUsaHandlersInlineEnLaSeccionNueva()
    {
        var view = LeerVista();

        Assert.DoesNotContain("onclick=", view);
    }

    [Fact]
    public void Vista_FormularioCrearVersionPosteaAUnaAccionDistintaDeCreditoPersonal()
    {
        var view = LeerVista();

        Assert.Contains("asp-action=\"CrearVersionPunitorio\"", view);
        Assert.Contains("id=\"punitorio-form\"", view);
    }

    [Fact]
    public void Vista_ModalRetroactivoTieneRolDialogoYAriaModal()
    {
        var view = LeerVista();

        Assert.Contains("id=\"m-punitorio-retro\"", view);
        Assert.Contains("role=\"dialog\"", view);
        Assert.Contains("aria-modal=\"true\"", view);
        Assert.Contains("aria-labelledby=\"punRetroTitulo\"", view);
    }

    [Fact]
    public void Vista_PreviewDePunitoriosUsaElEndpointServerSide_NoFormulaPropia()
    {
        var view = LeerVista();

        Assert.Contains("PreviewPunitorio", view);
        // La fórmula (saldoBase * porcentaje/100 * dias/periodoDias) no debe aparecer en JS.
        Assert.DoesNotContain("porcentaje / 100 * dias", view);
        Assert.DoesNotContain("porcentaje/100*dias", view);
    }

    [Fact]
    public void Vista_HistorialMarcaVigenteFuturaInactivaYRetroactiva()
    {
        var view = LeerVista();

        Assert.Contains("vigente", view);
        Assert.Contains("futura", view);
        Assert.Contains("inactiva", view);
        Assert.Contains("retroactiva", view);
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
