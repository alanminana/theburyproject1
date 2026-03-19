using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Historial de cambios de puntaje del cliente.
    /// </summary>
    public class ClientePuntajeHistorial
    {
        public int Id { get; set; }

        public int ClienteId { get; set; }

        public decimal Puntaje { get; set; }

        public NivelRiesgoCredito? NivelRiesgo { get; set; }

        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(100)]
        public string Origen { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Observacion { get; set; }

        [StringLength(200)]
        public string? RegistradoPor { get; set; }

        public virtual Cliente Cliente { get; set; } = null!;
    }
}
