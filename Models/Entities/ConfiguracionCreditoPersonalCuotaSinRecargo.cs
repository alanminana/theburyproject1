using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Número de cuota específico que un plan global de Crédito Personal
    /// (<see cref="ConfiguracionCreditoPersonalCuota"/>) marca como "sin recargo comercial"
    /// (CSR-ML2). El capital sigue repartiéndose entre TODAS las cuotas del plan; el recargo
    /// total del plan (<see cref="ConfiguracionCreditoPersonalCuota.TasaMensual"/>) se reparte
    /// solo entre las cuotas que NO están en esta colección. Pertenece exclusivamente al plan
    /// global — no a Producto, Cliente, Perfil ni Venta — y no tiene relación con mora/punitorio:
    /// "sin recargo" nunca significa "sin mora".
    /// </summary>
    public class ConfiguracionCreditoPersonalCuotaSinRecargo
    {
        public int Id { get; set; }

        public int ConfiguracionCreditoPersonalCuotaId { get; set; }

        /// <summary>
        /// Número de cuota (1-based) dentro del plan, sin recargo. Validado contra
        /// [1, <see cref="ConfiguracionCreditoPersonalCuota.CantidadCuotas"/>] por el servicio
        /// (<see cref="Services.ConfiguracionPagoService.GuardarCuotasSinRecargoCreditoPersonalAsync"/>),
        /// no solo por este rango técnico [1, 120].
        /// </summary>
        [Range(1, 120)]
        public int NumeroCuota { get; set; }

        public virtual ConfiguracionCreditoPersonalCuota ConfiguracionCreditoPersonalCuota { get; set; } = null!;
    }
}
