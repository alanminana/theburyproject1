using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    public class ConfiguracionCreditoMontoPorPuntaje
    {
        public int Id { get; set; }

        /// <summary>Puntaje entero 0–10 asignado externamente al cliente.</summary>
        [Range(0, 10)]
        public int Puntaje { get; set; }

        [Range(0, double.MaxValue)]
        public decimal MontoMaximoFinanciable { get; set; }

        public bool RequiereAnalisis { get; set; }

        public bool Activo { get; set; } = true;

        public int Orden { get; set; }

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? UsuarioActualizacion { get; set; }
    }
}
