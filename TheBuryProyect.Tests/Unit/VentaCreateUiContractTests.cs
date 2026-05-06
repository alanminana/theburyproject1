namespace TheBuryProject.Tests.Unit;

public class VentaCreateUiContractTests
{
    [Fact]
    public void CreateView_PosteaCamposSnapshotDeTarjeta()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Venta", "Create_tw.cshtml"));

        Assert.Contains("name=\"DatosTarjeta.ConfiguracionTarjetaId\"", view);
        Assert.Contains("name=\"DatosTarjeta.NombreTarjeta\"", view);
        Assert.Contains("name=\"DatosTarjeta.TipoTarjeta\"", view);
        Assert.Contains("id=\"hdn-tarjeta-nombre\"", view);
        Assert.Contains("id=\"hdn-tarjeta-tipo\"", view);
    }

    [Fact]
    public void VentaCreateJs_PueblaSnapshotDeTarjetaDesdeTarjetaActiva()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("hdnTarjetaNombre.value = info.nombre", script);
        Assert.Contains("hdnTarjetaTipo.value = info.tipo", script);
        Assert.Contains("await cargarTarjetasActivas()", script);
        Assert.Contains("ventaForm.requestSubmit()", script);
    }

    [Fact]
    public void VentaCreateJs_MantienePreviewRecargoDebitoComoMontoYPorcentaje()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "venta-create.js"));

        Assert.Contains("recargoDebitoPreview", script);
        Assert.Contains("function formatRecargoDebitoPreview", script);
        Assert.Contains("formatCurrency(recargo.monto)", script);
        Assert.Contains("formatPercent(recargo.porcentaje)", script);
        Assert.Contains("if (recargoDebitoPreview?.monto > 0)", script);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}
