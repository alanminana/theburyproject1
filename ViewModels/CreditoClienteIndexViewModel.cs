namespace TheBuryProject.ViewModels
{
    public class CreditoClienteIndexViewModel
    {
        public ClienteResumenViewModel Cliente { get; set; } = new();

        public string Documento { get; set; } = string.Empty;

        public int CantidadCreditos { get; set; }

        public decimal SaldoPendienteTotal { get; set; }

        public int CuotasVencidas { get; set; }

        public DateTime? ProximoVencimiento { get; set; }

        public string EstadoConsolidado { get; set; } = string.Empty;

        public List<CreditoViewModel> Creditos { get; set; } = new();
    }
}
