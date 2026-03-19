using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class ChequeFilterViewModel
    {
        [Display(Name = "Buscar")]
        [StringLength(100, ErrorMessage = "La búsqueda no puede tener más de 100 caracteres")]
        public string? SearchTerm { get; set; }

        [Display(Name = "Proveedor")]
        public int? ProveedorId { get; set; }

        [Display(Name = "Estado")]
        public EstadoCheque? Estado { get; set; }

        [Display(Name = "Fecha Emisión Desde")]
        [DataType(DataType.Date)]
        public DateTime? FechaEmisionDesde { get; set; }

        [Display(Name = "Fecha Emisión Hasta")]
        [DataType(DataType.Date)]
        public DateTime? FechaEmisionHasta { get; set; }

        [Display(Name = "Fecha Vencimiento Desde")]
        [DataType(DataType.Date)]
        public DateTime? FechaVencimientoDesde { get; set; }

        [Display(Name = "Fecha Vencimiento Hasta")]
        [DataType(DataType.Date)]
        public DateTime? FechaVencimientoHasta { get; set; }

        [Display(Name = "Solo Vencidos")]
        public bool SoloVencidos { get; set; }

        [Display(Name = "Solo Por Vencer (7 días)")]
        public bool SoloPorVencer { get; set; }

        [Display(Name = "Ordenar por")]
        public string? OrderBy { get; set; }

        [Display(Name = "Dirección")]
        public string OrderDirection { get; set; } = "asc";
    }
}