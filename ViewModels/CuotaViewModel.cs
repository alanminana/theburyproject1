using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class CuotaViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Crédito")]
        public int CreditoId { get; set; }

        // NUEVAS PROPIEDADES
        public string CreditoNumero { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;

        [Display(Name = "Cuota Nro.")]
        public int NumeroCuota { get; set; }

        [Display(Name = "Capital")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoCapital { get; set; }

        [Display(Name = "Interés")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoInteres { get; set; }

        [Display(Name = "Total Cuota")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoTotal { get; set; }

        [Display(Name = "Fecha Vencimiento")]
        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}")]
        public DateTime FechaVencimiento { get; set; }

        [Display(Name = "Fecha Pago")]
        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:dd/MM/yyyy}")]
        public DateTime? FechaPago { get; set; }

        [Display(Name = "Monto Pagado")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoPagado { get; set; }

        [Display(Name = "Punitorio")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoPunitorio { get; set; }

        [Display(Name = "Estado")]
        public EstadoCuota Estado { get; set; }

        [Display(Name = "Medio de Pago")]
        public string? MedioPago { get; set; }

        [Display(Name = "Comprobante")]
        public string? ComprobantePago { get; set; }

        [Display(Name = "Observaciones")]
        [DataType(DataType.MultilineText)]
        public string? Observaciones { get; set; }

        // Propiedades calculadas
        public string EstadoTexto => Estado.ToString();
        public bool EstaVencida => Estado == EstadoCuota.Vencida || (Estado == EstadoCuota.Pendiente && FechaVencimiento < DateTime.UtcNow);
        public int DiasAtraso => EstaVencida ? (DateTime.UtcNow - FechaVencimiento).Days : 0;
        public decimal SaldoPendiente => MontoTotal + MontoPunitorio - MontoPagado;

        // Propiedades de alerta visual
        [Display(Name = "Color de Alerta")]
        public string ColorAlerta { get; set; } = "#FF0000"; // Default rojo

        [Display(Name = "Descripción Alerta")]
        public string? DescripcionAlerta { get; set; }

        [Display(Name = "Prioridad")]
        public int NivelPrioridad { get; set; } = 5; // Default alta prioridad
    }
}