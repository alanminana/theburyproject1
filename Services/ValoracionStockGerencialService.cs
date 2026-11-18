using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

/// <summary>
/// R-03: valoración gerencial del stock por costo promedio ponderado (SOLO LECTURA).
///
/// Fuente: el Kardex (<c>MovimientosStock</c>), que guarda por movimiento el stock anterior y nuevo, el costo unitario al
/// momento y la fuente del costo. Las compras recibidas son las entradas con <c>OrdenCompraId</c> y fuente
/// <c>OrdenCompraDetalle</c> (las que registra <c>OrdenCompraService.RecepcionarAsync</c>).
///
/// FÓRMULA 1 — promedio móvil exacto (<see cref="EstadoValoracionStock.PromedioMovilExacto"/>), movimientos en orden cronológico
/// (fecha de alta, luego Id), con d = StockNuevo − StockAnterior:
///   · entrada con costo c (compra, o entrada ajena a compras con costo informado):  A = (Q·A + d·c) / (Q + d);  Q = Q + d
///   · entrada sin costo informado: entra al promedio vigente A (no lo modifica); si todavía no hay A, queda «sin costo»
///   · salida o ajuste negativo: sale al promedio vigente (A no cambia); Q = Q − |d|
/// Es exacta solo si el Kardex concilia con el stock actual (Q = stock), ninguna unidad quedó sin costo, no hubo saldos
/// negativos ni movimientos incoherentes y existe al menos una compra recibida.
///
/// FÓRMULA 2 — composición por compras (<see cref="EstadoValoracionStock.ComposicionPorCompras"/>, conservadora), cuando la
/// fórmula 1 no es exacta pero hay compras recibidas: se toman las compras recibidas desde la más reciente hacia atrás hasta
/// cubrir el stock actual (supuesto: el stock presente proviene de las últimas compras) y el promedio es
/// Σ(cantidad·costo) / Σ(cantidad) sobre esas unidades (la última compra tomada se corta en lo que falta). Si las compras
/// registradas no cubren todo el stock, el promedio vale solo para las unidades cubiertas, se informa la cobertura y NO se
/// calcula un valor total (solo un valor parcial): no se inventa costo para el resto.
///
/// Sin compras recibidas → «sin costo calculable». Stock ≤ 0 → «sin stock».
/// Ningún importe se redondea: los decimales son los del origen (decimal de 28 dígitos); el redondeo es de presentación.
/// </summary>
public class ValoracionStockGerencialService : IValoracionStockGerencialService
{
    internal const string FuenteCompraRecibida = "OrdenCompraDetalle";
    private const decimal Tolerancia = 0.0001m;

    private readonly AppDbContext _context;

    public ValoracionStockGerencialService(AppDbContext context) => _context = context;

    public async Task<ResultadoValoracionStock> CalcularAsync(FiltroValoracionStock? filtro = null, CancellationToken cancellationToken = default)
    {
        filtro ??= new FiltroValoracionStock();

        var query = _context.Productos.AsNoTracking().Where(p => !p.IsDeleted);
        if (filtro.CategoriaId.HasValue)
            query = query.Where(p => p.CategoriaId == filtro.CategoriaId.Value);
        if (filtro.MarcaId.HasValue)
            query = query.Where(p => p.MarcaId == filtro.MarcaId.Value);
        var busqueda = filtro.Busqueda?.Trim();
        if (!string.IsNullOrEmpty(busqueda))
            query = query.Where(p => p.Codigo.Contains(busqueda) || p.Nombre.Contains(busqueda));

        var incluirSinStock = filtro.IncluirSinStock || filtro.Estado == EstadoValoracionStock.SinStock;
        if (!incluirSinStock)
            query = query.Where(p => p.StockActual > 0m);

        var productos = await query
            .OrderBy(p => p.Codigo)
            .Select(p => new
            {
                p.Id, p.Codigo, p.Nombre, p.StockActual, p.PrecioCompra,
                Categoria = p.Categoria != null ? p.Categoria.Nombre : null,
                Marca = p.Marca != null ? p.Marca.Nombre : null
            })
            .ToListAsync(cancellationToken);

        var ids = productos.Select(p => p.Id).ToList();
        var movimientosPorProducto = new Dictionary<int, List<MovimientoCosteo>>();
        // Se lee el Kardex por tandas de productos para no armar un IN gigante.
        foreach (var tanda in ids.Chunk(1000))
        {
            var filas = await _context.MovimientosStock.AsNoTracking()
                .Where(m => !m.IsDeleted && tanda.Contains(m.ProductoId))
                .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
                .Select(m => new
                {
                    m.ProductoId, m.Id, m.CreatedAt, m.Tipo, m.StockAnterior, m.StockNuevo, m.CostoUnitarioAlMomento,
                    m.FuenteCosto, m.OrdenCompraId
                })
                .ToListAsync(cancellationToken);

            foreach (var f in filas)
            {
                if (!movimientosPorProducto.TryGetValue(f.ProductoId, out var lista))
                    movimientosPorProducto[f.ProductoId] = lista = new List<MovimientoCosteo>();
                var esCompra = f.Tipo == TipoMovimiento.Entrada && f.OrdenCompraId.HasValue
                               && f.FuenteCosto == FuenteCompraRecibida && f.CostoUnitarioAlMomento > 0m
                               && f.StockNuevo > f.StockAnterior;
                lista.Add(new MovimientoCosteo(f.Id, f.CreatedAt, f.StockAnterior, f.StockNuevo, f.CostoUnitarioAlMomento, esCompra));
            }
        }

        var items = new List<ItemValoracionStock>(productos.Count);
        foreach (var p in productos)
        {
            movimientosPorProducto.TryGetValue(p.Id, out var movs);
            var item = CalcularProducto(p.StockActual, movs ?? new List<MovimientoCosteo>());
            item.ProductoId = p.Id;
            item.Codigo = p.Codigo;
            item.Nombre = p.Nombre;
            item.Categoria = p.Categoria;
            item.Marca = p.Marca;
            item.CostoFichaProducto = p.PrecioCompra;
            if (filtro.Estado.HasValue && item.Estado != filtro.Estado.Value)
                continue;
            items.Add(item);
        }

        return new ResultadoValoracionStock
        {
            FechaCalculo = DateTime.UtcNow,
            Items = items,
            Resumen = Resumir(items)
        };
    }

    /// <summary>Cálculo puro de un producto (sin acceso a datos): ver las fórmulas en la documentación de la clase.</summary>
    public static ItemValoracionStock CalcularProducto(decimal stockActual, IReadOnlyList<MovimientoCosteo> movimientos)
    {
        var item = new ItemValoracionStock { StockActual = stockActual };

        var compras = movimientos.Where(m => m.EsCompraRecibida).ToList();
        var ultimaCompra = compras.Count > 0 ? compras[^1] : null;
        if (ultimaCompra != null)
        {
            item.UltimoCostoCompra = ultimaCompra.CostoUnitario;
            item.FechaUltimaCompra = ultimaCompra.Fecha;
        }

        if (stockActual <= 0m)
        {
            item.Estado = EstadoValoracionStock.SinStock;
            item.Observacion = ultimaCompra == null
                ? "Sin stock y sin compras recibidas registradas."
                : "Sin stock: no hay valor que calcular.";
            return item;
        }

        if (compras.Count == 0)
        {
            item.Estado = EstadoValoracionStock.SinCompras;
            item.Observacion = "Sin costo calculable: no hay compras recibidas registradas para este producto.";
            return item;
        }

        // ── Fórmula 1: promedio móvil exacto ──
        decimal qCosteadas = 0m, qSinCosto = 0m;
        decimal? promedio = null;
        var incoherente = false;
        var incluyeNoCompra = false;
        foreach (var m in movimientos)
        {
            var d = m.StockNuevo - m.StockAnterior;
            if (d == 0m)
                continue;
            if (d > 0m)
            {
                var costo = m.CostoUnitario > 0m ? m.CostoUnitario : (decimal?)null;
                if (costo.HasValue)
                {
                    promedio = qCosteadas + d > 0m && promedio.HasValue
                        ? (qCosteadas * promedio.Value + d * costo.Value) / (qCosteadas + d)
                        : costo.Value;
                    qCosteadas += d;
                    if (!m.EsCompraRecibida) incluyeNoCompra = true;
                }
                else if (promedio.HasValue)
                {
                    qCosteadas += d; // entra al promedio vigente, sin modificarlo
                }
                else
                {
                    qSinCosto += d;
                }
            }
            else
            {
                var salida = -d;
                var deCosteadas = Math.Min(qCosteadas, salida);
                qCosteadas -= deCosteadas;
                salida -= deCosteadas;
                var deSinCosto = Math.Min(qSinCosto, salida);
                qSinCosto -= deSinCosto;
                salida -= deSinCosto;
                if (salida > Tolerancia)
                    incoherente = true; // salió más de lo que el Kardex dice que había
            }
        }

        var concilia = Math.Abs(qCosteadas + qSinCosto - stockActual) <= Tolerancia;
        if (!incoherente && concilia && qSinCosto == 0m && promedio.HasValue)
        {
            item.Estado = EstadoValoracionStock.PromedioMovilExacto;
            item.CostoPromedioPonderado = promedio.Value;
            item.UnidadesCubiertas = stockActual;
            item.IncluyeCostosNoCompra = incluyeNoCompra;
            item.Observacion = incluyeNoCompra
                ? "Promedio móvil exacto. Incluye entradas valuadas a un costo que no proviene de una compra recibida (stock inicial, ajuste o devolución)."
                : "Promedio móvil exacto sobre las compras recibidas.";
        }
        else
        {
            // ── Fórmula 2: composición por compras (conservadora) ──
            decimal restante = stockActual, unidades = 0m, importe = 0m;
            for (var i = compras.Count - 1; i >= 0 && restante > 0m; i--)
            {
                var c = compras[i];
                var cantidad = c.StockNuevo - c.StockAnterior;
                var tomadas = Math.Min(cantidad, restante);
                unidades += tomadas;
                importe += tomadas * c.CostoUnitario;
                restante -= tomadas;
            }

            item.Estado = EstadoValoracionStock.ComposicionPorCompras;
            item.CostoPromedioPonderado = importe / unidades;
            item.UnidadesCubiertas = unidades;
            item.Observacion = unidades >= stockActual
                ? "El Kardex no permite el promedio móvil exacto: se promediaron las últimas compras recibidas que componen el stock actual."
                : $"Las compras recibidas registradas cubren {unidades:0.####} de {stockActual:0.####} unidades: el resto no tiene costo calculable.";
        }

        // ── Valores ──
        var promedioFinal = item.CostoPromedioPonderado!.Value;
        if (item.CoberturaCompleta)
        {
            item.ValorGerencial = stockActual * promedioFinal;
            item.ValorAUltimoCosto = stockActual * item.UltimoCostoCompra!.Value;
            item.DiferenciaVsUltimoCosto = item.ValorAUltimoCosto - item.ValorGerencial;
        }
        else
        {
            item.ValorParcial = item.UnidadesCubiertas * promedioFinal;
        }
        item.DiferenciaUnitariaVsUltimoCosto = item.UltimoCostoCompra!.Value - promedioFinal;
        return item;
    }

    private static ResumenValoracionStock Resumir(List<ItemValoracionStock> items) => new()
    {
        ProductosListados = items.Count,
        ProductosConValorCompleto = items.Count(i => i.ValorGerencial.HasValue),
        ProductosConValorParcial = items.Count(i => i.ValorParcial.HasValue),
        ProductosSinCompras = items.Count(i => i.Estado == EstadoValoracionStock.SinCompras),
        ProductosSinStock = items.Count(i => i.Estado == EstadoValoracionStock.SinStock),
        ValorGerencialTotal = items.Sum(i => i.ValorGerencial ?? 0m),
        ValorAUltimoCostoTotal = items.Sum(i => i.ValorAUltimoCosto ?? 0m),
        DiferenciaVsUltimoCostoTotal = items.Sum(i => i.DiferenciaVsUltimoCosto ?? 0m),
        ValorParcialTotal = items.Sum(i => i.ValorParcial ?? 0m)
    };

    public static string EtiquetaEstado(EstadoValoracionStock estado) => estado switch
    {
        EstadoValoracionStock.PromedioMovilExacto => "Promedio móvil exacto",
        EstadoValoracionStock.ComposicionPorCompras => "Por composición de compras",
        EstadoValoracionStock.SinStock => "Sin stock",
        EstadoValoracionStock.SinCompras => "Sin costo calculable (sin compras)",
        _ => estado.ToString()
    };

    public async Task<byte[]> ExportarAsync(FiltroValoracionStock? filtro = null, CancellationToken cancellationToken = default)
    {
        var resultado = await CalcularAsync(filtro, cancellationToken);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Valoración gerencial");
        ws.Cell(1, 1).Value = "Valoración de stock (gerencial)";
        ws.Cell(2, 1).Value = "Uso gerencial. No es valuación fiscal ni contable; no modifica costos ni márgenes.";
        ws.Cell(3, 1).Value = $"Calculado al {resultado.FechaCalculo:dd/MM/yyyy HH:mm} UTC";

        string[] encabezados =
        {
            "Código", "Producto", "Categoría", "Marca", "Stock", "Estado", "Costo promedio ponderado", "Último costo de compra",
            "Fecha última compra", "Costo de ficha", "Unidades cubiertas", "Valor gerencial", "Valor parcial",
            "Valor a último costo", "Diferencia vs último costo", "Observación"
        };
        for (var c = 0; c < encabezados.Length; c++)
            ws.Cell(5, c + 1).Value = encabezados[c];
        ws.Row(5).Style.Font.Bold = true;

        var fila = 6;
        foreach (var i in resultado.Items)
        {
            ws.Cell(fila, 1).Value = i.Codigo;
            ws.Cell(fila, 2).Value = i.Nombre;
            ws.Cell(fila, 3).Value = i.Categoria ?? string.Empty;
            ws.Cell(fila, 4).Value = i.Marca ?? string.Empty;
            ws.Cell(fila, 5).Value = i.StockActual;
            ws.Cell(fila, 6).Value = EtiquetaEstado(i.Estado);
            Poner(ws, fila, 7, i.CostoPromedioPonderado);
            Poner(ws, fila, 8, i.UltimoCostoCompra);
            if (i.FechaUltimaCompra.HasValue) ws.Cell(fila, 9).Value = i.FechaUltimaCompra.Value;
            ws.Cell(fila, 10).Value = i.CostoFichaProducto;
            ws.Cell(fila, 11).Value = i.UnidadesCubiertas;
            Poner(ws, fila, 12, i.ValorGerencial);
            Poner(ws, fila, 13, i.ValorParcial);
            Poner(ws, fila, 14, i.ValorAUltimoCosto);
            Poner(ws, fila, 15, i.DiferenciaVsUltimoCosto);
            ws.Cell(fila, 16).Value = i.Observacion;
            fila++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Poner(IXLWorksheet ws, int fila, int columna, decimal? valor)
    {
        // Sin valor se deja la celda vacía: no se escribe un cero inventado.
        if (valor.HasValue)
            ws.Cell(fila, columna).Value = valor.Value;
    }
}
