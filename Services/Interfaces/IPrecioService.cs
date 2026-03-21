
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;


public interface IPrecioService
{
    // ============================================
    // GESTI�N DE LISTAS DE PRECIOS
    // ============================================

    /// <summary>
    /// Obtiene todas las listas de precios activas
    /// </summary>
    Task<List<ListaPrecio>> GetAllListasAsync(bool soloActivas = true);

    /// <summary>
    /// Obtiene una lista de precios por ID
    /// </summary>
    Task<ListaPrecio?> GetListaByIdAsync(int id);

    /// <summary>
    /// Obtiene la lista predeterminada del sistema
    /// </summary>
    Task<ListaPrecio?> GetListaPredeterminadaAsync();

    /// <summary>
    /// Crea una nueva lista de precios
    /// </summary>
    Task<ListaPrecio> CreateListaAsync(ListaPrecio lista);

    /// <summary>
    /// Actualiza una lista de precios existente
    /// </summary>
    Task<ListaPrecio> UpdateListaAsync(ListaPrecio lista, byte[] rowVersion);

    /// <summary>
    /// Elimina (soft delete) una lista de precios
    /// </summary>
    Task<bool> DeleteListaAsync(int id, byte[] rowVersion);

    // ============================================
    // CONSULTA DE PRECIOS VIGENTES
    // ============================================

    /// <summary>
    /// Obtiene el precio vigente de un producto en una lista espec�fica
    /// </summary>
    /// <param name="productoId">ID del producto</param>
    /// <param name="listaId">ID de la lista de precios</param>
    /// <param name="fecha">Fecha para la cual obtener el precio (null = hoy)</param>
    Task<ProductoPrecioLista?> GetPrecioVigenteAsync(int productoId, int listaId, DateTime? fecha = null);

    /// <summary>
    /// Obtiene todos los precios vigentes de un producto en todas las listas
    /// </summary>
    Task<List<ProductoPrecioLista>> GetPreciosProductoAsync(int productoId, DateTime? fecha = null);

    /// <summary>
    /// Obtiene el historial completo de precios de un producto en una lista
    /// </summary>
    Task<List<ProductoPrecioLista>> GetHistorialPreciosAsync(int productoId, int listaId);

    // ============================================
    // GESTI�N DE PRECIOS INDIVIDUALES
    // ============================================

    /// <summary>
    /// Establece un precio manual para un producto en una lista
    /// Genera una nueva vigencia
    /// </summary>
    Task<ProductoPrecioLista> SetPrecioManualAsync(
        int productoId,
        int listaId,
        decimal precio,
        decimal costo,
        DateTime? vigenciaDesde = null,
        string? notas = null);

    /// <summary>
    /// Calcula el precio autom�tico basado en costo y reglas de la lista
    /// </summary>
    Task<decimal> CalcularPrecioAutomaticoAsync(int productoId, int listaId, decimal costo);

    // ============================================
    // CAMBIO DIRECTO DE PRECIO (CATALOGO)
    // ============================================

    /// <summary>
    /// Aplica un cambio directo de precio a productos seleccionados o filtrados desde el catalogo.
    /// Actualiza Producto.PrecioVenta, crea historial y permite revertir.
    /// </summary>
    Task<ResultadoAplicacionPrecios> AplicarCambioPrecioDirectoAsync(AplicarCambioPrecioDirectoViewModel model);

    /// <summary>
    /// Obtiene eventos de cambios directos de precios.
    /// </summary>
    Task<List<CambioPrecioEvento>> GetCambioPrecioEventosAsync(int take = 200);

    /// <summary>
    /// Obtiene el historial de cambios de precio para un producto específico.
    /// </summary>
    Task<List<CambioPrecioDetalle>> GetCambiosPrecioProductoAsync(int productoId, int take = 50);

    /// <summary>
    /// Obtiene el último cambio directo por producto.
    /// </summary>
    Task<Dictionary<int, UltimoCambioProductoResumen>> GetUltimoCambioPorProductosAsync(IEnumerable<int> productoIds);

    /// <summary>
    /// Obtiene un evento de cambio directo con detalles.
    /// </summary>
    Task<CambioPrecioEvento?> GetCambioPrecioEventoAsync(int eventoId);

    /// <summary>
    /// Revierte un evento de cambio directo de precios.
    /// </summary>
    Task<(bool Exitoso, string Mensaje, int? EventoReversionId)> RevertirCambioPrecioEventoAsync(int eventoId);

    // ============================================
    // CAMBIOS MASIVOS - SIMULACI�N
    // ============================================

    /// <summary>
    /// Simula un cambio masivo de precios y retorna el batch en estado Simulado
    /// </summary>
    /// <param name="nombre">Nombre descriptivo del cambio</param>
    /// <param name="tipoCambio">Tipo de cambio a aplicar</param>
    /// <param name="tipoAplicacion">C�mo se aplica el cambio</param>
    /// <param name="valorCambio">Valor del cambio (% o absoluto)</param>
    /// <param name="listasIds">IDs de listas afectadas</param>
    /// <param name="categoriaIds">IDs de categor�as afectadas (null = todas)</param>
    /// <param name="marcaIds">IDs de marcas afectadas (null = todas)</param>
    /// <param name="productoIds">IDs espec�ficos de productos (null = por categor�a/marca)</param>
    Task<PriceChangeBatch> SimularCambioMasivoAsync(
        string nombre,
        TipoCambio tipoCambio,
        TipoAplicacion tipoAplicacion,
        decimal valorCambio,
        List<int> listasIds,
        List<int>? categoriaIds = null,
        List<int>? marcaIds = null,
        List<int>? productoIds = null);

    /// <summary>
    /// Obtiene el detalle de una simulaci�n existente
    /// </summary>
    Task<PriceChangeBatch?> GetSimulacionAsync(int batchId);

    /// <summary>
    /// Obtiene los items de una simulaci�n con paginaci�n
    /// </summary>
    Task<List<PriceChangeItem>> GetItemsSimulacionAsync(int batchId, int skip = 0, int take = 50);

    /// <summary>
    /// Obtiene los IDs de batches que contienen un producto específico.
    /// </summary>
    Task<List<int>> GetBatchIdsByProductoAsync(int productoId);

    // ============================================
    // CAMBIOS MASIVOS - AUTORIZACI�N
    /// ============================================

    /// <summary>
    /// Aprueba un batch de cambios de precios
    /// Cambia el estado a Aprobado
    /// </summary>
    Task<PriceChangeBatch> AprobarBatchAsync(int batchId, string aprobadoPor, byte[] rowVersion, string? notas = null);

    /// <summary>
    /// Rechaza un batch de cambios de precios
    /// Cambia el estado a Rechazado
    /// </summary>
    Task<PriceChangeBatch> RechazarBatchAsync(int batchId, string rechazadoPor, byte[] rowVersion, string motivo);

    /// <summary>
    /// Cancela un batch antes de aplicarlo
    /// </summary>
    Task<PriceChangeBatch> CancelarBatchAsync(int batchId, string canceladoPor, byte[] rowVersion, string? motivo = null);

    /// <summary>
    /// Verifica si un batch requiere autorizaci�n seg�n umbrales configurados
    /// </summary>
    Task<bool> RequiereAutorizacionAsync(int batchId);

    // ============================================
    // CAMBIOS MASIVOS - APLICACI�N
    // ============================================

    /// <summary>
    /// Aplica un batch de cambios de precios aprobado
    /// Genera nuevas vigencias para todos los productos afectados
    /// Transaccional: todo o nada
    /// </summary>
    Task<PriceChangeBatch> AplicarBatchAsync(int batchId, string aplicadoPor, byte[] rowVersion, DateTime? fechaVigencia = null);

    /// <summary>
    /// Revierte un batch de cambios aplicado
    /// Restaura la vigencia anterior o crea una nueva con los precios previos
    /// </summary>
    Task<PriceChangeBatch> RevertirBatchAsync(int batchId, string revertidoPor, byte[] rowVersion, string motivo);

    // ============================================
    // REPORTES Y ESTAD�STICAS
    // ============================================

    /// <summary>
    /// Obtiene todos los batches con filtros
    /// </summary>
    Task<List<PriceChangeBatch>> GetBatchesAsync(
        EstadoBatch? estado = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int skip = 0,
        int take = 50);

    /// <summary>
    /// Obtiene estad�sticas de un batch aplicado
    /// </summary>
    Task<Dictionary<string, object>> GetEstadisticasBatchAsync(int batchId);

    /// <summary>
    /// Exporta el historial de precios de productos a formato tabular
    /// </summary>
    Task<byte[]> ExportarHistorialPreciosAsync(List<int> productoIds, DateTime fechaDesde, DateTime fechaHasta);

    // ============================================
    // VALIDACIONES Y UTILIDADES
    // ============================================

    /// <summary>
    /// Valida que un precio cumpla con el margen m�nimo configurado
    /// </summary>
    Task<(bool esValido, string? mensaje)> ValidarMargenMinimoAsync(decimal precio, decimal costo, int listaId);

    /// <summary>
    /// Calcula el margen de ganancia
    /// </summary>
    decimal CalcularMargen(decimal precio, decimal costo);

    /// <summary>
    /// Aplica redondeo seg�n reglas de la lista
    /// </summary>
    decimal AplicarRedondeo(decimal precio, string? reglaRedondeo = null);
}
