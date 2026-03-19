using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa un cr�dito otorgado a un cliente
    /// </summary>
    public class Credito  : AuditableEntity
    {
        public int ClienteId { get; set; }

        [StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        public decimal MontoSolicitado { get; set; }
        public decimal MontoAprobado { get; set; }
        public decimal TasaInteres { get; set; } // Tasa mensual
        public int CantidadCuotas { get; set; }
        public decimal MontoCuota { get; set; }

        public decimal CFTEA { get; set; } // Costo Financiero Total Efectivo Anual
        public decimal TotalAPagar { get; set; }
        public decimal SaldoPendiente { get; set; }

        // TAREA 9.3: Auditabilidad del método de cálculo aplicado
        /// <summary>
        /// Método de cálculo usado al configurar este crédito
        /// </summary>
        public MetodoCalculoCredito? MetodoCalculoAplicado { get; set; }

        /// <summary>
        /// Fuente de configuración resultante (compatibilidad TAREA 6)
        /// </summary>
        public FuenteConfiguracionCredito? FuenteConfiguracionAplicada { get; set; }

        /// <summary>
        /// ID del perfil de crédito aplicado (si se usó método UsarPerfil)
        /// </summary>
        public int? PerfilCreditoAplicadoId { get; set; }

        /// <summary>
        /// Nombre del perfil aplicado (snapshot para auditoría)
        /// </summary>
        [StringLength(100)]
        public string? PerfilCreditoAplicadoNombre { get; set; }

        /// <summary>
        /// Gastos administrativos aplicados al momento de la configuración
        /// </summary>
        public decimal GastosAdministrativos { get; set; }

        /// <summary>
        /// Tasa de interés mensual aplicada al momento de la configuración (auditoría)
        /// </summary>
        public decimal? TasaInteresAplicada { get; set; }

        /// <summary>
        /// Mínimo de cuotas permitido al momento de configuración
        /// </summary>
        public int? CuotasMinimasPermitidas { get; set; }

        /// <summary>
        /// Máximo de cuotas permitido al momento de configuración
        /// </summary>
        public int? CuotasMaximasPermitidas { get; set; }

        public EstadoCredito Estado { get; set; } = EstadoCredito.Solicitado;

        public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;
        public DateTime? FechaAprobacion { get; set; }
        public DateTime? FechaFinalizacion { get; set; }
        public DateTime? FechaPrimeraCuota { get; set; }

        public decimal PuntajeRiesgoInicial { get; set; }

        // Garante (opcional)
        public int? GaranteId { get; set; }
        public bool RequiereGarante { get; set; } = false;

        // Datos de aprobaci�n
        [StringLength(100)]
        public string? AprobadoPor { get; set; }

        [StringLength(1000)]
        public string? Observaciones { get; set; }

        // Navigation Properties
        public virtual Cliente Cliente { get; set; } = null!;
        public virtual Garante? Garante { get; set; }
        public virtual PerfilCredito? PerfilCreditoAplicado { get; set; } // TAREA 9.3
        public virtual ICollection<Cuota> Cuotas { get; set; } = new List<Cuota>();
    }
}