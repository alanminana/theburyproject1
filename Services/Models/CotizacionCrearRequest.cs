using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public sealed class CotizacionCrearRequest
{
    public CotizacionSimulacionRequest Simulacion { get; init; } = new();
    public CotizacionOpcionPagoSeleccionadaRequest? OpcionSeleccionada { get; init; }
    public string? Observaciones { get; init; }
    public string? NombreClienteLibre { get; init; }
    public string? TelefonoClienteLibre { get; init; }
    public DateTime? FechaVencimiento { get; init; }
}

public sealed class CotizacionOpcionPagoSeleccionadaRequest
{
    public CotizacionMedioPagoTipo MedioPago { get; init; }
    public string? Plan { get; init; }
    public int? CantidadCuotas { get; init; }
}

public sealed class CotizacionFiltros
{
    public int? ClienteId { get; init; }
    public EstadoCotizacion? Estado { get; init; }
    public DateTime? FechaDesde { get; init; }
    public DateTime? FechaHasta { get; init; }
    public string? Busqueda { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed class CotizacionResultado
{
    public int Id { get; init; }
    public string Numero { get; init; } = string.Empty;
    public DateTime Fecha { get; init; }
    public EstadoCotizacion Estado { get; init; }
    public int? ClienteId { get; init; }
    public string? ClienteNombre { get; init; }
    public string? NombreClienteLibre { get; init; }
    public string? TelefonoClienteLibre { get; init; }
    public string? Observaciones { get; init; }
    public decimal Subtotal { get; init; }
    public decimal DescuentoTotal { get; init; }
    public decimal TotalBase { get; init; }
    public CotizacionMedioPagoTipo? MedioPagoSeleccionado { get; init; }
    public string? PlanSeleccionado { get; init; }
    public int? CantidadCuotasSeleccionada { get; init; }
    public decimal? TotalSeleccionado { get; init; }
    public decimal? ValorCuotaSeleccionada { get; init; }
    public DateTime? FechaVencimiento { get; init; }
    public IReadOnlyList<CotizacionDetalleResultado> Detalles { get; init; } = Array.Empty<CotizacionDetalleResultado>();
    public IReadOnlyList<CotizacionPagoSimuladoResultado> OpcionesPago { get; init; } = Array.Empty<CotizacionPagoSimuladoResultado>();

    // Trazabilidad: venta generada por conversión (null si no fue convertida aún)
    public int? VentaConvertidaId { get; init; }
    public string? NumeroVentaConvertida { get; init; }
}

public sealed class CotizacionDetalleResultado
{
    public int ProductoId { get; init; }
    public string CodigoProductoSnapshot { get; init; } = string.Empty;
    public string NombreProductoSnapshot { get; init; } = string.Empty;
    public decimal Cantidad { get; init; }
    public decimal PrecioUnitarioSnapshot { get; init; }
    public decimal? DescuentoPorcentajeSnapshot { get; init; }
    public decimal? DescuentoImporteSnapshot { get; init; }
    public decimal Subtotal { get; init; }
}

public sealed class CotizacionPagoSimuladoResultado
{
    public CotizacionMedioPagoTipo MedioPago { get; init; }
    public string NombreMedioPago { get; init; } = string.Empty;
    public CotizacionOpcionPagoEstado Estado { get; init; }
    public string? Plan { get; init; }
    public int? CantidadCuotas { get; init; }
    public decimal RecargoPorcentaje { get; init; }
    public decimal DescuentoPorcentaje { get; init; }
    public decimal InteresPorcentaje { get; init; }
    public decimal? TasaMensual { get; init; }
    public decimal? CostoFinancieroTotal { get; init; }
    public decimal Total { get; init; }
    public decimal? ValorCuota { get; init; }
    public bool Recomendado { get; init; }
    public bool Seleccionado { get; init; }
    public string? AdvertenciasJson { get; init; }
}

public sealed class CotizacionListadoItem
{
    public int Id { get; init; }
    public string Numero { get; init; } = string.Empty;
    public DateTime Fecha { get; init; }
    public EstadoCotizacion Estado { get; init; }
    public string Cliente { get; init; } = "Cliente mostrador";
    public decimal TotalBase { get; init; }
    public decimal? TotalSeleccionado { get; init; }
    public CotizacionMedioPagoTipo? MedioPagoSeleccionado { get; init; }
}

public sealed class CotizacionListadoResultado
{
    public IReadOnlyList<CotizacionListadoItem> Items { get; init; } = Array.Empty<CotizacionListadoItem>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
