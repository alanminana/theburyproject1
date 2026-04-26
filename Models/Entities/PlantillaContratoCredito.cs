using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Plantilla editable para contratos de venta con crédito personal.
    /// Los contratos generados guardan snapshot para no depender de cambios posteriores.
    /// </summary>
    public class PlantillaContratoCredito : AuditableEntity
    {
        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        public bool Activa { get; set; } = true;

        [Required]
        [StringLength(200)]
        public string NombreVendedor { get; set; } = string.Empty;

        [Required]
        [StringLength(300)]
        public string DomicilioVendedor { get; set; } = string.Empty;

        [StringLength(20)]
        public string? DniVendedor { get; set; }

        [StringLength(20)]
        public string? CuitVendedor { get; set; }

        [Required]
        [StringLength(120)]
        public string CiudadFirma { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Jurisdiccion { get; set; } = string.Empty;

        public decimal InteresMoraDiarioPorcentaje { get; set; }

        [Required]
        public string TextoContrato { get; set; } = string.Empty;

        [Required]
        public string TextoPagare { get; set; } = string.Empty;

        public DateTime VigenteDesde { get; set; } = DateTime.UtcNow;

        public DateTime? VigenteHasta { get; set; }
    }
}
