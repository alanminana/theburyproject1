using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// R-03: valoración GERENCIAL del stock por costo promedio ponderado. Servicio de SOLO LECTURA: no escribe en Producto,
/// ProductoPrecioLista, MovimientoStock ni en ninguna tabla de costo. No es valuación fiscal ni contable.
/// El último costo de compra se informa como indicador de reposición; los precios sugeridos y los márgenes siguen usando
/// el costo de ficha del producto como hasta ahora.
/// </summary>
public interface IValoracionStockGerencialService
{
    Task<ResultadoValoracionStock> CalcularAsync(FiltroValoracionStock? filtro = null, CancellationToken cancellationToken = default);

    /// <summary>Exporta el cálculo (todas las filas del filtro, sin paginar, sin redondear importes) a un .xlsx.</summary>
    Task<byte[]> ExportarAsync(FiltroValoracionStock? filtro = null, CancellationToken cancellationToken = default);
}
