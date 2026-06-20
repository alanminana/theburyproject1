using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Fila de la grilla de órdenes ML.
    /// </summary>
    public class MercadoLibreOrderViewModel
    {
        public int Id { get; set; }
        public long MeliOrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public MercadoLibreOrderEstadoInterno EstadoInterno { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal? NetoEstimado { get; set; }
        public decimal? NetoReal { get; set; }
        public string? BuyerNickname { get; set; }
        public int CantidadItems { get; set; }
        public int? VentaId { get; set; }
        public string? VentaNumero { get; set; }
        public string? ErrorProcesamiento { get; set; }
        public MercadoLibreDevolucionEstado DevolucionEstado { get; set; }
        public long? ShipmentId { get; set; }
        public string? ShipmentStatus { get; set; }
        public string? ShipmentSubStatus { get; set; }
        public string? TrackingNumber { get; set; }
        public MercadoLibreShipmentEstadoInterno EstadoEnvioInterno { get; set; }
        public int ClaimsPendientes { get; set; }
        public MercadoLibreClaimTipo? UltimoClaimTipo { get; set; }
        public MercadoLibreClaimEstado? UltimoClaimEstado { get; set; }
        public bool TieneClaimPendiente => ClaimsPendientes > 0;
        public bool EnvioRequiereAtencion =>
            EstadoEnvioInterno is MercadoLibreShipmentEstadoInterno.Cancelado
                or MercadoLibreShipmentEstadoInterno.Demorado
                or MercadoLibreShipmentEstadoInterno.Desconocido;
        public bool EsSimulada { get; set; }
        public bool EsSimuladaQa { get; set; }
        public bool EsSimuladaOperativa { get; set; }
    }

    /// <summary>
    /// Detalle completo de una orden ML.
    /// </summary>
    public class MercadoLibreOrderDetalleViewModel
    {
        public int Id { get; set; }
        public long MeliOrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public MercadoLibreOrderEstadoInterno EstadoInterno { get; set; }
        public bool EsSimulada { get; set; }
        public bool EsSimuladaQa { get; set; }
        public bool EsSimuladaOperativa { get; set; }
        public bool ModoSimulacion { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime? FechaProcesadoUtc { get; set; }
        public string CurrencyId { get; set; } = "ARS";

        public long? BuyerId { get; set; }
        public string? BuyerNickname { get; set; }

        public decimal TotalAmount { get; set; }
        public decimal? PaidAmount { get; set; }
        public decimal? MontoComision { get; set; }
        public decimal? MontoEnvio { get; set; }
        public decimal MontoOtrosCostos => 0m;
        public decimal? NetoEstimado { get; set; }
        public decimal? NetoReal { get; set; }
        public DateTime? FechaLiquidacionUtc { get; set; }
        public int? MovimientoCajaId { get; set; }

        public decimal? DiferenciaLiquidacion =>
            NetoReal.HasValue && NetoEstimado.HasValue ? NetoReal.Value - NetoEstimado.Value : null;

        public string EstadoLiquidacion =>
            MovimientoCajaId.HasValue || EstadoInterno == MercadoLibreOrderEstadoInterno.Liquidada
                ? "Liquidada"
                : EstadoInterno == MercadoLibreOrderEstadoInterno.Error
                    ? "Error"
                    : EstadoInterno == MercadoLibreOrderEstadoInterno.Ignorada
                        ? "Anulada"
                        : VentaId.HasValue
                            ? "Pendiente"
                            : "Pendiente de venta";

        public bool EsLiquidacionSimulada => EsSimuladaOperativa && MovimientoCajaId.HasValue;

        public int? VentaId { get; set; }
        public string? VentaNumero { get; set; }

        public long? ShipmentId { get; set; }
        public string? ShipmentStatus { get; set; }
        public string? ShipmentSubStatus { get; set; }
        public string? TrackingNumber { get; set; }
        public string? TrackingMethod { get; set; }
        public string? ShippingMode { get; set; }
        public string? ShippingType { get; set; }
        public DateTime? FechaDespachoUtc { get; set; }
        public DateTime? FechaEntregadoUtc { get; set; }
        public DateTime? FechaUltimaActualizacionEnvioUtc { get; set; }
        public MercadoLibreShipmentEstadoInterno EstadoEnvioInterno { get; set; }
        public bool EnvioRequiereAtencion =>
            EstadoEnvioInterno is MercadoLibreShipmentEstadoInterno.Cancelado
                or MercadoLibreShipmentEstadoInterno.Demorado
                or MercadoLibreShipmentEstadoInterno.Desconocido;

        public MercadoLibreDevolucionEstado DevolucionEstado { get; set; }
        public string? DevolucionNota { get; set; }
        public List<MercadoLibreClaimViewModel> Claims { get; set; } = new();
        public bool TieneClaimPendiente => Claims.Any(c => c.Estado == MercadoLibreClaimEstado.PendienteRevision);

        public string? ErrorProcesamiento { get; set; }

        public List<MercadoLibreOrderItemViewModel> Items { get; set; } = new();

        public bool PuedeCrearVenta =>
            !EsSimuladaQa
            && VentaId is null
            && (EstadoInterno is MercadoLibreOrderEstadoInterno.Importada
                or MercadoLibreOrderEstadoInterno.PendienteVinculacion
                or MercadoLibreOrderEstadoInterno.PendienteAsignarUnidad
                or MercadoLibreOrderEstadoInterno.Error)
            && string.Equals(Status, "paid", StringComparison.OrdinalIgnoreCase);

        /// <summary>Mientras no haya venta, las líneas trazables admiten (re)asignar unidades.</summary>
        public bool PuedeAsignarUnidades => VentaId is null;

        public bool PuedeLiquidar =>
            VentaId.HasValue
            && MovimientoCajaId is null
            && EstadoInterno == MercadoLibreOrderEstadoInterno.VentaCreada
            && !EsSimuladaQa
            && (!EsSimuladaOperativa || ModoSimulacion);
    }

    public class MercadoLibreOrderItemViewModel
    {
        public int Id { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public long? VariationId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal? SaleFee { get; set; }
        public string? SellerSku { get; set; }

        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }

        public bool Vinculado => ProductoId.HasValue;

        /// <summary>El producto es trazable o el origen de stock exige unidades físicas.</summary>
        public bool RequiereUnidadFisica { get; set; }

        public List<MercadoLibreUnidadOptionViewModel> UnidadesAsignadasDetalle { get; set; } = new();

        /// <summary>Unidades EnStock del producto para el selector de asignación manual.</summary>
        public List<MercadoLibreUnidadOptionViewModel> UnidadesDisponibles { get; set; } = new();
    }

    /// <summary>Opción de unidad física (selector de asignación y detalle).</summary>
    public class MercadoLibreUnidadOptionViewModel
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string? NumeroSerie { get; set; }

        public string Etiqueta => string.IsNullOrEmpty(NumeroSerie) ? Codigo : $"{Codigo} (S/N {NumeroSerie})";
    }

    public class MercadoLibreClaimViewModel
    {
        public int Id { get; set; }
        public string? MercadoLibreClaimId { get; set; }
        public MercadoLibreClaimTipo Tipo { get; set; }
        public MercadoLibreClaimEstado Estado { get; set; }
        public string? Motivo { get; set; }
        public string? ResolucionManual { get; set; }
        public MercadoLibreClaimAccionStock AccionStock { get; set; }
        public MercadoLibreClaimAccionEconomica AccionEconomica { get; set; }
        public int? MovimientoStockId { get; set; }
        public int? MovimientoCajaId { get; set; }
        public string? Observaciones { get; set; }
        public bool EsSimuladoLocal { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime? FechaResolucionUtc { get; set; }
        public string? UsuarioResolucion { get; set; }
        public bool Pendiente => Estado == MercadoLibreClaimEstado.PendienteRevision;
    }
}
