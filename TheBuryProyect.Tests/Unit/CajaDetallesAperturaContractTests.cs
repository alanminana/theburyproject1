using System.IO;
using Xunit;

namespace TheBuryProyect.Tests.Unit;

public class CajaDetallesAperturaContractTests
{
    private static string ReadView()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "Views", "Caja", "DetallesApertura_tw.cshtml");
        Assert.True(File.Exists(path), $"Vista no encontrada: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void DetallesApertura_ContieneTextoVentasEfectivas()
    {
        Assert.Contains("Ventas efectivas", ReadView());
    }

    [Fact]
    public void DetallesApertura_ContieneTextoSinImpactoEnCaja()
    {
        Assert.Contains("Sin impacto en caja", ReadView());
    }

    [Fact]
    public void DetallesApertura_ContieneRegistrosDeAuditoria()
    {
        Assert.Contains("Registros de auditoría", ReadView());
    }

    [Fact]
    public void DetallesApertura_ContieneOperacionesPendientes()
    {
        Assert.Contains("Operaciones pendientes", ReadView());
    }

    [Fact]
    public void DetallesApertura_UsaAgrupacionPorEstadoVenta()
    {
        var content = ReadView();
        Assert.Contains("ventasEfectivas", content);
        Assert.Contains("ventasPendientes", content);
        Assert.Contains("ventasAuditoria", content);
    }

    [Fact]
    public void DetallesApertura_ContadorDistingueEfectivasYSinImpacto()
    {
        var content = ReadView();
        Assert.Contains("venta efectiva", content);
        Assert.Contains("sin impacto inmediato", content);
    }

    [Fact]
    public void DetallesApertura_NoUsaHtmlRawEnSeccionVentas()
    {
        var content = ReadView();
        var start = content.IndexOf("Ventas vinculadas al turno");
        var end = content.IndexOf("Historial de movimientos");
        if (start >= 0 && end > start)
        {
            var ventasSection = content.Substring(start, end - start);
            Assert.DoesNotContain("Html.Raw", ventasSection);
        }
    }

    [Fact]
    public void DetallesApertura_BloquePendientesUsaBadgeAmber()
    {
        var content = ReadView();
        Assert.Contains("Sin ingreso inmediato", content);
    }

    [Fact]
    public void DetallesApertura_BloqueAuditoriaEsColapsable()
    {
        var content = ReadView();
        Assert.Contains("<details", content);
        Assert.Contains("<summary", content);
    }
}
