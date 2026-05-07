using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces;

public interface ICreditoConfiguracionVentaService
{
    Task<CreditoConfiguracionVentaResultado> ResolverAsync(
        ConfiguracionCreditoVentaViewModel modelo,
        VentaViewModel? venta,
        CancellationToken cancellationToken = default);
}
