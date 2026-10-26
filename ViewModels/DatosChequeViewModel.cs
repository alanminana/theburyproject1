using System.ComponentModel.DataAnnotations;
using TheBuryProject.Validation;

namespace TheBuryProject.ViewModels
{
    public class DatosChequeViewModel : IValidatableObject
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        [Display(Name = "Número de Cheque")]
        [Required(ErrorMessage = "El número de cheque es requerido")]
        [StringLength(50)]
        [RegularExpression(@"^\d{1,12}$", ErrorMessage = "El número de cheque debe contener solo dígitos.")]
        public string NumeroCheque { get; set; } = string.Empty;

        [Display(Name = "Banco")]
        [Required(ErrorMessage = "El banco es requerido")]
        [StringLength(100)]
        public string Banco { get; set; } = string.Empty;

        [Display(Name = "Titular")]
        [Required(ErrorMessage = "El titular es requerido")]
        [StringLength(200)]
        public string Titular { get; set; } = string.Empty;

        [Display(Name = "CUIT")]
        [StringLength(20)]
        [CuilCuitArgentino]
        public string? CUIT { get; set; }

        [Display(Name = "Fecha de Emisión")]
        [Required(ErrorMessage = "La fecha de emisión es requerida")]
        [DataType(DataType.Date)]
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        [Display(Name = "Fecha de Vencimiento")]
        [Required(ErrorMessage = "La fecha de vencimiento es requerida")]
        [DataType(DataType.Date)]
        public DateTime FechaVencimiento { get; set; }

        [Display(Name = "Monto")]
        [Required(ErrorMessage = "El monto es requerido")]
        [MontoArgentino(Minimo = 0.01)]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Monto { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(500)]
        public string? Observaciones { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Un cheque no puede vencer antes de su fecha de emisión.
            if (FechaVencimiento.Date < FechaEmision.Date)
            {
                yield return new ValidationResult(
                    "La fecha de vencimiento no puede ser anterior a la fecha de emisión.",
                    new[] { nameof(FechaVencimiento) });
            }
        }
    }
}