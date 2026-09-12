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

        // Catálogos para el picker de búsqueda+chips de los modales de Crear/Editar.
        // JSON de {id, nombre} (Productos incluye además codigo/marca/categoria).
        public string CategoriasPickerJson { get; set; } = "[]";
        public string MarcasPickerJson { get; set; } = "[]";
        public string ProductosPickerJson { get; set; } = "[]";
    }
}