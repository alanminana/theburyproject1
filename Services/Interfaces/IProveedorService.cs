using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

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

        /// <summary>
        /// Obtiene los productos asociados a un proveedor (relaciones activas, no eliminadas).
        /// </summary>
        Task<List<ProveedorProducto>> GetProductosProveedorAsync(int proveedorId);

        /// <summary>
        /// Obtiene los productos activos y no eliminados de un proveedor, proyectados como DTOs.
        /// </summary>
        Task<List<ProductoProveedorDto>> GetProductosActivosProveedorAsync(int proveedorId);
    }
}