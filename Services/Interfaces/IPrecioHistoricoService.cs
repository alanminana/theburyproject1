using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Interfaz para el servicio de gesti�n de historial de precios
    /// </summary>
    public interface IPrecioHistoricoService
    {
        /// <summary>
        /// Registra un cambio de precio en el historial
        /// </summary>
        /// <param name="productoId">ID del producto</param>
        /// <param name="precioCompraAnterior">precio de costo antes del cambio</param>
        /// <param name="precioCompraNuevo">precio de costo despu�s del cambio</param>
        /// <param name="precioVentaAnterior">Precio de venta antes del cambio</param>
        /// <param name="precioVentaNuevo">Precio de venta despu�s del cambio</param>
        /// <param name="motivoCambio">Motivo del cambio (opcional)</param>
        /// <param name="usuarioModificacion">Usuario que realiz� el cambio</param>
        Task<PrecioHistorico> RegistrarCambioAsync(
            int productoId,
            decimal precioCompraAnterior,
            decimal precioCompraNuevo,
            decimal precioVentaAnterior,
            decimal precioVentaNuevo,
            string? motivoCambio,
            string usuarioModificacion);

        /// <summary>
        /// Obtiene el historial completo de precios de un producto
        /// </summary>
        /// <param name="productoId">ID del producto</param>
        Task<List<PrecioHistorico>> GetHistorialByProductoIdAsync(int productoId);

        /// <summary>
        /// Obtiene el �ltimo cambio de precio de un producto
        /// </summary>
        /// <param name="productoId">ID del producto</param>
        Task<PrecioHistorico?> GetUltimoCambioAsync(int productoId);

        /// <summary>
        /// Revierte el �ltimo cambio de precio (Undo)
        /// Solo si no hay ventas posteriores al cambio
        /// </summary>
        /// <param name="historialId">ID del registro de historial a revertir</param>
        Task<bool> RevertirCambioAsync(int historialId);

        /// <summary>
        /// Obtiene estad�sticas de cambios de precios
        /// </summary>
        /// <param name="fechaDesde">Fecha inicial del per�odo</param>
        /// <param name="fechaHasta">Fecha final del per�odo</param>
        Task<PrecioHistoricoEstadisticasViewModel> GetEstadisticasAsync(DateTime? fechaDesde, DateTime? fechaHasta);

        /// <summary>
        /// Busca historial de precios con filtros
        /// </summary>
        /// <param name="filtro">Filtros de b�squeda</param>
        Task<PaginatedResult<PrecioHistoricoViewModel>> BuscarAsync(PrecioHistoricoFiltroViewModel filtro);

        /// <summary>
        /// Simula un cambio de precio y calcula el impacto en el margen
        /// </summary>
        /// <param name="productoId">ID del producto</param>
        /// <param name="precioCompraNuevo">Nuevo precio de costo propuesto</param>
        /// <param name="precioVentaNuevo">Nuevo precio de venta propuesto</param>
        Task<PrecioSimulacionViewModel> SimularCambioAsync(
            int productoId,
            decimal precioCompraNuevo,
            decimal precioVentaNuevo);

        /// <summary>
        /// Marca un cambio como no reversible (cuando hay ventas posteriores)
        /// </summary>
        /// <param name="historialId">ID del registro de historial</param>
        Task MarcarComoNoReversibleAsync(int historialId);
    }
}