namespace TheBuryProject.Services.Models;

public sealed class MediosPagoPorProductoResultado
{
    public bool SinRestriccionesPropias { get; init; }
    public IReadOnlyList<MedioHabilitadoDto> Medios { get; init; } = Array.Empty<MedioHabilitadoDto>();
}

public sealed class MedioHabilitadoDto
{
    public int TipoPago { get; init; }
    public decimal? PorcentajeRecargo { get; init; }
    public IReadOnlyList<ProductoCondicionPagoPlanDto> Planes { get; init; } = Array.Empty<ProductoCondicionPagoPlanDto>();
}
