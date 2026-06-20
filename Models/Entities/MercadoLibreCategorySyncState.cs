using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Estado de la última importación del catálogo de categorías por site.
    /// Una fila por SiteId: se actualiza en cada corrida del importador.
    /// </summary>
    public class MercadoLibreCategorySyncState
    {
        public int Id { get; set; }

        [StringLength(10)]
        public string SiteId { get; set; } = "MLA";

        [StringLength(500)]
        public string? SourceFilePath { get; set; }

        /// <summary>json | gzip | api.</summary>
        [StringLength(20)]
        public string? SourceKind { get; set; }

        /// <summary>date_created representativo del contenido, si se pudo derivar.</summary>
        public DateTime? LastContentCreated { get; set; }

        /// <summary>MD5 del archivo importado, para detectar reimportaciones idénticas.</summary>
        [StringLength(40)]
        public string? LastContentMd5 { get; set; }

        public DateTime LastImportedAtUtc { get; set; }
        public DateTime? LastSuccessAtUtc { get; set; }

        public string? LastError { get; set; }

        public int ImportedCategories { get; set; }
        public int ImportedAttributes { get; set; }
        public int LeafCategories { get; set; }
        public int ListingAllowedCategories { get; set; }

        public long DurationMs { get; set; }
    }
}
