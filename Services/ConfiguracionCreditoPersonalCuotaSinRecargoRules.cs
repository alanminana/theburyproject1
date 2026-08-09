namespace TheBuryProject.Services;

/// <summary>
/// Reglas puras de validación para la selección de "cuotas sin recargo" de un plan global de
/// Crédito Personal (CSR-ML2, ver <see cref="TheBuryProject.Models.Entities.ConfiguracionCreditoPersonalCuotaSinRecargo"/>).
/// No accede a DB ni depende de entidades persistidas — mismo estilo que
/// <see cref="ConfiguracionPagoGlobalRules"/>.
/// </summary>
public static class ConfiguracionCreditoPersonalCuotaSinRecargoRules
{
    /// <summary>
    /// Valida una selección candidata de números de cuota sin recargo contra el plan al que
    /// pertenece. Reglas:
    /// <list type="bullet">
    /// <item>cada número debe estar en [1, <paramref name="cantidadCuotas"/>];</item>
    /// <item>no se permiten duplicados;</item>
    /// <item>si <paramref name="tasaMensual"/> es explícito y mayor a 0, debe quedar al menos una
    /// cuota con recargo (no se puede marcar la totalidad); con 0% puede marcarse la totalidad
    /// (financieramente da lo mismo); con <c>null</c> el plan ya es inválido por el contrato
    /// ML2.1 congelado (plan activo sin porcentaje explícito) — esa regla no se reabre aquí, así
    /// que esta validación no aplica en ese caso.</item>
    /// </list>
    /// Lista vacía de retorno = selección válida.
    /// </summary>
    public static List<string> Validar(int cantidadCuotas, decimal? tasaMensual, IReadOnlyList<int>? numerosCuota)
    {
        var errores = new List<string>();
        numerosCuota ??= Array.Empty<int>();

        var fueraDeRango = numerosCuota.Where(n => n < 1 || n > cantidadCuotas).ToList();
        if (fueraDeRango.Count > 0)
            errores.Add(
                $"Numeros de cuota fuera de rango 1-{cantidadCuotas}: {string.Join(", ", fueraDeRango)}.");

        var duplicados = numerosCuota
            .GroupBy(n => n)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicados.Count > 0)
            errores.Add($"Numeros de cuota duplicados: {string.Join(", ", duplicados)}.");

        if (tasaMensual.HasValue && tasaMensual.Value > 0m && numerosCuota.Distinct().Count() >= cantidadCuotas)
        {
            errores.Add(
                "Con un recargo total mayor a 0% debe quedar al menos una cuota con recargo: no se " +
                $"puede marcar sin recargo la totalidad de las {cantidadCuotas} cuotas.");
        }

        return errores;
    }
}
