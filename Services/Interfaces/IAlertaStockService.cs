using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Interfaz para el servicio de gesti�n de alertas de stock
    /// </summary>
    public interface IAlertaStockService
    {
        /// <summary>

    /// <summary>
    /// Verifica y genera alertas para mltiples productos (batch), evitando N+1.
    /// Devuelve la cantidad de alertas nuevas creadas.
    /// </summary>
    Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds);
        /// Genera alertas para todos los productos con stock bajo
        /// </summary>
        Task<int> GenerarAlertasStockBajoAsync();

        /// <summary>
        /// Obtiene todas las alertas de stock pendientes
        /// </summary>
        Task<List<AlertaStock>> GetAlertasPendientesAsync();

        /// <summary>
        /// Obtiene alertas de stock con filtros
        /// </summary>
        Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro);

        /// <summary>
        /// Obtiene una alerta por ID
        /// </summary>
        Task<AlertaStockViewModel?> GetByIdAsync(int id);

        /// <summary>
        /// Marca una alerta como resuelta
        /// </summary>
        Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null);

        /// <summary>
        /// Marca una alerta como ignorada
        /// </summary>
        Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null);

        /// <summary>
        /// Obtiene estad�sticas de alertas de stock
        /// </summary>
        Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync();

        /// <summary>
        /// Obtiene alertas por producto
        /// </summary>
        Task<List<AlertaStock>> GetAlertasByProductoIdAsync(int productoId);

        /// <summary>
        /// Verifica y genera alerta para un producto espec�fico
        /// </summary>
        Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId);

        /// <summary>
        /// Elimina alertas resueltas antiguas (m�s de X d�as)
        /// </summary>
        Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30);

        /// <summary>
        /// Obtiene productos cr�ticos (sin stock o stock agotado)
        /// </summary>
        Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync();
    }
}