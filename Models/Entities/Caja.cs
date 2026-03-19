using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa una caja f�sica o punto de venta
    /// </summary>
    public class Caja : AuditableEntity
    {
        [Required]
        [StringLength(50)]
        public string Codigo { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        [StringLength(100)]
        public string? Sucursal { get; set; }

        [StringLength(100)]
        public string? Ubicacion { get; set; }

        public bool Activa { get; set; } = true;

        public EstadoCaja Estado { get; set; } = EstadoCaja.Cerrada;

        // Navegaci�n
        public virtual ICollection<AperturaCaja> Aperturas { get; set; } = new List<AperturaCaja>();
    }
}