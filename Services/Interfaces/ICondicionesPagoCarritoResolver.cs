using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface ICondicionesPagoCarritoResolver
{
    Task<CondicionesPagoCarritoResultado> ResolverAsync(
        IEnumerable<int> productoIds,
        TipoPago tipoPago,
        int? configuracionTarjetaId = null,
        decimal? totalReferencia = null,
        int? maxCuotasSinInteresGlobal = null,
        int? maxCuotasConInteresGlobal = null,
        int? maxCuotasCreditoGlobal = null,
        TipoTarjeta? tipoTarjetaLegacy = null,
        CancellationToken cancellationToken = default);

    Task<MediosPagoPorProductoResultado> ObtenerMediosPorProductoAsync(
        int productoId,
        int? configuracionTarjetaId = null,
        CancellationToken cancellationToken = default);
}
