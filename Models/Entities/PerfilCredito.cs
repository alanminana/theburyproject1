using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Perfiles/Planes de crédito personal predefinidos
    /// Permite configurar múltiples escenarios de crédito (Estándar, Conservador, Riesgoso, etc.)
    /// </summary>
    public class PerfilCredito : AuditableEntity
    {
        /// <summary>
        /// Nombre identificatorio del perfil (ej: "Estándar", "Conservador", "Riesgoso")
        /// </summary>
        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Descripción opcional del perfil
        /// </summary>
        [StringLength(500)]
        public string? Descripcion { get; set; }

        /// <summary>
        /// Tasa de interés mensual del perfil (porcentaje)
        /// Ejemplo: 7.5 significa 7.5% mensual
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(8,4)")]
        [Range(0, 100)]
        public decimal TasaMensual { get; set; }

        /// <summary>
        /// Gastos administrativos del perfil (monto fijo en pesos)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 999999.99)]
        public decimal GastosAdministrativos { get; set; } = 0m;

        /// <summary>
        /// Mínimo de cuotas permitidas en este perfil
        /// </summary>
        [Required]
        [Range(1, 120)]
        public int MinCuotas { get; set; } = 1;

        /// <summary>
        /// Máximo de cuotas permitidas en este perfil
        /// </summary>
        [Required]
        [Range(1, 120)]
        public int MaxCuotas { get; set; } = 24;

        /// <summary>
        /// Indica si este perfil está activo y disponible para usar
        /// </summary>
        public bool Activo { get; set; } = true;

        /// <summary>
        /// Orden de visualización (para ordenar en listas)
        /// </summary>
        public int Orden { get; set; } = 0;
    }
}
