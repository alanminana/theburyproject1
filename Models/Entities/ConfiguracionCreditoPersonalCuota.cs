using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Tasa mensual y disponibilidad de Crédito Personal por cantidad de cuotas.
    /// Si no hay registros activos, el cálculo global usa la tasa/rango únicos de ConfiguracionPago (compatibilidad).
    /// </summary>
    public class ConfiguracionCreditoPersonalCuota
    {
        public int Id { get; set; }

        [Range(1, 120)]
        public int CantidadCuotas { get; set; }

        [Range(0, 100)]
        public decimal TasaMensual { get; set; }

        public bool Activo { get; set; } = true;

        public int Orden { get; set; }

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? UsuarioActualizacion { get; set; }
    }
}
