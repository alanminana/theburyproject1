namespace TheBuryProject.ViewModels
{
    public class CalculoFinanciamientoViewModel
    {
        public decimal Total { get; set; }
        public decimal Anticipo { get; set; }
        public decimal TasaMensual { get; set; }
        public int Cuotas { get; set; }
        public decimal? IngresoNeto { get; set; }
        public decimal? OtrasDeudas { get; set; }
        public int? AntiguedadLaboralMeses { get; set; }
    }
}
