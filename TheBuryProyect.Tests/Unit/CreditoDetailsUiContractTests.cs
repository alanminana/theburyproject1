using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class CreditoDetailsUiContractTests
{
    // ── Tests de vista (contrato de presencia de elementos) ──────────────────

    [Fact]
    public void DetailsView_ContieneBloqueTrazabilidadRestriccionCuotas()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("FuenteRestriccionCuotasSnap", view);
        Assert.Contains("CuotasMaximasPermitidas", view);
        Assert.Contains("MaxCuotasBaseSnap", view);
        Assert.Contains("data-credito-restriccion-cuotas", view);
    }

    [Fact]
    public void DetailsView_MuestraBadgeRestringidoPorProductoCuandoFuenteEsProducto()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("Restringido por producto", view);
        Assert.Contains("\"Producto\"", view);
    }

    [Fact]
    public void DetailsView_MuestraSinRestriccionPorProductoCuandoFuenteEsGlobal()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("Sin restricción por producto", view);
        Assert.Contains("\"Global\"", view);
    }

    [Fact]
    public void DetailsView_MuestraProductoIdRestrictivoSnap()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("ProductoIdRestrictivoSnap", view);
        Assert.Contains("snapshot", view);
    }

    [Fact]
    public void DetailsView_BloqueTrazabilidadEsCondicionalParaNullsSeguros()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        // El bloque debe estar guardado con una condición null-safe
        Assert.Contains("FuenteRestriccionCuotasSnap != null", view);
        Assert.Contains("CuotasMaximasPermitidas.HasValue", view);
        Assert.Contains("MaxCuotasBaseSnap.HasValue", view);
    }

    [Fact]
    public void DetailsView_MuestraMaxCuotasBaseVsEfectivoParaComparar()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "Details_tw.cshtml"));

        Assert.Contains("Máx. cuotas base", view);
        Assert.Contains("Máx. cuotas efectivo", view);
    }

    // ── Tests de contrato del ViewModel ─────────────────────────────────────

    [Fact]
    public void CreditoViewModel_ExponeCamposSnapDeFase95b_DefaultNull()
    {
        var vm = new CreditoViewModel();

        Assert.Null(vm.FuenteRestriccionCuotasSnap);
        Assert.Null(vm.ProductoIdRestrictivoSnap);
        Assert.Null(vm.MaxCuotasBaseSnap);
        Assert.Null(vm.CuotasMaximasPermitidas);
        Assert.Null(vm.CuotasMinimasPermitidas);
    }

    [Fact]
    public void CreditoViewModel_AceptaFuenteProducto()
    {
        var vm = new CreditoViewModel
        {
            FuenteRestriccionCuotasSnap = "Producto",
            ProductoIdRestrictivoSnap = 42,
            MaxCuotasBaseSnap = 24,
            CuotasMaximasPermitidas = 12,
        };

        Assert.Equal("Producto", vm.FuenteRestriccionCuotasSnap);
        Assert.Equal(42, vm.ProductoIdRestrictivoSnap);
        Assert.Equal(24, vm.MaxCuotasBaseSnap);
        Assert.Equal(12, vm.CuotasMaximasPermitidas);
    }

    [Fact]
    public void CreditoViewModel_AceptaFuenteGlobal()
    {
        var vm = new CreditoViewModel
        {
            FuenteRestriccionCuotasSnap = "Global",
            MaxCuotasBaseSnap = 36,
            CuotasMaximasPermitidas = 36,
        };

        Assert.Equal("Global", vm.FuenteRestriccionCuotasSnap);
        Assert.Null(vm.ProductoIdRestrictivoSnap);
        Assert.Equal(vm.MaxCuotasBaseSnap, vm.CuotasMaximasPermitidas);
    }

    [Fact]
    public void CreditoViewModel_CamposSnap_NoAlteranTotalesNiCuotasFinancieras()
    {
        var vm = new CreditoViewModel
        {
            MontoAprobado = 100_000,
            CantidadCuotas = 12,
            MontoCuota = 9_500,
            TotalAPagar = 114_000,
            SaldoPendiente = 114_000,
            // Snap — no deben afectar los campos financieros
            FuenteRestriccionCuotasSnap = "Producto",
            ProductoIdRestrictivoSnap = 7,
            MaxCuotasBaseSnap = 24,
            CuotasMaximasPermitidas = 12,
        };

        Assert.Equal(100_000, vm.MontoAprobado);
        Assert.Equal(12, vm.CantidadCuotas);
        Assert.Equal(9_500, vm.MontoCuota);
        Assert.Equal(114_000, vm.TotalAPagar);
        Assert.Equal(114_000, vm.SaldoPendiente);
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
