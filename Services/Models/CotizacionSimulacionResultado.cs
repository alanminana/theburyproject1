namespace TheBuryProject.Services.Models;

public sealed class CotizacionSimulacionResultado
{
    public bool Exitoso { get; init; }
    public List<string> Advertencias { get; init; } = new();
    public List<string> Errores { get; init; } = new();
    public List<CotizacionProductoResultado> Productos { get; init; } = new();
    public List<CotizacionMedioPagoResultado> OpcionesPago { get; init; } = new();
    public decimal Subtotal { get; init; }
    public decimal DescuentoTotal { get; init; }
    public decimal TotalBase { get; init; }
    public DateTime FechaCalculo { get; init; }
}
