using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface ICotizacionService
{
    Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest request, string usuario, CancellationToken cancellationToken = default);
    Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default);
    Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros filtros, CancellationToken cancellationToken = default);
    Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest request, string usuario, CancellationToken cancellationToken = default);
    Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime fechaReferenciaUtc, string usuario, CancellationToken cancellationToken = default);
}
