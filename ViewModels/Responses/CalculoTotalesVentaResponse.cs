using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels.Responses
{
    public class CalculoTotalesVentaResponse
    {
        public decimal Subtotal { get; set; }

        public decimal DescuentoGeneralAplicado { get; set; }

        public decimal IVA { get; set; }

        public decimal Total { get; set; }

        public decimal? RecargoDebitoAplicado { get; set; }

        public decimal? PorcentajeRecargoDebitoAplicado { get; set; }

        public decimal? TotalConRecargoDebito { get; set; }

        public List<DetalleCalculoTotalesVentaResponse> Detalles { get; set; } = new();

        public int? MaxCuotasSinInteresEfectivo { get; set; }

        public bool CuotasSinInteresLimitadasPorProducto { get; set; }

        public decimal AjusteItemsAplicado { get; set; }

        public decimal? TotalConAjusteItems { get; set; }

        public decimal AjustePagoGlobalAplicado { get; set; }

        public decimal? PorcentajeAjustePagoGlobalAplicado { get; set; }

        public decimal? TotalConAjustePagoGlobal { get; set; }

        public int? CantidadCuotasPagoGlobal { get; set; }

        public decimal? ValorCuotaPagoGlobal { get; set; }

        public string? NombrePlanPagoGlobal { get; set; }

        /// <summary>Productos con descuentos, sin armados ni envío ni recargos.</summary>
        public decimal TotalProductos { get; set; }

        /// <summary>Suma de armados (precio × unidades) de todas las líneas, sin recargo.</summary>
        public decimal TotalArmados { get; set; }

        /// <summary>Envío seleccionado, sin recargo (0 si no hay).</summary>
        public decimal ImporteEnvio { get; set; }
    }

    public class DetalleCalculoTotalesVentaResponse : ILineaConIvaProrrateable
    {
        public int ProductoId { get; set; }

        public decimal PorcentajeIVA { get; set; }

        public int? AlicuotaIVAId { get; set; }

        public string? AlicuotaIVANombre { get; set; }

        public decimal SubtotalNeto { get; set; }

        public decimal SubtotalIVA { get; set; }

        public decimal Subtotal { get; set; }

        public decimal DescuentoGeneralProrrateado { get; set; }

        public decimal SubtotalFinalNeto { get; set; }

        public decimal SubtotalFinalIVA { get; set; }

        public decimal SubtotalFinal { get; set; }

        /// <summary>Precio global del armado elegido para la línea (0 = sin armado).</summary>
        public decimal ArmadoPrecioUnitario { get; set; }

        /// <summary>ArmadoPrecioUnitario × cantidad.</summary>
        public decimal ArmadoSubtotal { get; set; }
    }
}
