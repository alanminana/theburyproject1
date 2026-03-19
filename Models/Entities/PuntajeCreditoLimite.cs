using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración de límite de crédito por puntaje de cliente.
    /// </summary>
    public class PuntajeCreditoLimite
    {
        public int Id { get; set; }

        public NivelRiesgoCredito Puntaje { get; set; }

        [Range(0, double.MaxValue)]
        public decimal LimiteMonto { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? UsuarioActualizacion { get; set; }
    }
}