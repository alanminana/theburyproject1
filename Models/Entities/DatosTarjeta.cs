using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Datos de pago con tarjeta asociados a una venta
    /// </summary>
    public class DatosTarjeta  : AuditableEntity
    {
        public int VentaId { get; set; }
        public int? ConfiguracionTarjetaId { get; set; }
        public int? ConfiguracionPagoPlanId { get; set; }

        [Required]
        [StringLength(100)]
        public string NombreTarjeta { get; set; } = string.Empty;

        [Required]
        public TipoTarjeta TipoTarjeta { get; set; }

        // Para credito
        public int? CantidadCuotas { get; set; }
        public TipoCuotaTarjeta? TipoCuota { get; set; }
        public decimal? TasaInteres { get; set; }
        public decimal? MontoCuota { get; set; }
        public decimal? MontoTotalConInteres { get; set; }

        // Para debito con recargo
        public decimal? RecargoAplicado { get; set; }

        /// <summary>
        /// Snapshot del maximo efectivo de cuotas sin interes aplicado al confirmar.
        /// Null si la restriccion no fue limitada por producto (o si no aplica).
        /// Solo se guarda cuando LimitadoPorProducto = true en ObtenerMaxCuotasSinInteresEfectivoAsync.
        /// </summary>
        public int? MaxCuotasSinInteresEfectivoAplicado { get; set; }

        /// <summary>
        /// Plan de cuotas seleccionado a nivel global de la venta.
        /// Legacy: en nuevas ventas la fuente autoritativa de cobro es Venta.TipoPago.
        /// Mantenido por compatibilidad con ventas existentes y flujo de tarjeta global.
        /// </summary>
        public int? ProductoCondicionPagoPlanId { get; set; }

        /// <summary>
        /// Snapshot del porcentaje de ajuste del plan aplicado al confirmar la venta.
        /// Null si no se seleccionó plan o el plan tiene AjustePorcentaje = 0.
        /// </summary>
        public decimal? PorcentajeAjustePlanAplicado { get; set; }

        /// <summary>
        /// Snapshot del monto de ajuste del plan aplicado al confirmar la venta.
        /// Negativo si el plan es descuento, positivo si es recargo.
        /// </summary>
        public decimal? MontoAjustePlanAplicado { get; set; }

        /// <summary>
        /// Snapshot del porcentaje de ajuste global aplicado a la venta.
        /// Independiente de ProductoCondicionPagoPlanId, que pertenece al flujo legacy por producto.
        /// </summary>
        public decimal? PorcentajeAjustePagoAplicado { get; set; }

        /// <summary>
        /// Snapshot del monto de ajuste global aplicado a la venta.
        /// Negativo si el plan es descuento, positivo si es recargo.
        /// </summary>
        public decimal? MontoAjustePagoAplicado { get; set; }

        [StringLength(100)]
        public string? NombrePlanPagoSnapshot { get; set; }

        [StringLength(50)]
        public string? NumeroAutorizacion { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navigation
        public virtual Venta Venta { get; set; } = null!;
        public virtual ConfiguracionTarjeta? ConfiguracionTarjeta { get; set; }
        public virtual ConfiguracionPagoPlan? ConfiguracionPagoPlan { get; set; }
        public virtual ProductoCondicionPagoPlan? ProductoCondicionPagoPlan { get; set; }
    }
}
