namespace TheBuryProject.Services.Models
{
    public class CreditoDisponibleResultado
    {
        public decimal Limite { get; set; }

        public string OrigenLimite { get; set; } = "Puntaje";

        public decimal SaldoVigente { get; set; }

        public decimal Disponible { get; set; }
    }
}