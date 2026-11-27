namespace TheBuryProject.ViewModels;

/// <summary>Resultado del simulador de la ayuda de Valoración de stock (solo cálculo, no se guarda nada).</summary>
public sealed class ValoracionStockSimulacion
{
    public int Stock { get; set; }
    public decimal? CostoPromedio { get; set; }
    public decimal? UltimoCosto { get; set; }
    public decimal? ValorGerencial { get; set; }
    public decimal? ValorAUltimoCosto { get; set; }
    public decimal? Diferencia { get; set; }
    public string Estado { get; set; } = string.Empty;
}
