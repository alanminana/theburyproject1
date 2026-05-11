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
    }

    public class DetalleCalculoTotalesVentaResponse
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
    }
}
