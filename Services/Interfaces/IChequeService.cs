using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Interfaces
{
    public interface IChequeService
    {
        Task<IEnumerable<Cheque>> GetAllAsync();
        Task<Cheque?> GetByIdAsync(int id);
        Task<Cheque> CreateAsync(Cheque cheque);
        Task<Cheque> UpdateAsync(Cheque cheque);
        Task<bool> DeleteAsync(int id);

        Task<IEnumerable<Cheque>> SearchAsync(
            string? searchTerm = null,
            int? proveedorId = null,
            EstadoCheque? estado = null,
            DateTime? fechaEmisionDesde = null,
            DateTime? fechaEmisionHasta = null,
            DateTime? fechaVencimientoDesde = null,
            DateTime? fechaVencimientoHasta = null,
            bool soloVencidos = false,
            bool soloPorVencer = false,
            string? orderBy = null,
            string? orderDirection = "asc");

        Task<IEnumerable<Cheque>> GetByProveedorIdAsync(int proveedorId);
        Task<IEnumerable<Cheque>> GetByOrdenCompraIdAsync(int ordenCompraId);
        Task<IEnumerable<Cheque>> GetVencidosAsync();
        Task<IEnumerable<Cheque>> GetPorVencerAsync(int dias = 7);
        Task<bool> CambiarEstadoAsync(int id, EstadoCheque nuevoEstado);
        Task<bool> NumeroExisteAsync(string numero, int? excludeId = null);
    }
}