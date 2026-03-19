using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración de alertas de mora por días antes/después del vencimiento
    /// </summary>
    public class AlertaMora : AuditableEntity
    {
        /// <summary>
        /// ID de la configuración de mora a la que pertenece
        /// </summary>
        public int ConfiguracionMoraId { get; set; }

        /// <summary>
        /// Navegación a la configuración de mora
        /// </summary>
        public ConfiguracionMora? ConfiguracionMora { get; set; }

        /// <summary>
        /// Días antes del vencimiento (negativo) o después (positivo)
        /// Ejemplo: -7 = 7 días antes, 0 = día del vencimiento, 30 = 30 días después
        /// </summary>
        [Required]
        public int DiasRelativoVencimiento { get; set; }

        /// <summary>
        /// Color de la alerta en formato hexadecimal (ej: #FF0000 para rojo)
        /// </summary>
        [Required]
        [StringLength(7, MinimumLength = 7)]
        [RegularExpression(@"^#[0-9A-Fa-f]{6}$")]
        public string ColorAlerta { get; set; } = "#FF0000";

        /// <summary>
        /// Descripción de la alerta (ej: "Cuota vencida", "Próximo a vencer")
        /// </summary>
        [StringLength(100)]
        public string? Descripcion { get; set; }

        /// <summary>
        /// Nivel de prioridad de la alerta (1 = menor, 5 = mayor)
        /// </summary>
        public int NivelPrioridad { get; set; } = 1;

        /// <summary>
        /// Si esta alerta está activa
        /// </summary>
        public bool Activa { get; set; } = true;

        /// <summary>
        /// Orden de visualización
        /// </summary>
        public int Orden { get; set; }
    }
}
