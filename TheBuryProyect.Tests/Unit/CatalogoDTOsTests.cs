using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para las propiedades calculadas de CatalogoDTOs.
/// FilaCatalogo: SinPrecio, StockCritico, Inactivo, UltimoCambioPuedeRevertir.
/// FilaSimulacionPrecio: Diferencia, DiferenciaPorcentaje, EsAumento.
/// ResultadoCatalogo: ProductosActivos, ProductosStockCritico, ProductosSinPrecio.
/// ResultadoSimulacionPrecios: TotalProductos, ProductosConAumento, ProductosConDescuento, PorcentajePromedio.
/// </summary>
public class CatalogoDTOsTests
{
    // =========================================================================
    // FilaCatalogo — SinPrecio
    // =========================================================================

    [Fact]
    public void FilaCatalogo_SinPrecio_PrecioActualCero_RetornaTrue()
    {
        var fila = new FilaCatalogo { PrecioActual = 0m };
        Assert.True(fila.SinPrecio);
    }

    [Fact]
    public void FilaCatalogo_SinPrecio_PrecioNegativo_RetornaTrue()
    {
        var fila = new FilaCatalogo { PrecioActual = -1m };
        Assert.True(fila.SinPrecio);
    }

    [Fact]
    public void FilaCatalogo_SinPrecio_PrecioPositivo_RetornaFalse()
    {
        var fila = new FilaCatalogo { PrecioActual = 100m };
        Assert.False(fila.SinPrecio);
    }

    // =========================================================================
    // FilaCatalogo — StockCritico
    // =========================================================================

    [Fact]
    public void FilaCatalogo_StockCritico_StockMenorAlMinimo_RetornaTrue()
    {
        var fila = new FilaCatalogo { StockActual = 2m, StockMinimo = 5m };
        Assert.True(fila.StockCritico);
    }

    [Fact]
    public void FilaCatalogo_StockCritico_StockIgualAlMinimo_RetornaTrue()
    {
        var fila = new FilaCatalogo { StockActual = 5m, StockMinimo = 5m };
        Assert.True(fila.StockCritico);
    }

    [Fact]
    public void FilaCatalogo_StockCritico_StockMayorAlMinimo_RetornaFalse()
    {
        var fila = new FilaCatalogo { StockActual = 10m, StockMinimo = 5m };
        Assert.False(fila.StockCritico);
    }

    // =========================================================================
    // FilaCatalogo — Inactivo
    // =========================================================================

    [Fact]
    public void FilaCatalogo_Inactivo_ActivoFalse_RetornaTrue()
    {
        var fila = new FilaCatalogo { Activo = false };
        Assert.True(fila.Inactivo);
    }

    [Fact]
    public void FilaCatalogo_Inactivo_ActivoTrue_RetornaFalse()
    {
        var fila = new FilaCatalogo { Activo = true };
        Assert.False(fila.Inactivo);
    }

    // =========================================================================
    // FilaCatalogo — UltimoCambioPuedeRevertir
    // =========================================================================

    [Fact]
    public void FilaCatalogo_UltimoCambioPuedeRevertir_TodosOk_RetornaTrue()
    {
        var fila = new FilaCatalogo
        {
            UltimoCambioEventoId = 1,
            UltimoCambioRevertido = false,
            UltimoCambioEsReversion = false
        };
        Assert.True(fila.UltimoCambioPuedeRevertir);
    }

    [Fact]
    public void FilaCatalogo_UltimoCambioPuedeRevertir_SinEventoId_RetornaFalse()
    {
        var fila = new FilaCatalogo
        {
            UltimoCambioEventoId = null,
            UltimoCambioRevertido = false,
            UltimoCambioEsReversion = false
        };
        Assert.False(fila.UltimoCambioPuedeRevertir);
    }

    [Fact]
    public void FilaCatalogo_UltimoCambioPuedeRevertir_YaRevertido_RetornaFalse()
    {
        var fila = new FilaCatalogo
        {
            UltimoCambioEventoId = 1,
            UltimoCambioRevertido = true,
            UltimoCambioEsReversion = false
        };
        Assert.False(fila.UltimoCambioPuedeRevertir);
    }

    [Fact]
    public void FilaCatalogo_UltimoCambioPuedeRevertir_EsReversion_RetornaFalse()
    {
        var fila = new FilaCatalogo
        {
            UltimoCambioEventoId = 1,
            UltimoCambioRevertido = false,
            UltimoCambioEsReversion = true
        };
        Assert.False(fila.UltimoCambioPuedeRevertir);
    }

    // =========================================================================
    // FilaSimulacionPrecio — Diferencia
    // =========================================================================

    [Fact]
    public void FilaSimulacionPrecio_Diferencia_Aumento_RetornaPositivo()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 100m, PrecioNuevo = 120m };
        Assert.Equal(20m, fila.Diferencia);
    }

    [Fact]
    public void FilaSimulacionPrecio_Diferencia_Descuento_RetornaNegativo()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 100m, PrecioNuevo = 80m };
        Assert.Equal(-20m, fila.Diferencia);
    }

    [Fact]
    public void FilaSimulacionPrecio_Diferencia_SinCambio_RetornaCero()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 100m, PrecioNuevo = 100m };
        Assert.Equal(0m, fila.Diferencia);
    }

    // =========================================================================
    // FilaSimulacionPrecio — DiferenciaPorcentaje
    // =========================================================================

    [Fact]
    public void FilaSimulacionPrecio_DiferenciaPorcentaje_Aumento_CalculaCorrecto()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 100m, PrecioNuevo = 150m };
        Assert.Equal(50m, fila.DiferenciaPorcentaje);
    }

    [Fact]
    public void FilaSimulacionPrecio_DiferenciaPorcentaje_Descuento_RetornaNegativo()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 200m, PrecioNuevo = 100m };
        Assert.Equal(-50m, fila.DiferenciaPorcentaje);
    }

    [Fact]
    public void FilaSimulacionPrecio_DiferenciaPorcentaje_PrecioActualCero_RetornaCero()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 0m, PrecioNuevo = 100m };
        Assert.Equal(0m, fila.DiferenciaPorcentaje);
    }

    [Fact]
    public void FilaSimulacionPrecio_DiferenciaPorcentaje_RedondeaADosDecimales()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 300m, PrecioNuevo = 301m };
        Assert.Equal(0.33m, fila.DiferenciaPorcentaje);
    }

    // =========================================================================
    // FilaSimulacionPrecio — EsAumento
    // =========================================================================

    [Fact]
    public void FilaSimulacionPrecio_EsAumento_DiferenciaPositiva_RetornaTrue()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 100m, PrecioNuevo = 110m };
        Assert.True(fila.EsAumento);
    }

    [Fact]
    public void FilaSimulacionPrecio_EsAumento_DiferenciaNegativa_RetornaFalse()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 100m, PrecioNuevo = 90m };
        Assert.False(fila.EsAumento);
    }

    [Fact]
    public void FilaSimulacionPrecio_EsAumento_SinCambio_RetornaFalse()
    {
        var fila = new FilaSimulacionPrecio { PrecioActual = 100m, PrecioNuevo = 100m };
        Assert.False(fila.EsAumento);
    }

    // =========================================================================
    // ResultadoCatalogo — métricas calculadas
    // =========================================================================

    [Fact]
    public void ResultadoCatalogo_ProductosActivos_ContaSoloActivos()
    {
        var resultado = new ResultadoCatalogo
        {
            Filas = new List<FilaCatalogo>
            {
                new() { Activo = true },
                new() { Activo = true },
                new() { Activo = false }
            }
        };
        Assert.Equal(2, resultado.ProductosActivos);
    }

    [Fact]
    public void ResultadoCatalogo_ProductosStockCritico_ContaStockCritico()
    {
        var resultado = new ResultadoCatalogo
        {
            Filas = new List<FilaCatalogo>
            {
                new() { StockActual = 1m, StockMinimo = 5m },  // crítico
                new() { StockActual = 5m, StockMinimo = 5m },  // crítico (igual)
                new() { StockActual = 10m, StockMinimo = 5m }  // ok
            }
        };
        Assert.Equal(2, resultado.ProductosStockCritico);
    }

    [Fact]
    public void ResultadoCatalogo_ProductosSinPrecio_ContaSinPrecio()
    {
        var resultado = new ResultadoCatalogo
        {
            Filas = new List<FilaCatalogo>
            {
                new() { PrecioActual = 0m },   // sin precio
                new() { PrecioActual = 100m },  // con precio
                new() { PrecioActual = -1m }    // sin precio
            }
        };
        Assert.Equal(2, resultado.ProductosSinPrecio);
    }

    [Fact]
    public void ResultadoCatalogo_FilasVacias_TodasMetricasCero()
    {
        var resultado = new ResultadoCatalogo { Filas = new List<FilaCatalogo>() };
        Assert.Equal(0, resultado.ProductosActivos);
        Assert.Equal(0, resultado.ProductosStockCritico);
        Assert.Equal(0, resultado.ProductosSinPrecio);
    }

    // =========================================================================
    // ResultadoSimulacionPrecios — métricas calculadas
    // =========================================================================

    [Fact]
    public void ResultadoSimulacionPrecios_TotalProductos_RetornaCantidadFilas()
    {
        var resultado = new ResultadoSimulacionPrecios
        {
            Filas = new List<FilaSimulacionPrecio>
            {
                new() { PrecioActual = 100m, PrecioNuevo = 110m },
                new() { PrecioActual = 200m, PrecioNuevo = 190m }
            }
        };
        Assert.Equal(2, resultado.TotalProductos);
    }

    [Fact]
    public void ResultadoSimulacionPrecios_ProductosConAumento_ContaSoloAumentos()
    {
        var resultado = new ResultadoSimulacionPrecios
        {
            Filas = new List<FilaSimulacionPrecio>
            {
                new() { PrecioActual = 100m, PrecioNuevo = 110m }, // aumento
                new() { PrecioActual = 100m, PrecioNuevo = 90m },  // descuento
                new() { PrecioActual = 100m, PrecioNuevo = 100m }  // sin cambio
            }
        };
        Assert.Equal(1, resultado.ProductosConAumento);
    }

    [Fact]
    public void ResultadoSimulacionPrecios_ProductosConDescuento_ContaSoloDescuentos()
    {
        var resultado = new ResultadoSimulacionPrecios
        {
            Filas = new List<FilaSimulacionPrecio>
            {
                new() { PrecioActual = 100m, PrecioNuevo = 110m }, // aumento
                new() { PrecioActual = 100m, PrecioNuevo = 90m },  // descuento
                new() { PrecioActual = 100m, PrecioNuevo = 100m }  // sin cambio — no cuenta
            }
        };
        Assert.Equal(1, resultado.ProductosConDescuento);
    }

    [Fact]
    public void ResultadoSimulacionPrecios_PorcentajePromedio_CalculaPromedio()
    {
        var resultado = new ResultadoSimulacionPrecios
        {
            Filas = new List<FilaSimulacionPrecio>
            {
                new() { PrecioActual = 100m, PrecioNuevo = 110m }, // +10%
                new() { PrecioActual = 100m, PrecioNuevo = 130m }  // +30%
            }
        };
        Assert.Equal(20m, resultado.PorcentajePromedio);
    }

    [Fact]
    public void ResultadoSimulacionPrecios_PorcentajePromedio_FilasVacias_RetornaCero()
    {
        var resultado = new ResultadoSimulacionPrecios { Filas = new List<FilaSimulacionPrecio>() };
        Assert.Equal(0m, resultado.PorcentajePromedio);
    }

    [Fact]
    public void ResultadoSimulacionPrecios_PorcentajePromedio_RedondeaADosDecimales()
    {
        var resultado = new ResultadoSimulacionPrecios
        {
            Filas = new List<FilaSimulacionPrecio>
            {
                new() { PrecioActual = 300m, PrecioNuevo = 301m }, // 0.33%
                new() { PrecioActual = 300m, PrecioNuevo = 302m }  // 0.67%
            }
        };
        Assert.Equal(0.50m, resultado.PorcentajePromedio);
    }
}
