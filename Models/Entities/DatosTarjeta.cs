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

        [StringLength(50)]
        public string? NumeroAutorizacion { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navigation
        public virtual Venta Venta { get; set; } = null!;
        public virtual ConfiguracionTarjeta? ConfiguracionTarjeta { get; set; }
    }
}
