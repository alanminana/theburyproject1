using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para ReporteService.CalcularMargenPorcentaje.
///
/// Documenta el contrato aritmético del cálculo de margen extraído de
/// GenerarReporteVentasAsync y GenerarReporteMargenesAsync.
///
/// No requiere DB ni infraestructura — función pura.
/// </summary>
public class ReporteServiceMargenTests
{
    // ---------------------------------------------------------------------------
    // base > 0, ganancia > 0 → margen positivo
    // ---------------------------------------------------------------------------

    [Fact]
    public void BasePositiva_GananciaPositiva_DevuelveMargenPositivo()
    {
        var resultado = ReporteService.CalcularMargenPorcentaje(ganancia: 30m, @base: 100m);

        Assert.Equal(30m, resultado);
    }

    // ---------------------------------------------------------------------------
    // base > 0, ganancia < 0 → margen negativo
    // ---------------------------------------------------------------------------

    [Fact]
    public void BasePositiva_GananciaNegativa_DevuelveMargenNegativo()
    {
        var resultado = ReporteService.CalcularMargenPorcentaje(ganancia: -20m, @base: 100m);

        Assert.Equal(-20m, resultado);
    }

    // ---------------------------------------------------------------------------
    // base = 0 → 0 (evita división por cero)
    // ---------------------------------------------------------------------------

    [Fact]
    public void BaseCero_DevuelveCero()
    {
        var resultado = ReporteService.CalcularMargenPorcentaje(ganancia: 50m, @base: 0m);

        Assert.Equal(0m, resultado);
    }

    // ---------------------------------------------------------------------------
    // ganancia = 0 → 0
    // ---------------------------------------------------------------------------

    [Fact]
    public void GananciaCero_DevuelveCero()
    {
        var resultado = ReporteService.CalcularMargenPorcentaje(ganancia: 0m, @base: 100m);

        Assert.Equal(0m, resultado);
    }

    // ---------------------------------------------------------------------------
    // ganancia = base → 100%
    // ---------------------------------------------------------------------------

    [Fact]
    public void GananciaIgualABase_Devuelve100()
    {
        var resultado = ReporteService.CalcularMargenPorcentaje(ganancia: 200m, @base: 200m);

        Assert.Equal(100m, resultado);
    }

    // ---------------------------------------------------------------------------
    // ganancia > base → margen > 100% (costo negativo o descuento mayor al precio)
    // ---------------------------------------------------------------------------

    [Fact]
    public void GananciaMayorQueBase_DevuelveMasDe100()
    {
        var resultado = ReporteService.CalcularMargenPorcentaje(ganancia: 150m, @base: 100m);

        Assert.True(resultado > 100m);
        Assert.Equal(150m, resultado);
    }
}
