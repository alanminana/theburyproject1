using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class OrdenCompraFilterViewModel
    {
        [Display(Name = "Buscar")]
        [StringLength(100, ErrorMessage = "La búsqueda no puede tener más de 100 caracteres")]
        public string? SearchTerm { get; set; }

        [Display(Name = "Proveedor")]
        public int? ProveedorId { get; set; }

        [Display(Name = "Estado")]
        public EstadoOrdenCompra? Estado { get; set; }

        [Display(Name = "Desde")]
        [DataType(DataType.Date)]
        public DateTime? FechaDesde { get; set; }

        [Display(Name = "Hasta")]
        [DataType(DataType.Date)]
        public DateTime? FechaHasta { get; set; }

        [Display(Name = "Ordenar por")]
        public string? OrderBy { get; set; }

        [Display(Name = "Dirección")]
        public string OrderDirection { get; set; } = "desc";
    }
}