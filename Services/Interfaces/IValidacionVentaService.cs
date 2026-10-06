using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio de validación unificada para ventas con crédito personal.
    /// Consolida la evaluación de documentación, cupo, mora y permisos.
    /// </summary>
    public interface IValidacionVentaService
    {
        /// <summary>
        /// Prevalida si un cliente puede recibir crédito para el monto especificado.
        /// NO persiste nada; solo devuelve resultado para informar en UI.
        /// Evalúa: documentación, cupo/límite, mora, estado de aptitud.
        /// </summary>
        /// <param name="clienteId">ID del cliente</param>
        /// <param name="monto">Monto a validar</param>
        /// <returns>Resultado de prevalidación: Aprobable, RequiereAutorizacion, o NoViable</returns>
        Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto);

        /// <summary>
        /// Valida si una venta con crédito personal puede proceder.
        /// Evalúa documentación, cupo disponible, mora y estado de aptitud del cliente.
        /// </summary>
        /// <param name="clienteId">ID del cliente</param>
        /// <param name="montoVenta">Monto total de la venta</param>
        /// <param name="creditoId">ID del crédito (opcional, si ya existe)</param>
        /// <returns>Resultado de validación con razones y requisitos</returns>
        Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
            int clienteId, 
            decimal montoVenta, 
            int? creditoId = null);

        /// <summary>
        /// Valida si una venta existente puede ser confirmada.
        /// Re-evalúa todos los requisitos antes de confirmar.
        /// </summary>
        /// <param name="ventaId">ID de la venta</param>
        /// <returns>Resultado de validación</returns>
        Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId);
    }
}
