using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio centralizado para gestión de productos.
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

        /// <summary>
        /// Resuelve el porcentaje de IVA aplicable al producto y convierte PrecioVenta
        /// ingresado sin IVA al precio final persistible con IVA.
        /// </summary>
        Task PrepararPrecioVentaConIvaAsync(Producto producto);

        /// <summary>
        /// Convierte un precio final con IVA a precio base sin IVA para formularios.
        /// </summary>
        decimal ObtenerPrecioVentaSinIva(decimal precioVentaConIva, decimal porcentajeIVA);
        
        // Búsqueda y filtrado
        Task<IEnumerable<Producto>> SearchAsync(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false,
            string? orderBy = null,
            string? orderDirection = "asc");

        /// <summary>
        /// Busca IDs de productos aplicando los mismos filtros que SearchAsync.
        /// Más eficiente cuando solo se necesitan IDs (no carga entidades completas).
        /// </summary>
        Task<List<int>> SearchIdsAsync(
            string? searchTerm = null,
            int? categoriaId = null,
            int? marcaId = null,
            bool stockBajo = false,
            bool soloActivos = false);

        /// <summary>
        /// Busca productos para el panel de venta: aplica filtros de stock y precio,
        /// resuelve el precio vigente de lista y proyecta a DTO listo para serializar.
        /// </summary>
        Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(
            string term,
            int take = 20,
            int? categoriaId = null,
            int? marcaId = null,
            bool soloConStock = true,
            decimal? precioMin = null,
            decimal? precioMax = null);

        /// <summary>
        /// Obtiene el precio efectivo de venta de un producto activo, usando precio vigente
        /// de lista cuando existe y PrecioVenta del producto como fallback.
        /// </summary>
        Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId);

        // Stock
        Task<Producto> ActualizarStockAsync(int id, decimal cantidad);

        // Comisión
        Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje);

        // Destacado
        Task<bool> ToggleDestacadoAsync(int id);

        // Trazabilidad individual
        /// <summary>
        /// Activa o desactiva la trazabilidad individual (RequiereNumeroSerie) del producto.
        /// Para desactivar, el producto no debe tener unidades físicas registradas (no soft-deleted).
        /// </summary>
        Task CambiarTrazabilidadIndividualAsync(int productoId, bool requiereTrazabilidad);

        // Validaciones
        Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null);
    }
}
