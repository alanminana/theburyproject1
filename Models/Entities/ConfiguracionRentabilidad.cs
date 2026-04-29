using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración global para clasificación de rentabilidad en reportes.
    /// </summary>
    public class ConfiguracionRentabilidad : AuditableEntity
    {
        [Range(0, 100)]
        public decimal MargenBajoMax { get; set; } = 20m;

        [Range(0, 100)]
        public decimal MargenAltoMin { get; set; } = 35m;
    }
}
