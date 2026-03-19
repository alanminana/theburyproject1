namespace TheBuryProject.ViewModels
{
    public class ClienteFilterViewModel
    {
        public string? SearchTerm { get; set; }
        public string? TipoDocumento { get; set; }
        public bool? SoloActivos { get; set; }
        public bool? ConCreditosActivos { get; set; }
        public decimal? PuntajeMinimo { get; set; }
        public string? OrderBy { get; set; }
        public string? OrderDirection { get; set; }

        // Resultados
        public IEnumerable<ClienteViewModel> Clientes { get; set; } = new List<ClienteViewModel>();
        public int TotalResultados { get; set; }
    }
}