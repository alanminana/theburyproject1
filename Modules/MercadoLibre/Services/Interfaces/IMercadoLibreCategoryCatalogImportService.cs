namespace TheBuryProject.Modules.MercadoLibre.Services.Interfaces
{
    /// <summary>
    /// Resultado de una importación del catálogo de categorías ML desde archivo local.
    /// </summary>
    public sealed class MercadoLibreCategoryImportResult
    {
        public bool Ok { get; set; }
        public string SiteId { get; set; } = "MLA";
        public string? SourceFilePath { get; set; }
        public string? SourceKind { get; set; }
        public int ImportedCategories { get; set; }
        public int ImportedAttributes { get; set; }
        public int LeafCategories { get; set; }
        public int ListingAllowedCategories { get; set; }
        public long DurationMs { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Importa el árbol completo de categorías ML (con atributos) desde un archivo
    /// LOCAL grande (.json o .json.gz) usando lectura por streaming: nunca carga el
    /// archivo entero en memoria ni lo versiona. Reconstruye el caché local wholesale.
    /// </summary>
    public interface IMercadoLibreCategoryCatalogImportService
    {
        /// <summary>
        /// Importa desde un archivo local. Soporta .json y .gz (GZipStream). El parseo
        /// es incremental (objeto raíz {categoryId: {...}}): cada categoría se procesa
        /// y persiste por lotes. No llama a Mercado Libre.
        /// </summary>
        Task<MercadoLibreCategoryImportResult> ImportFromFileAsync(
            string filePath, string siteId = "MLA", CancellationToken ct = default);
    }
}
