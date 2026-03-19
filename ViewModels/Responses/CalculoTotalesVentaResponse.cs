namespace TheBuryProject.ViewModels.Responses
{
    public class CalculoTotalesVentaResponse
    {
        public decimal Subtotal { get; set; }

        public decimal DescuentoGeneralAplicado { get; set; }

        public decimal IVA { get; set; }

        public decimal Total { get; set; }
    }
}
