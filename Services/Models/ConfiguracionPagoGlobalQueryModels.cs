using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public sealed class ConfiguracionPagoGlobalResultado
{
    public IReadOnlyList<MedioPagoGlobalDto> Medios { get; init; } = Array.Empty<MedioPagoGlobalDto>();
}

public sealed class MedioPagoGlobalDto
{
    public int Id { get; init; }
    public TipoPago TipoPago { get; init; }
    public string NombreVisible { get; init; } = string.Empty;
    public bool Activo { get; init; }
    public string? Observaciones { get; init; }
    public AjusteMedioPagoGlobalDto Ajuste { get; init; } = new();
    public IReadOnlyList<TarjetaPagoGlobalDto> Tarjetas { get; init; } = Array.Empty<TarjetaPagoGlobalDto>();
    public IReadOnlyList<PlanPagoGlobalConfiguradoDto> Planes { get; init; } = Array.Empty<PlanPagoGlobalConfiguradoDto>();
}

public sealed class AjusteMedioPagoGlobalDto
{
    public bool PermiteDescuento { get; init; }
    public decimal? PorcentajeDescuentoMaximo { get; init; }
    public bool TieneRecargo { get; init; }
    public decimal? PorcentajeRecargo { get; init; }
}

public sealed class TarjetaPagoGlobalDto
{
    public int Id { get; init; }
    public int ConfiguracionPagoId { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public TipoTarjeta TipoTarjeta { get; init; }
    public bool Activa { get; init; }
    public bool PermiteCuotas { get; init; }
    public int? CantidadMaximaCuotas { get; init; }
    public TipoCuotaTarjeta? TipoCuota { get; init; }
    public decimal? TasaInteresesMensual { get; init; }
    public bool TieneRecargoDebito { get; init; }
    public decimal? PorcentajeRecargoDebito { get; init; }
    public string? Observaciones { get; init; }
}

public sealed class PlanPagoGlobalConfiguradoDto
{
    public int Id { get; init; }
    public int ConfiguracionPagoId { get; init; }
    public int? ConfiguracionTarjetaId { get; init; }
    public TipoPago TipoPago { get; init; }
    public int CantidadCuotas { get; init; }
    public bool Activo { get; init; }
    public TipoAjustePagoPlan TipoAjuste { get; init; }
    public decimal AjustePorcentaje { get; init; }
    public string? Etiqueta { get; init; }
    public int Orden { get; init; }
    public string? Observaciones { get; init; }
    public bool EsPlanGeneral => ConfiguracionTarjetaId is null;
}
