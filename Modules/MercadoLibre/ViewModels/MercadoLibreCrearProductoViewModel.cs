using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.Modules.MercadoLibre.ViewModels
{
    /// <summary>
    /// Alta de un Producto interno a partir de una publicación ML sin vincular.
    /// Los campos llegan prellenados desde la publicación pero el operador puede
    /// corregirlos antes de confirmar. No modifica nada en Mercado Libre.
    /// </summary>
    public class MercadoLibreCrearProductoViewModel
    {
        public int ListingId { get; set; }

        [Required(ErrorMessage = "El código es obligatorio")]
        [StringLength(50, ErrorMessage = "El código no puede superar 50 caracteres")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [StringLength(200, ErrorMessage = "El nombre no puede superar 200 caracteres")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(1000, ErrorMessage = "La descripción no puede superar 1000 caracteres")]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "La categoría es obligatoria")]
        public int? CategoriaId { get; set; }

        [Required(ErrorMessage = "La marca es obligatoria")]
        public int? MarcaId { get; set; }

        [Range(0, 999999999, ErrorMessage = "El precio de costo no puede ser negativo")]
        public decimal PrecioCompra { get; set; }

        [Range(0, 999999999, ErrorMessage = "El precio de venta no puede ser negativo")]
        public decimal PrecioVenta { get; set; }

        [Range(0, 100, ErrorMessage = "El IVA debe estar entre 0 y 100")]
        public decimal PorcentajeIVA { get; set; } = 21m;

        /// <summary>Stock inicial del producto (genera movimiento de stock trazado).</summary>
        [Range(0, 99999999, ErrorMessage = "El stock inicial no puede ser negativo")]
        public decimal StockInicial { get; set; }

        public bool RequiereNumeroSerie { get; set; }

        // Datos de la publicación de origen (solo lectura, para mostrar en la vista)
        public string ListingItemId { get; set; } = string.Empty;
        public string ListingTitulo { get; set; } = string.Empty;
        public decimal ListingPrecio { get; set; }
        public int ListingStock { get; set; }
        public string? ListingSku { get; set; }
        public string? ListingCondicion { get; set; }
        public string? ListingPermalink { get; set; }
    }
}
