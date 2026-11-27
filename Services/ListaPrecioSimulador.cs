using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

/// <summary>
/// Simulador de la ayuda de Listas de precios. No lee ni escribe datos: arma una lista transitoria y usa las mismas
/// reglas que el ERP (<see cref="IPrecioService.CalcularPrecioSugerido"/> y <see cref="IPrecioService.CalcularMargen"/>).
/// Solo agrega el desglose para mostrarlo; no define reglas de precio propias.
/// </summary>
public static class ListaPrecioSimulador
{
    public static ListaPrecioSimulacion Simular(
        IPrecioService precios,
        decimal costo,
        decimal? margen,
        decimal? recargo,
        decimal? margenMinimo,
        string? redondeo,
        decimal? precioPrueba)
    {
        var lista = new ListaPrecio
        {
            MargenPorcentaje = margen,
            RecargoPorcentaje = recargo,
            ReglaRedondeo = string.IsNullOrWhiteSpace(redondeo) ? null : redondeo.Trim().ToLowerInvariant()
        };

        var sugerido = precios.CalcularPrecioSugerido(lista, costo);

        // Desglose para el gráfico: el mismo orden que el cálculo (primero margen, después recargo sobre el subtotal).
        var margenImporte = margen is > 0 ? costo * margen.Value / 100 : 0m;
        var subtotal = costo + margenImporte;
        var recargoImporte = recargo is > 0 ? subtotal * recargo.Value / 100 : 0m;
        var antesDeRedondeo = subtotal + recargoImporte;

        var resultado = new ListaPrecioSimulacion
        {
            Costo = costo,
            MargenImporte = margenImporte,
            Subtotal = subtotal,
            RecargoImporte = recargoImporte,
            AntesDeRedondeo = antesDeRedondeo,
            AjusteRedondeo = sugerido - antesDeRedondeo,
            Sugerido = sugerido,
            MargenSugerido = costo > 0 ? precios.CalcularMargen(sugerido, costo) : null,
            // Menor importe (a centavos) cuyo margen sobre el costo alcanza el mínimo.
            PrecioMinimo = margenMinimo.HasValue && costo > 0
                ? Math.Ceiling(costo * (100 + margenMinimo.Value)) / 100
                : null
        };

        resultado.SugeridoBajoMinimo = RechazaPorMinimo(precios, sugerido, costo, margenMinimo);

        if (precioPrueba is > 0)
        {
            resultado.PrecioPrueba = precioPrueba;
            resultado.MargenPrueba = costo > 0 ? precios.CalcularMargen(precioPrueba.Value, costo) : null;
            resultado.PruebaRechazada = RechazaPorMinimo(precios, precioPrueba.Value, costo, margenMinimo);
        }

        return resultado;
    }

    // Misma condición que IPrecioService.SetPrecioManualAsync (sin mínimo o sin costo, no se controla).
    private static bool RechazaPorMinimo(IPrecioService precios, decimal precio, decimal costo, decimal? margenMinimo) =>
        margenMinimo.HasValue && costo > 0 && precios.CalcularMargen(precio, costo) < margenMinimo.Value;
}
