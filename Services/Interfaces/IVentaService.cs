using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Services.Interfaces
{
    public interface IVentaService
    {
        Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null);
        Task<VentaViewModel?> GetByIdAsync(int id);
        Task<VentaViewModel> CreateAsync(VentaViewModel viewModel);
        Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel);
        Task<bool> DeleteAsync(int id);
        Task<bool> PrepararVentaDesdeCotizacionAsync(int id)
        {
            throw new NotImplementedException();
        }

        // VENTA-COTIZACION-REWORK-03 (auditoría en vivo del usuario, 2026-09-15): expone la
        // misma lógica de autorización/creación de crédito que ya usa CreateAsync (antes
        // privada), para que quien cree una Venta con TipoPago=CreditoPersonal FUERA del POST
        // estándar de Venta/Create — hoy sólo CotizacionConversionService — reutilice
        // exactamente el mismo proceso real en vez de inventar un segundo camino de
        // autorización. Default en NotImplementedException: ningún stub de test existente que
        // implementa esta interfaz necesita tocarse salvo que efectivamente ejerza esta rama
        // (CotizacionConversionService sólo la llama cuando TipoPago == CreditoPersonal).

        /// <summary>
        /// Aplica el resultado de <see cref="IValidacionVentaService.ValidarVentaCreditoPersonalAsync"/>
        /// a una <see cref="Venta"/> con TipoPago=CreditoPersonal AÚN NO GUARDADA (setea
        /// RequiereAutorizacion/EstadoAutorizacion/RazonesAutorizacionJson/Estado). Debe llamarse
        /// antes de persistir la venta. <paramref name="validacion"/> no debe tener NoViable=true
        /// (mismo contrato que la rama interna de CreateAsync: el caller decide qué hacer con un
        /// NoViable antes de llegar acá — para conversión de cotización, rechazar esa alternativa).
        /// </summary>
        Task AplicarResultadoValidacionAsync(Venta venta, ValidacionVentaResult validacion, string usuarioActual)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Crea el <see cref="Models.Entities.Credito"/> real (PendienteConfiguracion) para una
        /// venta con TipoPago=CreditoPersonal ya persistida (requiere <c>venta.Id</c>) y aprobable
        /// (RequiereAutorizacion=false). Si <c>venta.CotizacionOrigenId</c> está seteado, precarga
        /// cuotas/anticipo intencionados desde esa cotización (mismo comportamiento que ya tiene
        /// para ventas creadas directamente desde Cotización vía CreateAsync).
        /// </summary>
        Task CrearCreditoPendienteParaVentaAsync(Venta venta)
        {
            throw new NotImplementedException();
        }

        Task<bool> ConfirmarVentaAsync(int id);
        /// <summary>
        /// Confirma una venta con crédito personal: genera cuotas, marca crédito como Generado
        /// </summary>
        Task<bool> ConfirmarVentaCreditoAsync(int id);
        Task<bool> CancelarVentaAsync(int id, string motivo);
        Task AsociarCreditoAVentaAsync(int ventaId, int creditoId);
        Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel);
        Task<int?> AnularFacturaAsync(int facturaId, string motivo);
        Task<bool> ValidarStockAsync(int ventaId);

        // Autorización
        Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo);
        Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo);
        Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo);
        Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo);
        Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel);

        // Métodos para datos adicionales
        Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta);
        Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque);
        Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas);
        Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId);
        Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto);

        CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje);
        Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje);
        Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewConPagoGlobalAsync(
            List<DetalleCalculoVentaRequest> detalles,
            decimal descuentoGeneral,
            bool descuentoEsPorcentaje,
            global::TheBuryProject.Models.Enums.TipoPago tipoPago,
            int? configuracionTarjetaId,
            int? configuracionPagoPlanId)
        {
            return CalcularTotalesPreviewAsync(detalles, descuentoGeneral, descuentoEsPorcentaje);
        }

        /// <summary>
        /// Resuelve el total efectivo de una venta: usa venta.Total si es válido,
        /// o recalcula desde los detalles (subtotal − descuento + IVA).
        /// Devuelve null si la venta no existe.
        /// </summary>
        Task<decimal?> GetTotalVentaAsync(int ventaId);
    }
}
