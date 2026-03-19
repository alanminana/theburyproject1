using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Plantilla de mensaje para notificaciones de mora
    /// </summary>
    public class PlantillaNotificacionMora : AuditableEntity
    {
        /// <summary>
        /// Tipo de notificación (preventiva, vencida, recordatorio, etc.)
        /// </summary>
        public TipoPlantillaMora Tipo { get; set; }

        /// <summary>
        /// Canal de envío (WhatsApp, Email)
        /// </summary>
        public CanalNotificacion Canal { get; set; }

        /// <summary>
        /// Nombre descriptivo de la plantilla
        /// </summary>
        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Asunto del mensaje (solo para email)
        /// </summary>
        [StringLength(200)]
        public string? Asunto { get; set; }

        /// <summary>
        /// Contenido del mensaje con variables {Variable}
        /// Variables disponibles:
        /// {NombreCliente}, {Apellido}, {NombreCompleto}
        /// {NumeroCuota}, {MontoCuota}, {FechaVencimiento}
        /// {DiasAtraso}, {MontoMora}, {MontoTotal}
        /// {NumeroCredito}, {NombreNegocio}, {Telefono}
        /// </summary>
        [Required]
        public string Contenido { get; set; } = string.Empty;

        /// <summary>
        /// Si la plantilla está habilitada para uso
        /// </summary>
        public bool Activa { get; set; } = true;

        /// <summary>
        /// Orden de prioridad si hay múltiples plantillas del mismo tipo
        /// </summary>
        public int Orden { get; set; } = 0;
    }
}
