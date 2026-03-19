using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    public interface IProveedorService
    {
        Task<IEnumerable<Proveedor>> GetAllAsync();
        Task<Proveedor?> GetByIdAsync(int id);
        Task CreateAsync(Proveedor proveedor);
        Task UpdateAsync(Proveedor proveedor);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsCuitAsync(string cuit, int? excludeId = null);
        Task<IEnumerable<Proveedor>> SearchAsync(
            string? searchTerm = null,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc");
    }
}