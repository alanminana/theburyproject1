using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Servicio para gesti�n de devoluciones, garant�as y RMAs
/// </summary>
public interface IDevolucionService
{
    // ============================================
    // DEVOLUCIONES
    // ============================================

    Task<List<Devolucion>> ObtenerTodasDevolucionesAsync();
    Task<List<Devolucion>> ObtenerDevolucionesPorClienteAsync(int clienteId);
    Task<List<Devolucion>> ObtenerDevolucionesPorEstadoAsync(EstadoDevolucion estado);
    Task<Devolucion?> ObtenerDevolucionAsync(int id);
    Task<Devolucion?> ObtenerDevolucionPorNumeroAsync(string numeroDevolucion);
    Task<Devolucion> CrearDevolucionAsync(Devolucion devolucion, List<DevolucionDetalle> detalles);
    Task<Devolucion> ActualizarDevolucionAsync(Devolucion devolucion);
    Task<Devolucion> AprobarDevolucionAsync(int id, string aprobadoPor, byte[] rowVersion);
    Task<Devolucion> RechazarDevolucionAsync(int id, string motivo, byte[] rowVersion);
    Task<Devolucion> CompletarDevolucionAsync(int id, byte[] rowVersion);
    Task<string> GenerarNumeroDevolucionAsync();
    Task<bool> PuedeDevolverVentaAsync(int ventaId);
    Task<int> ObtenerDiasDesdeVentaAsync(int ventaId);

    // ============================================
    // DETALLES DE DEVOLUCI�N
    // ============================================

    Task<List<DevolucionDetalle>> ObtenerDetallesDevolucionAsync(int devolucionId);
    Task<DevolucionDetalle> AgregarDetalleAsync(DevolucionDetalle detalle);
    Task<DevolucionDetalle> ActualizarEstadoProductoAsync(int detalleId, EstadoProductoDevuelto estado, AccionProducto accion);
    Task<bool> VerificarAccesoriosAsync(int detalleId, bool completos, string? faltantes);

    // ============================================
    // GARANT�AS
    // ============================================

    Task<List<Garantia>> ObtenerTodasGarantiasAsync();
    Task<List<Garantia>> ObtenerGarantiasVigentesAsync();
    Task<List<Garantia>> ObtenerGarantiasPorClienteAsync(int clienteId);
    Task<Garantia?> ObtenerGarantiaAsync(int id);
    Task<Garantia?> ObtenerGarantiaPorNumeroAsync(string numeroGarantia);
    Task<Garantia> CrearGarantiaAsync(Garantia garantia);
    Task<Garantia> ActualizarGarantiaAsync(Garantia garantia);
    Task<bool> ValidarGarantiaVigenteAsync(int garantiaId);
    Task<List<Garantia>> ObtenerGarantiasProximasVencerAsync(int dias = 30);
    Task<string> GenerarNumeroGarantiaAsync();

    // ============================================
    // RMA (Return Merchandise Authorization)
    // ============================================

    Task<List<RMA>> ObtenerTodosRMAsAsync();
    Task<List<RMA>> ObtenerRMAsPorEstadoAsync(EstadoRMA estado);
    Task<List<RMA>> ObtenerRMAsPorProveedorAsync(int proveedorId);
    Task<RMA?> ObtenerRMAAsync(int id);
    Task<RMA?> ObtenerRMAPorNumeroAsync(string numeroRMA);
    Task<RMA> CrearRMAAsync(RMA rma, byte[] devolucionRowVersion);
    Task<RMA> ActualizarRMAAsync(RMA rma);
    Task<RMA> AprobarRMAProveedorAsync(int rmaId, string numeroRMAProveedor);
    Task<RMA> RegistrarEnvioRMAAsync(int rmaId, string numeroGuia);
    Task<RMA> RegistrarRecepcionProveedorAsync(int rmaId);
    Task<RMA> ResolverRMAAsync(int rmaId, TipoResolucionRMA tipoResolucion, decimal? montoReembolso, string detalleResolucion);
    Task<string> GenerarNumeroRMAAsync();

    // ============================================
    // NOTAS DE CR�DITO
    // ============================================

    Task<List<NotaCredito>> ObtenerTodasNotasCreditoAsync();
    Task<List<NotaCredito>> ObtenerNotasCreditoPorClienteAsync(int clienteId);
    Task<List<NotaCredito>> ObtenerNotasCreditoVigentesAsync(int clienteId);
    Task<NotaCredito?> ObtenerNotaCreditoAsync(int id);
    Task<NotaCredito?> ObtenerNotaCreditoPorNumeroAsync(string numeroNotaCredito);
    Task<NotaCredito> CrearNotaCreditoAsync(NotaCredito notaCredito);
    Task<NotaCredito> UtilizarNotaCreditoAsync(int notaCreditoId, decimal monto);
    Task<decimal> ObtenerCreditoDisponibleClienteAsync(int clienteId);
    Task<string> GenerarNumeroNotaCreditoAsync();

    // ============================================
    // REPORTES Y ESTAD�STICAS
    // ============================================

    Task<Dictionary<MotivoDevolucion, int>> ObtenerEstadisticasMotivoDevolucionAsync(DateTime? desde = null, DateTime? hasta = null);
    Task<List<Producto>> ObtenerProductosMasDevueltosAsync(int top = 10);
    Task<decimal> ObtenerTotalDevolucionesPeriodoAsync(DateTime desde, DateTime hasta);
    Task<int> ObtenerCantidadRMAsPendientesAsync();
}