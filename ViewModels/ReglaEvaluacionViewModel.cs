using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
 
    /// <summary>
    /// Resultado de una regla específica de evaluación
    /// </summary>
    public class ReglaEvaluacionViewModel
    {
        public string Nombre { get; set; } = string.Empty;
        public bool Cumple { get; set; }
        public decimal Peso { get; set; } // Puntos que aporta o resta
        public string? Detalle { get; set; }
        public bool EsCritica { get; set; } // Si el no cumplimiento es crítico
    }

}
