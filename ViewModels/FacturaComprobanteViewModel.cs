using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class FacturaComprobanteViewModel
    {
        public FacturaComprobanteFacturaViewModel Factura { get; set; } = new();
        public FacturaComprobanteVentaViewModel Venta { get; set; } = new();
        public FacturaComprobanteClienteViewModel Cliente { get; set; } = new();
        public List<FacturaComprobanteLineaViewModel> Lineas { get; set; } = new();
        public List<FacturaAlicuotaResumenViewModel> ResumenAlicuotas { get; set; } = new();
        public FacturaComprobanteTotalesViewModel Totales { get; set; } = new();
        // Fase 16.6: desglose por grupo cuando hay pagos por ítem. Vacío si todos los detalles usan TipoPago global.
        public List<FacturaComprobanteGrupoPagoViewModel> GruposPagoPorItem { get; set; } = new();
    }

    public class FacturaComprobanteGrupoPagoViewModel
    {
        public string TipoPagoLabel { get; set; } = string.Empty;
        public decimal? PorcentajeAjuste { get; set; }
        public decimal Subtotal { get; set; }
        public decimal AjusteMonto { get; set; }
        public decimal Total { get; set; }
    }

    public class FacturaComprobanteFacturaViewModel
    {
        public int Id { get; set; }
        public string Numero { get; set; } = string.Empty;
        public TipoFactura Tipo { get; set; }
        public string? PuntoVenta { get; set; }
        public DateTime FechaEmision { get; set; }
        public string? CAE { get; set; }
        public DateTime? FechaVencimientoCAE { get; set; }
        public bool Anulada { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string? MotivoAnulacion { get; set; }
    }

    public class FacturaComprobanteVentaViewModel
    {
        public int Id { get; set; }
        public string Numero { get; set; } = string.Empty;
        public DateTime FechaVenta { get; set; }
        public TipoPago TipoPago { get; set; }
        public EstadoVenta Estado { get; set; }
        public string? VendedorNombre { get; set; }
        public string? Observaciones { get; set; }
    }

    public class FacturaComprobanteClienteViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Documento { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string? Domicilio { get; set; }
    }

    public class FacturaComprobanteLineaViewModel
    {
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal Descuento { get; set; }
        public decimal PorcentajeIVA { get; set; }
        public string AlicuotaIVANombre { get; set; } = string.Empty;
        public decimal SubtotalNeto { get; set; }
        public decimal IVA { get; set; }
        public decimal Total { get; set; }
    }

    public class FacturaComprobanteTotalesViewModel
    {
        public decimal SubtotalNeto { get; set; }
        public decimal IVA { get; set; }
        public decimal TotalProductos { get; set; }
        public decimal RecargoDebitoAplicado { get; set; }
        public decimal? PorcentajeAjustePlanAplicado { get; set; }
        public decimal? MontoAjustePlanAplicado { get; set; }
        public decimal Total { get; set; }
    }
}
