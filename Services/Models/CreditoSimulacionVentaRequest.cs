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
    /// Ignorado salvo que <see cref="FuenteConfiguracion"/> y <see cref="MetodoCalculo"/> sean
    /// ambos Manual: de lo contrario el servidor resuelve el porcentaje (plan/producto/global).
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

    public MetodoCalculoCredito? MetodoCalculo { get; init; }
    public FuenteConfiguracionCredito? FuenteConfiguracion { get; init; }
}
