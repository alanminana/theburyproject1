using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Alerta de stock generada autom�ticamente por el sistema
    /// </summary>
    public class AlertaStock  : AuditableEntity
    {
        /// <summary>
        /// ID del producto que gener� la alerta
        /// </summary>
        [Required]
        public int ProductoId { get; set; }

        /// <summary>
        /// Tipo de alerta de stock
        /// </summary>
        [Required]
        public TipoAlertaStock Tipo { get; set; }

        /// <summary>
        /// Prioridad de la alerta
        /// </summary>
        [Required]
        public PrioridadAlerta Prioridad { get; set; }

        /// <summary>
        /// Estado actual de la alerta
        /// </summary>
        [Required]
        public EstadoAlerta Estado { get; set; } = EstadoAlerta.Pendiente;

        /// <summary>
        /// Mensaje descriptivo de la alerta
        /// </summary>
        [Required]
        [StringLength(500)]
        public string Mensaje { get; set; } = string.Empty;

        /// <summary>
        /// Stock actual cuando se gener� la alerta
        /// </summary>
        [Required]
        public decimal StockActual { get; set; }

        /// <summary>
        /// Stock m�nimo configurado
        /// </summary>
        [Required]
        public decimal StockMinimo { get; set; }

        /// <summary>
        /// Cantidad sugerida para reposici�n
        /// </summary>
        public decimal? CantidadSugeridaReposicion { get; set; }

        /// <summary>
        /// Fecha en que se gener� la alerta
        /// </summary>
        [Required]
        public DateTime FechaAlerta { get; set; }

        /// <summary>
        /// Fecha en que se resolvi� la alerta
        /// </summary>
        public DateTime? FechaResolucion { get; set; }

        /// <summary>
        /// Usuario que resolvi� la alerta
        /// </summary>
        [StringLength(100)]
        public string? UsuarioResolucion { get; set; }

        /// <summary>
        /// Observaciones sobre la alerta
        /// </summary>
        [StringLength(1000)]
        public string? Observaciones { get; set; }

        /// <summary>
        /// Indica si se debe notificar urgentemente
        /// </summary>
        public bool NotificacionUrgente { get; set; }

        // Navigation Properties
        /// <summary>
        /// Producto asociado a la alerta
        /// </summary>
        public virtual Producto Producto { get; set; } = null!;

        // Propiedades calculadas
        /// <summary>
        /// Porcentaje de stock respecto al m�nimo
        /// </summary>
        public decimal PorcentajeStockMinimo =>
            StockMinimo == 0 ? 0 : (StockActual / StockMinimo) * 100;

        /// <summary>
        /// D�as transcurridos desde la alerta
        /// </summary>
        public int DiasDesdeAlerta =>
            (DateTime.UtcNow - FechaAlerta).Days;

        /// <summary>
        /// Indica si la alerta est� vencida (m�s de 7 d�as sin resolver)
        /// </summary>
        public bool EstaVencida =>
            Estado == EstadoAlerta.Pendiente && DiasDesdeAlerta > 7;
    }
}