using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces
{
    public interface ICreditoDisponibleService
    {
        Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default);

        Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default);

        Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default);
    }
}