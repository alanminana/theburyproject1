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
