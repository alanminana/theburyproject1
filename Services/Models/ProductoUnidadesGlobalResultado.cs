using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    public class ProductoUnidadesGlobalResultado
    {
        public int TotalUnidades { get; set; }
        public int TotalEnStock { get; set; }
        public int TotalVendidas { get; set; }
        public int TotalFaltantes { get; set; }
        public int TotalBaja { get; set; }
        public int TotalDevueltas { get; set; }
        public int TotalEnReparacion { get; set; }
        public int TotalAnuladas { get; set; }
        public int TotalReservadas { get; set; }
        public IReadOnlyList<ProductoUnidadGlobalItem> Items { get; set; } = Array.Empty<ProductoUnidadGlobalItem>();
    }

    public class ProductoUnidadGlobalItem
    {
        public int Id { get; set; }
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public string CodigoInternoUnidad { get; set; } = string.Empty;
        public string? NumeroSerie { get; set; }
        public EstadoUnidad Estado { get; set; }
        public string? UbicacionActual { get; set; }
        public DateTime FechaIngreso { get; set; }
        public int? ClienteId { get; set; }
        public string? ClienteNombre { get; set; }
        public int? VentaDetalleId { get; set; }
        public DateTime? FechaVenta { get; set; }
    }
}
