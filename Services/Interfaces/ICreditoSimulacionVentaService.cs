using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface ICreditoSimulacionVentaService
{
    Task<CreditoSimulacionVentaResultado> SimularAsync(
        CreditoSimulacionVentaRequest request,
        CancellationToken cancellationToken = default);
}
