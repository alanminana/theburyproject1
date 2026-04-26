using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class PlantillaContratoCreditoViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Nombre")]
        [Required(ErrorMessage = "El nombre de la plantilla es requerido")]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [Display(Name = "Activa")]
        public bool Activa { get; set; } = true;

        [Display(Name = "Nombre del vendedor")]
        [Required(ErrorMessage = "El nombre del vendedor es requerido")]
        [StringLength(200)]
        public string NombreVendedor { get; set; } = string.Empty;

        [Display(Name = "Domicilio del vendedor")]
        [Required(ErrorMessage = "El domicilio del vendedor es requerido")]
        [StringLength(300)]
        public string DomicilioVendedor { get; set; } = string.Empty;

        [Display(Name = "DNI del vendedor")]
        [StringLength(20)]
        public string? DniVendedor { get; set; }

        [Display(Name = "CUIT del vendedor")]
        [StringLength(20)]
        public string? CuitVendedor { get; set; }

        [Display(Name = "Ciudad de firma")]
        [Required(ErrorMessage = "La ciudad de firma es requerida")]
        [StringLength(120)]
        public string CiudadFirma { get; set; } = string.Empty;

        [Display(Name = "Jurisdicción")]
        [Required(ErrorMessage = "La jurisdicción es requerida")]
        [StringLength(200)]
        public string Jurisdiccion { get; set; } = string.Empty;

        [Display(Name = "Interés por mora diario (%)")]
        [Range(0.0001, 100, ErrorMessage = "El interés por mora diario debe ser mayor a 0 y no superar 100")]
        public decimal InteresMoraDiarioPorcentaje { get; set; }

        [Display(Name = "Texto del contrato")]
        [Required(ErrorMessage = "El texto del contrato es requerido")]
        public string TextoContrato { get; set; } = string.Empty;

        [Display(Name = "Texto del pagaré")]
        [Required(ErrorMessage = "El texto del pagaré es requerido")]
        public string TextoPagare { get; set; } = string.Empty;

        [Display(Name = "Vigente desde")]
        [Required(ErrorMessage = "La fecha de inicio de vigencia es requerida")]
        [DataType(DataType.Date)]
        public DateTime VigenteDesde { get; set; } = DateTime.Today;

        [Display(Name = "Vigente hasta")]
        [DataType(DataType.Date)]
        public DateTime? VigenteHasta { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }

        public bool EsNueva => Id == 0;
    }
}
