using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IOrdenCompraService
    {
        Task<IEnumerable<OrdenCompra>> GetAllAsync();
        Task<OrdenCompra?> GetByIdAsync(int id);
        Task<OrdenCompra> CreateAsync(OrdenCompra ordenCompra);
        Task<OrdenCompra> UpdateAsync(OrdenCompra ordenCompra);
        Task<bool> DeleteAsync(int id);
        Task<IEnumerable<OrdenCompra>> SearchAsync(
            string? searchTerm = null,
            int? proveedorId = null,
            EstadoOrdenCompra? estado = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null,
            string? orderBy = null,
            string? orderDirection = "asc");
        Task<IEnumerable<OrdenCompra>> GetByProveedorIdAsync(int proveedorId);
        Task<bool> CambiarEstadoAsync(int id, EstadoOrdenCompra nuevoEstado);
        Task<bool> NumeroOrdenExisteAsync(string numero, int? excludeId = null);
        Task<string> GenerarNumeroOrdenAsync();
        Task<decimal> CalcularTotalOrdenAsync(int ordenId);

        // NUEVO: M�todo para recepcionar
        Task<OrdenCompra> RecepcionarAsync(int ordenId, byte[] rowVersion, List<RecepcionDetalleViewModel> detallesRecepcion);
    }
}