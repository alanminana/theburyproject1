namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FE-D-003: el menú lateral off-canvas (mobile) y el panel de tickets cerrados se ocultaban solo con transform/clase y sus
/// controles seguían recibiendo foco con Tab. Ahora están `inert` mientras están cerrados. El comportamiento real se validó en
/// un navegador (Chrome): Tab no entra en paneles cerrados y sí llega al menú abierto y al menú de escritorio.
/// </summary>
public class PanelesCerradosInertTests
{
    [Fact]
    public void Layout_SidebarMobileCerrado_EsInert_YSoloEnMobile()
    {
        var js = File.ReadAllText(ArchivoRepo("wwwroot", "js", "layout.js"));

        Assert.Contains("sidebar.inert = mobileMq.matches && !sidebar.classList.contains('open')", js);
        Assert.Contains("(max-width: 1023px)", js);
    }

    [Fact]
    public void PanelDeTickets_ArrancaInert_ySeAlternaAlAbrirYCerrar()
    {
        var vista = File.ReadAllText(ArchivoRepo("Views", "Shared", "_TicketPanel.cshtml"));
        var js = File.ReadAllText(ArchivoRepo("wwwroot", "js", "ticket-panel.js"));

        Assert.Matches(@"aria-hidden=""true""\s+inert\s+data-can-edit", vista);
        Assert.Contains("panel.inert = false;", js);
        Assert.Contains("panel.inert = true;", js);
    }

    private static string ArchivoRepo(params string[] partes)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(partes).ToArray());
    }
}
