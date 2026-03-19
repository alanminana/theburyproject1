using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class OrdenCompraViewModel
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
        public decimal Descuento { get; set; }

        [Display(Name = "IVA")]
        public decimal Iva { get; set; }

        [Display(Name = "Total")]
        public decimal Total { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(1000)]
        public string? Observaciones { get; set; }

        // Propiedades calculadas
        public int TotalItems { get; set; }
        public int TotalRecibido { get; set; }

        // ⭐ ESTA ES LA PROPIEDAD CLAVE
        public List<OrdenCompraDetalleViewModel> Detalles { get; set; } = new List<OrdenCompraDetalleViewModel>();
    }
}