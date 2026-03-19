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
        public int TotalResultados { get; set; }

        // Asociaciones para el modal de creación
        public List<SelectListItem> CategoriasDisponibles { get; set; } = new();
        public List<SelectListItem> MarcasDisponibles { get; set; } = new();
        public List<SelectListItem> ProductosDisponibles { get; set; } = new();
    }
}