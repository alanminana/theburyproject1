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

        // Legacy pago por item: el preview activo de Nueva Venta ignora este campo.
        public TipoPago? TipoPago { get; set; }

        // Legacy pago por item: se conserva por compatibilidad de contrato, no como fuente principal.
        public int? ProductoCondicionPagoPlanId { get; set; }
    }
}
