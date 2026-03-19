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

        // Para cr�dito
        public int? CantidadCuotas { get; set; }
        public TipoCuotaTarjeta? TipoCuota { get; set; }
        public decimal? TasaInteres { get; set; }
        public decimal? MontoCuota { get; set; }
        public decimal? MontoTotalConInteres { get; set; }

        // Para d�bito con recargo
        public decimal? RecargoAplicado { get; set; }

        [StringLength(50)]
        public string? NumeroAutorizacion { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Navigation
        public virtual Venta Venta { get; set; } = null!;
        public virtual ConfiguracionTarjeta? ConfiguracionTarjeta { get; set; }
    }
}