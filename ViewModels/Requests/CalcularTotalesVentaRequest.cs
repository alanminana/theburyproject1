using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Requests
{
    public class CalcularTotalesVentaRequest
    {
        [Required]
        public List<DetalleCalculoVentaRequest> Detalles { get; set; } = new();

        public decimal DescuentoGeneral { get; set; }

        public bool DescuentoEsPorcentaje { get; set; }

        public int? TarjetaId { get; set; }
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

        public TipoPago? TipoPago { get; set; }

        public int? ProductoCondicionPagoPlanId { get; set; }
    }
}
