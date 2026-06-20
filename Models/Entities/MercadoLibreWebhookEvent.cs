using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Notificación (webhook) cruda recibida de Mercado Libre.
    /// El endpoint guarda el evento y responde 200 rápido; el procesamiento
    /// real se hace después en forma idempotente (Procesado/ProcesadoUtc).
    /// </summary>
    public class MercadoLibreWebhookEvent : AuditableEntity
    {
        /// <summary>
        /// Tópico de la notificación: orders_v2, items, questions, messages, shipments, claims.
        /// </summary>
        [StringLength(50)]
        public string Topic { get; set; } = string.Empty;

        /// <summary>
        /// Resource notificado (ej: "/orders/123456789").
        /// </summary>
        [StringLength(300)]
        public string Resource { get; set; } = string.Empty;

        /// <summary>
        /// user_id del vendedor notificado.
        /// </summary>
        public long? MeliUserId { get; set; }

        /// <summary>
        /// Intentos de envío reportados por Mercado Libre.
        /// </summary>
        public int? Attempts { get; set; }

        /// <summary>
        /// Body crudo del POST recibido.
        /// </summary>
        [Required]
        public string RawBody { get; set; } = string.Empty;

        public DateTime RecibidoUtc { get; set; } = DateTime.UtcNow;

        public bool Procesado { get; set; }

        public DateTime? ProcesadoUtc { get; set; }

        [StringLength(1000)]
        public string? ErrorProcesamiento { get; set; }

        /// <summary>
        /// Intentos de procesamiento interno (reintentos controlados del background service).
        /// </summary>
        public int IntentosProcesamiento { get; set; }
    }
}
