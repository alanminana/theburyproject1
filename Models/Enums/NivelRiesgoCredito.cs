using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Enums
{
    /// <summary>
    /// Nivel de riesgo crediticio asignado manualmente al cliente.
    /// Escala de 1 a 5 donde 1 es rechazado y 5 es aprobado sin condiciones.
    /// </summary>
    public enum NivelRiesgoCredito
    {
        /// <summary>
        /// Rechazado - Cliente no apto para crédito
        /// </summary>
        [Display(Name = "1 - Rechazado", Description = "Cliente no apto para crédito")]
        Rechazado = 1,

        /// <summary>
        /// Rechazado con posibilidad de revisión futura
        /// </summary>
        [Display(Name = "2 - Rechazado (Revisar)", Description = "Rechazado pero puede revisarse en el futuro")]
        RechazadoRevisar = 2,

        /// <summary>
        /// Aprobado con condiciones especiales (requiere garante, anticipo mayor, etc.)
        /// </summary>
        [Display(Name = "3 - Aprobado Condicional", Description = "Aprobado con condiciones (garante, anticipo, etc.)")]
        AprobadoCondicional = 3,

        /// <summary>
        /// Aprobado con límite de crédito reducido
        /// </summary>
        [Display(Name = "4 - Aprobado Limitado", Description = "Aprobado con límite reducido")]
        AprobadoLimitado = 4,

        /// <summary>
        /// Aprobado sin condiciones - Cliente de bajo riesgo
        /// </summary>
        [Display(Name = "5 - Aprobado Total", Description = "Aprobado sin restricciones")]
        AprobadoTotal = 5
    }
}
