using TheBuryProject.Services.Models;

namespace TheBuryProject.ViewModels
{
    /// <summary>R-03: estado de pantalla de la valoración gerencial de stock (solo presentación).</summary>
    public class ValoracionStockIndexViewModel
    {
        public FiltroValoracionStock Filtro { get; set; } = new();
        public List<ItemValoracionStock> Items { get; set; } = new();
        public ResumenValoracionStock Resumen { get; set; } = new();
        public DateTime FechaCalculo { get; set; }
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 50;
        public int TotalItems { get; set; }
        public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)TamanoPagina));
        public List<(int Id, string Nombre)> Categorias { get; set; } = new();
        public List<(int Id, string Nombre)> Marcas { get; set; } = new();
    }
}
