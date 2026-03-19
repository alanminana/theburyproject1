using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Registro de apertura de caja
    /// </summary>
    public class AperturaCaja  : AuditableEntity
    {
        [Required]
        public int CajaId { get; set; }

        [Required]
        public DateTime FechaApertura { get; set; } = DateTime.UtcNow;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoInicial { get; set; }

        [Required]
        [StringLength(50)]
        public string UsuarioApertura { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ObservacionesApertura { get; set; }

        public bool Cerrada { get; set; } = false;

        // Navegaci�n
        public virtual Caja Caja { get; set; } = null!;
        public virtual ICollection<MovimientoCaja> Movimientos { get; set; } = new List<MovimientoCaja>();
        public virtual CierreCaja? Cierre { get; set; }
    }
}