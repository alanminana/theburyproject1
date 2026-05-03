using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

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

        /// <summary>
        /// Obtiene las alícuotas de IVA activas para poblar dropdowns de formulario.
        /// Ordenadas por predeterminada primero, luego por porcentaje ascendente.
        /// </summary>
        Task<List<AlicuotaIVAFormItem>> ObtenerAlicuotasIVAParaFormAsync();

        /// <summary>
        /// Devuelve el porcentaje de una alícuota activa y no eliminada, o null si no existe.
        /// </summary>
        Task<decimal?> ObtenerPorcentajeAlicuotaAsync(int alicuotaIVAId);
    }
}
