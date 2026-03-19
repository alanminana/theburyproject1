using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Cuota individual de un acuerdo de pago
    /// </summary>
    public class CuotaAcuerdo : AuditableEntity
    {
        /// <summary>
        /// Acuerdo de pago al que pertenece la cuota
        /// </summary>
        public int AcuerdoPagoId { get; set; }
        public virtual AcuerdoPago? AcuerdoPago { get; set; }

        /// <summary>
        /// Número de cuota (1, 2, 3...)
        /// </summary>
        public int NumeroCuota { get; set; }

        /// <summary>
        /// Porción de capital incluida en la cuota
        /// </summary>
        public decimal MontoCapital { get; set; }

        /// <summary>
        /// Porción de mora incluida en la cuota
        /// </summary>
        public decimal MontoMora { get; set; }

        /// <summary>
        /// Total de la cuota (capital + mora)
        /// </summary>
        public decimal MontoTotal { get; set; }

        /// <summary>
        /// Fecha de vencimiento de la cuota
        /// </summary>
        public DateTime FechaVencimiento { get; set; }

        /// <summary>
        /// Fecha en que se pagó la cuota
        /// </summary>
        public DateTime? FechaPago { get; set; }

        /// <summary>
        /// Monto efectivamente pagado
        /// </summary>
        public decimal MontoPagado { get; set; }

        /// <summary>
        /// Estado actual de la cuota
        /// </summary>
        public EstadoCuotaAcuerdo Estado { get; set; } = EstadoCuotaAcuerdo.Pendiente;

        /// <summary>
        /// Referencia del comprobante de pago
        /// </summary>
        [StringLength(100)]
        public string? ComprobantePago { get; set; }

        /// <summary>
        /// Medio de pago utilizado
        /// </summary>
        [StringLength(50)]
        public string? MedioPago { get; set; }

        /// <summary>
        /// Observaciones sobre el pago
        /// </summary>
        [StringLength(500)]
        public string? Observaciones { get; set; }
    }
}
