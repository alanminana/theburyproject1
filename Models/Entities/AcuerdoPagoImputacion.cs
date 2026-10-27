using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    public enum EstadoImputacionAcuerdo
    {
        /// <summary>Intención registrada antes de cobrar: el cobro pudo o no confirmarse.</summary>
        Pendiente = 1,

        /// <summary>El cobro se confirmó y su importe ya está reflejado en el acuerdo.</summary>
        Aplicada = 2,

        /// <summary>Se verificó que el cobro nunca se confirmó (no hay pago en el libro): no hay nada que reflejar.</summary>
        Descartada = 3
    }

    /// <summary>
    /// Registro de intención de una imputación de un pago de acuerdo a una cuota vencida del crédito. Se guarda ANTES de
    /// cobrar (el cobro abre su propia transacción) y se cierra al reflejar el cobro en el acuerdo: si el proceso se cae
    /// entre ambos pasos, la conciliación busca el pago en el libro (PagosCuota) y completa el acuerdo sin cobrar de nuevo.
    /// </summary>
    public class AcuerdoPagoImputacion : AuditableEntity
    {
        public int AcuerdoPagoId { get; set; }
        public virtual AcuerdoPago? AcuerdoPago { get; set; }

        /// <summary>Identidad de la operación: viaja al cobro y queda en PagoCuota.OperacionExternaId.</summary>
        public Guid OperacionId { get; set; } = Guid.NewGuid();

        /// <summary>Cuota del acuerdo a la que se imputa; null = entrega inicial.</summary>
        public int? CuotaAcuerdoId { get; set; }

        /// <summary>Cuota vencida del crédito que se cobra.</summary>
        public int CuotaCreditoId { get; set; }

        public decimal Monto { get; set; }

        [StringLength(50)]
        public string? MedioPago { get; set; }

        [StringLength(100)]
        public string? Comprobante { get; set; }

        public EstadoImputacionAcuerdo Estado { get; set; } = EstadoImputacionAcuerdo.Pendiente;

        /// <summary>Pago del libro que respalda la imputación (único: un pago no se cuenta dos veces).</summary>
        public int? PagoCuotaId { get; set; }

        public decimal? MontoAplicado { get; set; }
    }
}
