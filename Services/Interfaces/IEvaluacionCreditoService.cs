using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio para evaluaci�n autom�tica de solicitudes de cr�dito
    /// Consolida l�gica de scoring, validaciones y c�lculos de capacidad de pago
    /// </summary>
    /// <remarks>
    /// LEGACY: flujo no productivo, sin caller real fuera de tests.
    /// No usar para nuevas validaciones. El flujo canonico es
    /// VentaService/ValidacionVentaService/ClienteAptitudService.
    /// </remarks>
    public interface IEvaluacionCreditoService
    {
        /// <summary>
        /// Realiza evaluaci�n completa de una solicitud de cr�dito
        /// </summary>
        /// <remarks>
        /// LEGACY: no usar para nuevas validaciones. El flujo canonico es
        /// VentaService/ValidacionVentaService/ClienteAptitudService.
        /// TODO: evaluar eliminacion cuando se confirme que no queda ningun caller productivo.
        /// </remarks>
        Task<EvaluacionCreditoViewModel> EvaluarSolicitudAsync(
            int clienteId,
            decimal montoSolicitado,
            int? garanteId = null);

        /// <summary>
        /// Obtiene la �ltima evaluaci�n de un cr�dito
        /// </summary>
        Task<EvaluacionCreditoViewModel?> GetEvaluacionByCreditoIdAsync(int creditoId);

        /// <summary>
        /// Obtiene todas las evaluaciones de un cliente
        /// </summary>
        Task<List<EvaluacionCreditoViewModel>> GetEvaluacionesByClienteIdAsync(int clienteId);

    }
}