using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Plantilla de un tipo de documento. El contenido NO vive acá sino en
    /// <see cref="PlantillaDocumentoVersion"/>: cada edición de contenido crea una versión nueva e
    /// inmutable, y cada documento generado queda atado a la versión exacta que usó.
    /// </summary>
    public class PlantillaDocumento : AuditableEntity
    {
        public int TipoDocumentoId { get; set; }

        [Required, StringLength(80)]
        public string Codigo { get; set; } = string.Empty;

        [Required, StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public bool Activa { get; set; } = true;

        public DateTime VigenteDesde { get; set; } = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime? VigenteHasta { get; set; }

        /// <summary>Número de la versión vigente (la que usan los documentos nuevos).</summary>
        public int VersionActual { get; set; }

        public bool RequiereFirma { get; set; }

        /// <summary>Roles que deben firmar, separados por coma (ej. "vendedor,comprador,fiador").</summary>
        [StringLength(200)]
        public string? FirmantesRequeridos { get; set; }

        public int Copias { get; set; } = 1;

        public virtual TipoDocumento TipoDocumento { get; set; } = null!;
        public virtual ICollection<PlantillaDocumentoVersion> Versiones { get; set; } = new List<PlantillaDocumentoVersion>();
    }

    /// <summary>Versión inmutable del contenido de una plantilla.</summary>
    public class PlantillaDocumentoVersion : AuditableEntity
    {
        public int PlantillaDocumentoId { get; set; }

        public int Numero { get; set; }

        [Required]
        public string Contenido { get; set; } = string.Empty;

        /// <summary>Variables que no pueden quedar vacías al emitir el documento definitivo (coma-separadas).</summary>
        [StringLength(1000)]
        public string? VariablesRequeridas { get; set; }

        [StringLength(300)]
        public string? Comentario { get; set; }

        public virtual PlantillaDocumento PlantillaDocumento { get; set; } = null!;
    }
}
