using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Pregunta preventa de Mercado Libre asociada (cuando se puede) a una
    /// publicación y a un producto del ERP. Idempotente por QuestionId.
    /// Responder es siempre una acción manual: en ModoSimulacion la respuesta
    /// se guarda local y se loguea, sin llamar a Mercado Libre.
    /// </summary>
    public class MercadoLibreQuestion : AuditableEntity
    {
        public int AccountId { get; set; }

        /// <summary>
        /// Id de la pregunta en Mercado Libre. Único (idempotencia).
        /// Para preguntas simuladas locales se usa un id sintético "QA-...".
        /// </summary>
        public long QuestionId { get; set; }

        /// <summary>Publicación ML local asociada (si se pudo resolver el ItemId).</summary>
        public int? ListingId { get; set; }

        /// <summary>ItemId de ML (MLA...) al que pertenece la pregunta.</summary>
        [StringLength(30)]
        public string? ItemId { get; set; }

        /// <summary>Producto del ERP vinculado a la publicación (si existe).</summary>
        public int? ProductoId { get; set; }

        /// <summary>user_id del comprador que preguntó (si viene en la notificación).</summary>
        public long? MeliUserId { get; set; }

        [Required]
        [StringLength(2000)]
        public string TextoPregunta { get; set; } = string.Empty;

        public MercadoLibreQuestionEstado Estado { get; set; } = MercadoLibreQuestionEstado.Pendiente;

        [StringLength(2000)]
        public string? RespuestaTexto { get; set; }

        public DateTime FechaPreguntaUtc { get; set; } = DateTime.UtcNow;

        public DateTime? FechaRespuestaUtc { get; set; }

        /// <summary>Pregunta y/o respuesta generadas en QA local (nunca tocaron ML).</summary>
        public bool EsSimulada { get; set; }

        /// <summary>JSON crudo de la pregunta tal como llegó (no se pierde nunca).</summary>
        public string? RawJson { get; set; }

        [StringLength(1000)]
        public string? ErrorProcesamiento { get; set; }

        [StringLength(100)]
        public string? UsuarioRespuesta { get; set; }

        public virtual MercadoLibreAccount Account { get; set; } = null!;
        public virtual MercadoLibreListing? Listing { get; set; }
        public virtual Producto? Producto { get; set; }
    }
}
