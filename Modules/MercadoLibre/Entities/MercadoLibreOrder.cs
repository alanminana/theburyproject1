using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Modules.MercadoLibre.Entities
{
    /// <summary>
    /// Orden de Mercado Libre importada al ERP.
    /// MeliOrderId tiene índice único para garantizar idempotencia:
    /// una orden de ML nunca puede importarse dos veces ni generar dos Ventas.
    /// </summary>
    public class MercadoLibreOrder : AuditableEntity
    {
        public int AccountId { get; set; }

        /// <summary>
        /// Id de la orden en Mercado Libre. Único.
        /// </summary>
        public long MeliOrderId { get; set; }

        [StringLength(30)]
        public string Status { get; set; } = string.Empty;

        public decimal TotalAmount { get; set; }

        [StringLength(10)]
        public string CurrencyId { get; set; } = "ARS";

        /// <summary>
        /// Fecha de creación de la orden en ML (UTC).
        /// </summary>
        public DateTime FechaCreacionUtc { get; set; }

        public long? BuyerId { get; set; }

        [StringLength(100)]
        public string? BuyerNickname { get; set; }

        public long? ShipmentId { get; set; }

        /// <summary>
        /// Venta interna asociada (si se generó). Null = aún sin asociar.
        /// </summary>
        public int? VentaId { get; set; }

        /// <summary>
        /// JSON crudo de la orden tal como lo devolvió la API.
        /// </summary>
        public string? RawJson { get; set; }

        // ------------------------------------------------------------------
        // Procesamiento interno (Fase C)
        // ------------------------------------------------------------------

        /// <summary>Estado del procesamiento dentro del ERP (no confundir con Status de ML).</summary>
        public MercadoLibreOrderEstadoInterno EstadoInterno { get; set; } = MercadoLibreOrderEstadoInterno.Importada;

        /// <summary>Fecha del último procesamiento (creación de venta o intento).</summary>
        public DateTime? FechaProcesadoUtc { get; set; }

        [StringLength(1000)]
        public string? ErrorProcesamiento { get; set; }

        // ------------------------------------------------------------------
        // Liquidación (Fase D) — ML no es efectivo directo: la venta queda
        // pendiente de liquidación hasta que MercadoPago acredita el neto.
        // ------------------------------------------------------------------

        /// <summary>Monto efectivamente pagado por el comprador (paid_amount).</summary>
        public decimal? PaidAmount { get; set; }

        /// <summary>Comisión total de ML (suma de sale_fee de las líneas).</summary>
        public decimal? MontoComision { get; set; }

        /// <summary>Costo de envío a cargo del vendedor (real si se conoce, si no estimado).</summary>
        public decimal? MontoEnvio { get; set; }

        /// <summary>Neto estimado a recibir = bruto - comisión - envío - otros costos estimados.</summary>
        public decimal? NetoEstimado { get; set; }

        /// <summary>Neto real acreditado en la liquidación (lo carga el operador al liquidar).</summary>
        public decimal? NetoReal { get; set; }

        public DateTime? FechaLiquidacionUtc { get; set; }

        /// <summary>Movimiento de caja generado al registrar la liquidación.</summary>
        public int? MovimientoCajaId { get; set; }

        // ------------------------------------------------------------------
        // Envío / devoluciones (Fase H)
        // ------------------------------------------------------------------

        [StringLength(30)]
        public string? ShipmentStatus { get; set; }

        [StringLength(50)]
        public string? ShipmentSubStatus { get; set; }

        [StringLength(60)]
        public string? TrackingNumber { get; set; }

        [StringLength(100)]
        public string? TrackingMethod { get; set; }

        [StringLength(50)]
        public string? ShippingMode { get; set; }

        [StringLength(50)]
        public string? ShippingType { get; set; }

        public DateTime? FechaDespachoUtc { get; set; }

        public DateTime? FechaEntregadoUtc { get; set; }

        public DateTime? FechaUltimaActualizacionEnvioUtc { get; set; }

        public string? RawShipmentJson { get; set; }

        public MercadoLibreShipmentEstadoInterno EstadoEnvioInterno { get; set; } =
            MercadoLibreShipmentEstadoInterno.Pendiente;

        /// <summary>Estado de devolución. El stock nunca se reingresa sin decisión manual.</summary>
        public MercadoLibreDevolucionEstado DevolucionEstado { get; set; } = MercadoLibreDevolucionEstado.Ninguna;

        [StringLength(500)]
        public string? DevolucionNota { get; set; }

        public virtual MercadoLibreAccount Account { get; set; } = null!;
        public virtual Venta? Venta { get; set; }
        public virtual MovimientoCaja? MovimientoCaja { get; set; }
        public virtual ICollection<MercadoLibreOrderItem> Items { get; set; } = new List<MercadoLibreOrderItem>();
        public virtual ICollection<MercadoLibreClaim> Claims { get; set; } = new List<MercadoLibreClaim>();
    }
}
