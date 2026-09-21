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

    // COTIZACION-MIVENTA-02: chequeo de sólo lectura (no persiste nada) para saber, ANTES de
    // crear la Venta, si "Confirmar Mi Venta" podría terminar de verdad — ver comentario de
    // CotizacionMiVentaPreflightResultado.
    Task<CotizacionMiVentaPreflightResultado> PreflightConversionAsync(
        int cotizacionId,
        CotizacionConversionRequest request,
        string usuario,
        CancellationToken cancellationToken = default);

    // COTIZACION-MIVENTA-02: preview de Subtotal/IVA/alícuotas para el modal "Facturar al
    // confirmar", sin crear ninguna Venta ni Factura.
    Task<CotizacionFacturaPreviewResultado> PreviewFacturaAsync(
        int cotizacionId,
        CancellationToken cancellationToken = default);
}
