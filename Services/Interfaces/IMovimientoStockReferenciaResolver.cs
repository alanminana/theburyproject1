using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Enriquece los movimientos de stock con información contextual y navegable de su origen:
    /// entradas por compra (orden + proveedor) y salidas por venta (venta + cliente + modalidad de pago).
    /// </summary>
    public interface IMovimientoStockReferenciaResolver
    {
        Task EnriquecerAsync(IReadOnlyCollection<MovimientoStockViewModel> movimientos, CancellationToken cancellationToken = default);
    }
}
