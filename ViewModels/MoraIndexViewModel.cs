namespace TheBuryProject.ViewModels
{
    public class MoraIndexViewModel
    {
        public ConfiguracionMoraViewModel Configuracion { get; set; } = new();
        public List<AlertaCobranzaViewModel> Alertas { get; set; } = new();
        public int TotalAlertas { get; set; }
        public int AlertasPendientes { get; set; }
        public int AlertasResueltas { get; set; }
        public decimal MontoTotalVencido { get; set; }
    }
}