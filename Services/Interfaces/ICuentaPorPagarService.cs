using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// D-03: libro administrativo de cuentas por pagar a proveedores (comprobantes, pagos, imputaciones y
    /// notas de crédito). Registro puro: NO mueve Caja, NO toca stock ni costo de producto, NO calcula IVA
    /// crédito fiscal y NO emite comprobantes fiscales; los importes son los informados por el proveedor.
    /// Cada operación que cambia saldos corre en una transacción que serializa por proveedor.
    /// </summary>
    public interface ICuentaPorPagarService
    {
        // ---------- escrituras ----------
        Task<ResultadoCxp> RegistrarComprobanteAsync(ComprobanteProveedorRequest request, string usuario);

        /// <summary>Anula (no borra) un comprobante sin imputaciones vigentes. Exige motivo.</summary>
        Task<ResultadoCxp> AnularComprobanteAsync(int comprobanteId, string? motivo, string usuario);

        /// <summary>
        /// Registra un pago y lo imputa a facturas/notas de débito del mismo proveedor. La suma de las imputaciones
        /// debe igualar el monto del pago y ninguna puede superar el saldo del comprobante.
        /// </summary>
        Task<ResultadoCxp> RegistrarPagoAsync(PagoProveedorRequest request, string usuario);

        /// <summary>Contra-registro: el pago queda como historia y deja de contar para los saldos.</summary>
        Task<ResultadoCxp> AnularPagoAsync(int pagoId, string? motivo, string usuario);

        /// <summary>Aplica una nota de crédito a facturas/notas de débito del mismo proveedor.</summary>
        Task<ResultadoCxp> ImputarNotaCreditoAsync(
            int notaCreditoId, IReadOnlyList<ImputacionRequest> imputaciones, string? claveIdempotencia, string usuario);

        Task<ResultadoCxp> AnularImputacionNotaCreditoAsync(int imputacionId, string? motivo, string usuario);

        // ---------- consultas ----------
        Task<ListadoComprobantesProveedor> ListarComprobantesAsync(FiltroComprobantesProveedor filtro);

        Task<ComprobanteProveedorDetalle?> ObtenerComprobanteAsync(int comprobanteId);

        /// <summary>Facturas y notas de débito vigentes con saldo, ordenadas por vencimiento (las más urgentes primero).</summary>
        Task<List<ComprobanteProveedorItem>> ObtenerPendientesAsync(int proveedorId);

        /// <summary>Notas de crédito vigentes con importe aún sin aplicar.</summary>
        Task<List<ComprobanteProveedorItem>> ObtenerNotasCreditoConSaldoAsync(int proveedorId);

        Task<PagoProveedorItem?> ObtenerPagoAsync(int pagoId);

        Task<List<PagoProveedorItem>> ListarPagosAsync(int proveedorId);

        Task<EstadoCuentaProveedor?> ObtenerEstadoCuentaAsync(int proveedorId);

        /// <summary>Antigüedad de deuda al día comercial actual; <paramref name="proveedorId"/> nulo = todos los proveedores.</summary>
        Task<AntiguedadDeuda> ObtenerAntiguedadAsync(int? proveedorId = null);

        Task<List<ProveedorOpcion>> ObtenerProveedoresAsync();

        Task<ProveedorOpcion?> ObtenerProveedorAsync(int proveedorId);

        Task<List<OrdenCompraOpcion>> ObtenerOrdenesCompraAsync(int proveedorId);

        /// <summary>Resuelve el proveedor de una orden de compra (para prellenar el alta desde la orden).</summary>
        Task<int?> ObtenerProveedorDeOrdenCompraAsync(int ordenCompraId);

        /// <summary>Cheques del proveedor (solo referencia para el pago).</summary>
        Task<List<ChequeOpcion>> ObtenerChequesAsync(int proveedorId);
    }
}
