namespace TheBuryProject.Services.Models;

/// <summary>
/// R-03: resultado del cálculo del costo promedio ponderado por producto. Solo lectura: nada de esto se persiste.
/// </summary>
public enum EstadoValoracionStock
{
    /// <summary>Promedio móvil exacto: el Kardex concilia con el stock actual y todas las unidades tienen costo.</summary>
    PromedioMovilExacto = 0,

    /// <summary>
    /// Variante conservadora: el Kardex no permite el cálculo exacto; promedio de las compras recibidas más recientes
    /// que componen el stock actual.
    /// </summary>
    ComposicionPorCompras = 1,

    /// <summary>Sin stock: no hay valor que calcular (se informa el último costo de compra si existe).</summary>
    SinStock = 2,

    /// <summary>Con stock pero sin ninguna compra recibida registrada: sin costo calculable.</summary>
    SinCompras = 3
}

public sealed class FiltroValoracionStock
{
    public string? Busqueda { get; set; }
    public int? CategoriaId { get; set; }
    public int? MarcaId { get; set; }
    public EstadoValoracionStock? Estado { get; set; }

    /// <summary>Si es falso (por defecto) se omiten los productos con stock cero o negativo, salvo que se filtre por «SinStock».</summary>
    public bool IncluirSinStock { get; set; }
}

/// <summary>Un movimiento del Kardex con los datos que usa el costeo. Orden: fecha de alta y luego Id.</summary>
public sealed record MovimientoCosteo(
    int Id,
    DateTime Fecha,
    decimal StockAnterior,
    decimal StockNuevo,
    decimal CostoUnitario,
    bool EsCompraRecibida);

public sealed class ItemValoracionStock
{
    public int ProductoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string? Marca { get; set; }
    public decimal StockActual { get; set; }
    public EstadoValoracionStock Estado { get; set; }

    /// <summary>Costo promedio ponderado (sin redondear). Null si no es calculable.</summary>
    public decimal? CostoPromedioPonderado { get; set; }

    /// <summary>Costo unitario de la última compra recibida (indicador de reposición). Null si nunca se recibió una compra.</summary>
    public decimal? UltimoCostoCompra { get; set; }
    public DateTime? FechaUltimaCompra { get; set; }

    /// <summary>Costo de ficha del producto (<c>Producto.PrecioCompra</c>), el que usan precios y márgenes. Solo informativo.</summary>
    public decimal CostoFichaProducto { get; set; }

    /// <summary>Unidades del stock actual cubiertas por el cálculo (0..stock).</summary>
    public decimal UnidadesCubiertas { get; set; }

    /// <summary>Stock × promedio ponderado. Solo cuando el cálculo cubre todo el stock; si no, null (ver <see cref="ValorParcial"/>).</summary>
    public decimal? ValorGerencial { get; set; }

    /// <summary>Unidades cubiertas × promedio ponderado, cuando la cobertura es parcial.</summary>
    public decimal? ValorParcial { get; set; }

    /// <summary>Stock × último costo de compra (valor de reposición). Solo con cobertura completa.</summary>
    public decimal? ValorAUltimoCosto { get; set; }

    /// <summary>Valor a último costo menos valor gerencial: cuánto más (o menos) costaría reponer el stock.</summary>
    public decimal? DiferenciaVsUltimoCosto { get; set; }

    /// <summary>Último costo menos promedio, por unidad.</summary>
    public decimal? DiferenciaUnitariaVsUltimoCosto { get; set; }

    /// <summary>El promedio incluye entradas valuadas a un costo que no proviene de una compra recibida (stock inicial, ajuste, devolución).</summary>
    public bool IncluyeCostosNoCompra { get; set; }

    public string Observacion { get; set; } = string.Empty;

    public bool CoberturaCompleta => StockActual > 0m && UnidadesCubiertas >= StockActual;
}

public sealed class ResumenValoracionStock
{
    public int ProductosListados { get; set; }
    public int ProductosConValorCompleto { get; set; }
    public int ProductosConValorParcial { get; set; }
    public int ProductosSinCompras { get; set; }
    public int ProductosSinStock { get; set; }
    public decimal ValorGerencialTotal { get; set; }
    public decimal ValorAUltimoCostoTotal { get; set; }
    public decimal DiferenciaVsUltimoCostoTotal { get; set; }
    public decimal ValorParcialTotal { get; set; }
}

public sealed class ResultadoValoracionStock
{
    public DateTime FechaCalculo { get; set; }
    public List<ItemValoracionStock> Items { get; set; } = new();
    public ResumenValoracionStock Resumen { get; set; } = new();
}
