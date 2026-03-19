namespace TheBuryProject.ViewModels
{
    public class MarcaFilterViewModel
    {
        public string? SearchTerm { get; set; }
        public bool SoloActivos { get; set; }
        public string? OrderBy { get; set; }
        public string? OrderDirection { get; set; }

        public IEnumerable<MarcaViewModel> Marcas { get; set; } = new List<MarcaViewModel>();
        public int TotalResultados { get; set; }
    }
}