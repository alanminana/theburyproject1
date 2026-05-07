using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface IProductoCondicionPagoService
{
    Task<ProductoCondicionesPagoLecturaDto> ObtenerPorProductoAsync(
        int productoId,
        CancellationToken cancellationToken = default);

    Task<ProductoCondicionesPagoEditableDto> ObtenerEstadoEditableAsync(
        int productoId,
        CancellationToken cancellationToken = default);

    Task<ProductoCondicionPagoDto> GuardarCondicionAsync(
        int productoId,
        GuardarProductoCondicionPagoItem request,
        CancellationToken cancellationToken = default);

    Task<ProductoCondicionPagoTarjetaDto> GuardarReglaTarjetaAsync(
        int productoCondicionPagoId,
        GuardarProductoCondicionPagoTarjetaItem request,
        CancellationToken cancellationToken = default);

    Task GuardarCondicionesCompletasAsync(
        int productoId,
        GuardarProductoCondicionesPagoRequest request,
        CancellationToken cancellationToken = default);
}
