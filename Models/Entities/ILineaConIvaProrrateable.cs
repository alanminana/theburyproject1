namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Contrato mínimo que comparten VentaDetalle (persistencia) y DetalleCalculoTotalesVentaResponse (preview)
    /// para que VentaService prorratee el descuento general y derive neto/IVA con una sola implementación.
    /// </summary>
    internal interface ILineaConIvaProrrateable
    {
        decimal PorcentajeIVA { get; }
        decimal Subtotal { get; }
        decimal SubtotalNeto { get; }
        decimal SubtotalIVA { get; }
        decimal DescuentoGeneralProrrateado { get; set; }
        decimal SubtotalFinal { get; set; }
        decimal SubtotalFinalNeto { get; set; }
        decimal SubtotalFinalIVA { get; set; }
    }
}
