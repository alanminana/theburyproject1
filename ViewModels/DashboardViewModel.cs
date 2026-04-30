using TheBuryProject.Models.DTOs;

namespace TheBuryProject.ViewModels
{
    public class DashboardViewModel
    {
        // KPIs Generales
        public int TotalClientes { get; set; }
        public int ClientesActivos { get; set; }
        public int ClientesNuevosMes { get; set; }
        public decimal VentasTotalesHoy { get; set; }
        public decimal VentasTotalesMes { get; set; }
        public decimal VentasTotalesAnio { get; set; }
        public int CantidadVentasMes { get; set; }
        public decimal TicketPromedio { get; set; }

        // KPIs de Créditos
        public int CreditosActivos { get; set; }
        public decimal? MontoTotalCreditos { get; set; }
        public decimal SaldoPendienteTotal { get; set; }
        public int CuotasVencidasTotal { get; set; }
        public decimal MontoVencidoTotal { get; set; }

        // KPIs de Cobranza
        public decimal CobranzaHoy { get; set; }
        public decimal CobranzaMes { get; set; }
        public decimal CobranzaAnio { get; set; }
        public decimal TasaMorosidad { get; set; }
        public decimal EfectividadCobranza { get; set; }

        // KPIs de Stock
        public int ProductosTotales { get; set; }
        public int ProductosStockBajo { get; set; }
        public decimal ValorStockPrecioVenta { get; set; }
        public decimal ValorStockCostoActual { get; set; }
        public decimal ValorTotalStock { get; set; }

        // Datos para gráficos
        public List<VentasPorDiaDto> VentasUltimos7Dias { get; set; } = new();
        public List<VentasPorMesDto> VentasUltimos12Meses { get; set; } = new();
        public List<ProductoMasVendidoDto> ProductosMasVendidos { get; set; } = new();
        public List<EstadoCreditoDto> CreditosPorEstado { get; set; } = new();
        public List<CobranzaPorMesDto> CobranzaUltimos6Meses { get; set; } = new();

        // Alertas y Cuotas
        public List<CuotaProximaVencerDto> CuotasProximasVencer { get; set; } = new();
        public List<CuotaVencidaDto> CuotasVencidasLista { get; set; } = new();
        public int CuotasProximasVencerCount { get; set; }
        public decimal MontoCuotasProximasVencer { get; set; }

        // Órdenes de Compra pendientes (Pagos Proveedores)
        public List<OrdenCompraPendienteDto> OrdenesCompraPendientes { get; set; } = new();
        public int OrdenesCompraPendientesCount { get; set; }
        public decimal MontoOrdenesCompraPendientes { get; set; }
    }

    /// <summary>
    /// DTO para cuotas próximas a vencer (próximos 7 días)
    /// </summary>
    public class CuotaProximaVencerDto
    {
        public int CuotaId { get; set; }
        public int CreditoId { get; set; }
        public string CreditoNumero { get; set; } = string.Empty;
        public int NumeroCuota { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public int ClienteId { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal Monto { get; set; }
        public int DiasParaVencer { get; set; }
    }

    /// <summary>
    /// DTO para cuotas vencidas sin pagar
    /// </summary>
    public class CuotaVencidaDto
    {
        public int CuotaId { get; set; }
        public int CreditoId { get; set; }
        public string CreditoNumero { get; set; } = string.Empty;
        public int NumeroCuota { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public int ClienteId { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal Monto { get; set; }
        public int DiasVencidos { get; set; }
        public decimal MontoPunitorio { get; set; }
    }

    /// <summary>
    /// DTO para órdenes de compra pendientes (panel Pagos Proveedores)
    /// </summary>
    public class OrdenCompraPendienteDto
    {
        public int OrdenCompraId { get; set; }
        public string Numero { get; set; } = string.Empty;
        public string ProveedorNombre { get; set; } = string.Empty;
        public int ProveedorId { get; set; }
        public DateTime FechaEmision { get; set; }
        public DateTime? FechaEntregaEstimada { get; set; }
        public decimal Total { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string EstadoColor { get; set; } = "neutral";
    }
}
