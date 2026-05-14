using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Auditoría del ciclo de vida de cada unidad física individual.
    /// Registra cada cambio de estado de una ProductoUnidad.
    /// </summary>
    public class ProductoUnidadMovimiento : AuditableEntity
    {
        [Required]
        public int ProductoUnidadId { get; set; }

        public EstadoUnidad EstadoAnterior { get; set; }

        public EstadoUnidad EstadoNuevo { get; set; }

        [Required]
        [StringLength(500)]
        public string Motivo { get; set; } = string.Empty;

        [StringLength(200)]
        public string? OrigenReferencia { get; set; }

        [StringLength(200)]
        public string? UsuarioResponsable { get; set; }

        public DateTime FechaCambio { get; set; } = DateTime.UtcNow;

        // Navegación
        public virtual ProductoUnidad ProductoUnidad { get; set; } = null!;
    }
}
