namespace TheBuryProject.ViewModels
{
    public class ClienteFilterViewModel
    {
        private int _pageNumber = 1;
        private int _pageSize = 25;

        public string? SearchTerm { get; set; }
        public string? TipoDocumento { get; set; }
        public bool? SoloActivos { get; set; }
        public bool? ConCreditosActivos { get; set; }
        public decimal? PuntajeMinimo { get; set; }

        /// <summary>Franja de riesgo a filtrar: "bajo" | "medio" | "alto". Ver ClienteHelper.</summary>
        public string? NivelRiesgoFiltro { get; set; }

        public string? OrderBy { get; set; }
        public string? OrderDirection { get; set; }

        /// <summary>Página actual (1-based). Valores menores a 1 se normalizan a 1.</summary>
        public int PageNumber
        {
            get => _pageNumber;
            set => _pageNumber = value < 1 ? 1 : value;
        }

        /// <summary>Cantidad de resultados por página. Acotado a [1, 100].</summary>
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 25 : Math.Min(value, 100);
        }

        // Resultados
        public IEnumerable<ClienteViewModel> Clientes { get; set; } = new List<ClienteViewModel>();
        public int TotalResultados { get; set; }
    }
}
