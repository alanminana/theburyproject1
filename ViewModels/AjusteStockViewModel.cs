using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class AjusteStockViewModel : IValidatableObject
    {
        [Required(ErrorMessage = "El producto es requerido")]
        [Display(Name = "Producto")]
        public int ProductoId { get; set; }

        [Required(ErrorMessage = "El tipo de movimiento es requerido")]
        [Display(Name = "Tipo de Movimiento")]
        public TipoMovimiento Tipo { get; set; }

        [Required(ErrorMessage = "La cantidad es requerida")]
        [Range(0, 999999.99, ErrorMessage = "La cantidad debe estar entre 0 y 999999.99")]
        [Display(Name = "Cantidad")]
        public decimal Cantidad { get; set; }

        [Display(Name = "Referencia")]
        [StringLength(200, ErrorMessage = "La referencia no puede exceder 200 caracteres")]
        public string? Referencia { get; set; }

        [Required(ErrorMessage = "El motivo es requerido")]
        [Display(Name = "Motivo")]
        [StringLength(500, ErrorMessage = "El motivo no puede exceder 500 caracteres")]
        public string Motivo { get; set; } = string.Empty;

        // Info del producto (para mostrar en la vista)
        public string? ProductoNombre { get; set; }
        public string? ProductoCodigo { get; set; }
        public decimal StockActual { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Tipo == TipoMovimiento.Ajuste)
            {
                if (Cantidad < 0)
                {
                    yield return new ValidationResult(
                        "Para un ajuste, la cantidad (stock objetivo) no puede ser negativa.",
                        new[] { nameof(Cantidad) });
                }
            }
            else
            {
                if (Cantidad <= 0)
                {
                    yield return new ValidationResult(
                        "La cantidad debe ser mayor a 0 para entradas y salidas.",
                        new[] { nameof(Cantidad) });
                }
            }
        }
    }
}