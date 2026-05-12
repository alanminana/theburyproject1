namespace TheBuryProject.Tests.Unit;

public sealed class ConfiguracionPagoGlobalAdminViewTests
{
    [Fact]
    public void MediosPagoView_ContieneTextosGlobalesYNoMencionaFlujoExcluido()
    {
        var viewPath = FindRepoRoot()
            .GetDirectories("Views", SearchOption.TopDirectoryOnly)
            .Single()
            .FullName;
        var contenido = File.ReadAllText(Path.Combine(viewPath, "ConfiguracionPago", "MediosPago_tw.cshtml"));

        Assert.Contains("Configuración global de pagos", contenido);
        Assert.Contains("El ajuste se aplica una sola vez sobre el total", contenido);
        Assert.Contains("Ajuste positivo = recargo", contenido);
        Assert.Contains("ajuste negativo = descuento", contenido);
        Assert.Contains("Cuotas indica la cantidad total de pagos", contenido);
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

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
