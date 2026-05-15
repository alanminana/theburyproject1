using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface ICotizacionPagoCalculator
{
    Task<CotizacionSimulacionResultado> SimularAsync(
        CotizacionSimulacionRequest request,
        CancellationToken cancellationToken = default);
}
