using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Configuración de límite de crédito por puntaje interno de comportamiento del cliente.
    /// Puntaje 0–5 (0 = cliente nuevo). Es el eje único que gobierna el cupo.
    /// </summary>
    public class PuntajeCreditoLimite
    {
        public int Id { get; set; }

        /// <summary>Puntaje interno de comportamiento (0–5) al que aplica este límite.</summary>
        [Range(0, 5)]
        public int Puntaje { get; set; }

        [Range(0, double.MaxValue)]
        public decimal LimiteMonto { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? UsuarioActualizacion { get; set; }
    }
}