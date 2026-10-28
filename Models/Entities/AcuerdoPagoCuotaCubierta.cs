using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Cuota original del crédito cubierta por un acuerdo de pago, con la deuda congelada al activarlo: capital pendiente y
    /// punitorio aplicado pendiente. Los pagos del acuerdo se imputan exclusivamente a estas cuotas, y mientras el acuerdo
    /// esté activo ni se cobran por otro circuito ni se aplican o anulan punitorios sobre ellas.
    /// </summary>
    public class AcuerdoPagoCuotaCubierta : AuditableEntity
    {
        public int AcuerdoPagoId { get; set; }
        public virtual AcuerdoPago? AcuerdoPago { get; set; }

        public int CuotaId { get; set; }

        /// <summary>Orden de imputación (1 = la cuota más vieja).</summary>
        public int Orden { get; set; }

        /// <summary>Capital pendiente de la cuota al activar el acuerdo.</summary>
        public decimal CapitalCubierto { get; set; }

        /// <summary>Punitorio aplicado pendiente de la cuota al activar el acuerdo.</summary>
        public decimal PunitorioCubierto { get; set; }
    }
}
