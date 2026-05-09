using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio unificado para consulta del catálogo de productos con precios.
    /// Consolida la lógica de obtención de productos, categorías, marcas y precios
    /// en un único punto de acceso.
    /// </summary>
    public interface ICatalogoService
    {
        /// <summary>
        /// Obtiene el catálogo completo con productos y precios según la lista especificada.
        /// Este método es el punto único de entrada para la vista de catálogo.
        /// </summary>
        /// <param name="filtros">Filtros de búsqueda y ordenamiento</param>
        /// <returns>Resultado con filas de productos, dropdowns y métricas</returns>
        Task<ResultadoCatalogo> ObtenerCatalogoAsync(FiltrosCatalogo filtros);

        /// <summary>
        /// Obtiene una fila específica del catálogo por ID de producto
        /// </summary>
        /// <param name="productoId">ID del producto</param>
        /// <param name="listaPrecioId">ID de la lista de precios (null = predeterminada)</param>
        /// <returns>Fila del catálogo o null si no existe</returns>
        Task<FilaCatalogo?> ObtenerFilaAsync(int productoId, int? listaPrecioId = null);

        // ──────────────────────────────────────────────────────────────
        // Acciones masivas de precios
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Simula un cambio masivo de precios sin persistir.
        /// Calcula los nuevos precios y devuelve un preview con Actual/Nuevo/Diferencia.
        /// El BatchId devuelto es temporal (solo existe en memoria o caché corto).
        /// </summary>
        /// <param name="solicitud">Regla y alcance de la simulación</param>
        /// <returns>Resultado con filas simuladas y métricas</returns>
        Task<ResultadoSimulacionPrecios> SimularCambioPreciosAsync(SolicitudSimulacionPrecios solicitud);

        /// <summary>
        /// Aplica el cambio masivo de precios previamente simulado.
        /// Persiste los cambios con auditoría e historial.
        /// </summary>
        /// <param name="solicitud">Referencia al batch y configuración de aplicación</param>
        /// <returns>Resultado con éxito/error y conteo de actualizaciones</returns>
        Task<ResultadoAplicacionPrecios> AplicarCambioPreciosAsync(SolicitudAplicarPrecios solicitud);

        /// <summary>
        /// Aplica un cambio directo de precio a productos seleccionados o filtrados desde el catálogo.
        /// Actualiza Producto.PrecioVenta, crea historial y permite revertir.
        /// </summary>
        /// <param name="model">Datos del cambio directo</param>
        /// <returns>Resultado con éxito/error y conteo de actualizaciones</returns>
        Task<ResultadoAplicacionPrecios> AplicarCambioPrecioDirectoAsync(AplicarCambioPrecioDirectoViewModel model);

        Task<CambioPrecioHistorialViewModel> GetHistorialCambiosPrecioAsync();

        Task<CambioPrecioDetalleViewModel?> GetCambioPrecioDetalleAsync(int eventoId);

        Task<List<HistorialPrecioProductoItemViewModel>> GetHistorialCambiosPrecioProductoAsync(int productoId);

        Task<(bool Exitoso, string Mensaje, int? EventoReversionId)> RevertirCambioPrecioAsync(int eventoId);

        /// <summary>
        /// Alterna el estado EsDestacado del producto y devuelve el nuevo valor.
        /// </summary>
        Task<bool> ToggleDestacadoAsync(int productoId);
    }
}
