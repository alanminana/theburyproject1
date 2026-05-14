using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Helpers;

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

        [Display(Name = "Alícuota IVA")]
        public int? AlicuotaIVAId { get; set; }

        public string? AlicuotaIVANombre { get; set; }

        [Display(Name = "Comisión vendedor (%)")]
        [ModelBinder(typeof(DecimalModelBinder))]
        [Range(0, 100, ErrorMessage = "La comisión vendedor debe estar entre 0 y 100")]
        public decimal ComisionPorcentaje { get; set; } = 0m;

        [Display(Name = "Máx. cuotas sin interés")]
        [Range(1, 60, ErrorMessage = "El máximo de cuotas debe estar entre 1 y 60")]
        public int? MaxCuotasSinInteresPermitidas { get; set; }

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

    /// <summary>
    /// Resultado de búsqueda de producto para el panel de venta.
    /// Incluye precio vigente de lista y campo de coincidencia exacta de código.
    /// </summary>
    public class ProductoVentaDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Marca { get; set; }
        public string? Submarca { get; set; }
        public string? Categoria { get; set; }
        public string? Subcategoria { get; set; }
        public string? Descripcion { get; set; }
        public decimal StockActual { get; set; }
        public decimal PrecioVenta { get; set; }
        public bool RequiereNumeroSerie { get; set; }
        public List<ProductoCaracteristicaDto> Caracteristicas { get; set; } = new();
        public string CaracteristicasResumen { get; set; } = string.Empty;
        public bool CodigoExacto { get; set; }
    }

    public class ProductoCaracteristicaDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string Valor { get; set; } = string.Empty;
    }
}
