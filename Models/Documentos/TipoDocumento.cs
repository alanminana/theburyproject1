using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Clase de documento del negocio (contrato, pagaré, recibo, presupuesto, ...). Es dato, no
    /// código: agregar un tipo no requiere tocar los flujos de venta/cobranza. Cada tipo lleva su
    /// propia secuencia de numeración.
    /// </summary>
    public class TipoDocumento : AuditableEntity
    {
        [Required, StringLength(50)]
        public string Codigo { get; set; } = string.Empty;

        [Required, StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public CategoriaDocumento Categoria { get; set; } = CategoriaDocumento.Otro;

        public bool Activo { get; set; } = true;

        /// <summary>
        /// false: una misma operación admite un único documento vigente de este tipo (si dos reglas
        /// coinciden se emite solo el de mayor prioridad). true: puede haber varios (ej. recibos).
        /// </summary>
        public bool PermiteMultiples { get; set; }

        public bool RequiereFirma { get; set; }

        /// <summary>Prefijo de la numeración (ej. "CVC", "PAG", "REC").</summary>
        [Required, StringLength(10)]
        public string Prefijo { get; set; } = "DOC";

        /// <summary>Último número consumido. Se incrementa de forma atómica (ver DocumentoNumeracionService).</summary>
        public long UltimoNumero { get; set; }

        /// <summary>Tipos de sistema (sembrados) no pueden eliminarse.</summary>
        public bool EsSistema { get; set; }
    }
}
