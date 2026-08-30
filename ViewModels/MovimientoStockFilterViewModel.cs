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

        // Paginación
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        // Resultados
        public IEnumerable<MovimientoStockViewModel> Movimientos { get; set; } = new List<MovimientoStockViewModel>();
        public int TotalResultados { get; set; }

        // Agregados sobre el total filtrado real (no solo esta página) — ver
        // MovimientoStockService.SearchPaginadoAsync.
        public decimal TotalEntradas { get; set; }
        public decimal TotalSalidas { get; set; }
        public int TotalAjustes { get; set; }

        // Propiedades calculadas para paginación
        public int TotalPages => TotalResultados == 0 ? 1 : (int)Math.Ceiling((double)TotalResultados / PageSize);
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }
}
