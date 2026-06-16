namespace TheBuryProject.Modules.MercadoLibre.ViewModels
{
    /// <summary>
    /// Sugerencia del predictor de categorías (domain_discovery). Cada una apunta
    /// a una categoría hoja candidata para el título consultado.
    /// </summary>
    public class CategoriaSugerenciaVm
    {
        public string CategoryId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Dominio { get; set; }
    }

    /// <summary>
    /// Nodo del árbol para el navegado manual (raíz o hijos de una categoría).
    /// </summary>
    public class CategoriaNodoVm
    {
        public string CategoryId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resultado de navegar un nivel del árbol: el contexto del padre (si aplica)
    /// y sus categorías hijas. Si la categoría consultada es hoja, Hijos viene
    /// vacío y EsHoja queda en true.
    /// </summary>
    public class CategoriaNivelVm
    {
        public string? CategoryId { get; set; }
        public string? Nombre { get; set; }
        public string? Path { get; set; }
        public bool EsHoja { get; set; }
        public List<CategoriaNodoVm> Hijos { get; set; } = new();
    }

    /// <summary>
    /// Categoría resuelta contra ML al momento de seleccionarla: datos autoritativos
    /// que el picker snapshotea en el borrador. Solo se puede publicar si EsHoja y
    /// ListingAllowed.
    /// </summary>
    public class CategoriaResueltaVm
    {
        public string CategoryId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool EsHoja { get; set; }
        public bool ListingAllowed { get; set; }
        public int? MaxTitleLength { get; set; }
    }
}
