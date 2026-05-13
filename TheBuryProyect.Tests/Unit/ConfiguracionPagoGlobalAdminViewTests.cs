namespace TheBuryProject.Tests.Unit;

public sealed class ConfiguracionPagoGlobalAdminViewTests
{
    [Fact]
    public void MediosPagoView_ContieneTextosGlobalesFormularioYNoMencionaFlujoExcluido()
    {
        var viewPath = FindRepoRoot()
            .GetDirectories("Views", SearchOption.TopDirectoryOnly)
            .Single()
            .FullName;
        var contenido = File.ReadAllText(Path.Combine(viewPath, "ConfiguracionPago", "MediosPago_tw.cshtml"));

        Assert.Contains("Configuracion global de pagos", contenido);
        Assert.Contains("El ajuste se aplica una sola vez sobre el total", contenido);
        Assert.Contains("Ajuste positivo = recargo", contenido);
        Assert.Contains("ajuste negativo = descuento", contenido);
        Assert.Contains("Cuotas indica la cantidad total de pagos", contenido);
        Assert.Contains("CrearPlanGlobal", contenido);
        Assert.Contains("EditarPlanGlobal", contenido);
        Assert.Contains("CambiarEstadoPlanGlobal", contenido);
        Assert.Contains("Plan general del medio", contenido);
        Assert.DoesNotContain("pago por producto", contenido, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("condiciones por producto", contenido, StringComparison.OrdinalIgnoreCase);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
