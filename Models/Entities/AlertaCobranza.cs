using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Alerta de cobranza generada por el sistema para seguimiento de mora
    /// </summary>
    public class AlertaCobranza : AuditableEntity
    {
        #region Relaciones principales

        /// <summary>
        /// Crédito asociado a la alerta
        /// </summary>
        public int CreditoId { get; set; }
        public virtual Credito? Credito { get; set; }

        /// <summary>
        /// Cliente asociado a la alerta
        /// </summary>
        public int ClienteId { get; set; }
        public virtual Cliente? Cliente { get; set; }

        /// <summary>
        /// Cuota específica (si aplica a una sola cuota)
        /// </summary>
        public int? CuotaId { get; set; }
        public virtual Cuota? Cuota { get; set; }

        #endregion

        #region Clasificación

        /// <summary>
        /// Tipo de alerta de cobranza
        /// </summary>
        public TipoAlertaCobranza Tipo { get; set; }

        /// <summary>
        /// Prioridad de la alerta
        /// </summary>
        public PrioridadAlerta Prioridad { get; set; }

        /// <summary>
        /// Mensaje descriptivo de la alerta
        /// </summary>
        [StringLength(2000)]
        public string Mensaje { get; set; } = string.Empty;

        #endregion

        #region Montos y cantidades

        /// <summary>
        /// Monto de capital vencido
        /// </summary>
        public decimal MontoVencido { get; set; }

        /// <summary>
        /// Monto de mora calculada
        /// </summary>
        public decimal MontoMoraCalculada { get; set; }

        /// <summary>
        /// Monto total (vencido + mora)
        /// </summary>
        public decimal MontoTotal { get; set; }

        /// <summary>
        /// Días de atraso actuales
        /// </summary>
        public int DiasAtraso { get; set; }

        /// <summary>
        /// Cantidad de cuotas vencidas
        /// </summary>
        public int CuotasVencidas { get; set; }

        #endregion

        #region Gestión de cobranza

        /// <summary>
        /// Estado actual del workflow de gestión
        /// </summary>
        public EstadoGestionCobranza EstadoGestion { get; set; } = EstadoGestionCobranza.Pendiente;

        /// <summary>
        /// Usuario asignado para gestionar el caso
        /// </summary>
        [StringLength(450)]
        public string? GestorAsignadoId { get; set; }

        /// <summary>
        /// Fecha en que se asignó el gestor
        /// </summary>
        public DateTime? FechaAsignacion { get; set; }

        /// <summary>
        /// Fecha de la alerta
        /// </summary>
        public DateTime FechaAlerta { get; set; }

        /// <summary>
        /// Fecha en que el cliente prometió pagar
        /// </summary>
        public DateTime? FechaPromesaPago { get; set; }

        /// <summary>
        /// Monto que el cliente prometió pagar
        /// </summary>
        public decimal? MontoPromesaPago { get; set; }

        #endregion

        #region Resolución

        /// <summary>
        /// Si la alerta está resuelta/cerrada
        /// </summary>
        public bool Resuelta { get; set; }

        /// <summary>
        /// Fecha de resolución/cierre
        /// </summary>
        public DateTime? FechaResolucion { get; set; }

        /// <summary>
        /// Motivo por el cual se cerró la alerta
        /// </summary>
        [StringLength(500)]
        public string? MotivoResolucion { get; set; }

        /// <summary>
        /// Observaciones del gestor
        /// </summary>
        [StringLength(2000)]
        public string? Observaciones { get; set; }

        #endregion

        #region Notificaciones

        /// <summary>
        /// Cantidad de notificaciones enviadas para esta alerta
        /// </summary>
        public int NotificacionesEnviadas { get; set; }

        /// <summary>
        /// Fecha de la última notificación enviada
        /// </summary>
        public DateTime? UltimaNotificacion { get; set; }

        #endregion

        #region Navigation Properties

        /// <summary>
        /// Historial de contactos relacionados a esta alerta
        /// </summary>
        public virtual ICollection<HistorialContacto> HistorialContactos { get; set; } = new List<HistorialContacto>();

        /// <summary>
        /// Acuerdos de pago generados a partir de esta alerta
        /// </summary>
        public virtual ICollection<AcuerdoPago> Acuerdos { get; set; } = new List<AcuerdoPago>();

        #endregion

        #region Propiedades calculadas (no persistidas)

        /// <summary>
        /// Si la alerta ha sido leída (basado en UpdatedAt)
        /// </summary>
        public bool Leida => UpdatedAt != null && UpdatedAt > CreatedAt;

        /// <summary>
        /// Días desde que se generó la alerta
        /// </summary>
        public int DiasDesdeAlerta => (DateTime.Now - FechaAlerta).Days;

        /// <summary>
        /// Si la promesa de pago está vencida
        /// </summary>
        public bool PromesaVencida => FechaPromesaPago.HasValue && FechaPromesaPago.Value < DateTime.Today && EstadoGestion == EstadoGestionCobranza.PromesaPago;

        #endregion
    }
}