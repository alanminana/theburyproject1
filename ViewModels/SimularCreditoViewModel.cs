using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class SimularCreditoViewModel
    {
        [Display(Name = "Monto del Crédito")]
        [Required(ErrorMessage = "El monto es requerido")]
        [Range(1000, 10000000, ErrorMessage = "El monto debe estar entre $1.000 y $10.000.000")]
        public decimal MontoSolicitado { get; set; }

        [Display(Name = "Tasa de Interés Mensual")]
        [Required(ErrorMessage = "La tasa de interés es requerida")]
        [Range(0, 1, ErrorMessage = "La tasa debe estar entre 0 y 1 (ejemplo: 0.05 = 5%)")]
        public decimal TasaInteresMensual { get; set; }

        [Display(Name = "Cantidad de Cuotas")]
        [Required(ErrorMessage = "La cantidad de cuotas es requerida")]
        [Range(1, 60, ErrorMessage = "Las cuotas deben estar entre 1 y 60")]
        public int CantidadCuotas { get; set; }

        // Resultados de la simulación
        public decimal MontoCuota { get; set; }
        public decimal TotalAPagar { get; set; }
        public decimal TotalIntereses { get; set; }
        public decimal CFTEA { get; set; }
        public List<CuotaSimuladaViewModel>? PlanPagos { get; set; }
    }

    public class CuotaSimuladaViewModel
    {
        public int NumeroCuota { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoCapital { get; set; }
        public decimal MontoInteres { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal SaldoCapital { get; set; }
    }
}