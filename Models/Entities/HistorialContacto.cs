using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Registro de cada intento de contacto con un cliente moroso
    /// </summary>
    public class HistorialContacto : AuditableEntity
    {
        /// <summary>
        /// Alerta de cobranza asociada
        /// </summary>
        public int AlertaCobranzaId { get; set; }
        public virtual AlertaCobranza? AlertaCobranza { get; set; }

        /// <summary>
        /// Cliente contactado
        /// </summary>
        public int ClienteId { get; set; }
        public virtual Cliente? Cliente { get; set; }

        /// <summary>
        /// Usuario que realizó el contacto
        /// </summary>
        [StringLength(450)]
        public string GestorId { get; set; } = string.Empty;

        /// <summary>
        /// Fecha y hora del contacto
        /// </summary>
        public DateTime FechaContacto { get; set; } = DateTime.Now;

        /// <summary>
        /// Tipo de contacto realizado
        /// </summary>
        public TipoContacto TipoContacto { get; set; }

        /// <summary>
        /// Resultado del intento de contacto
        /// </summary>
        public ResultadoContacto Resultado { get; set; }

        /// <summary>
        /// Teléfono usado para el contacto (auditoría)
        /// </summary>
        [StringLength(50)]
        public string? Telefono { get; set; }

        /// <summary>
        /// Email usado para el contacto (auditoría)
        /// </summary>
        [StringLength(200)]
        public string? Email { get; set; }

        /// <summary>
        /// Notas del gestor sobre el contacto
        /// </summary>
        [StringLength(2000)]
        public string? Observaciones { get; set; }

        /// <summary>
        /// Duración de la llamada en minutos (si aplica)
        /// </summary>
        public int? DuracionMinutos { get; set; }

        /// <summary>
        /// Fecha sugerida para próximo contacto
        /// </summary>
        public DateTime? ProximoContacto { get; set; }

        /// <summary>
        /// Si el cliente prometió pagar, en qué fecha
        /// </summary>
        public DateTime? FechaPromesaPago { get; set; }

        /// <summary>
        /// Si el cliente prometió pagar, qué monto
        /// </summary>
        public decimal? MontoPromesaPago { get; set; }
    }
}
