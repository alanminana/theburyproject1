using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class PagarCuotaViewModel
    {
        // NUEVA PROPIEDAD
        public int CreditoId { get; set; }

        public int CuotaId { get; set; }

        [Display(Name = "Número de Cuota")]
        public int NumeroCuota { get; set; }

        [Display(Name = "Monto Cuota")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoCuota { get; set; }

        [Display(Name = "Punitorio")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoPunitorio { get; set; }

        [Display(Name = "Total a Pagar")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal TotalAPagar { get; set; }

        [Display(Name = "Monto Pagado")]
        [Required(ErrorMessage = "El monto pagado es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0")]
        public decimal MontoPagado { get; set; }

        [Display(Name = "Fecha de Pago")]
        [Required(ErrorMessage = "La fecha de pago es requerida")]
        [DataType(DataType.Date)]
        public DateTime FechaPago { get; set; } = DateTime.UtcNow;

        [Display(Name = "Medio de Pago")]
        [Required(ErrorMessage = "El medio de pago es requerido")]
        public string MedioPago { get; set; } = "Efectivo";

        [Display(Name = "Número de Comprobante")]
        public string? ComprobantePago { get; set; }

        [Display(Name = "Observaciones")]
        [DataType(DataType.MultilineText)]
        public string? Observaciones { get; set; }

        // Información adicional para mostrar
        public string? ClienteNombre { get; set; }
        public string? NumeroCreditoTexto { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public bool EstaVencida { get; set; }
        public int DiasAtraso { get; set; }
    }
}