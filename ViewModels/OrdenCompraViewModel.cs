using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class OrdenCompraViewModel : System.ComponentModel.DataAnnotations.IValidatableObject
    {
        public int Id { get; set; }

        public byte[]? RowVersion { get; set; }

        [Required(ErrorMessage = "El número de orden es obligatorio")]
        [Display(Name = "Número de Orden")]
        public string Numero { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha de emisión es obligatoria")]
        [Display(Name = "Fecha de Emisión")]
        public DateTime FechaEmision { get; set; } = DateTime.Today;

        [Display(Name = "Fecha de Entrega Estimada")]
        public DateTime? FechaEntregaEstimada { get; set; }

        [Display(Name = "Fecha de Recepción")]
        public DateTime? FechaRecepcion { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un proveedor")]
        [Display(Name = "Proveedor")]
        public int ProveedorId { get; set; }

        [Display(Name = "Proveedor")]
        public string? ProveedorNombre { get; set; }

        [Required(ErrorMessage = "El estado es obligatorio")]
        [Display(Name = "Estado")]
        public EstadoOrdenCompra Estado { get; set; } = EstadoOrdenCompra.Borrador;

        [Display(Name = "Estado")]
        public string? EstadoNombre { get; set; }

        [Display(Name = "Subtotal")]
        public decimal Subtotal { get; set; }

        [Display(Name = "Descuento")]
        [TheBuryProject.Validation.MontoSinAmbiguedad]
        public decimal Descuento { get; set; }

        [Display(Name = "IVA")]
        public decimal Iva { get; set; }

        [Display(Name = "Total")]
        public decimal Total { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(1000)]
        public string? Observaciones { get; set; }

        public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(
            System.ComponentModel.DataAnnotations.ValidationContext validationContext)
        {
            // Emisión: una fecha imposible (1900 sin sentido comercial, 2999) descuadra reportes y vencimientos.
            if (FechaEmision.Date < new DateTime(2000, 1, 1) || FechaEmision.Date > DateTime.UtcNow.Date.AddDays(1))
            {
                yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                    "La fecha de emisión debe estar entre el 01/01/2000 y hoy",
                    new[] { nameof(FechaEmision) });
            }

            if (FechaEntregaEstimada.HasValue)
            {
                if (FechaEntregaEstimada.Value.Date < FechaEmision.Date)
                {
                    yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                        "La entrega estimada no puede ser anterior a la fecha de emisión",
                        new[] { nameof(FechaEntregaEstimada) });
                }
                else if (FechaEntregaEstimada.Value.Date > FechaEmision.Date.AddYears(2))
                {
                    yield return new System.ComponentModel.DataAnnotations.ValidationResult(
                        "La entrega estimada no puede superar los 2 años desde la emisión",
                        new[] { nameof(FechaEntregaEstimada) });
                }
            }
        }

        // Propiedades calculadas
        public int TotalItems { get; set; }
        public string? MarcasResumen { get; set; }
        public int TotalRecibido { get; set; }

        // ⭐ ESTA ES LA PROPIEDAD CLAVE
        public List<OrdenCompraDetalleViewModel> Detalles { get; set; } = new List<OrdenCompraDetalleViewModel>();
    }
}