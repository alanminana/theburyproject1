using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Entidad que registra el historial de cambios de precios de productos
    /// Permite auditor�a completa y funci�n de Undo
    /// </summary>
    public class PrecioHistorico  : AuditableEntity
    {
        /// <summary>
        /// ID del producto al que pertenece este registro de precio
        /// </summary>
        [Required]
        public int ProductoId { get; set; }

        /// <summary>
        /// precio de costo anterior antes del cambio
        /// </summary>
        [Required]
        public decimal PrecioCompraAnterior { get; set; }

        /// <summary>
        /// Nuevo precio de costo despu�s del cambio
        /// </summary>
        [Required]
        public decimal PrecioCompraNuevo { get; set; }

        /// <summary>
        /// Precio de venta anterior antes del cambio
        /// </summary>
        [Required]
        public decimal PrecioVentaAnterior { get; set; }

        /// <summary>
        /// Nuevo precio de venta despu�s del cambio
        /// </summary>
        [Required]
        public decimal PrecioVentaNuevo { get; set; }

        /// <summary>
        /// Motivo del cambio de precio (opcional)
        /// Ej: "Ajuste por inflaci�n", "Cambio de proveedor", "Promoci�n"
        /// </summary>
        [StringLength(500)]
        public string? MotivoCambio { get; set; }

        /// <summary>
        /// Indica si este cambio puede ser revertido (Undo)
        /// Se establece en false cuando hay ventas posteriores al cambio
        /// </summary>
        public bool PuedeRevertirse { get; set; } = true;

        /// <summary>
        /// Fecha en que se realiz� el cambio de precio
        /// </summary>
        [Required]
        public DateTime FechaCambio { get; set; }

        /// <summary>
        /// Usuario que realiz� el cambio (para auditor�a)
        /// </summary>
        [Required]
        [StringLength(100)]
        public string UsuarioModificacion { get; set; } = string.Empty;

        // Navigation Properties
        /// <summary>
        /// Producto al que pertenece este historial
        /// </summary>
        public virtual Producto Producto { get; set; } = null!;

        // Propiedades calculadas
        /// <summary>
        /// Calcula el porcentaje de cambio en el precio de costo
        /// </summary>
        public decimal PorcentajeCambioCompra =>
            PrecioCompraAnterior == 0 ? 0 :
            ((PrecioCompraNuevo - PrecioCompraAnterior) / PrecioCompraAnterior) * 100;

        /// <summary>
        /// Calcula el porcentaje de cambio en el precio de venta
        /// </summary>
        public decimal PorcentajeCambioVenta =>
            PrecioVentaAnterior == 0 ? 0 :
            ((PrecioVentaNuevo - PrecioVentaAnterior) / PrecioVentaAnterior) * 100;

        /// <summary>
        /// Calcula el margen de ganancia anterior
        /// </summary>
        public decimal MargenAnterior =>
            PrecioVentaAnterior == 0 ? 0 :
            ((PrecioVentaAnterior - PrecioCompraAnterior) / PrecioVentaAnterior) * 100;

        /// <summary>
        /// Calcula el margen de ganancia nuevo
        /// </summary>
        public decimal MargenNuevo =>
            PrecioVentaNuevo == 0 ? 0 :
            ((PrecioVentaNuevo - PrecioCompraNuevo) / PrecioVentaNuevo) * 100;
    }
}