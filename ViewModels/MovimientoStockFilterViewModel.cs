using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class MovimientoStockFilterViewModel
    {
        public int? ProductoId { get; set; }
        public TipoMovimiento? Tipo { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string? OrderBy { get; set; }
        public string? OrderDirection { get; set; }

        // Resultados
        public IEnumerable<MovimientoStockViewModel> Movimientos { get; set; } = new List<MovimientoStockViewModel>();
        public int TotalResultados { get; set; }
    }
}
