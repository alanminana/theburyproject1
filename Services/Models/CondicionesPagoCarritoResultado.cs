using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public enum FuenteCondicionPagoEfectiva
{
    Global = 0,
    Producto = 1,
    TarjetaGeneral = 2,
    TarjetaEspecifica = 3,
    Carrito = 4
}

public enum AlcanceBloqueoPago
{
    Ninguno = 0,
    Medio = 1,
    TarjetaEspecifica = 2
}

public sealed class CondicionesPagoCarritoResultado
{
    public TipoPago TipoPago { get; init; }

    public int? ConfiguracionTarjetaId { get; init; }

    public bool Permitido { get; init; } = true;

    public FuenteCondicionPagoEfectiva FuentePermitido { get; init; } = FuenteCondicionPagoEfectiva.Global;

    public FuenteCondicionPagoEfectiva FuenteRestriccion { get; init; } = FuenteCondicionPagoEfectiva.Global;

    public AlcanceBloqueoPago AlcanceBloqueo { get; init; } = AlcanceBloqueoPago.Ninguno;

    public int? MaxCuotasSinInteres { get; init; }

    public int? MaxCuotasConInteres { get; init; }

    public int? MaxCuotasCredito { get; init; }

    public bool TieneRestriccionesPorProducto { get; init; }

    public IReadOnlyList<int> ProductoIdsBloqueantes { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> ProductoIdsRestrictivos { get; init; } = Array.Empty<int>();

    public IReadOnlyList<CondicionPagoBloqueoDetalleDto> Bloqueos { get; init; } =
        Array.Empty<CondicionPagoBloqueoDetalleDto>();

    public IReadOnlyList<CondicionPagoRestriccionCuotasDto> Restricciones { get; init; } =
        Array.Empty<CondicionPagoRestriccionCuotasDto>();

    public IReadOnlyList<CondicionPagoAjusteInformativoDto> AjustesInformativos { get; init; } =
        Array.Empty<CondicionPagoAjusteInformativoDto>();

    /// <summary>
    /// Planes activos disponibles resueltos con precedencia tarjeta-específica > tarjeta-general > medio-general.
    /// Vacío cuando no hay planes configurados (se usa fallback de máximos escalares).
    /// AjustePorcentaje es informativo en esta fase: no modifica totales.
    /// </summary>
    public IReadOnlyList<ProductoCondicionPagoPlanDto> PlanesDisponibles { get; init; } =
        Array.Empty<ProductoCondicionPagoPlanDto>();

    /// <summary>
    /// True cuando al menos un producto tiene planes activos configurados.
    /// </summary>
    public bool UsaPlanesEspecificos { get; init; }

    /// <summary>
    /// True cuando no hay planes activos: se usan los máximos escalares como única restricción de cuotas.
    /// </summary>
    public bool UsaFallbackGlobalPlanes { get; init; }

    /// <summary>
    /// Total de referencia recibido por el helper. Se devuelve intacto para explicitar que no aplica ajustes.
    /// </summary>
    public decimal? TotalReferencia { get; init; }

    public decimal? TotalSinAplicarAjustes { get; init; }
}

public sealed class CondicionPagoBloqueoDetalleDto
{
    public int ProductoId { get; init; }

    public TipoPago TipoPago { get; init; }

    public int? ConfiguracionTarjetaId { get; init; }

    public AlcanceBloqueoPago Alcance { get; init; }

    public FuenteCondicionPagoEfectiva Fuente { get; init; }

    public string Motivo { get; init; } = string.Empty;
}

public sealed class CondicionPagoRestriccionCuotasDto
{
    public int ProductoId { get; init; }

    public TipoPago TipoPago { get; init; }

    public int? ConfiguracionTarjetaId { get; init; }

    public TipoRestriccionCuotas TipoRestriccion { get; init; }

    public int Valor { get; init; }

    public FuenteCondicionPagoEfectiva Fuente { get; init; }
}

public sealed class CondicionPagoAjusteInformativoDto
{
    public int ProductoId { get; init; }

    public FuenteCondicionPagoEfectiva Fuente { get; init; }

    public decimal? PorcentajeRecargo { get; init; }

    public decimal? PorcentajeDescuentoMaximo { get; init; }
}
