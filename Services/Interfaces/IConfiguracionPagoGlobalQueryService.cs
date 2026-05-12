using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface IConfiguracionPagoGlobalQueryService
{
    Task<ConfiguracionPagoGlobalResultado> ObtenerActivaParaVentaAsync(
        CancellationToken cancellationToken = default);
}
