using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Recargo TOTAL del plan (no tasa mensual ni compuesta) y disponibilidad de Crédito
    /// Personal por cantidad de cuotas. El nombre de la propiedad (<c>TasaMensual</c>) es
    /// legacy y se conserva por compatibilidad de columna/binding; el valor representa un
    /// porcentaje de recargo único aplicado una sola vez sobre el saldo financiado.
    /// Si no hay registros activos, Crédito Personal no ofrece cuotas (sin fallback a rango).
    /// </summary>
    public class ConfiguracionCreditoPersonalCuota
    {
        public int Id { get; set; }

        [Range(1, 120)]
        public int CantidadCuotas { get; set; }

        /// <summary>
        /// Porcentaje de recargo TOTAL propio de esta cantidad de cuotas (no mensual, no
        /// compuesto) — autoridad primaria del porcentaje financiero. null = sin porcentaje
        /// propio: al resolverse contra una venta hereda el recargo único global de
        /// <see cref="ConfiguracionPago.TasaInteresMensualCreditoPersonal"/> (fallback); solo si
        /// ese valor tampoco está configurado el plan queda sin porcentaje válido.
        /// 0 = sin recargo (0 % explícito, válido y distinto de "no configurado"); X = recargo.
        /// </summary>
        [Range(0, 100)]
        public decimal? TasaMensual { get; set; }

        public bool Activo { get; set; } = true;

        public int Orden { get; set; }

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? UsuarioActualizacion { get; set; }

        /// <summary>
        /// Números de cuota de este plan marcados como "sin recargo comercial" (CSR-ML2). Vacía
        /// por defecto — comportamiento actual preservado — hasta que se guarde una selección
        /// explícita vía <see cref="Services.ConfiguracionPagoService.GuardarCuotasSinRecargoCreditoPersonalAsync"/>.
        /// </summary>
        public virtual ICollection<ConfiguracionCreditoPersonalCuotaSinRecargo> CuotasSinRecargo { get; set; } =
            new List<ConfiguracionCreditoPersonalCuotaSinRecargo>();
    }
}
