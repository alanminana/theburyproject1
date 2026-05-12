using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

/// <summary>
/// Reglas puras para calcular ajustes globales de pago aplicados a una venta completa.
/// No accede a DB ni depende de entidades persistidas.
/// </summary>
public static class ConfiguracionPagoGlobalRules
{
    public static AjustePagoGlobalResultado Calcular(AjustePagoGlobalRequest request)
    {
        if (!request.MedioActivo)
        {
            return Invalido(request, EstadoValidacionPagoGlobal.MedioInactivo, "El medio de pago esta inactivo.");
        }

        if (request.TarjetaActiva == false)
        {
            return Invalido(request, EstadoValidacionPagoGlobal.TarjetaInactiva, "La tarjeta esta inactiva.");
        }

        if (request.PlanActivo == false)
        {
            return Invalido(request, EstadoValidacionPagoGlobal.PlanInactivo, "El plan de pago esta inactivo.");
        }

        if (request.BaseVenta < 0m)
        {
            return Invalido(request, EstadoValidacionPagoGlobal.BaseNegativa, "La base de la venta no puede ser negativa.");
        }

        if (request.CantidadCuotas < 1)
        {
            return Invalido(request, EstadoValidacionPagoGlobal.CuotasInvalidas, "La cantidad de cuotas debe ser al menos 1.");
        }

        var montoAjuste = RedondearMoneda(request.BaseVenta * request.PorcentajeAjuste / 100m);
        var totalFinal = request.BaseVenta + montoAjuste;

        if (totalFinal < 0m)
        {
            return Invalido(request, EstadoValidacionPagoGlobal.DescuentoMayorAlTotal, "El descuento no puede dejar el total por debajo de cero.");
        }

        totalFinal = RedondearMoneda(totalFinal);
        var valorCuota = RedondearMoneda(totalFinal / request.CantidadCuotas);

        return new AjustePagoGlobalResultado
        {
            Estado = EstadoValidacionPagoGlobal.Valido,
            BaseVenta = request.BaseVenta,
            PorcentajeAjuste = request.PorcentajeAjuste,
            MontoAjuste = montoAjuste,
            TotalFinal = totalFinal,
            CantidadCuotas = request.CantidadCuotas,
            ValorCuota = valorCuota
        };
    }

    public static AjustePagoGlobalResultado Calcular(decimal baseVenta, PlanPagoGlobalDto plan, bool medioActivo = true, bool? tarjetaActiva = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return Calcular(new AjustePagoGlobalRequest
        {
            BaseVenta = baseVenta,
            PorcentajeAjuste = plan.AjustePorcentaje,
            CantidadCuotas = plan.CantidadCuotas,
            MedioActivo = medioActivo,
            TarjetaActiva = tarjetaActiva,
            PlanActivo = plan.Activo
        });
    }

    private static AjustePagoGlobalResultado Invalido(
        AjustePagoGlobalRequest request,
        EstadoValidacionPagoGlobal estado,
        string mensaje)
    {
        return new AjustePagoGlobalResultado
        {
            Estado = estado,
            Mensaje = mensaje,
            BaseVenta = request.BaseVenta,
            PorcentajeAjuste = request.PorcentajeAjuste,
            CantidadCuotas = request.CantidadCuotas
        };
    }

    private static decimal RedondearMoneda(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
