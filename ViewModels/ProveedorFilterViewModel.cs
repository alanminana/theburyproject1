using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para búsqueda y filtros de proveedores
    /// </summary>
    public class ProveedorFilterViewModel
    {
        public string? SearchTerm { get; set; }
        public bool SoloActivos { get; set; }
        public string? OrderBy { get; set; }
        public string? OrderDirection { get; set; }

        public IEnumerable<ProveedorViewModel> Proveedores { get; set; } = new List<ProveedorViewModel>();
        /// <summary>Total del filtro aplicado (todas las páginas), no solo los de la página actual.</summary>
        public int TotalResultados { get; set; }
        public int TotalActivos { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 25;

        // Asociaciones para el modal de creación
        public List<SelectListItem> CategoriasDisponibles { get; set; } = new();
        public List<SelectListItem> MarcasDisponibles { get; set; } = new();
        public List<SelectListItem> ProductosDisponibles { get; set; } = new();

        // JSON enriquecido para el product picker (incluye marca y categoría)
        public string ProductosPickerJson { get; set; } = "[]";
    }
}