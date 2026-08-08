using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Plan de cuotas de Crédito Personal específico de un producto. Decide QUÉ cantidades de
    /// cuotas ofrece el producto (reemplaza la lista de cantidades de la configuración global si
    /// tiene registros propios activos; si no tiene ninguno, ofrece las cantidades globales).
    /// </summary>
    /// <remarks>
    /// ML2.1/ML3 — Contrato congelado: <see cref="TasaMensual"/> ya NO es fuente del porcentaje
    /// financiero bajo ningún valor (ni null, ni 0, ni explícito). El porcentaje de cada cantidad
    /// sale siempre de <see cref="ConfiguracionCreditoPersonalCuota"/> (la cuota global) cuando
    /// existe una para esa cantidad; si no existe, es <c>null</c> (configuración inválida). Ver
    /// <see cref="Services.ConfiguracionPagoService.ResolverPlanesCreditoPersonalAsync"/>.
    /// </remarks>
    public class ProductoCreditoPersonalCuota
    {
        public int Id { get; set; }

        public int ProductoId { get; set; }

        [Range(1, 120)]
        public int CantidadCuotas { get; set; }

        /// <summary>
        /// Recargo propio histórico del producto para esta cantidad de cuotas. LEGACY sin
        /// autoridad financiera (ML2.1/ML3): nunca se lee para resolver el porcentaje de una
        /// venta — ese siempre sale de <see cref="ConfiguracionCreditoPersonalCuota"/> (la cuota
        /// global). Desde ML5 tampoco es editable ni se reescribe desde el editor de Producto
        /// (Create/Edit): <c>ProductoCreditoPersonalConfigService.GuardarAsync</c> nunca escribe
        /// esta columna — las filas nuevas nacen en <c>null</c> y las existentes conservan su
        /// valor histórico intacto. Se conserva físicamente por compatibilidad de dato histórico,
        /// sin migración de esquema; el único efecto real de este registro es decidir que esta
        /// cantidad de cuotas está disponible para el producto.
        /// </summary>
        [Range(0, 100)]
        public decimal? TasaMensual { get; set; }

        public bool Activo { get; set; } = true;

        public int Orden { get; set; }

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? UsuarioActualizacion { get; set; }

        public virtual Producto Producto { get; set; } = null!;
    }
}
