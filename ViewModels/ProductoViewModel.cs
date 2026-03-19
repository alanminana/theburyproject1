using System.ComponentModel.DataAnnotations;


namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para la gestión de productos en las vistas
    /// </summary>
    public class ProductoViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El código es obligatorio")]
        [Display(Name = "Código")]
        [StringLength(50, ErrorMessage = "El código no puede superar 50 caracteres")]
        public string Codigo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es obligatorio")]
        [Display(Name = "Nombre")]
        [StringLength(200, ErrorMessage = "El nombre no puede superar 200 caracteres")]
        public string Nombre { get; set; } = string.Empty;

        [Display(Name = "Descripción")]
        [StringLength(1000, ErrorMessage = "La descripción no puede superar 1000 caracteres")]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "La categoría es obligatoria")]
        [Display(Name = "Categoría")]
        public int CategoriaId { get; set; }

        [Display(Name = "Categoría")]
        public string? CategoriaNombre { get; set; }

        [Display(Name = "Subcategoría")]
        public int? SubcategoriaId { get; set; }

        [Display(Name = "Subcategoría")]
        public string? SubcategoriaNombre { get; set; }

        [Required(ErrorMessage = "La marca es obligatoria")]
        [Display(Name = "Marca")]
        public int MarcaId { get; set; }

        [Display(Name = "Marca")]
        public string? MarcaNombre { get; set; }

        [Display(Name = "Submarca")]
        public int? SubmarcaId { get; set; }

        [Display(Name = "Submarca")]
        public string? SubmarcaNombre { get; set; }

        [Required(ErrorMessage = "El precio de costo es obligatorio")]
        [Display(Name = "precio de costo")]
        [Range(0, double.MaxValue, ErrorMessage = "El precio de costo debe ser mayor o igual a 0")]
        [DisplayFormat(DataFormatString = "{0:C2}", ApplyFormatInEditMode = false)]
        public decimal PrecioCompra { get; set; }

        [Required(ErrorMessage = "El precio de venta es obligatorio")]
        [Display(Name = "Precio de Venta")]
        [Range(0, double.MaxValue, ErrorMessage = "El precio de venta debe ser mayor o igual a 0")]
        [DisplayFormat(DataFormatString = "{0:C2}", ApplyFormatInEditMode = false)]
        public decimal PrecioVenta { get; set; }

        [Required(ErrorMessage = "El porcentaje de IVA es obligatorio")]
        [Display(Name = "IVA (%)")]
        [Range(0, 100, ErrorMessage = "El IVA debe estar entre 0 y 100")]
        public decimal PorcentajeIVA { get; set; } = 21m;

        [Display(Name = "Stock Mínimo")]
        [Range(0, double.MaxValue, ErrorMessage = "El stock mínimo debe ser mayor o igual a 0")]
        public decimal StockMinimo { get; set; } = 0;

        [Display(Name = "Stock Actual")]
        [Range(0, double.MaxValue, ErrorMessage = "El stock actual debe ser mayor o igual a 0")]
        public decimal StockActual { get; set; } = 0;

        [Display(Name = "Activo")]
        public bool Activo { get; set; } = true;

        public List<ProductoCaracteristicaViewModel> Caracteristicas { get; set; } = new();


        /// <summary>
        /// RowVersion para control de concurrencia optimista.
        /// Debe enviarse en POST/PUT para detectar conflictos.
        /// </summary>
        public byte[]? RowVersion { get; set; }

        // Propiedades calculadas
        [Display(Name = "Margen de Ganancia")]
        public decimal? MargenGanancia
        {
            get
            {
                if (PrecioCompra > 0)
                {
                    return ((PrecioVenta - PrecioCompra) / PrecioCompra) * 100;
                }
                return null;
            }
        }

        [Display(Name = "Estado Stock")]
        public string EstadoStock
        {
            get
            {
                if (StockActual <= 0)
                    return "Sin Stock";
                else if (StockActual <= StockMinimo)
                    return "Stock Bajo";
                else
                    return "Stock OK";
            }
        }

        // Propiedades de auditoría (para mostrar en detalles)
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }
    }
}