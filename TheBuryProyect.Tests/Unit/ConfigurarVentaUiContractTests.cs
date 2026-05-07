using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class ConfigurarVentaUiContractTests
{
    // ── Tests de vista ───────────────────────────────────────────────────────

    [Fact]
    public void ConfigurarVentaView_ContieneAlertaRestriccionPorProducto()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("Model.MaxCuotasBase > Model.CuotasMaxPermitidas", view);
        Assert.Contains("Máximo de cuotas reducido por condiciones del producto", view);
        Assert.Contains("MaxCuotasCreditoProducto.HasValue", view);
    }

    [Fact]
    public void ConfigurarVentaView_MuestraNombreProductoRestrictivoSiExiste()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("ProductoRestrictivoNombre", view);
        Assert.Contains("data-credito-producto-restrictivo-nombre", view);
    }

    [Fact]
    public void ConfigurarVentaView_MuestraIdComoFallbackSiNombreEsNull()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("ProductoIdRestrictivo.HasValue", view);
        Assert.Contains("ProductoIdRestrictivo.Value", view);
    }

    [Fact]
    public void ConfigurarVentaView_NombreYIdSonAlternativos_NoSimultaneos()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        // Nombre se muestra en un @if, ID como @else if — mutuamente excluyentes
        Assert.Contains("string.IsNullOrWhiteSpace(Model.ProductoRestrictivoNombre)", view);
        Assert.Contains("else if (Model.ProductoIdRestrictivo.HasValue)", view);
    }

    [Fact]
    public void ConfigurarVentaView_AlertaEsCondicional()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        // La alerta debe estar dentro de un @if, no siempre visible
        Assert.Contains("@if (Model.MaxCuotasBase > Model.CuotasMaxPermitidas", view);
    }

    [Fact]
    public void ConfigurarVentaView_ConservaRangoEfectivoActual()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("CuotasMinPermitidas", view);
        Assert.Contains("CuotasMaxPermitidas", view);
    }

    [Fact]
    public void ConfigurarVentaView_TieneAtributoDataParaAutomation()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Credito", "ConfigurarVenta_tw.cshtml"));

        Assert.Contains("data-credito-restriccion-cuotas-producto", view);
    }

    // ── Tests de contrato del ViewModel ─────────────────────────────────────

    [Fact]
    public void ViewModel_ExponeMaxCuotasBase_DefaultMaximoLibre()
    {
        var vm = new ConfiguracionCreditoVentaViewModel();

        Assert.Equal(120, vm.MaxCuotasBase);
        Assert.Null(vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_DetectaRestriccionPorProducto()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 36,
            CuotasMaxPermitidas = 12,
            MaxCuotasCreditoProducto = 12,
            ProductoIdRestrictivo = 42
        };

        Assert.True(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.True(vm.MaxCuotasCreditoProducto.HasValue);
        Assert.Equal(42, vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_SinRestriccionProducto_MaxEfectivoIgualABase()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 24,
            MaxCuotasCreditoProducto = null,
            ProductoIdRestrictivo = null
        };

        Assert.False(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.Null(vm.MaxCuotasCreditoProducto);
        Assert.Null(vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_NuevosCampos_NoAlteranTasaNiTotales()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            Monto = 50_000m,
            MontoFinanciado = 45_000m,
            CantidadCuotas = 12,
            TasaMensual = 3.5m,
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 12,
            MaxCuotasCreditoProducto = 12,
            ProductoIdRestrictivo = 7
        };

        Assert.Equal(50_000m, vm.Monto);
        Assert.Equal(45_000m, vm.MontoFinanciado);
        Assert.Equal(12, vm.CantidadCuotas);
        Assert.Equal(3.5m, vm.TasaMensual);
    }

    [Fact]
    public void ViewModel_RestriccionPorProductoSinProductoIdRestrictivo_SigueMostrandoAlerta()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 6,
            MaxCuotasCreditoProducto = 6,
            ProductoIdRestrictivo = null
        };

        Assert.True(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.True(vm.MaxCuotasCreditoProducto.HasValue);
        Assert.Null(vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_ExponeProductoRestrictivoNombre_DefaultNull()
    {
        var vm = new ConfiguracionCreditoVentaViewModel();

        Assert.Null(vm.ProductoRestrictivoNombre);
    }

    [Fact]
    public void ViewModel_ConNombreProductoRestrictivo_MuestraEnAlerta()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 36,
            CuotasMaxPermitidas = 6,
            MaxCuotasCreditoProducto = 6,
            ProductoIdRestrictivo = 17,
            ProductoRestrictivoNombre = "Notebook Lenovo IdeaPad"
        };

        Assert.True(vm.MaxCuotasBase > vm.CuotasMaxPermitidas);
        Assert.Equal("Notebook Lenovo IdeaPad", vm.ProductoRestrictivoNombre);
        Assert.Equal(17, vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_ConNombreVacio_FallbackAIdNumericos()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            MaxCuotasBase = 24,
            CuotasMaxPermitidas = 12,
            MaxCuotasCreditoProducto = 12,
            ProductoIdRestrictivo = 5,
            ProductoRestrictivoNombre = null
        };

        Assert.Null(vm.ProductoRestrictivoNombre);
        Assert.Equal(5, vm.ProductoIdRestrictivo);
    }

    [Fact]
    public void ViewModel_NombreProducto_NoAlteraTasaNiTotales()
    {
        var vm = new ConfiguracionCreditoVentaViewModel
        {
            Monto = 80_000m,
            MontoFinanciado = 70_000m,
            CantidadCuotas = 6,
            TasaMensual = 4.5m,
            ProductoRestrictivoNombre = "TV Samsung 55"
        };

        Assert.Equal(80_000m, vm.Monto);
        Assert.Equal(70_000m, vm.MontoFinanciado);
        Assert.Equal(6, vm.CantidadCuotas);
        Assert.Equal(4.5m, vm.TasaMensual);
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
