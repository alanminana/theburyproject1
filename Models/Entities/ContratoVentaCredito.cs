using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Contrato y pagaré generados para una venta con crédito personal.
    /// Conserva snapshots para preservar el documento legal emitido.
    /// </summary>
    public class ContratoVentaCredito : AuditableEntity
    {
        public int VentaId { get; set; }

        public int CreditoId { get; set; }

        public int ClienteId { get; set; }

        public int PlantillaContratoCreditoId { get; set; }

        [Required]
        [StringLength(50)]
        public string NumeroContrato { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string NumeroPagare { get; set; } = string.Empty;

        public DateTime FechaGeneracionUtc { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(100)]
        public string UsuarioGeneracion { get; set; } = string.Empty;

        public EstadoDocumento EstadoDocumento { get; set; } = EstadoDocumento.Verificado;

        [StringLength(500)]
        public string? RutaArchivo { get; set; }

        [StringLength(200)]
        public string? NombreArchivo { get; set; }

        [StringLength(128)]
        public string? ContentHash { get; set; }

        [Required]
        public string TextoContratoSnapshot { get; set; } = string.Empty;

        [Required]
        public string TextoPagareSnapshot { get; set; } = string.Empty;

        [Required]
        public string DatosSnapshotJson { get; set; } = string.Empty;

        public DateTime? FechaImpresionUtc { get; set; }

        public virtual Venta Venta { get; set; } = null!;
        public virtual Credito Credito { get; set; } = null!;
        public virtual Cliente Cliente { get; set; } = null!;
        public virtual PlantillaContratoCredito PlantillaContratoCredito { get; set; } = null!;
    }
}
