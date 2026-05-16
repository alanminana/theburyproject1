using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public sealed class CotizacionConversionPreviewResultado
{
    public bool Convertible { get; init; }
    public List<string> Errores { get; init; } = new();
    public List<string> Advertencias { get; init; } = new();
    public int CotizacionId { get; init; }
    public EstadoCotizacion EstadoCotizacion { get; init; }
    public int? ClienteId { get; init; }
    public bool ClienteFaltante { get; init; }
    public bool CotizacionVencida { get; init; }
    public bool HayCambiosDePrecios { get; init; }
    public bool HayProductosTrazables { get; init; }
    public decimal TotalCotizado { get; init; }
    public List<CotizacionConversionDetallePreview> Detalles { get; init; } = new();
}

public sealed class CotizacionConversionDetallePreview
{
    public int ProductoId { get; init; }
    public string CodigoProducto { get; init; } = string.Empty;
    public string NombreProducto { get; init; } = string.Empty;
    public int Cantidad { get; init; }
    public decimal PrecioCotizado { get; init; }
    public decimal? PrecioActual { get; init; }
    public bool ProductoActivo { get; init; }
    public bool PrecioCambio { get; init; }
    public bool RequiereUnidadFisica { get; init; }
    public decimal? DiferenciaUnitaria { get; init; }
    public decimal? DiferenciaTotal { get; init; }
    public List<string> Advertencias { get; init; } = new();
}

public sealed class CotizacionConversionRequest
{
    public bool UsarPrecioCotizado { get; init; } = true;
    public bool ConfirmarAdvertencias { get; init; } = false;
    public int? ClienteIdOverride { get; init; }
    public string? ObservacionesAdicionales { get; init; }
}

public sealed class CotizacionConversionResultado
{
    public bool Exitoso { get; init; }
    public List<string> Errores { get; init; } = new();
    public List<string> Advertencias { get; init; } = new();
    public int CotizacionId { get; init; }
    public int? VentaId { get; init; }
    public string? NumeroVenta { get; init; }
    public EstadoVenta? EstadoVenta { get; init; }

    public static CotizacionConversionResultado Fallido(int cotizacionId, IEnumerable<string> errores) =>
        new() { Exitoso = false, CotizacionId = cotizacionId, Errores = errores.ToList() };
}
