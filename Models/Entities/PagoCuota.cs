using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Ledger de pagos por cuota (PUN-ML2). Fuente histórica canónica de cuánto se pagó, en qué
    /// fecha comercial, sobre qué cuota y mediante qué movimiento de caja. Reemplaza la necesidad
    /// de inferir esa información desde <c>Cuota.MontoPagado</c> (acumulado) o <c>Cuota.FechaPago</c>
    /// (se pisa en cada cobro): ver PUN-ML1, <c>Legacy_MontoPunitorioSePisaEnCadaCobro...</c>.
    ///
    /// No persiste todavía una fórmula de punitorio nueva ni decide el orden de imputación de un
    /// pago insuficiente (PUN-ML1 §12, decisión P1 pendiente): cuando esa composición no es
    /// confiable, <see cref="ImporteAplicadoCuota"/> e <see cref="ImporteAplicadoPunitorio"/>
    /// quedan en <c>null</c> y <see cref="HistorialCompleto"/> es <c>false</c>. El único valor
    /// siempre confiable es <see cref="ImporteTotal"/> (el importe real del cobro).
    /// </summary>
    public class PagoCuota : AuditableEntity
    {
        public int CuotaId { get; set; }

        /// <summary>
        /// Fecha comercial argentina del pago (<c>IRelojComercial</c>), no la fecha que haya
        /// enviado el navegador ni la hora de servidor sin convertir.
        /// </summary>
        public DateOnly FechaPagoComercial { get; set; }

        /// <summary>
        /// Importe real aplicado en este pago (equivalente a <c>MontoBase</c> del cobro: excluye
        /// recargo del medio de pago, que se audita aparte en <c>Cuota.RecargoMedioPago</c>).
        /// Siempre exacto — nunca se infiere.
        /// </summary>
        public decimal ImporteTotal { get; set; }

        /// <summary>
        /// Parte de <see cref="ImporteTotal"/> aplicada al valor de la cuota. <c>null</c> cuando
        /// la composición no es reconstruible de forma confiable (<see cref="HistorialCompleto"/> = false).
        /// </summary>
        public decimal? ImporteAplicadoCuota { get; set; }

        /// <summary>
        /// Parte de <see cref="ImporteTotal"/> aplicada a punitorio. <c>null</c> en las mismas
        /// condiciones que <see cref="ImporteAplicadoCuota"/>.
        /// </summary>
        public decimal? ImporteAplicadoPunitorio { get; set; }

        /// <summary>
        /// Movimiento de caja asociado. Nulo solo en filas que no llegaron a vincularse a uno
        /// (no debería ocurrir para pagos nuevos; ver diagnóstico de backfill para el histórico).
        /// </summary>
        public int? MovimientoCajaId { get; set; }

        public string? MedioPago { get; set; }

        public OrigenPagoCuota Origen { get; set; }

        public EstadoPagoCuota Estado { get; set; } = EstadoPagoCuota.Aplicado;

        /// <summary>
        /// <c>true</c> cuando <see cref="ImporteAplicadoCuota"/>/<see cref="ImporteAplicadoPunitorio"/>
        /// son confiables. <c>false</c> cuando solo se conoce <see cref="ImporteTotal"/>.
        /// </summary>
        public bool HistorialCompleto { get; set; } = true;

        public string? MotivoIncompleto { get; set; }

        public DateTime? FechaAnulacion { get; set; }

        /// <summary>
        /// Si esta fila es la reversión de otra, apunta al <see cref="PagoCuota"/> revertido.
        /// Sin productor en PUN-ML2 (no existe reversión de cobros todavía).
        /// </summary>
        public int? PagoCuotaOrigenId { get; set; }

        /// <summary>
        /// Aplicación de punitorio (PUN-ML5) a la que se imputó <see cref="ImporteAplicadoPunitorio"/>
        /// (PUN-ML6). <c>null</c> cuando el pago no imputó nada a punitorio; obligatorio en la
        /// práctica cuando <see cref="ImporteAplicadoPunitorio"/> es mayor a cero (siempre apunta a la
        /// aplicación activa cobrada). Fuente de verdad para derivar cuánto de una
        /// <see cref="PunitorioAplicado"/> ya fue efectivamente pagado — no se guarda un acumulado
        /// redundante en esa entidad.
        /// </summary>
        public int? PunitorioAplicadoId { get; set; }

        public virtual Cuota Cuota { get; set; } = null!;
        public virtual MovimientoCaja? MovimientoCaja { get; set; }
        public virtual PagoCuota? PagoCuotaOrigen { get; set; }
        public virtual PunitorioAplicado? PunitorioAplicado { get; set; }
    }
}
