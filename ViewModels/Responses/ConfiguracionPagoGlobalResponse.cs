using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels.Responses;

public sealed class ConfiguracionPagoGlobalResponse
{
    public IReadOnlyList<MedioPagoGlobalResponse> Medios { get; init; } = Array.Empty<MedioPagoGlobalResponse>();
}

public sealed class MedioPagoGlobalResponse
{
    public int Id { get; init; }
    public TipoPago TipoPago { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public bool Activo { get; init; }
    public string? Observaciones { get; init; }
    public AjusteMedioPagoGlobalResponse Ajuste { get; init; } = new();
    public IReadOnlyList<TarjetaPagoGlobalResponse> Tarjetas { get; init; } = Array.Empty<TarjetaPagoGlobalResponse>();
    public IReadOnlyList<PlanPagoGlobalResponse> PlanesGenerales { get; init; } = Array.Empty<PlanPagoGlobalResponse>();
}

public sealed class AjusteMedioPagoGlobalResponse
{
    public bool PermiteDescuento { get; init; }
    public decimal? PorcentajeDescuentoMaximo { get; init; }
    public bool TieneRecargo { get; init; }
    public decimal? PorcentajeRecargo { get; init; }
}

public sealed class TarjetaPagoGlobalResponse
{
    public int Id { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public TipoTarjeta TipoTarjeta { get; init; }
    public IReadOnlyList<PlanPagoGlobalResponse> PlanesEspecificos { get; init; } = Array.Empty<PlanPagoGlobalResponse>();
}

public sealed class PlanPagoGlobalResponse
{
    public int Id { get; init; }
    public int CantidadCuotas { get; init; }
    public TipoAjustePagoPlan TipoAjuste { get; init; }
    public decimal AjustePorcentaje { get; init; }
    public string? Etiqueta { get; init; }
    public int Orden { get; init; }
    public string? Observaciones { get; init; }
}
