using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class OrdenCompraDetalleViewModel
    {
        public int Id { get; set; }

        public int ProductoId { get; set; }

        [Display(Name = "Producto")]
        public string? ProductoNombre { get; set; }

        [Display(Name = "Código")]
        public string? ProductoCodigo { get; set; }

        [Required]
        [Display(Name = "Cantidad")]
        public int Cantidad { get; set; }

        [Required]
        [Display(Name = "Precio Unitario")]
        public decimal PrecioUnitario { get; set; }

        [Display(Name = "Subtotal")]
        public decimal Subtotal { get; set; }

        [Display(Name = "Cantidad Recibida")]
        public int CantidadRecibida { get; set; }

        [Display(Name = "Observaciones")]
        public string? Observaciones { get; set; }
    }
}