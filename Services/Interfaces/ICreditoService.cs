using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface ICreditoService
    {
        // CRUD b�sico
        Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null);
        Task<CreditoViewModel?> GetByIdAsync(int id);
        Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId);
        Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel);
        Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal);
        Task<bool> UpdateAsync(CreditoViewModel viewModel);
        Task<bool> DeleteAsync(int id);

        // Operaciones de cr�dito
        Task<SimularCreditoViewModel> SimularCreditoAsync(SimularCreditoViewModel modelo);
        Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor);
        Task<bool> RechazarCreditoAsync(int creditoId, string motivo);
        Task<bool> CancelarCreditoAsync(int creditoId, string motivo);
        Task<(bool Success, string? NumeroCredito, string? ErrorMessage)> SolicitarCreditoAsync(
    SolicitudCreditoViewModel solicitud,
    string usuarioSolicitante,
    CancellationToken cancellationToken = default);

        // Operaciones de cuotas
        Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId);
        Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId);
        Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago);
        
        /// <summary>
        /// Adelanta el pago de una cuota (paga la última cuota pendiente para reducir el plazo).
        /// </summary>
        Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago);
        
        /// <summary>
        /// Obtiene la primera cuota pendiente (para pago normal en orden).
        /// </summary>
        Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId);
        
        /// <summary>
        /// Obtiene la última cuota pendiente (para adelanto de cuotas).
        /// </summary>
        Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId);
        
        Task<List<CuotaViewModel>> GetCuotasVencidasAsync();
        Task ActualizarEstadoCuotasAsync();

        // Operaciones de saldo
        Task<bool> RecalcularSaldoCreditoAsync(int creditoId);
    }
}