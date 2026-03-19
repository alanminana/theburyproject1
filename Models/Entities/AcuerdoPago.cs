using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Acuerdo de refinanciación de deuda con un cliente moroso
    /// </summary>
    public class AcuerdoPago : AuditableEntity
    {
        /// <summary>
        /// Alerta de cobranza que originó el acuerdo
        /// </summary>
        public int AlertaCobranzaId { get; set; }
        public virtual AlertaCobranza? AlertaCobranza { get; set; }

        /// <summary>
        /// Cliente con quien se hace el acuerdo
        /// </summary>
        public int ClienteId { get; set; }
        public virtual Cliente? Cliente { get; set; }

        /// <summary>
        /// Crédito original que se está refinanciando
        /// </summary>
        public int CreditoId { get; set; }
        public virtual Credito? Credito { get; set; }

        /// <summary>
        /// Número único de acuerdo
        /// </summary>
        [Required]
        [StringLength(50)]
        public string NumeroAcuerdo { get; set; } = string.Empty;

        /// <summary>
        /// Fecha de creación del acuerdo
        /// </summary>
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        /// <summary>
        /// Fecha en que el acuerdo pasó a estado Activo
        /// </summary>
        public DateTime? FechaActivacion { get; set; }

        /// <summary>
        /// Estado actual del acuerdo
        /// </summary>
        public EstadoAcuerdo Estado { get; set; } = EstadoAcuerdo.Borrador;

        /// <summary>
        /// Monto de capital vencido al momento del acuerdo
        /// </summary>
        public decimal MontoDeudaOriginal { get; set; }

        /// <summary>
        /// Monto de mora acumulada al momento del acuerdo
        /// </summary>
        public decimal MontoMoraOriginal { get; set; }

        /// <summary>
        /// Monto de mora que se perdona al cliente
        /// </summary>
        public decimal MontoCondonado { get; set; }

        /// <summary>
        /// Total que el cliente debe pagar en el acuerdo
        /// </summary>
        public decimal MontoTotalAcuerdo { get; set; }

        /// <summary>
        /// Monto que el cliente debe pagar como entrega inicial
        /// </summary>
        public decimal MontoEntregaInicial { get; set; }

        /// <summary>
        /// Si el cliente ya pagó la entrega inicial
        /// </summary>
        public bool EntregaInicialPagada { get; set; }

        /// <summary>
        /// Fecha en que se pagó la entrega inicial
        /// </summary>
        public DateTime? FechaPagoEntrega { get; set; }

        /// <summary>
        /// Cantidad de cuotas del acuerdo
        /// </summary>
        public int CantidadCuotas { get; set; }

        /// <summary>
        /// Fecha de vencimiento de la primera cuota
        /// </summary>
        public DateTime FechaPrimeraCuota { get; set; }

        /// <summary>
        /// Monto de cada cuota del acuerdo
        /// </summary>
        public decimal MontoCuotaAcuerdo { get; set; }

        /// <summary>
        /// Usuario que creó el acuerdo
        /// </summary>
        [StringLength(450)]
        public string? CreadoPor { get; set; }

        /// <summary>
        /// Usuario que aprobó el acuerdo (si requiere aprobación)
        /// </summary>
        [StringLength(450)]
        public string? AprobadoPor { get; set; }

        /// <summary>
        /// Notas sobre el acuerdo
        /// </summary>
        [StringLength(2000)]
        public string? Observaciones { get; set; }

        /// <summary>
        /// Motivo si el acuerdo se marcó como incumplido
        /// </summary>
        [StringLength(500)]
        public string? MotivoIncumplimiento { get; set; }

        /// <summary>
        /// Motivo si el acuerdo se canceló
        /// </summary>
        [StringLength(500)]
        public string? MotivoCancelacion { get; set; }

        // Navigation Properties
        public virtual ICollection<CuotaAcuerdo> Cuotas { get; set; } = new List<CuotaAcuerdo>();
    }
}
