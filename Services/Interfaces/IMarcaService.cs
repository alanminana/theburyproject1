using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Interfaz de servicio para operaciones de negocio de Marcas
    /// </summary>
    public interface IMarcaService
    {
        /// <summary>
        /// Obtiene todas las marcas activas
        /// </summary>
        Task<IEnumerable<Marca>> GetAllAsync();

        /// <summary>
        /// Obtiene una marca por su Id
        /// </summary>
        Task<Marca?> GetByIdAsync(int id);

        /// <summary>
        /// Obtiene una marca por su c�digo
        /// </summary>
        Task<Marca?> GetByCodigoAsync(string codigo);

        /// <summary>
        /// Crea una nueva marca
        /// </summary>
        Task<Marca> CreateAsync(Marca marca);

        /// <summary>
        /// Actualiza una marca existente
        /// </summary>
        Task<Marca> UpdateAsync(Marca marca);

        /// <summary>
        /// Elimina una marca (soft delete)
        /// </summary>
        Task<bool> DeleteAsync(int id);

        /// <summary>
        /// Verifica si existe una marca con el c�digo especificado
        /// </summary>
        Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null);

        Task<IEnumerable<Marca>> SearchAsync(
    string? searchTerm = null,
    bool soloActivos = false,
    string? orderBy = null,
    string? orderDirection = "asc");

        /// <summary>
        /// Obtiene las submarcas (hijas) de una marca padre
        /// </summary>
        Task<IEnumerable<Marca>> GetChildrenAsync(int parentId);
    }
}