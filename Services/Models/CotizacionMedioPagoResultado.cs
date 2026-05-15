namespace TheBuryProject.Services.Models;

public sealed class CotizacionMedioPagoResultado
{
    public CotizacionMedioPagoTipo MedioPago { get; init; }
    public string NombreMedioPago { get; init; } = string.Empty;
    public CotizacionOpcionPagoEstado Estado { get; init; } = CotizacionOpcionPagoEstado.Disponible;
    public bool Disponible { get; init; }
    public string? MotivoNoDisponible { get; init; }
    public List<CotizacionPlanPagoResultado> Planes { get; init; } = new();
}
