using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface ICotizacionConversionService
{
    Task<CotizacionConversionPreviewResultado> PreviewConversionAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default);

    Task<CotizacionConversionResultado> ConvertirAVentaAsync(
        int cotizacionId,
        CotizacionConversionRequest request,
        string usuario,
        CancellationToken cancellationToken = default);
}
