using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public sealed class CreditoSimulacionVentaRequest
{
    /// <summary>Ignorado cuando <see cref="VentaId"/> tiene valor: el total real de la venta manda.</summary>
    public decimal TotalVenta { get; init; }
    public decimal? Anticipo { get; init; }
    public int Cuotas { get; init; }
    public decimal? GastosAdministrativos { get; init; }
    public string? FechaPrimeraCuota { get; init; }

    /// <summary>
    /// ML6.1 — Contrato congelado: ignorado siempre. El servidor resuelve el porcentaje
    /// exclusivamente desde el plan de cuotas (o la tasa única global sin contexto de productos).
    /// Se conserva el campo por compatibilidad de API, nunca como autoridad del porcentaje —ni
    /// siquiera con <see cref="FuenteConfiguracion"/> y <see cref="MetodoCalculo"/> ambos Manual.
    /// </summary>
    public decimal? TasaMensual { get; init; }

    /// <summary>Venta a la que pertenece la simulación. Autoritativa para el total y los planes.</summary>
    public int? VentaId { get; init; }

    /// <summary>
    /// Productos a cotizar cuando todavía no existe una venta persistida (p. ej. Cotización).
    /// Ignorados cuando <see cref="VentaId"/> tiene valor: en ese caso los productos salen de
    /// <c>venta.Detalles</c>. Habilita la misma resolución de planes/porcentaje que usa una venta
    /// real, sin necesidad de crearla primero.
    /// </summary>
    public IEnumerable<int>? ProductoIds { get; init; }

    /// <summary>Cliente a evaluar cuando no hay <see cref="VentaId"/> (ver <see cref="ProductoIds"/>).</summary>
    public int? ClienteId { get; init; }

    /// <summary>
    /// ML6.1: ya no decide de dónde sale el porcentaje (siempre del plan). Solo sigue
    /// determinando si hace falta resolver <see cref="ClienteId"/> (rama "por cliente").
    /// </summary>
    public MetodoCalculoCredito? MetodoCalculo { get; init; }

    /// <summary>
    /// ML6.1: ya no decide de dónde sale el porcentaje (siempre del plan). Solo sigue
    /// determinando si hace falta resolver <see cref="ClienteId"/> (rama "por cliente").
    /// </summary>
    public FuenteConfiguracionCredito? FuenteConfiguracion { get; init; }
}
