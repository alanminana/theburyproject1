using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    public interface ICreditoDisponibleService
    {
        Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default);

        Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default);

        Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default);

        Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(
            IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items,
            string usuario);

        /// <summary>
        /// Obtiene todos los registros de límites por puntaje ordenados.
        /// </summary>
        Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync();
    }
}