using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class VentaCreditoCuotaViewModel
    {
        public int Id { get; set; }

        public int VentaId { get; set; }

        public int CreditoId { get; set; }

        [Display(Name = "Número de Cuota")]
        public int NumeroCuota { get; set; }

        [Display(Name = "Fecha de Vencimiento")]
        [DataType(DataType.Date)]
        public DateTime FechaVencimiento { get; set; }

        [Display(Name = "Monto")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Monto { get; set; }

        [Display(Name = "Saldo")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal Saldo { get; set; }

        [Display(Name = "Pagada")]
        public bool Pagada { get; set; } = false;

        [Display(Name = "Fecha de Pago")]
        [DataType(DataType.Date)]
        public DateTime? FechaPago { get; set; }

        [Display(Name = "Monto Pagado")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal? MontoPagado { get; set; }
    }
}