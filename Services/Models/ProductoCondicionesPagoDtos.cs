using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public enum TipoRestriccionCuotas
{
    MaxCuotasSinInteres = 0,
    MaxCuotasConInteres = 1,
    MaxCuotasCredito = 2
}

public enum SeveridadValidacionCondicionPago
{
    Advertencia = 0,
    Error = 1
}

public enum CodigoValidacionCondicionPago
{
    ReglaTarjetaGeneralDuplicada = 0,
    ReglaTarjetaEspecificaDuplicada = 1,
    CuotasMenoresAUno = 2,
    TipoPagoTarjetaLegacyAmbiguo = 3
}

/// <summary>
/// Contrato puro de lectura/resolucion para declarar condiciones de pago por producto, sin persistencia asociada.
/// </summary>
public sealed class ProductoCondicionPagoDto
{
    public int? Id { get; init; }

    public int ProductoId { get; init; }

    public TipoPago TipoPago { get; init; }

    /// <summary>
    /// Null significa usar la configuracion global. False bloquea el medio para el producto.
    /// </summary>
    public bool? Permitido { get; init; }

    public int? MaxCuotasSinInteres { get; init; }

    public int? MaxCuotasConInteres { get; init; }

    public int? MaxCuotasCredito { get; init; }

    public decimal? PorcentajeRecargo { get; init; }

    public decimal? PorcentajeDescuentoMaximo { get; init; }

    public bool Activo { get; init; } = true;

    public string? Observaciones { get; init; }

    public byte[]? RowVersion { get; init; }

    public IReadOnlyList<ProductoCondicionPagoTarjetaDto> Tarjetas { get; init; } =
        Array.Empty<ProductoCondicionPagoTarjetaDto>();
}

/// <summary>
/// Regla opcional de tarjeta. ConfiguracionTarjetaId null representa regla general del tipo tarjeta.
/// </summary>
public sealed class ProductoCondicionPagoTarjetaDto
{
    public int? Id { get; init; }

    public int? ConfiguracionTarjetaId { get; init; }

    /// <summary>
    /// Null hereda de la condicion general y luego de la configuracion global.
    /// </summary>
    public bool? Permitido { get; init; }

    public int? MaxCuotasSinInteres { get; init; }

    public int? MaxCuotasConInteres { get; init; }

    public decimal? PorcentajeRecargo { get; init; }

    public decimal? PorcentajeDescuentoMaximo { get; init; }

    public bool Activo { get; init; } = true;

    public string? Observaciones { get; init; }

    public byte[]? RowVersion { get; init; }
}

public sealed class ProductoCondicionesPagoLecturaDto
{
    public int ProductoId { get; init; }

    public string? ProductoCodigo { get; init; }

    public string? ProductoNombre { get; init; }

    public IReadOnlyList<ProductoCondicionPagoDto> Condiciones { get; init; } =
        Array.Empty<ProductoCondicionPagoDto>();
}

public sealed class GuardarProductoCondicionesPagoRequest
{
    public int ProductoId { get; init; }

    public IReadOnlyList<GuardarProductoCondicionPagoItem> Condiciones { get; init; } =
        Array.Empty<GuardarProductoCondicionPagoItem>();
}

public sealed class GuardarProductoCondicionPagoItem
{
    public int? Id { get; init; }

    public TipoPago TipoPago { get; init; }

    public bool? Permitido { get; init; }

    public int? MaxCuotasSinInteres { get; init; }

    public int? MaxCuotasConInteres { get; init; }

    public int? MaxCuotasCredito { get; init; }

    public decimal? PorcentajeRecargo { get; init; }

    public decimal? PorcentajeDescuentoMaximo { get; init; }

    public bool Activo { get; init; } = true;

    public string? Observaciones { get; init; }

    public byte[]? RowVersion { get; init; }

    public IReadOnlyList<GuardarProductoCondicionPagoTarjetaItem> Tarjetas { get; init; } =
        Array.Empty<GuardarProductoCondicionPagoTarjetaItem>();
}

public sealed class GuardarProductoCondicionPagoTarjetaItem
{
    public int? Id { get; init; }

    public int? ConfiguracionTarjetaId { get; init; }

    public bool? Permitido { get; init; }

    public int? MaxCuotasSinInteres { get; init; }

    public int? MaxCuotasConInteres { get; init; }

    public decimal? PorcentajeRecargo { get; init; }

    public decimal? PorcentajeDescuentoMaximo { get; init; }

    public bool Activo { get; init; } = true;

    public string? Observaciones { get; init; }

    public byte[]? RowVersion { get; init; }
}

public sealed class ProductoCondicionPagoValidacionDto
{
    public int ProductoId { get; init; }

    public TipoPago TipoPago { get; init; }

    public int? ConfiguracionTarjetaId { get; init; }

    public CodigoValidacionCondicionPago Codigo { get; init; }

    public SeveridadValidacionCondicionPago Severidad { get; init; }

    public string Motivo { get; init; } = string.Empty;
}

public sealed class ProductoCondicionesPagoEditableDto
{
    public int ProductoId { get; init; }

    public string? ProductoCodigo { get; init; }

    public string? ProductoNombre { get; init; }

    public IReadOnlyList<ProductoCondicionPagoDto> Condiciones { get; init; } =
        Array.Empty<ProductoCondicionPagoDto>();

    public IReadOnlyList<ProductoCondicionPagoTarjetaDisponibleDto> TarjetasDisponibles { get; init; } =
        Array.Empty<ProductoCondicionPagoTarjetaDisponibleDto>();

    public IReadOnlyList<ProductoCondicionPagoValidacionDto> Validaciones { get; init; } =
        Array.Empty<ProductoCondicionPagoValidacionDto>();
}

public sealed class ProductoCondicionPagoTarjetaDisponibleDto
{
    public int Id { get; init; }

    public string NombreTarjeta { get; init; } = string.Empty;

    public TipoTarjeta TipoTarjeta { get; init; }

    public bool Activa { get; init; }
}
