using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Datos de la empresa que firma los documentos (variables <c>empresa.*</c>). Una sola fila vigente.
    /// Antes salían de la plantilla de contrato; ahora tienen su propia configuración y la plantilla
    /// legada se mantiene sincronizada para no romper el contrato anterior.
    /// </summary>
    public class EmpresaConfiguracion : AuditableEntity
    {
        [Required, StringLength(200)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Cuit { get; set; }

        [StringLength(20)]
        public string? Dni { get; set; }

        [Required, StringLength(300)]
        public string Domicilio { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Ciudad { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string Jurisdiccion { get; set; } = string.Empty;

        public decimal InteresMoraDiarioPorcentaje { get; set; }
    }
}
