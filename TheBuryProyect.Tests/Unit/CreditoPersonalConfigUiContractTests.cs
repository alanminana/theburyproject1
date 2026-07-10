namespace TheBuryProject.Tests.Unit;

public class CreditoPersonalConfigUiContractTests
{
    private static string LeerVista() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "ConfiguracionPago", "CreditoPersonal_tw.cshtml"));

    [Fact]
    public void Vista_ExponeLimitesCanonicosPorPuntaje()
    {
        var view = LeerVista();

        Assert.Contains("LimitesPorPuntaje", view);
        Assert.Contains("PuntajeCliente 0-5", view);
        Assert.Contains("PuntajesCreditoLimite", view);
        Assert.Contains("Limites reales por puntaje", view);
    }

    [Fact]
    public void Vista_NoExponeConfiguracionLegacyDeMontos0A10()
    {
        var view = LeerVista();

        Assert.DoesNotContain("MontosPorPuntaje", view);
        Assert.DoesNotContain("MontoPorPuntajeCreditoViewModel", view);
        Assert.DoesNotContain("0-10", view);
        Assert.DoesNotContain("Monto disponible por puntaje", view);
    }

    [Fact]
    public void Vista_NoExponeScoringLegacyDeAprobacion()
    {
        var view = LeerVista();

        Assert.DoesNotContain("ScoringThresholds", view);
        Assert.DoesNotContain("PuntajeMinimoParaAprobacion", view);
        Assert.DoesNotContain("PuntajeMinimoParaAnalisis", view);
        Assert.DoesNotContain("MontoRequiereGarante", view);
        Assert.DoesNotContain("Configuracion avanzada del motor de scoring", view);
    }

    [Fact]
    public void Vista_NoUsaHandlersInline()
    {
        var view = LeerVista();

        Assert.DoesNotContain("onclick=", view);
        Assert.DoesNotContain("openCreditoModal", view);
        Assert.DoesNotContain("m-historial", view);
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
