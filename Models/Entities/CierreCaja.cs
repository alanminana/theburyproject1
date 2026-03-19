using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Cierre de caja con arqueo
    /// </summary>
    public class CierreCaja  : AuditableEntity
    {
        [Required]
        public int AperturaCajaId { get; set; }

        [Required]
        public DateTime FechaCierre { get; set; } = DateTime.UtcNow;

        // Montos calculados del sistema
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoInicialSistema { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalIngresosSistema { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalEgresosSistema { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoEsperadoSistema { get; set; }

        // Montos del arqueo f�sico
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal EfectivoContado { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ChequesContados { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ValesContados { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoTotalReal { get; set; }

        // Diferencia
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Diferencia { get; set; }

        public bool TieneDiferencia { get; set; } = false;

        [StringLength(1000)]
        public string? JustificacionDiferencia { get; set; }

        [Required]
        [StringLength(50)]
        public string UsuarioCierre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ObservacionesCierre { get; set; }

        // Detalles de billetes y monedas (opcional)
        [StringLength(2000)]
        public string? DetalleArqueo { get; set; }

        // Navegaci�n
        public virtual AperturaCaja AperturaCaja { get; set; } = null!;
    }
}