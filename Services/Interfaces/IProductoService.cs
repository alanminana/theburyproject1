using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// ✅ Servicio centralizado para gestión de productos
    /// </summary>
    public interface IProductoService
    {
        // CRUD básico
        Task<IEnumerable<Producto>> GetAllAsync();
        Task<Producto?> GetByIdAsync(int id);
        Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId);
        Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId);
        Task<IEnumerable<Producto>> GetProductosConStockBajoAsync();
        Task<Producto> CreateAsync(Producto producto);
        Task<Producto> UpdateAsync(Producto producto);
        Task<bool> DeleteAsync(int id);
        
        // Búsqueda y filtrado
        Task<IEnumerable<Producto>> SearchAsync(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc");

        // Stock
        Task<Producto> ActualizarStockAsync(int id, decimal cantidad);

        // Validaciones
        Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null);
    }
}