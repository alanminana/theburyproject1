using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests;

/// <summary>
/// Traducción de una tabla de cuotas globales al resultado canónico de planes, para los stubs
/// de <c>IConfiguracionPagoService</c> que no tocan base.
/// </summary>
/// <remarks>
/// Refleja la regla del Micro-lote 4: los planes activos son la única fuente de cantidades. Sin
/// cuotas globales el stub declara "sin planes globales activos" (rechazo), nunca "usar el rango
/// global": no existe fallback. Un stub no puede producir una intersección vacía entre productos
/// porque no los resuelve.
/// ML2.1 — Contrato congelado: el plan global es la única autoridad del porcentaje, tal cual
/// (espeja <c>ConfiguracionPagoService.ConstruirPlanesSoloGlobales</c>). <paramref
/// name="tasaGlobalUnica"/> se mantiene solo por compatibilidad de firma con callers existentes;
/// YA NO se usa como fallback del porcentaje de un plan con <c>TasaMensual</c> null — eso volvería
/// a introducir la cascada que ML2.1 elimina. Un plan sin porcentaje explícito se traduce tal cual
/// (null = configuración inválida).
/// </remarks>
internal static class PlanesCreditoPersonalStub
{
    public static PlanesCreditoPersonalResultado DesdeGlobales(
        IReadOnlyCollection<CuotaCreditoPersonalViewModel> globales,
        decimal? tasaGlobalUnica = null)
    {
        _ = tasaGlobalUnica; // ML2.1: ya no es fallback del porcentaje: ver remarks.

        if (globales.Count == 0)
            return PlanesCreditoPersonalResultado.SinPlanesGlobales(
                "No hay planes de Credito Personal globales activos.");

        var planes = globales
            .OrderBy(g => g.CantidadCuotas)
            .Select(g => new PlanCuotaCreditoPersonal(
                g.CantidadCuotas,
                g.TasaMensual,
                Array.Empty<int>(),
                true))
            .ToArray();

        return PlanesCreditoPersonalResultado.Resuelto(planes, OrigenPlanesCredito.Global);
    }
}
