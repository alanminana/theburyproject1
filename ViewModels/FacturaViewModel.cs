using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.ViewModels
{
    public class FacturaViewModel
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        [Display(Name = "Número de Factura")]
        [StringLength(50)]
        public string Numero { get; set; } = string.Empty;

        [Display(Name = "Tipo de Factura")]
        [Required]
        public TipoFactura Tipo { get; set; } = TipoFactura.B;

        [Display(Name = "Punto de Venta")]
        [StringLength(50)]
        public string? PuntoVenta { get; set; }

        [Display(Name = "Fecha de Emisión")]
        [Required]
        [DataType(DataType.Date)]
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        [Display(Name = "Subtotal")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Subtotal { get; set; }

        [Display(Name = "IVA")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal IVA { get; set; }

        [Display(Name = "Total")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Total { get; set; }

        [Display(Name = "CAE")]
        [StringLength(100)]
        public string? CAE { get; set; }

        [Display(Name = "Vencimiento CAE")]
        [DataType(DataType.Date)]
        public DateTime? FechaVencimientoCAE { get; set; }

        public bool Anulada { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }
    }
}