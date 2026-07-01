using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Representa un garante asociado a un cliente
    /// </summary>
    public class Garante  : AuditableEntity
    {
        public int ClienteId { get; set; } // Cliente que necesita el garante

        public int? GaranteClienteId { get; set; } // Cliente que actúa como garante (si es cliente del sistema)

        // Si el garante NO es cliente del sistema, se cargan estos datos
        [StringLength(20)]
        public string? TipoDocumento { get; set; }

        [StringLength(20)]
        public string? NumeroDocumento { get; set; }

        [StringLength(100)]
        public string? Apellido { get; set; }

        [StringLength(100)]
        public string? Nombre { get; set; }

        [StringLength(20)]
        public string? Telefono { get; set; }

        [StringLength(200)]
        public string? Domicilio { get; set; }

        [StringLength(100)]
        public string? Relacion { get; set; } // Familiar, Amigo, Conocido, etc.

        [StringLength(500)]
        public string? Observaciones { get; set; }

        // Baja de la relación (soft-delete de la relación, independiente de IsDeleted)
        public DateTime? FechaBaja { get; set; }

        [StringLength(500)]
        public string? MotivoBaja { get; set; }

        // Navigation Properties
        public virtual Cliente Cliente { get; set; } = null!;
        public virtual Cliente? GaranteCliente { get; set; }
    }
}