namespace TheBuryProject.Tests.Unit;

/// <summary>
/// FASE 12B: contrato de UI de la pantalla canónica de configuración de mora
/// (Views/Mora/ConfiguracionExpandida.cshtml). Verifica que el formulario exponga
/// el flag ImpactarScorePorMora con su copy y ayuda de auditoría, que no se mezcle
/// con CambiarEstadoCuotaAuto y que envíe al action canónico.
/// </summary>
public class ConfiguracionMoraUiContractTests
{
    private static string LeerVista() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Mora", "ConfiguracionExpandida.cshtml"));

    [Fact]
    public void Vista_ExisteYUsaViewModelExpandido()
    {
        var view = LeerVista();

        Assert.Contains("ConfiguracionMoraExpandidaViewModel", view);
        Assert.Contains("asp-action=\"GuardarConfiguracionExpandida\"", view);
    }

    [Fact]
    public void Vista_ExponeFlagImpactarScorePorMora_ConCopyYAyudaDeAuditoria()
    {
        var view = LeerVista();

        Assert.Contains("asp-for=\"ImpactarScorePorMora\"", view);
        Assert.Contains("Impactar puntaje por mora después de días de gracia", view);
        Assert.Contains("Los cambios se auditan en el historial de puntaje", view);
    }

    [Fact]
    public void Vista_NoMezclaScorePorMoraConCambiarEstadoCuotaAuto()
    {
        var view = LeerVista();

        // Ambos son controles distintos: el flag de score no debe reutilizar el
        // binding de CambiarEstadoCuotaAuto.
        Assert.Contains("asp-for=\"CambiarEstadoCuotaAuto\"", view);
        Assert.Contains("asp-for=\"ImpactarScorePorMora\"", view);
        Assert.NotEqual(
            view.IndexOf("asp-for=\"CambiarEstadoCuotaAuto\"", StringComparison.Ordinal),
            view.IndexOf("asp-for=\"ImpactarScorePorMora\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Vista_ExponeCamposDeScorePorMora()
    {
        var view = LeerVista();

        Assert.Contains("asp-for=\"PuntosRestarPorCuotaVencida\"", view);
        Assert.Contains("asp-for=\"PuntosRestarPorDiaMora\"", view);
        Assert.Contains("asp-for=\"PuntosMaximosARestar\"", view);
        Assert.Contains("asp-for=\"RecuperarScoreAlPagar\"", view);
        Assert.Contains("asp-for=\"PorcentajeRecuperacionScore\"", view);
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
