using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Lote de cambio masivo de precios en Mercado Libre.
    /// Workflow espejo de PriceChangeBatch interno: Simulado → Aplicado → Revertido.
    /// El preview y el snapshot anterior son obligatorios; el rollback re-publica
    /// el precio anterior de cada item del lote.
    /// </summary>
    public class MercadoLibrePriceBatch : AuditableEntity
    {
        [Required]
        [StringLength(200)]
        public string Nombre { get; set; } = string.Empty;

        public MercadoLibrePriceBatchEstado Estado { get; set; } = MercadoLibrePriceBatchEstado.Simulado;

        public MercadoLibrePriceBatchOrigen Origen { get; set; }

        /// <summary>
        /// Porcentaje de ajuste. Con Origen=PorcentajeSobrePrecioMl es el aumento directo;
        /// con Origen=DesdePrecioErp es un ajuste extra sobre el precio de canal calculado.
        /// </summary>
        public decimal ValorAjustePorcentaje { get; set; }

        /// <summary>Filtros usados para armar el lote (categorías, marcas, estado, etc.) en JSON.</summary>
        [Required]
        public string FiltrosJson { get; set; } = "{}";

        public int CantidadPublicaciones { get; set; }

        /// <summary>Resumen de la simulación (totales, promedios, advertencias) en JSON.</summary>
        public string? SimulacionJson { get; set; }

        /// <summary>True si el lote se "aplicó" con modo simulación activo (no llamó a ML).</summary>
        public bool AplicadoEnSimulacion { get; set; }

        [Required]
        [StringLength(50)]
        public string SolicitadoPor { get; set; } = string.Empty;

        public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? AplicadoPor { get; set; }

        public DateTime? FechaAplicacion { get; set; }

        [StringLength(50)]
        public string? RevertidoPor { get; set; }

        public DateTime? FechaReversion { get; set; }

        [StringLength(500)]
        public string? MotivoReversion { get; set; }

        public virtual ICollection<MercadoLibrePriceBatchItem> Items { get; set; } = new List<MercadoLibrePriceBatchItem>();
    }
}
