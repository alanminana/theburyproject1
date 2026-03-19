using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para filtros de reporte de ventas
    /// </summary>
    public class ReporteVentasFiltroViewModel
    {
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public int? ClienteId { get; set; }
        public int? ProductoId { get; set; }
        public int? CategoriaId { get; set; }
        public int? MarcaId { get; set; }
        public TipoPago? TipoPago { get; set; }
        public string? VendedorId { get; set; }

        // Para agrupación
        public string? AgruparPor { get; set; } // "dia", "mes", "producto", "cliente", "categoria"
    }

    /// <summary>
    /// ViewModel para resultados de reporte de ventas
    /// </summary>
    public class ReporteVentasResultadoViewModel
    {
        public List<VentaReporteItemViewModel> Ventas { get; set; } = new();
        public decimal TotalVentas { get; set; }
        public decimal TotalCosto { get; set; }
        public decimal TotalGanancia { get; set; }
        public decimal MargenPromedio { get; set; }
        public int CantidadVentas { get; set; }
        public int CantidadProductosVendidos { get; set; }
        public decimal TicketPromedio { get; set; }

        // Estadísticas adicionales
        public Dictionary<string, decimal> VentasPorTipoPago { get; set; } = new();
        public List<ProductoMasVendidoViewModel> ProductosMasVendidos { get; set; } = new();
        public List<ClienteTopViewModel> ClientesTop { get; set; } = new();
    }

    /// <summary>
    /// ViewModel para item de venta en reporte
    /// </summary>
    public class VentaReporteItemViewModel
    {
        public int Id { get; set; }
        public string NumeroVenta { get; set; } = string.Empty;
        public DateTime FechaVenta { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string VendedorNombre { get; set; } = string.Empty;
        public TipoPago TipoPago { get; set; }
        public string TipoPagoDescripcion => TipoPago.ToString();
        public decimal Subtotal { get; set; }
        public decimal Descuento { get; set; }
        public decimal Total { get; set; }
        public decimal Costo { get; set; }
        public decimal Ganancia { get; set; }
        public decimal MargenPorcentaje { get; set; }
        public int CantidadProductos { get; set; }
    }

    /// <summary>
    /// ViewModel para productos más vendidos
    /// </summary>
    public class ProductoMasVendidoViewModel
    {
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public string CategoriaNombre { get; set; } = string.Empty;
        public int CantidadVendida { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal GananciaTotal { get; set; }
        public decimal MargenPromedio { get; set; }
    }

    /// <summary>
    /// ViewModel para clientes top
    /// </summary>
    public class ClienteTopViewModel
    {
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string ClienteDocumento { get; set; } = string.Empty;
        public int CantidadCompras { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal TicketPromedio { get; set; }
        public DateTime? UltimaCompra { get; set; }
    }

    /// <summary>
    /// ViewModel para reporte de márgenes de productos
    /// </summary>
    public class ReporteMargenesViewModel
    {
        public List<ProductoMargenViewModel> Productos { get; set; } = new();
        public decimal MargenPromedioGeneral { get; set; }
        public decimal GananciaTotalPotencial { get; set; }
        public int ProductosConMargenBajo { get; set; }
        public int ProductosConMargenAlto { get; set; }
    }

    /// <summary>
    /// ViewModel para margen de producto
    /// </summary>
    public class ProductoMargenViewModel
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string CategoriaNombre { get; set; } = string.Empty;
        public string? MarcaNombre { get; set; }
        public decimal PrecioCompra { get; set; }
        public decimal PrecioVenta { get; set; }
        public decimal Ganancia { get; set; }
        public decimal MargenPorcentaje { get; set; }
        public decimal StockActual { get; set; }
        public decimal GananciaPotencial { get; set; }
        public int VentasUltimos30Dias { get; set; }
        public decimal RotacionMensual { get; set; }

        public string MargenCategoria => MargenPorcentaje switch
        {
            < 10 => "Muy Bajo",
            < 20 => "Bajo",
            < 35 => "Normal",
            < 50 => "Alto",
            _ => "Muy Alto"
        };

        public string BadgeMargen => MargenPorcentaje switch
        {
            < 10 => "bg-danger",
            < 20 => "bg-warning",
            < 35 => "bg-info",
            < 50 => "bg-primary",
            _ => "bg-success"
        };
    }

    /// <summary>
    /// ViewModel para reporte de morosidad
    /// </summary>
    public class ReporteMorosidadViewModel
    {
        public List<ClienteMorosoViewModel> ClientesMorosos { get; set; } = new();
        public decimal TotalDeudaVencida { get; set; }
        public decimal TotalDeudaVigente { get; set; }
        public int CantidadClientesMorosos { get; set; }
        public int CantidadCreditosVencidos { get; set; }
        public decimal PromedioDeudaPorCliente { get; set; }
        public decimal DeudaMayor30Dias { get; set; }
        public decimal DeudaMayor60Dias { get; set; }
        public decimal DeudaMayor90Dias { get; set; }
    }

    /// <summary>
    /// ViewModel para cliente moroso
    /// </summary>
    public class ClienteMorosoViewModel
    {
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string ClienteDocumento { get; set; } = string.Empty;
        public string? ClienteTelefono { get; set; }
        public int CantidadCreditosVencidos { get; set; }
        public decimal TotalDeudaVencida { get; set; }
        public decimal TotalDeudaVigente { get; set; }
        public DateTime? FechaPrimerVencimiento { get; set; }
        public int DiasMaximoAtraso { get; set; }
        public decimal MontoCuotaVencidaMasAntigua { get; set; }
        public int CreditoIdMasAntiguo { get; set; }

        public string NivelRiesgo => DiasMaximoAtraso switch
        {
            < 30 => "Bajo",
            < 60 => "Medio",
            < 90 => "Alto",
            _ => "Crítico"
        };

        public string BadgeRiesgo => DiasMaximoAtraso switch
        {
            < 30 => "bg-warning",
            < 60 => "bg-danger",
            < 90 => "bg-dark",
            _ => "bg-dark text-white"
        };
    }

    /// <summary>
    /// ViewModel para ventas agrupadas (para gráficos)
    /// </summary>
    public class VentasAgrupadasViewModel
    {
        public string Etiqueta { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public int Cantidad { get; set; }
        public decimal Ganancia { get; set; }
    }
}
