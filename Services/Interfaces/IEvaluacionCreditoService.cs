using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio para evaluaci�n autom�tica de solicitudes de cr�dito
    /// Consolida l�gica de scoring, validaciones y c�lculos de capacidad de pago
    /// </summary>
    public interface IEvaluacionCreditoService
    {
        /// <summary>
        /// Realiza evaluaci�n completa de una solicitud de cr�dito
        /// </summary>
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