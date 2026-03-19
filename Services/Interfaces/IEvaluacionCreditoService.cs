using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio para evaluación automática de solicitudes de crédito
    /// Consolida lógica de scoring, validaciones y cálculos de capacidad de pago
    /// </summary>
    public interface IEvaluacionCreditoService
    {
        /// <summary>
        /// Realiza evaluación completa de una solicitud de crédito
        /// </summary>
        Task<EvaluacionCreditoViewModel> EvaluarSolicitudAsync(
            int clienteId,
            decimal montoSolicitado,
            int? garanteId = null);

        /// <summary>
        /// Obtiene la última evaluación de un crédito
        /// </summary>
        Task<EvaluacionCreditoViewModel?> GetEvaluacionByCreditoIdAsync(int creditoId);

        /// <summary>
        /// Obtiene todas las evaluaciones de un cliente
        /// </summary>
        Task<List<EvaluacionCreditoViewModel>> GetEvaluacionesByClienteIdAsync(int clienteId);

        /// <summary>
        /// Obtiene configuración de parámetros de evaluación
        /// </summary>
        Task<ConfiguracionEvaluacionViewModel> GetConfiguracionAsync();

        /// <summary>
        /// Actualiza configuración de parámetros de evaluación
        /// </summary>
        Task<bool> ActualizarConfiguracionAsync(ConfiguracionEvaluacionViewModel config);
    }
}