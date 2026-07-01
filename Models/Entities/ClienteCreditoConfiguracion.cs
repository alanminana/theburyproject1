using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración crediticia 1:1 por cliente.
    /// Define preset opcional, override absoluto y excepción aditiva con vigencia/auditoría.
    /// </summary>
    public class ClienteCreditoConfiguracion
    {
        [Key]
        public int ClienteId { get; set; }

        public int? CreditoPresetId { get; set; }

        public decimal? LimiteOverride { get; set; }

        /// <summary>Override manual del puntaje interno (0–5) que gobierna el cupo. Null = usar el automático.
        /// (Se mantiene el nombre de columna NivelCreditoManual por compatibilidad; hoy representa un puntaje 0–5.)</summary>
        public int? NivelCreditoManual { get; set; }

        [StringLength(1000)]
        public string? MotivoNivelCreditoManual { get; set; }

        [StringLength(200)]
        public string? NivelCreditoManualAsignadoPor { get; set; }

        public DateTime? NivelCreditoManualAsignadoEnUtc { get; set; }

        public decimal? ExcepcionDelta { get; set; }
        public DateTime? ExcepcionDesde { get; set; }
        public DateTime? ExcepcionHasta { get; set; }

        [StringLength(1000)]
        public string? MotivoExcepcion { get; set; }

        [StringLength(200)]
        public string? AprobadoPor { get; set; }

        public DateTime? AprobadoEnUtc { get; set; }

        [StringLength(1000)]
        public string? MotivoOverride { get; set; }

        [StringLength(200)]
        public string? OverrideAprobadoPor { get; set; }

        public DateTime? OverrideAprobadoEnUtc { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = default!;

        public virtual Cliente Cliente { get; set; } = null!;
        public virtual PuntajeCreditoLimite? CreditoPreset { get; set; }
    }
}
