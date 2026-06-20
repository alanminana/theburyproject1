using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Item de un lote masivo de precios ML. Guarda el snapshot del precio
    /// anterior (obligatorio para rollback) por publicación o por variación.
    /// </summary>
    public class MercadoLibrePriceBatchItem : AuditableEntity
    {
        public int BatchId { get; set; }

        public int ListingId { get; set; }

        /// <summary>Id de la publicación en ML (desnormalizado para reporting).</summary>
        [Required]
        [StringLength(30)]
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Variación afectada; null = precio de la publicación completa.</summary>
        public long? VariationId { get; set; }

        [StringLength(300)]
        public string Titulo { get; set; } = string.Empty;

        /// <summary>Snapshot del precio ML antes del cambio (obligatorio para rollback).</summary>
        public decimal PrecioAnterior { get; set; }

        public decimal PrecioNuevo { get; set; }

        public decimal DiferenciaPorcentaje { get; set; }

        /// <summary>
        /// Payload exacto de precio que se envio o se habria enviado a Mercado Libre.
        /// Para variaciones, el VariationId viaja en la URL; el payload no incluye
        /// arrays destructivos de variaciones.
        /// </summary>
        public string? PayloadAplicacionJson { get; set; }

        public bool TieneAdvertencia { get; set; }

        [StringLength(500)]
        public string? MensajeAdvertencia { get; set; }

        public bool Aplicado { get; set; }

        public bool Revertido { get; set; }

        [StringLength(500)]
        public string? Error { get; set; }

        public virtual MercadoLibrePriceBatch Batch { get; set; } = null!;
        public virtual MercadoLibreListing Listing { get; set; } = null!;
    }
}
