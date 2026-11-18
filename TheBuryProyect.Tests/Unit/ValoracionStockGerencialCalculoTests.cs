using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// R-03: cálculo puro del costo promedio ponderado (sin base de datos). Las fórmulas están documentadas en
/// <see cref="ValoracionStockGerencialService"/>.
/// </summary>
public class ValoracionStockGerencialCalculoTests
{
    private sealed class Kardex
    {
        private readonly List<MovimientoCosteo> _movs = new();
        private int _id;
        public decimal Stock { get; private set; }

        public Kardex Compra(decimal cantidad, decimal costo) => Agregar(cantidad, costo, true);
        public Kardex EntradaConCosto(decimal cantidad, decimal costo) => Agregar(cantidad, costo, false);
        public Kardex EntradaSinCosto(decimal cantidad) => Agregar(cantidad, 0m, false);

        /// <summary>Venta, devolución a proveedor o ajuste negativo: sale al promedio.</summary>
        public Kardex Salida(decimal cantidad, decimal costoSnapshot = 999m) => Agregar(-cantidad, costoSnapshot, false);

        private Kardex Agregar(decimal delta, decimal costo, bool esCompra)
        {
            var anterior = Stock;
            Stock += delta;
            _movs.Add(new MovimientoCosteo(++_id, new DateTime(2026, 1, 1).AddDays(_id), anterior, Stock, costo, esCompra));
            return this;
        }

        public ItemValoracionStock Calcular(decimal? stockActual = null) =>
            ValoracionStockGerencialService.CalcularProducto(stockActual ?? Stock, _movs);
    }

    [Fact]
    public void ComprasMultiplesADistintoPrecio_PromedioPonderadoExacto_SinRedondeo()
    {
        var item = new Kardex().Compra(10, 100m).Compra(10, 120m).Calcular();

        Assert.Equal(EstadoValoracionStock.PromedioMovilExacto, item.Estado);
        Assert.Equal(110m, item.CostoPromedioPonderado);
        Assert.Equal(120m, item.UltimoCostoCompra);
        Assert.Equal(20m * 110m, item.ValorGerencial);
        Assert.Equal(20m * 120m, item.ValorAUltimoCosto);
        Assert.Equal(200m, item.DiferenciaVsUltimoCosto);
        Assert.Equal(10m, item.DiferenciaUnitariaVsUltimoCosto);
        Assert.True(item.CoberturaCompleta);
    }

    [Fact]
    public void LasSalidasSalenAlPromedio_YNoLoModifican_YUnaCompraPosteriorLoRecalculaSobreElSaldo()
    {
        // 10 @100 → vende 6 (quedan 4 a 100) → compra 10 @130: (4·100 + 10·130) / 14. Sin redondear.
        var item = new Kardex().Compra(10, 100m).Salida(6).Compra(10, 130m).Calcular();

        Assert.Equal(EstadoValoracionStock.PromedioMovilExacto, item.Estado);
        Assert.Equal((4m * 100m + 10m * 130m) / 14m, item.CostoPromedioPonderado);
        Assert.Equal(14m * ((4m * 100m + 10m * 130m) / 14m), item.ValorGerencial);
        Assert.Equal(130m, item.UltimoCostoCompra);
    }

    [Fact]
    public void DevolucionAProveedorYAjusteNegativo_SonSalidas_NoCambianElPromedio()
    {
        var item = new Kardex().Compra(10, 100m).Compra(10, 200m).Salida(5).Salida(3).Calcular();

        Assert.Equal(150m, item.CostoPromedioPonderado);
        Assert.Equal(12m, item.StockActual);
        Assert.Equal(12m * 150m, item.ValorGerencial);
    }

    [Fact]
    public void AjustePositivoSinCosto_EntraAlPromedioVigente_SinModificarlo()
    {
        var item = new Kardex().Compra(10, 100m).Compra(10, 200m).EntradaSinCosto(5).Calcular();

        Assert.Equal(EstadoValoracionStock.PromedioMovilExacto, item.Estado);
        Assert.Equal(150m, item.CostoPromedioPonderado);
        Assert.Equal(25m * 150m, item.ValorGerencial);
        Assert.False(item.IncluyeCostosNoCompra);
    }

    [Fact]
    public void EntradaConCostoQueNoEsCompra_SeIncluyeYSeInforma()
    {
        // Stock inicial valuado a costo de ficha (no es una compra) + una compra: el promedio lo incluye y lo marca.
        var item = new Kardex().EntradaConCosto(10, 80m).Compra(10, 100m).Calcular();

        Assert.Equal(EstadoValoracionStock.PromedioMovilExacto, item.Estado);
        Assert.Equal(90m, item.CostoPromedioPonderado);
        Assert.True(item.IncluyeCostosNoCompra);
        Assert.Contains("no proviene de una compra", item.Observacion);
    }

    [Fact]
    public void ProductoSinCompras_NoTieneCostoCalculable_NiCerosInventados()
    {
        var item = new Kardex().EntradaConCosto(10, 80m).Calcular();

        Assert.Equal(EstadoValoracionStock.SinCompras, item.Estado);
        Assert.Null(item.CostoPromedioPonderado);
        Assert.Null(item.UltimoCostoCompra);
        Assert.Null(item.ValorGerencial);
        Assert.Null(item.ValorParcial);
        Assert.Null(item.ValorAUltimoCosto);
        Assert.Null(item.DiferenciaVsUltimoCosto);
    }

    [Fact]
    public void ProductoSinMovimientosConStock_EsSinCompras()
    {
        var item = ValoracionStockGerencialService.CalcularProducto(7m, Array.Empty<MovimientoCosteo>());

        Assert.Equal(EstadoValoracionStock.SinCompras, item.Estado);
        Assert.Null(item.ValorGerencial);
    }

    [Fact]
    public void ProductoSinStock_SeMuestraComoTal_ConElUltimoCostoDeReposicion()
    {
        var item = new Kardex().Compra(10, 100m).Salida(10).Calcular();

        Assert.Equal(EstadoValoracionStock.SinStock, item.Estado);
        Assert.Equal(100m, item.UltimoCostoCompra);
        Assert.Null(item.CostoPromedioPonderado);
        Assert.Null(item.ValorGerencial);
    }

    [Fact]
    public void KardexQueNoConciliaConElStock_UsaLaComposicionPorLasUltimasCompras()
    {
        // El Kardex dice 20 pero el stock real es 15 (edición manual fuera del Kardex): se toman 10 @120 y 5 @100.
        var item = new Kardex().Compra(10, 100m).Compra(10, 120m).Calcular(stockActual: 15m);

        Assert.Equal(EstadoValoracionStock.ComposicionPorCompras, item.Estado);
        Assert.Equal((10m * 120m + 5m * 100m) / 15m, item.CostoPromedioPonderado);
        Assert.Equal(15m, item.UnidadesCubiertas);
        Assert.Equal(15m * ((10m * 120m + 5m * 100m) / 15m), item.ValorGerencial);
        Assert.Equal(120m, item.UltimoCostoCompra);
    }

    [Fact]
    public void ComprasQueNoCubrenElStock_DanValorParcial_YNoInventanCostoParaElResto()
    {
        var item = new Kardex().Compra(10, 100m).Compra(10, 120m).Calcular(stockActual: 25m);

        Assert.Equal(EstadoValoracionStock.ComposicionPorCompras, item.Estado);
        Assert.Equal(110m, item.CostoPromedioPonderado);
        Assert.Equal(20m, item.UnidadesCubiertas);
        Assert.Null(item.ValorGerencial);
        Assert.Equal(20m * 110m, item.ValorParcial);
        Assert.Null(item.ValorAUltimoCosto);
        Assert.Null(item.DiferenciaVsUltimoCosto);
        Assert.False(item.CoberturaCompleta);
        Assert.Contains("20 de 25", item.Observacion);
    }

    [Fact]
    public void UnidadesIngresadasSinCostoAntesDeLaPrimeraCompra_NoPermitenElCalculoExacto()
    {
        var item = new Kardex().EntradaSinCosto(5).Compra(10, 100m).Calcular();

        Assert.Equal(EstadoValoracionStock.ComposicionPorCompras, item.Estado);
        Assert.Equal(10m, item.UnidadesCubiertas);
        Assert.Equal(15m, item.StockActual);
        Assert.Null(item.ValorGerencial);
        Assert.Equal(1000m, item.ValorParcial);
    }

    [Fact]
    public void SalidaMayorQueElSaldoDelKardex_EsIncoherente_YUsaLaVarianteConservadora()
    {
        var movs = new List<MovimientoCosteo>
        {
            new(1, new DateTime(2026, 1, 1), 0m, 10m, 100m, true),
            new(2, new DateTime(2026, 1, 2), 10m, -5m, 100m, false) // sale más de lo que había
        };

        var item = ValoracionStockGerencialService.CalcularProducto(3m, movs);

        Assert.Equal(EstadoValoracionStock.ComposicionPorCompras, item.Estado);
        Assert.Equal(100m, item.CostoPromedioPonderado);
    }

    [Fact]
    public void ElUltimoCostoDeCompraEsElDeLaUltimaCompraRecibida_NoElDeUnaSalida()
    {
        var item = new Kardex().Compra(10, 100m).Compra(5, 150m).Salida(4, costoSnapshot: 9999m).Calcular();

        Assert.Equal(150m, item.UltimoCostoCompra);
        Assert.Equal(new DateTime(2026, 1, 1).AddDays(2), item.FechaUltimaCompra);
    }
}
