using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class VentaDetalleViewModel
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        [Display(Name = "Producto")]
        [Required(ErrorMessage = "El producto es requerido")]
        public int ProductoId { get; set; }

        public string? ProductoNombre { get; set; }
        public string? ProductoCodigo { get; set; }
        public int StockDisponible { get; set; }

        [Display(Name = "Cantidad")]
        [Required(ErrorMessage = "La cantidad es requerida")]
        [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0")]
        public int Cantidad { get; set; }

        [Display(Name = "Precio Unitario")]
        [Required(ErrorMessage = "El precio es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El precio debe ser mayor a 0")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal PrecioUnitario { get; set; }

        [Display(Name = "Descuento")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Descuento { get; set; }

        [Display(Name = "Subtotal")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Subtotal { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(200)]
        public string? Observaciones { get; set; }
    }
}