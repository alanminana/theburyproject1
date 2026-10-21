using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface ICotizacionPdfService
{
    byte[] Generar(CotizacionResultado cotizacion);

    /// <summary>
    /// DESCUENTO-PERMISO-01: con <paramref name="incluirDescuentos"/> en false el PDF omite la columna y el
    /// total de descuentos manuales (usuario sin ventas.discount) y muestra el subtotal neto. Los importes
    /// finales no cambian. Sin implementación por defecto a propósito: una que delegara en
    /// <see cref="Generar(CotizacionResultado)"/> incluiría descuentos (fail-open).
    /// </summary>
    byte[] Generar(CotizacionResultado cotizacion, bool incluirDescuentos);
}
