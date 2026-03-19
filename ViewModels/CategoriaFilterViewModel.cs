namespace TheBuryProject.ViewModels
{
    public class CategoriaFilterViewModel
    {
        public string? SearchTerm { get; set; }
        public bool SoloActivos { get; set; }
        public string? OrderBy { get; set; }
        public string? OrderDirection { get; set; }

        public IEnumerable<CategoriaViewModel> Categorias { get; set; } = new List<CategoriaViewModel>();
        public int TotalResultados { get; set; }
    }
}