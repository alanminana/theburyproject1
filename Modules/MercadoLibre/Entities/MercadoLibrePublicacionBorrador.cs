using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Modules.MercadoLibre.Entities
{
    /// <summary>
    /// Borrador de publicación ML creado desde un Producto del ERP (Fase F).
    /// Nace SIEMPRE vinculado a un Producto y nunca publica solo:
    /// publicar exige validación previa, flag PermitirPublicacionDesdeErp,
    /// modo simulación desactivado y confirmación explícita.
    /// </summary>
    public class MercadoLibrePublicacionBorrador : AuditableEntity
    {
        /// <summary>Producto ERP de origen. La publicación resultante queda vinculada a él.</summary>
        public int ProductoId { get; set; }

        public MercadoLibreBorradorEstado Estado { get; set; } = MercadoLibreBorradorEstado.Borrador;

        [Required]
        [StringLength(60)] // límite de título de Mercado Libre
        public string Titulo { get; set; } = string.Empty;

        public string? Descripcion { get; set; }

        public decimal Precio { get; set; }

        [StringLength(10)]
        public string CurrencyId { get; set; } = "ARS";

        /// <summary>Stock a publicar. Validado contra el origen de stock configurado.</summary>
        public int Stock { get; set; }

        /// <summary>Categoría de Mercado Libre (ej: "MLA1055"). Obligatoria para publicar.</summary>
        [StringLength(30)]
        public string? CategoryIdMl { get; set; }

        /// <summary>Condición del ítem: new | used.</summary>
        [StringLength(20)]
        public string Condicion { get; set; } = "new";

        /// <summary>Tipo de publicación ML (gold_special, free, etc.).</summary>
        [StringLength(30)]
        public string ListingTypeId { get; set; } = "gold_special";

        /// <summary>Texto de garantía declarado (informativo en el MVP).</summary>
        [StringLength(200)]
        public string? Garantia { get; set; }

        /// <summary>Resultado de la última validación (mensajes separados por '\n'). Null = nunca validado.</summary>
        [StringLength(2000)]
        public string? ErroresValidacion { get; set; }

        public DateTime? FechaValidacionUtc { get; set; }

        /// <summary>ItemId devuelto por ML cuando se publicó realmente.</summary>
        [StringLength(30)]
        public string? PublicadoItemId { get; set; }

        public DateTime? FechaPublicadoUtc { get; set; }

        /// <summary>True si la "publicación" fue solo simulada (no existe en ML).</summary>
        public bool PublicadoEnSimulacion { get; set; }

        /// <summary>Fecha de la ultima simulacion local de publicacion.</summary>
        public DateTime? FechaSimulacionUtc { get; set; }

        /// <summary>Payload local que se habria enviado a POST /items durante la simulacion.</summary>
        public string? PayloadSimuladoJson { get; set; }

        public virtual Producto Producto { get; set; } = null!;
    }
}
