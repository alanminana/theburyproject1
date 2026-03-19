using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuraci�n general de tipo de pago
    /// </summary>
    public class ConfiguracionPago  : AuditableEntity
    {
        [Required]
        public TipoPago TipoPago { get; set; }

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public bool Activo { get; set; } = true;

        // Descuento
        public bool PermiteDescuento { get; set; } = false;
        public decimal? PorcentajeDescuentoMaximo { get; set; }

        // Recargo
        public bool TieneRecargo { get; set; } = false;
        public decimal? PorcentajeRecargo { get; set; }

        // ============================================================
        // CRÉDITO PERSONAL - DEFAULTS GLOBALES (TAREA 7.1.1)
        // ============================================================
        
        /// <summary>
        /// Tasa de interés mensual default para crédito personal (%)
        /// Este valor se usa como fallback cuando no hay perfil seleccionado
        /// </summary>
        [Column(TypeName = "decimal(8,4)")]
        public decimal? TasaInteresMensualCreditoPersonal { get; set; }

        /// <summary>
        /// Gastos administrativos default para crédito personal ($)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal? GastosAdministrativosDefaultCreditoPersonal { get; set; }

        /// <summary>
        /// Mínimo de cuotas default para crédito personal
        /// </summary>
        public int? MinCuotasDefaultCreditoPersonal { get; set; }

        /// <summary>
        /// Máximo de cuotas default para crédito personal
        /// </summary>
        public int? MaxCuotasDefaultCreditoPersonal { get; set; }

        // Relaciones espec�ficas
        public virtual ICollection<ConfiguracionTarjeta> ConfiguracionesTarjeta { get; set; } = new List<ConfiguracionTarjeta>();
    }
}
