using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Decide CUÁNDO (evento) + BAJO QUÉ CONDICIONES (JSON estructurado, nunca código) se genera
    /// QUÉ (una plantilla o un paquete de plantillas).
    /// </summary>
    public class ReglaDocumento : AuditableEntity
    {
        [Required, StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string EventoCodigo { get; set; } = string.Empty;

        /// <summary>Árbol de condiciones (ver CondicionDocumento). Nulo/vacío = siempre aplica.</summary>
        public string? CondicionJson { get; set; }

        public int? PlantillaDocumentoId { get; set; }
        public int? PaqueteDocumentalId { get; set; }

        /// <summary>Mayor número = se evalúa primero.</summary>
        public int Prioridad { get; set; } = 100;

        public bool Activa { get; set; } = true;

        /// <summary>
        /// true: si el documento no puede generarse, el paso del flujo que dispara el evento falla
        /// (rollback). false: se registra el error y el flujo continúa (recuperable).
        /// </summary>
        public bool Obligatoria { get; set; }

        /// <summary>
        /// Reglas del mismo evento con el mismo grupo son excluyentes: solo aplica la de mayor
        /// prioridad que coincida (regla específica reemplaza a la general). Sin grupo: todas las que
        /// coinciden aplican.
        /// </summary>
        [StringLength(50)]
        public string? GrupoExclusion { get; set; }

        /// <summary>true: el documento generado debe estar firmado para poder continuar la operación.</summary>
        public bool ExigeFirmaParaContinuar { get; set; }

        public virtual PlantillaDocumento? PlantillaDocumento { get; set; }
        public virtual PaqueteDocumental? PaqueteDocumental { get; set; }
    }

    public class PaqueteDocumental : AuditableEntity
    {
        [Required, StringLength(80)]
        public string Codigo { get; set; } = string.Empty;

        [Required, StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public bool Activo { get; set; } = true;

        public virtual ICollection<PaqueteDocumentalItem> Items { get; set; } = new List<PaqueteDocumentalItem>();
    }

    public class PaqueteDocumentalItem : AuditableEntity
    {
        public int PaqueteDocumentalId { get; set; }
        public int PlantillaDocumentoId { get; set; }
        public int Orden { get; set; }

        public virtual PaqueteDocumental PaqueteDocumental { get; set; } = null!;
        public virtual PlantillaDocumento PlantillaDocumento { get; set; } = null!;
    }
}
