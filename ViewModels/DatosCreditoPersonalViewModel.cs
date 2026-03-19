using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class DatosCreditoPersonallViewModel
    {
        public int CreditoId { get; set; }
        public string CreditoNumero { get; set; } = string.Empty;

        [Display(Name = "Crédito Total Asignado")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal CreditoTotalAsignado { get; set; }

        [Display(Name = "Crédito Disponible")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal CreditoDisponible { get; set; }

        [Display(Name = "Monto a Financiar")]
        [Required(ErrorMessage = "El monto a financiar es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoAFinanciar { get; set; }

        [Display(Name = "Cantidad de Cuotas")]
        [Required(ErrorMessage = "La cantidad de cuotas es requerida")]
        [Range(1, 120, ErrorMessage = "La cantidad de cuotas debe estar entre 1 y 120")]
        public int CantidadCuotas { get; set; }

        [Display(Name = "Monto por Cuota")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal MontoCuota { get; set; }

        [Display(Name = "Fecha Primera Cuota")]
        [Required(ErrorMessage = "La fecha de primera cuota es requerida")]
        [DataType(DataType.Date)]
        public DateTime FechaPrimeraCuota { get; set; } = DateTime.Today.AddMonths(1);

        [Display(Name = "Tasa de Interés Mensual (%)")]
        [DisplayFormat(DataFormatString = "{0:N2}")]
        public decimal TasaInteresMensual { get; set; }

        [Display(Name = "Total a Pagar")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal TotalAPagar { get; set; }

        [Display(Name = "Interés Total")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal InteresTotal { get; set; }

        [Display(Name = "Saldo Restante del Crédito")]
        [DisplayFormat(DataFormatString = "{0:C2}")]
        public decimal SaldoRestante { get; set; }

        public List<VentaCreditoCuotaViewModel> Cuotas { get; set; } = new List<VentaCreditoCuotaViewModel>();
    }

}
