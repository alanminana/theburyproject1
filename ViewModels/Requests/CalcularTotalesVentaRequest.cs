using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels.Requests
{
    public class CalcularTotalesVentaRequest
    {
        [Required]
        public List<DetalleCalculoVentaRequest> Detalles { get; set; } = new();

        public decimal DescuentoGeneral { get; set; }

        public bool DescuentoEsPorcentaje { get; set; }
    }

    public class DetalleCalculoVentaRequest
    {
        public int ProductoId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Cantidad { get; set; }

        [Range(0, double.MaxValue)]
        public decimal PrecioUnitario { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Descuento { get; set; }
    }
}
