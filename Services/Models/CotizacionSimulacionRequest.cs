namespace TheBuryProject.Services.Models;

public sealed class CotizacionSimulacionRequest
{
    public List<CotizacionProductoRequest> Productos { get; init; } = new();
    public int? ClienteId { get; init; }
    public string? NombreClienteLibre { get; init; }

    /// <summary>
    /// Anticipo para Credito personal. Se aplica antes del recargo (saldo = total - anticipo).
    /// Ignorado por el resto de los medios de pago. Validado server-side contra el total real
    /// (0 &lt;= anticipo &lt;= total): nunca se calcula en el navegador.
    /// </summary>
    public decimal? Anticipo { get; init; }
    public decimal? DescuentoGeneralPorcentaje { get; init; }
    public decimal? DescuentoGeneralImporte { get; init; }
    public bool IncluirEfectivo { get; init; } = true;
    public bool IncluirTransferencia { get; init; } = true;
    public bool IncluirTarjetaCredito { get; init; } = true;
    public bool IncluirTarjetaDebito { get; init; } = true;
    public bool IncluirMercadoPago { get; init; } = true;
    public bool IncluirCreditoPersonal { get; init; } = true;
    public int? ConfiguracionTarjetaId { get; init; }
    public int[]? CuotasSolicitadas { get; init; }
    public DateTime? FechaCotizacion { get; init; }

    /// <summary>Envío Ciudad/Rural (null = sin envío). El precio sale de la tabla global.</summary>
    public TheBuryProject.Models.Enums.TipoServicioVenta? TipoEnvio { get; init; }

    /// <summary>
    /// True si el pedido trae algún descuento comercial manual (general o por línea). Es el dato que
    /// exige el permiso independiente <c>ventas.discount</c>; los ajustes automáticos por medio de
    /// pago (recargo/descuento del plan) no pasan por acá.
    /// </summary>
    public bool TieneDescuentosManuales() =>
        DescuentoGeneralPorcentaje is > 0m
        || DescuentoGeneralImporte is > 0m
        || Productos.Any(p => p.DescuentoPorcentaje is > 0m || p.DescuentoImporte is > 0m);
}
