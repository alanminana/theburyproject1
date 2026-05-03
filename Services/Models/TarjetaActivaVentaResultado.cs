using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public sealed class TarjetaActivaVentaResultado
{
    public int Id { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public TipoTarjeta Tipo { get; init; }
    public bool PermiteCuotas { get; init; }
    public int? CantidadMaximaCuotas { get; init; }
    public TipoCuotaTarjeta? TipoCuota { get; init; }
    public decimal? TasaInteres { get; init; }
    public bool TieneRecargo { get; init; }
    public decimal? PorcentajeRecargo { get; init; }
}
