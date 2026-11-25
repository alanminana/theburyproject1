namespace TheBuryProject.Tests.Unit;

/// <summary>
/// A6: "Ver alertas" del Dashboard solo se ofrece si el destino es abrible por el rol. La ronda de interfaz sobre Docker + Caddy
/// encontró que se decidía con cotizaciones.view y llevaba a "Acceso denegado" a gerente, cajero, técnico y contador.
/// Catálogo exige productos.view (pestaña Alertas: stock.viewalerts) y la pantalla standalone, stock.viewalerts.
/// </summary>
public class DashboardVerAlertasUiContractTests
{
    private static string LeerDashboard()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "Views", "Dashboard", "Index.cshtml"));
    }

    [Fact]
    public void VerAlertas_SeGatePorElPermisoDelDestino()
    {
        var html = LeerDashboard();

        Assert.Contains("User.TienePermiso(\"stock\", \"viewalerts\")", html);
        Assert.Contains("puedeVerAlertasStock && User.TienePermiso(\"productos\", \"view\")", html);
        Assert.Contains("@if (puedeVerAlertasStock)", html);
        Assert.DoesNotContain("alertasEnCatalogo = User.TienePermiso(\"cotizaciones\", \"view\")", html);
    }
}
