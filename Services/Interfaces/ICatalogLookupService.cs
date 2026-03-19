using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Provee accesos combinados a catálogos para reducir llamadas duplicadas.
    /// </summary>
    public interface ICatalogLookupService
    {
        Task<(IEnumerable<Categoria> categorias, IEnumerable<Marca> marcas)> GetCategoriasYMarcasAsync();

        Task<(IEnumerable<Categoria> categorias, IEnumerable<Marca> marcas, IEnumerable<Producto> productos)> GetCategoriasMarcasYProductosAsync();

        /// <summary>
        /// Obtiene las subcategorías (hijas) de una categoría padre
        /// </summary>
        Task<IEnumerable<Categoria>> GetSubcategoriasAsync(int categoriaId);

        /// <summary>
        /// Obtiene las submarcas (hijas) de una marca padre
        /// </summary>
        Task<IEnumerable<Marca>> GetSubmarcasAsync(int marcaId);
    }
}
