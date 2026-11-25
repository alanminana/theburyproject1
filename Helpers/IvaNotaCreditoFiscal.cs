namespace TheBuryProject.Helpers;

/// <summary>
/// Neteo INFORMATIVO del IVA de una nota de crédito fiscal externa vinculada (criterio interno del propietario, 2026-11-25; no es una
/// certificación fiscal del contador). Reparte el importe de la NC fiscal entre las alícuotas de lo devuelto y separa neto e IVA de cada una.
/// Función pura: no lee ni modifica ninguna venta, devolución ni importe persistido.
/// </summary>
public static class IvaNotaCreditoFiscal
{
    public sealed record Parte(decimal Porcentaje, decimal Neto, decimal Iva, decimal Total);

    /// <summary>
    /// Reparte <paramref name="importe"/> (total con IVA de la NC fiscal) proporcionalmente a <paramref name="pesos"/> (bruto devuelto por alícuota).
    /// Garantías: la suma de los totales de las partes es EXACTAMENTE el importe (el último reparto absorbe el residuo); en cada parte
    /// <c>Neto + Iva = Total</c> (el redondeo del neto se absorbe en el IVA, igual que en la venta); alícuota 0 no genera IVA.
    /// Sin pesos positivos o con importe no positivo devuelve una lista vacía (el llamador lo informa, nunca inventa un desglose).
    /// </summary>
    public static IReadOnlyList<Parte> Repartir(decimal importe, IEnumerable<(decimal Porcentaje, decimal Peso)> pesos)
    {
        if (importe <= 0m) return Array.Empty<Parte>();

        var grupos = pesos
            .Where(p => p.Peso > 0m)
            .GroupBy(p => p.Porcentaje)
            .Select(g => (Porcentaje: g.Key, Peso: g.Sum(x => x.Peso)))
            .OrderByDescending(g => g.Peso).ThenBy(g => g.Porcentaje)
            .ToList();
        var pesoTotal = grupos.Sum(g => g.Peso);
        if (grupos.Count == 0 || pesoTotal <= 0m) return Array.Empty<Parte>();

        var partes = new List<Parte>(grupos.Count);
        var asignado = 0m;
        for (var i = 0; i < grupos.Count; i++)
        {
            var total = i == grupos.Count - 1
                ? Math.Round(importe - asignado, 2, MidpointRounding.AwayFromZero)
                : Math.Round(importe * grupos[i].Peso / pesoTotal, 2, MidpointRounding.AwayFromZero);
            asignado += total;
            var divisor = 1m + grupos[i].Porcentaje / 100m;
            var neto = grupos[i].Porcentaje > 0m ? Math.Round(total / divisor, 2, MidpointRounding.AwayFromZero) : total;
            partes.Add(new Parte(grupos[i].Porcentaje, neto, total - neto, total));
        }
        return partes;
    }
}
