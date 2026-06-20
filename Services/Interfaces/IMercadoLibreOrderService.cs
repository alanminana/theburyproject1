using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Órdenes de Mercado Libre (Fases C, D y H).
    /// Reglas duras:
    /// - Idempotencia por MeliOrderId (índice único): importar dos veces actualiza, nunca duplica.
    /// - Una orden genera a lo sumo UNA Venta interna y descuenta stock UNA sola vez.
    /// - La venta ML no toca caja al crearse: queda pendiente de liquidación (Fase D).
    /// - Las devoluciones nunca reingresan stock sin decisión manual (Fase H).
    /// </summary>
    public interface IMercadoLibreOrderService
    {
        /// <summary>
        /// Importa (upsert) una orden desde la API por su id de ML. Si la config
        /// tiene CrearVentaAutomatica y la orden está paga, intenta crear la venta.
        /// </summary>
        Task<MercadoLibreOrder> ImportarOrdenAsync(int accountId, long meliOrderId, CancellationToken ct = default);

        /// <summary>
        /// Importa las órdenes recientes del vendedor (manual, /orders/search).
        /// Devuelve cantidad importada/actualizada.
        /// </summary>
        Task<int> ImportarOrdenesRecientesAsync(int accountId, DateTime? desdeUtc = null, CancellationToken ct = default);

        /// <summary>
        /// Genera una orden local de QA sin llamar a Mercado Libre. Solo debe
        /// habilitarse en Development o con ModoSimulacion activo.
        /// </summary>
        Task<MercadoLibreOrderSimulationResult> CrearOrdenSimuladaAsync(
            string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default);

        /// <summary>
        /// Genera una orden local operativa simulada sin llamar a Mercado Libre.
        /// Se diferencia de la QA bloqueada porque permite probar la creacion de
        /// Venta interna/stock en ModoSimulacion o Development.
        /// </summary>
        Task<MercadoLibreOrderSimulationResult> CrearOrdenOperativaSimuladaAsync(
            string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default);

        /// <summary>
        /// Crea la Venta interna de una orden paga: cliente ML configurado,
        /// todos los ítems vinculados, descuento de stock con la lógica canónica
        /// del ERP (MovimientoStockService) en una única transacción.
        /// </summary>
        Task<MercadoLibreOrderProcessResult> CrearVentaInternaAsync(int orderId, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Registra la liquidación real (neto acreditado por MercadoPago) como
        /// ingreso de caja con concepto LiquidacionMercadoLibre. Requiere caja
        /// abierta del usuario.
        /// </summary>
        Task RegistrarLiquidacionAsync(int orderId, decimal netoReal, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Decisión manual sobre una devolución: reingresar stock, dañado,
        /// garantía, merma o no reingresar. Solo ReingresarStock toca stock.
        /// </summary>
        Task DecidirDevolucionAsync(
            int orderId, MercadoLibreDevolucionEstado decision, string? nota, string usuario, CancellationToken ct = default);

        /// <summary>
        /// Genera localmente un reclamo/devolucion/garantia para QA. No llama a
        /// Mercado Libre y siempre queda pendiente de revision manual.
        /// </summary>
        Task<MercadoLibreOrderSimulationResult> SimularClaimAsync(
            int orderId,
            MercadoLibreClaimTipo tipo,
            string? motivo,
            string usuario,
            bool permitirPorDevelopment = false,
            CancellationToken ct = default);

        /// <summary>
        /// Resuelve manualmente un claim ML. Solo ReingresarStock toca stock; la
        /// accion economica queda registrada, pero no crea caja automaticamente.
        /// </summary>
        Task ResolverClaimAsync(
            int claimId,
            MercadoLibreClaimAccionStock accionStock,
            MercadoLibreClaimAccionEconomica accionEconomica,
            string? resolucionManual,
            string? observaciones,
            string usuario,
            CancellationToken ct = default);

        /// <summary>
        /// Asigna manualmente unidades físicas a una línea de la orden (productos
        /// trazables). La cantidad de unidades debe coincidir con la cantidad de
        /// la línea; todas deben estar EnStock y pertenecer al producto vinculado.
        /// </summary>
        Task AsignarUnidadesAsync(
            int orderId, int orderItemId, IReadOnlyCollection<int> unidadIds, string usuario, CancellationToken ct = default);

        /// <summary>Marca la orden para no procesar (ej: orden de prueba).</summary>
        Task MarcarIgnoradaAsync(int orderId, string usuario, CancellationToken ct = default);

        /// <summary>Consulta y actualiza el estado del envío de la orden (Fase H).</summary>
        Task ActualizarEnvioAsync(int orderId, CancellationToken ct = default);

        /// <summary>
        /// Simula localmente estados de envio sin llamar a Mercado Libre.
        /// Solo debe habilitarse en Development o con ModoSimulacion activo.
        /// </summary>
        Task<MercadoLibreOrderSimulationResult> SimularEnvioAsync(
            int orderId, string escenario, string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default);

        Task<List<MercadoLibreOrderViewModel>> GetOrdenesAsync(string? filtro = null, CancellationToken ct = default);

        Task<MercadoLibreOrderDetalleViewModel?> GetOrdenAsync(int orderId, CancellationToken ct = default);
    }

    /// <summary>
    /// Resultado de intentar crear la venta interna de una orden.
    /// </summary>
    public sealed record MercadoLibreOrderProcessResult(
        bool VentaCreada,
        int? VentaId,
        string? VentaNumero,
        MercadoLibreOrderEstadoInterno EstadoInterno,
        string? Mensaje);

    public sealed record MercadoLibreOrderSimulationResult(
        bool Ok,
        int? OrderId,
        string Mensaje);
}
