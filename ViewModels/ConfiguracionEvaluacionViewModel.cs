using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;

/// <summary>
/// Configuración de parámetros para evaluación crediticia
/// </summary>
public class ConfiguracionEvaluacionViewModel
{
    public int Id { get; set; }
    public decimal PuntajeRiesgoMinimo { get; set; } = 3.0m;
    public decimal RelacionCuotaIngresoMax { get; set; } = 0.35m; // 35%
    public decimal MontoRequiereGarante { get; set; } = 500000m;
    public decimal PuntajeMinimoParaAprobacion { get; set; } = 70m; // 70/100
    public decimal PuntajeMinimoParaAnalisis { get; set; } = 50m;  // 50/100
}

