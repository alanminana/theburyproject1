using TheBuryProject.Models.DTOs;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

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
        Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor);
        Task<bool> RechazarCreditoAsync(int creditoId, string motivo);
        Task<bool> CancelarCreditoAsync(int creditoId, string motivo);

        // Operaciones de cuotas
        Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId);
        Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId);

        /// <summary>Obtiene el contexto autoritativo del pago individual por Id de cuota.</summary>
        Task<PagoCuotaContextoResultado?> ObtenerContextoPagoCuotaAsync(
            int cuotaId,
            CancellationToken cancellationToken = default);

        /// <summary>Calcula un preview read-only con las mismas reglas que la confirmación.</summary>
        Task<PagoCuotaPreviewResultado?> PrevisualizarPagoCuotaAsync(
            PagoCuotaIndividualComando comando,
            CancellationToken cancellationToken = default);

        /// <summary>Confirma el pago individual y devuelve el resultado persistido real.</summary>
        Task<PagoCuotaResultado?> RegistrarPagoCuotaIndividualAsync(
            PagoCuotaIndividualComando comando,
            CancellationToken cancellationToken = default);

        // Contrato legacy conservado para primera cuota, adelanto y consumidores existentes.
        Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago);
        Task<PagoMultipleCuotasResult> PagarCuotasAsync(
            PagoMultipleCuotasRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Cobra la primera cuota del crédito recién generado si vence hoy (fecha comercial de
        /// Argentina) y está pendiente. Reutiliza <see cref="PagarCuotaAsync"/>, por lo que aplica
        /// el recargo del medio de pago como concepto separado e impacta en caja. Si la cuota no
        /// vence hoy o no está pendiente devuelve <see cref="EstadoCobroPrimeraCuota.NoAplica"/>
        /// sin cobrar. Requiere una caja abierta.
        /// </summary>
        /// <param name="medioPago">
        /// Medio de pago. Si es null/vacío, se toma el medio persistido en la decisión de
        /// configuración del crédito (F2, server-authoritative).
        /// </param>
        Task<CobroPrimeraCuotaResultado> CobrarPrimeraCuotaAlGenerarAsync(
            int creditoId,
            string? medioPago = null,
            string? comprobante = null,
            string? observaciones = null);

        /// <summary>
        /// Adelanta el pago de una cuota (paga la última cuota pendiente para reducir el plazo).
        /// Contrato legacy conservado: sigue tomando el importe del formulario clásico. La UI de
        /// adelanto (PUN-ML9-E) usa el contrato comando-based de abajo.
        /// </summary>
        Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago);

        /// <summary>PUN-ML9-E: contexto autoritativo de la última cuota adelantable del crédito.</summary>
        Task<PagoCuotaContextoResultado?> ObtenerContextoAdelantoAsync(
            int creditoId,
            CancellationToken cancellationToken = default);

        /// <summary>PUN-ML9-E: preview read-only del adelanto, misma autoridad que la confirmación.</summary>
        Task<PagoCuotaPreviewResultado?> PrevisualizarAdelantoAsync(
            AdelantoCuotaComando comando,
            CancellationToken cancellationToken = default);

        /// <summary>PUN-ML9-E: confirma el adelanto y devuelve el resultado persistido real.</summary>
        Task<PagoCuotaResultado?> RegistrarAdelantoAsync(
            AdelantoCuotaComando comando,
            CancellationToken cancellationToken = default);

        /// <summary>PUN-ML9-E: preview read-only del pago múltiple, misma autoridad que la confirmación.</summary>
        Task<PagoMultiplePreviewResultado> PrevisualizarPagoMultipleAsync(
            int clienteId,
            List<int> cuotaIds,
            string medioPago,
            CancellationToken cancellationToken = default);

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

        /// <summary>
        /// Aplica la configuración de un crédito (tasa, cuotas, método, perfil) y actualiza
        /// el estado de la venta asociada si corresponde. Todos los parámetros ya deben estar
        /// resueltos por el caller; este método solo persiste y cambia estados.
        /// </summary>
        Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando);
    }
}
