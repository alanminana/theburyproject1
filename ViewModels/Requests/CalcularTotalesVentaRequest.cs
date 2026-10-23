using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;
using TheBuryProject.Validation;

namespace TheBuryProject.ViewModels.Requests
{
    public class CalcularTotalesVentaRequest
    {
        [Required]
        public List<DetalleCalculoVentaRequest> Detalles { get; set; } = new();

        /// <summary>
        /// Venta en edición (opcional). Sin el permiso ventas.discount el servidor ignora los descuentos
        /// que envíe el cliente y usa los persistidos de esta venta (por producto, en orden), para que el
        /// preview coincida con lo que guarda Edit sin exponer el desglose.
        /// </summary>
        public int? VentaId { get; set; }

        public decimal DescuentoGeneral { get; set; }

        public bool DescuentoEsPorcentaje { get; set; }

        public TipoPago TipoPago { get; set; } = TipoPago.Efectivo;

        public int? TarjetaId { get; set; }

        public int? ConfiguracionPagoPlanId { get; set; }

        /// <summary>Envío opcional (Ciudad/Rural). Null = sin envío. El importe sale del precio global.</summary>
        public TipoServicioVenta? TipoEnvio { get; set; }
    }

    public class DetalleCalculoVentaRequest
    {
        public int ProductoId { get; set; }

        /// <summary>
        /// Id de la línea persistida de la venta en edición (opcional). Sin ventas.discount permite al servidor
        /// emparejar exactamente cada línea con su descuento persistido, aun con el mismo producto repetido.
        /// </summary>
        public int? LineaId { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Cantidad { get; set; }

        [MontoArgentino]
        public decimal PrecioUnitario { get; set; }

        // VENTA-CREDITO-ELEGIBILIDAD-DESCUENTO-FIX: porcentaje (0-100) sobre PrecioUnitario*Cantidad,
        // no importe absoluto. Único consumidor autoritativo: VentaService.CalcularSubtotalLineaConDescuento.
        [Range(0, 100)]
        public decimal Descuento { get; set; }

        // Legacy pago por item: el preview activo de Nueva Venta ignora este campo.
        public TipoPago? TipoPago { get; set; }

        // Legacy pago por item: se conserva por compatibilidad de contrato, no como fuente principal.
        public int? ProductoCondicionPagoPlanId { get; set; }

        /// <summary>Armado opcional de la línea (Armado N.º 1..6). Se cobra por unidad.</summary>
        public TipoServicioVenta? TipoArmado { get; set; }

        /// <summary>Producto entregado en caja cerrada: sin armado ni costo.</summary>
        public bool EntregaCajaCerrada { get; set; }
    }
}
