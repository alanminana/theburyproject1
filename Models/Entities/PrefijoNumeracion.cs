using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Prefijo configurado para el número de un tipo de documento. Sin fila vale el prefijo por defecto
    /// (<see cref="TipoPrefijoNumeracionExtensions.PrefijoPorDefecto"/>). Cambiarlo solo afecta a los números
    /// nuevos: los ya emitidos conservan el que tenían.
    /// </summary>
    public class PrefijoNumeracion : AuditableEntity
    {
        public TipoPrefijoNumeracion Tipo { get; set; }

        [Required, MaxLength(10)]
        public string Prefijo { get; set; } = string.Empty;
    }
}
