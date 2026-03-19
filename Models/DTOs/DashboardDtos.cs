using System.Globalization;

namespace TheBuryProject.Models.DTOs
{
    public class VentasPorDiaDto
    {
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public int Cantidad { get; set; }
    }

    public class VentasPorMesDto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string MesNombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int Cantidad { get; set; }
    }

    public class ProductoMasVendidoDto
    {
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal TotalVendido { get; set; }
    }

    public class EstadoCreditoDto
    {
        public string Estado { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    public class CobranzaPorMesDto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public string MesNombre { get; set; } = string.Empty;
        public decimal MontoEsperado { get; set; }
        public decimal MontoRecaudado { get; set; }
        public decimal PorcentajeEfectividad { get; set; }
    }
}