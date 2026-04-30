using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities
{
    /// <summary>
    /// Entidad que representa un producto en el sistema
    /// </summary>
    public class Producto  : AuditableEntity


    {
        /// <summary>
        /// C�digo �nico del producto
        /// </summary>
        [Required(ErrorMessage = "El c�digo es obligatorio")]
        [StringLength(50, ErrorMessage = "El c�digo no puede superar 50 caracteres")]
        public string Codigo { get; set; } = string.Empty;

        /// <summary>
        /// Nombre del producto
        /// </summary>
        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(200, ErrorMessage = "El nombre no puede superar 200 caracteres")]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// Descripci�n detallada del producto
        /// </summary>
        [StringLength(1000, ErrorMessage = "La descripci�n no puede superar 1000 caracteres")]
        public string? Descripcion { get; set; }

        /// <summary>
        /// ID de la categor�a a la que pertenece
        /// </summary>
        [Required(ErrorMessage = "La categor�a es obligatoria")]
        public int CategoriaId { get; set; }

        /// <summary>
        /// ID de la subcategor�a (opcional, debe ser hija de CategoriaId)
        /// </summary>
        public int? SubcategoriaId { get; set; }

        /// <summary>
        /// ID de la marca del producto
        /// </summary>
        [Required(ErrorMessage = "La marca es obligatoria")]
        public int MarcaId { get; set; }

        /// <summary>
        /// ID de la submarca (opcional, debe ser hija de MarcaId)
        /// </summary>
        public int? SubmarcaId { get; set; }

        /// <summary>
        /// precio de costo del producto
        /// </summary>
        [Required(ErrorMessage = "El precio de costo es obligatorio")]
        [Range(0, double.MaxValue, ErrorMessage = "El precio de costo debe ser mayor o igual a 0")]
        public decimal PrecioCompra { get; set; }

        /// <summary>
        /// Precio de venta del producto
        /// </summary>
        [Required(ErrorMessage = "El precio de venta es obligatorio")]
        [Range(0, double.MaxValue, ErrorMessage = "El precio de venta debe ser mayor o igual a 0")]
        public decimal PrecioVenta { get; set; }

        /// <summary>
        /// Porcentaje de IVA aplicable al producto (ej: 21, 10.5, 27)
        /// </summary>
        [Required(ErrorMessage = "El porcentaje de IVA es obligatorio")]
        [Range(0, 100, ErrorMessage = "El IVA debe estar entre 0 y 100")]
        public decimal PorcentajeIVA { get; set; } = 21m;

        /// <summary>
        /// Alícuota de IVA configurable. Si está informada y activa, tiene prioridad sobre PorcentajeIVA.
        /// </summary>
        public int? AlicuotaIVAId { get; set; }

        /// <summary>
        /// Porcentaje de comisión que gana el vendedor al vender este producto.
        /// Ejemplo: 8 representa 8%.
        /// </summary>
        public decimal ComisionPorcentaje { get; set; } = 0m;

        /// <summary>
        /// Indica si el producto requiere n�mero de serie para control individual
        /// </summary>
        public bool RequiereNumeroSerie { get; set; } = false;

        /// <summary>
        /// Stock m�nimo que debe mantener el producto (alerta)
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "El stock m�nimo debe ser mayor o igual a 0")]
        public decimal StockMinimo { get; set; } = 0;

        /// <summary>
        /// Stock actual del producto
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "El stock actual debe ser mayor o igual a 0")]
        public decimal StockActual { get; set; } = 0;

        /// <summary>
        /// Unidad de medida (ej: "UN", "KG", "MT", "LT")
        /// </summary>
        [StringLength(10, ErrorMessage = "La unidad de medida no puede superar 10 caracteres")]
        public string UnidadMedida { get; set; } = "UN";

        /// <summary>
        /// Indica si el producto est� activo para la venta
        /// </summary>
        public bool Activo { get; set; } = true;

        // Propiedades de navegaci�n
        /// <summary>
        /// Categor�a a la que pertenece el producto
        /// </summary>
        public virtual Categoria Categoria { get; set; } = null!;

        /// <summary>
        /// Subcategor�a del producto (opcional)
        /// </summary>
        public virtual Categoria? Subcategoria { get; set; }

        /// <summary>
        /// Marca del producto
        /// </summary>
        public virtual Marca Marca { get; set; } = null!;

        /// <summary>
        /// Submarca del producto (opcional)
        /// </summary>
        public virtual Marca? Submarca { get; set; }

        /// <summary>
        /// Alícuota de IVA configurable del producto.
        /// </summary>
        public virtual AlicuotaIVA? AlicuotaIVA { get; set; }

        /// <summary>
        /// Características variables del producto (ej: color, pulgadas, capacidad)
        /// </summary>
        public virtual ICollection<ProductoCaracteristica> Caracteristicas { get; set; } = new List<ProductoCaracteristica>();
    }
}
