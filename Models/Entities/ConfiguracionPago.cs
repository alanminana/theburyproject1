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

        // Crédito personal - defaults globales
        
        /// <summary>
        /// Porcentaje de recargo TOTAL único global de Crédito Personal (no una tasa mensual
        /// ni compuesta: se aplica una sola vez sobre el saldo financiado). El nombre de la
        /// propiedad es legacy y se conserva por compatibilidad de columna; ver
        /// <see cref="Services.Interfaces.IConfiguracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync"/>
        /// para la fuente canónica de lectura. null = nunca configurado; 0 = recargo cero
        /// explícito y válido.
        /// </summary>
        /// <remarks>
        /// ML2.1/ML3 — Contrato congelado: SIN autoridad financiera sobre planes de
        /// <see cref="ConfiguracionCreditoPersonalCuota"/>. Un plan activo con <c>TasaMensual</c>
        /// null es configuración inválida, nunca cae a este valor (dejó de ser fallback). Solo se
        /// lee como tasa efectiva en <see cref="Services.CreditoConfiguracionVentaService"/> /
        /// <see cref="Services.CreditoSimulacionVentaService"/> cuando no existe ninguna tabla de
        /// planes en absoluto (<c>RigeConfiguracionUnicaGlobal</c>, solo dobles de test — el
        /// resolutor productivo no emite ese caso) y como gate de "tasa global no configurada".
        /// </remarks>
        [Column(TypeName = "decimal(8,4)")]
        public decimal? TasaInteresMensualCreditoPersonal { get; set; }

        /// <summary>
        /// Gastos administrativos default para crédito personal ($)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal? GastosAdministrativosDefaultCreditoPersonal { get; set; }

        /// <summary>
        /// LEGACY INERTE (Micro-lote 4). Antiguo rango mínimo de cuotas default de crédito personal.
        /// La disponibilidad de cuotas surge exclusivamente de los planes activos
        /// (<see cref="ConfiguracionCreditoPersonalCuota"/>): esta columna ya no se lee ni se escribe.
        /// Se conserva físicamente para no aplicar una migración destructiva sobre la base viva.
        /// </summary>
        public int? MinCuotasDefaultCreditoPersonal { get; set; }

        /// <summary>
        /// LEGACY INERTE (Micro-lote 4). Antiguo rango máximo de cuotas default de crédito personal.
        /// La disponibilidad de cuotas surge exclusivamente de los planes activos
        /// (<see cref="ConfiguracionCreditoPersonalCuota"/>): esta columna ya no se lee ni se escribe.
        /// Se conserva físicamente para no aplicar una migración destructiva sobre la base viva.
        /// </summary>
        public int? MaxCuotasDefaultCreditoPersonal { get; set; }

        // Relaciones espec�ficas
        public virtual ICollection<ConfiguracionTarjeta> ConfiguracionesTarjeta { get; set; } = new List<ConfiguracionTarjeta>();
        public virtual ICollection<ConfiguracionPagoPlan> PlanesPago { get; set; } = new List<ConfiguracionPagoPlan>();
    }
}
