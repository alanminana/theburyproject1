using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
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
        [RegularExpression(@"^[A-Za-z0-9]+(?:[ _.-][A-Za-z0-9]+)*$", ErrorMessage = "El código solo puede contener letras sin tilde, números, espacios simples, punto, guion y guion bajo (ej. CAT-01 o 001002).")]
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

        [TheBuryProject.Validation.MontoArgentino]
        public decimal PrecioCompra { get; set; }

        [TheBuryProject.Validation.MontoArgentino(Minimo = 0.01)]
        public decimal PrecioVenta { get; set; }

        [TheBuryProject.Validation.AlicuotaIvaArgentina]
        public decimal PorcentajeIVA { get; set; } = 21m;

        /// <summary>Stock inicial del producto (genera movimiento de stock trazado).</summary>
        [Range(0, 999999.99, ErrorMessage = "El stock inicial debe estar entre 0 y 999999.99")]
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
