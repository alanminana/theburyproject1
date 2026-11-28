namespace TheBuryProject.Helpers;

/// <summary>
/// Texto legible para las referencias internas que guardan los movimientos de stock y de unidades. El dato guardado no
/// cambia (otros procesos lo usan); solo su presentación. Una referencia no reconocida se muestra tal cual.
/// </summary>
public static class ReferenciaStockUi
{
    public static string? Legible(string? referencia)
    {
        if (string.IsNullOrWhiteSpace(referencia)) return referencia;

        if (referencia == "AltaUnidad") return "Alta de unidad";
        if (referencia.StartsWith("ConciliacionUnidad:", StringComparison.Ordinal)) return "Conciliación con unidades físicas";
        return referencia switch
        {
            "AjusteUnidad:Faltante" => "Unidad marcada como faltante",
            "AjusteUnidad:Baja" => "Baja de unidad",
            "AjusteUnidad:Reintegro" => "Unidad reintegrada a stock",
            _ => referencia
        };
    }
}
