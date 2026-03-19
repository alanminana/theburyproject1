using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    public interface ICategoriaService
    {
        Task<IEnumerable<Categoria>> GetAllAsync();
        Task<Categoria?> GetByIdAsync(int id);
        Task<Categoria?> GetByCodigoAsync(string codigo);
        Task<Categoria> CreateAsync(Categoria categoria);
        Task<Categoria> UpdateAsync(Categoria categoria);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null);
        Task<IEnumerable<Categoria>> SearchAsync(
            string? searchTerm = null,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc");

        /// <summary>
        /// Obtiene las subcategorías (hijas) de una categoría padre
        /// </summary>
        Task<IEnumerable<Categoria>> GetChildrenAsync(int parentId);
    }
}