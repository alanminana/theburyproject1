using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

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

        [Display(Name = "IVA")]
        [DisplayFormat(DataFormatString = "{0:N2}%")]
        public decimal PorcentajeIVA { get; set; }

        public int? AlicuotaIVAId { get; set; }

        [Display(Name = "Alícuota IVA")]
        public string? AlicuotaIVANombre { get; set; }

        [Display(Name = "Precio Unitario Neto")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal PrecioUnitarioNeto { get; set; }

        [Display(Name = "IVA Unitario")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal IVAUnitario { get; set; }

        [Display(Name = "Subtotal Neto")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal SubtotalNeto { get; set; }

        [Display(Name = "IVA Línea")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal SubtotalIVA { get; set; }

        [Display(Name = "Descuento General Prorrateado")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal DescuentoGeneralProrrateado { get; set; }

        [Display(Name = "Subtotal Final Neto")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal SubtotalFinalNeto { get; set; }

        [Display(Name = "IVA Final Línea")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal SubtotalFinalIVA { get; set; }

        [Display(Name = "Subtotal Final")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal SubtotalFinal { get; set; }

        [Display(Name = "Costo Unitario al Momento")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal CostoUnitarioAlMomento { get; set; }

        [Display(Name = "Costo Total al Momento")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal CostoTotalAlMomento { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(200)]
        public string? Observaciones { get; set; }

        // Forma de pago por ítem (Fase 16.2 — todos nullable para compatibilidad con ventas existentes)
        public TipoPago? TipoPago { get; set; }
        public int? ProductoCondicionPagoPlanId { get; set; }
        public decimal? PorcentajeAjustePlanAplicado { get; set; }
        public decimal? MontoAjustePlanAplicado { get; set; }
        public string? ResumenFormaPago { get; set; }

        // Trazabilidad individual (Fase 8.2.E — nullable para compatibilidad con ventas históricas)
        public int? ProductoUnidadId { get; set; }
    }
}
