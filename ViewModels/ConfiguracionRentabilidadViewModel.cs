using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class ConfiguracionRentabilidadViewModel : IValidatableObject
    {
        [Display(Name = "Margen bajo maximo (%)")]
        [Range(0, 100, ErrorMessage = "El margen bajo debe estar entre 0% y 100%.")]
        public decimal MargenBajoMax { get; set; }

        [Display(Name = "Margen alto minimo (%)")]
        [Range(0, 100, ErrorMessage = "El margen alto debe estar entre 0% y 100%.")]
        public decimal MargenAltoMin { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (MargenBajoMax >= MargenAltoMin)
            {
                yield return new ValidationResult(
                    "El margen bajo debe ser menor al margen alto.",
                    new[] { nameof(MargenBajoMax), nameof(MargenAltoMin) });
            }
        }
    }
}
