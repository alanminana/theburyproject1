using System.Linq.Expressions;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services;

/// <summary>
/// Qué cuenta como descuento comercial manual (gobernado por ventas.discount): descuento general, descuento (%) de
/// línea o su prorrateo persistido. Los ajustes automáticos del medio de pago NO cuentan. Una sola definición para la
/// visibilidad (<see cref="DescuentoVisibilidadService"/>) y para marcar los documentos al emitirlos.
/// </summary>
internal static class DescuentosComerciales
{
    internal static readonly Expression<Func<Venta, bool>> VentaConDescuento = v =>
        v.Descuento != 0m
        || v.Detalles.Any(d => !d.IsDeleted && (d.Descuento != 0m || d.DescuentoGeneralProrrateado != 0m));

    internal static readonly Expression<Func<Cotizacion, bool>> CotizacionConDescuento = c =>
        c.DescuentoTotal != 0m
        || c.Detalles.Any(d => (d.DescuentoImporteSnapshot ?? 0m) != 0m || (d.DescuentoPorcentajeSnapshot ?? 0m) != 0m);
}
