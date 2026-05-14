namespace TheBuryProject.Services.Models
{
    public class ProductoUnidadConciliacionReadModel
    {
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = string.Empty;
        public string ProductoCodigo { get; set; } = string.Empty;
        public bool RequiereNumeroSerie { get; set; }
        public decimal StockActual { get; set; }
        public int UnidadesEnStock { get; set; }
        public int UnidadesVendidas { get; set; }
        public int UnidadesFaltantes { get; set; }
        public int UnidadesBaja { get; set; }
        public int UnidadesDevueltas { get; set; }
        public int UnidadesReservadas { get; set; }
        public int UnidadesEnReparacion { get; set; }
        public int TotalUnidadesActivas { get; set; }
        public decimal DiferenciaStockVsUnidadesEnStock { get; set; }
        public bool HayDiferencia => DiferenciaStockVsUnidadesEnStock != 0m;
        public DateTime? UltimoMovimientoStockFecha { get; set; }
        public DateTime? UltimoMovimientoUnidadFecha { get; set; }
    }
}
