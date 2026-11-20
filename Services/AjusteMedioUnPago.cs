using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

/// <summary>
/// Descuento o recargo de Efectivo / Transferencia. Misma regla que CotizacionPagoCalculator.CrearOpcionUnPago:
/// plan general de 1 pago del medio activo (o, si no existe, cualquier plan general). Fuente única para que la
/// venta convertida desde el cotizador (y su edición posterior) cobre el mismo "Total a cobrar" que se cotizó.
/// </summary>
internal static class AjusteMedioUnPago
{
    public static bool Aplica(TipoPago tipoPago) => tipoPago is TipoPago.Efectivo or TipoPago.Transferencia;

    /// <summary>
    /// P-02 / C5: parte de la base sobre la que se aplica el porcentaje. Con un descuento (porcentaje negativo) y un criterio fiscal que
    /// lo limita a productos (V2) se excluyen los servicios (envío y armado); un recargo o V1 alcanzan a toda la base (null).
    /// </summary>
    public static decimal? BaseSujetaAAjuste(ICriterioFiscal criterio, decimal porcentaje, decimal baseVenta, decimal serviciosEnBase) =>
        criterio.DescuentoDeMedioSoloSobreProductos && porcentaje < 0m && serviciosEnBase > 0m
            ? Math.Max(0m, baseVenta - serviciosEnBase)
            : null;

    /// <returns>El cálculo sobre <paramref name="baseVenta"/>, o null si no hay plan o el ajuste es 0.</returns>
    public static async Task<AjustePagoGlobalResultado?> ResolverAsync(
        AppDbContext context, TipoPago tipoPago, decimal baseVenta, ICriterioFiscal criterio, decimal serviciosEnBase = 0m,
        CancellationToken cancellationToken = default)
    {
        if (!Aplica(tipoPago))
            return null;

        var planes = await context.ConfiguracionPagoPlanes
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Activo && p.TipoPago == tipoPago && p.ConfiguracionTarjetaId == null
                && p.ConfiguracionPago.Activo && !p.ConfiguracionPago.IsDeleted)
            .OrderBy(p => p.Orden)
            .ThenBy(p => p.CantidadCuotas)
            .ToListAsync(cancellationToken);

        var plan = planes.FirstOrDefault(p => p.CantidadCuotas == 1) ?? planes.FirstOrDefault();
        if (plan == null || plan.AjustePorcentaje == 0m)
            return null;

        var calculo = ConfiguracionPagoGlobalRules.Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = baseVenta,
            BaseSujetaAAjuste = BaseSujetaAAjuste(criterio, plan.AjustePorcentaje, baseVenta, serviciosEnBase),
            PorcentajeAjuste = plan.AjustePorcentaje,
            CantidadCuotas = 1,
            MedioActivo = true,
            PlanActivo = plan.Activo
        });

        return calculo.EsValido ? calculo : null;
    }
}
