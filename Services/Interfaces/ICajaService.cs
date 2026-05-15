using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio centralizado para gestión de cajas, aperturas, movimientos y cierres
    /// </summary>
    public interface ICajaService
    {
        #region CRUD de Cajas

        Task<List<Caja>> ObtenerTodasCajasAsync();
        Task<Caja?> ObtenerCajaPorIdAsync(int id);
        Task<Caja> CrearCajaAsync(CajaViewModel model);
        Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model);
        Task EliminarCajaAsync(int id, byte[]? rowVersion = null);
        Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null);

        #endregion

        #region Apertura de Caja

        Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario);
        Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId);
        Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id);
        Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync();
        Task<bool> TieneCajaAbiertaAsync(int cajaId);
        Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario);
        
        /// <summary>
        /// Verifica si existe al menos una caja abierta en el sistema.
        /// Usado para validar que se pueden realizar ventas.
        /// </summary>
        Task<bool> ExisteAlgunaCajaAbiertaAsync();

        #endregion

        #region Movimientos de Caja

        Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario);
        Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId);
        Task<decimal> CalcularSaldoActualAsync(int aperturaId);

        /// <summary>
        /// Calcula el saldo real de caja: solo dinero efectivamente acreditado.
        /// Incluye ingresos Acreditados (o sin estado), egresos sin estado, y
        /// egresos ReversionVenta solo si el ingreso original era Acreditado.
        /// </summary>
        Task<decimal> CalcularSaldoRealAsync(int aperturaId);

        /// <summary>
        /// Marca un movimiento de ingreso como Acreditado.
        /// Solo admite la transición Pendiente → Acreditado.
        /// </summary>
        Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario);

        /// <summary>
        /// Registra automáticamente el movimiento de caja para una venta confirmada.
        /// Solo aplica para ventas de contado (Efectivo, Tarjeta, Cheque, Transferencia, MercadoPago).
        /// Las ventas a crédito personal no generan ingreso inmediato (se cobra por cuotas).
        /// </summary>
        /// <param name="ventaId">ID de la venta</param>
        /// <param name="ventaNumero">Número de la venta (para referencia)</param>
        /// <param name="monto">Monto total de la venta</param>
        /// <param name="tipoPago">Tipo de pago de la venta</param>
        /// <param name="usuario">Usuario que confirma la venta</param>
        /// <returns>MovimientoCaja creado, o null si no aplica (ej: crédito personal)</returns>
        Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(
            int ventaId,
            string ventaNumero,
            decimal monto,
            TipoPago tipoPago,
            string usuario);

        /// <summary>
        /// Obtiene la primera apertura de caja activa disponible para registrar una venta.
        /// </summary>
        Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync();

        /// <summary>
        /// Registra automáticamente el movimiento de caja para el pago de una cuota de crédito.
        /// </summary>
        /// <param name="cuotaId">ID de la cuota pagada</param>
        /// <param name="creditoNumero">Número del crédito (para referencia)</param>
        /// <param name="numeroCuota">Número de la cuota</param>
        /// <param name="monto">Monto pagado</param>
        /// <param name="medioPago">Medio de pago (Efectivo, Tarjeta, etc.)</param>
        /// <param name="usuario">Usuario que registra el pago</param>
        /// <returns>MovimientoCaja creado, o null si no hay caja abierta</returns>
        Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
            int cuotaId,
            string creditoNumero,
            int numeroCuota,
            decimal monto,
            string medioPago,
            string usuario);

        /// <summary>
        /// Registra automáticamente el movimiento de caja para un anticipo de crédito.
        /// El anticipo es un pago inicial que reduce el monto a financiar.
        /// </summary>
        /// <param name="creditoId">ID del crédito</param>
        /// <param name="creditoNumero">Número del crédito (para referencia)</param>
        /// <param name="montoAnticipo">Monto del anticipo</param>
        /// <param name="usuario">Usuario que registra el anticipo</param>
        /// <returns>MovimientoCaja creado, o null si no hay caja abierta o anticipo es 0</returns>
        Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(
            int creditoId,
            string creditoNumero,
            decimal montoAnticipo,
            string usuario);

        /// <summary>
        /// Registra un contramovimiento de egreso en caja para neutralizar el ingreso
        /// generado por una venta que se está cancelando.
        /// Retorna null si la venta no tenía ingreso de caja (crédito personal, cotización, etc.)
        /// o si ya existía un contramovimiento previo (guard anti-duplicación).
        /// </summary>
        Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(
            int ventaId,
            string ventaNumero,
            string motivo,
            string usuario);

        /// <summary>
        /// Registra el egreso de caja por reembolso al cliente al completar una devolución.
        /// </summary>
        Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(
            int devolucionId,
            int ventaId,
            string ventaNumero,
            string devolucionNumero,
            decimal monto,
            string usuario);

        #endregion

        #region Cierre de Caja

        Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario);
        Task<CierreCaja?> ObtenerCierrePorIdAsync(int id);
        Task<List<CierreCaja>> ObtenerHistorialCierresAsync(
            int? cajaId = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null);

        #endregion

        #region Reportes y Estadsticas

        Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId);
        Task<ReporteCajaViewModel> GenerarReporteCajaAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            int? cajaId = null);
        Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(
            int? cajaId = null,
            DateTime? fechaDesde = null,
            DateTime? fechaHasta = null);

        #endregion
    }
}
