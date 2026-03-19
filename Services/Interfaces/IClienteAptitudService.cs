using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    /// <summary>
    /// Servicio para evaluación de aptitud crediticia del cliente.
    /// Implementa el semáforo Apto/NoApto/RequiereAutorizacion.
    /// </summary>
    public interface IClienteAptitudService
    {
        #region Evaluación de Aptitud

        /// <summary>
        /// Evalúa la aptitud crediticia completa del cliente.
        /// Considera: documentación, cupo, mora.
        /// </summary>
        /// <param name="clienteId">ID del cliente a evaluar</param>
        /// <param name="guardarResultado">Si true, actualiza el estado en la base de datos</param>
        /// <returns>Resultado de la evaluación con detalles</returns>
        Task<AptitudCrediticiaViewModel> EvaluarAptitudAsync(int clienteId, bool guardarResultado = true);

        /// <summary>
        /// Evalúa la aptitud crediticia del cliente sin persistir cambios.
        /// Útil para preview o simulaciones.
        /// </summary>
        Task<AptitudCrediticiaViewModel> EvaluarAptitudSinGuardarAsync(int clienteId);

        /// <summary>
        /// Obtiene la última evaluación guardada del cliente.
        /// </summary>
        Task<AptitudCrediticiaViewModel?> GetUltimaEvaluacionAsync(int clienteId);

        /// <summary>
        /// Verifica si el cliente está apto para un monto específico de crédito.
        /// </summary>
        Task<(bool EsApto, string? Motivo)> VerificarAptitudParaMontoAsync(int clienteId, decimal monto);

        #endregion

        #region Evaluaciones Parciales

        /// <summary>
        /// Evalúa solo la documentación del cliente.
        /// </summary>
        Task<AptitudDocumentacionDetalle> EvaluarDocumentacionAsync(int clienteId);

        /// <summary>
        /// Evalúa solo el cupo/límite de crédito del cliente.
        /// </summary>
        Task<AptitudCupoDetalle> EvaluarCupoAsync(int clienteId);

        /// <summary>
        /// Evalúa solo el estado de mora del cliente.
        /// </summary>
        Task<AptitudMoraDetalle> EvaluarMoraAsync(int clienteId);

        #endregion

        #region Configuración

        /// <summary>
        /// Obtiene la configuración actual del sistema de aptitud.
        /// </summary>
        Task<ConfiguracionCredito> GetConfiguracionAsync();

        /// <summary>
        /// Actualiza la configuración del sistema de aptitud.
        /// </summary>
        Task<ConfiguracionCredito> UpdateConfiguracionAsync(ConfiguracionCreditoViewModel viewModel);

        /// <summary>
        /// Verifica si la configuración está completa y activa.
        /// </summary>
        Task<(bool EstaConfigurando, string? Mensaje)> VerificarConfiguracionAsync();

        #endregion

        #region Gestión de Límite de Crédito

        /// <summary>
        /// Asigna o actualiza el límite de crédito de un cliente.
        /// </summary>
        Task<bool> AsignarLimiteCreditoAsync(int clienteId, decimal limite, string? motivo = null);

        /// <summary>
        /// Obtiene el cupo disponible actual del cliente.
        /// </summary>
        Task<decimal> GetCupoDisponibleAsync(int clienteId);

        /// <summary>
        /// Obtiene el crédito utilizado actual del cliente.
        /// </summary>
        Task<decimal> GetCreditoUtilizadoAsync(int clienteId);

        #endregion
    }
}
