using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Modules.MercadoLibre.Entities
{
    /// <summary>
    /// Mensaje postventa de Mercado Libre asociado (cuando se puede) a una
    /// orden del ERP. Idempotente por MessageId. Responder es siempre manual:
    /// en ModoSimulacion el mensaje saliente se guarda local y se loguea,
    /// sin llamar a Mercado Libre.
    /// </summary>
    public class MercadoLibreMessage : AuditableEntity
    {
        public int AccountId { get; set; }

        /// <summary>
        /// Id del mensaje en Mercado Libre. Único (idempotencia).
        /// Para mensajes simulados locales se usa un id sintético "QA-...".
        /// </summary>
        [StringLength(80)]
        public string MessageId { get; set; } = string.Empty;

        /// <summary>Orden ML local asociada (si se pudo resolver).</summary>
        public int? OrderId { get; set; }

        /// <summary>order_id de ML (pack/order) al que pertenece la conversación.</summary>
        public long? MeliOrderId { get; set; }

        [Required]
        [StringLength(2000)]
        public string Texto { get; set; } = string.Empty;

        public MercadoLibreMessageDireccion Direccion { get; set; } = MercadoLibreMessageDireccion.Entrante;

        public MercadoLibreMessageEstado Estado { get; set; } = MercadoLibreMessageEstado.Recibido;

        /// <summary>user_id de quien originó el mensaje (comprador o vendedor).</summary>
        public long? MeliUserId { get; set; }

        public DateTime FechaMensajeUtc { get; set; } = DateTime.UtcNow;

        public DateTime? FechaRespuestaUtc { get; set; }

        /// <summary>Mensaje generado en QA local (nunca tocó ML).</summary>
        public bool EsSimulado { get; set; }

        public string? RawJson { get; set; }

        [StringLength(1000)]
        public string? ErrorProcesamiento { get; set; }

        [StringLength(100)]
        public string? UsuarioEnvio { get; set; }

        public virtual MercadoLibreAccount Account { get; set; } = null!;
        public virtual MercadoLibreOrder? Order { get; set; }
    }
}
