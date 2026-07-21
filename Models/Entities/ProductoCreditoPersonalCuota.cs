using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Plan de cuotas de Crédito Personal específico de un producto (cantidad + tasa propia).
    /// Sobrescribe la configuración global de ConfiguracionCreditoPersonalCuota para ese producto.
    /// Si un producto no tiene registros activos, usa la configuración global (herencia).
    /// </summary>
    public class ProductoCreditoPersonalCuota
    {
        public int Id { get; set; }

        public int ProductoId { get; set; }

        [Range(1, 120)]
        public int CantidadCuotas { get; set; }

        [Range(0, 100)]
        public decimal TasaMensual { get; set; }

        public bool Activo { get; set; } = true;

        public int Orden { get; set; }

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? UsuarioActualizacion { get; set; }

        public virtual Producto Producto { get; set; } = null!;
    }
}
