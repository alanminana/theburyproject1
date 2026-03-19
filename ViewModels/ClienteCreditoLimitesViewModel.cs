using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class ClienteCreditoLimitesViewModel
    {
        public List<ClienteCreditoLimiteItemViewModel> Items { get; set; } = new();
    }

    public class ClienteCreditoLimiteItemViewModel : IValidatableObject
    {
        public int Id { get; set; }

        public NivelRiesgoCredito Puntaje { get; set; }

        [Range(0, 999999999999.99, ErrorMessage = "El límite debe ser mayor o igual a 0")]
        [Display(Name = "Límite")]
        public decimal LimiteMonto { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (LimiteMonto != decimal.Truncate(LimiteMonto))
            {
                yield return new ValidationResult(
                    "El límite debe ser un número entero.",
                    new[] { nameof(LimiteMonto) });
            }
        }

        public bool Activo { get; set; } = true;

        public DateTime? FechaActualizacion { get; set; }

        public string? UsuarioActualizacion { get; set; }
    }
}