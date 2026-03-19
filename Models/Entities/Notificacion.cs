using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Notificaci�n en el sistema
    /// </summary>
    public class Notificacion  : AuditableEntity
    {
        [Required]
        [StringLength(100)]
        public string UsuarioDestino { get; set; } = string.Empty;

        [Required]
        public TipoNotificacion Tipo { get; set; }

        [Required]
        public PrioridadNotificacion Prioridad { get; set; }

        [Required]
        [StringLength(200)]
        public string Titulo { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string Mensaje { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Url { get; set; }

        [StringLength(50)]
        public string? IconoCss { get; set; }

        public bool Leida { get; set; } = false;

        public DateTime? FechaLeida { get; set; }

        public DateTime FechaNotificacion { get; set; } = DateTime.UtcNow;

        // Referencia opcional a la entidad origen
        [StringLength(100)]
        public string? EntidadOrigen { get; set; }

        public int? EntidadOrigenId { get; set; }

        // Datos adicionales en JSON (opcional)
        [StringLength(2000)]
        public string? DatosAdicionales { get; set; }
    }
}