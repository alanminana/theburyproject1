using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para filtrado, bsqueda y paginacin de documentos de clientes
    /// </summary>
    public class DocumentoClienteFilterViewModel
    {
        // Filtros de bsqueda
        public int? ClienteId { get; set; }
        public int? TipoDocumento { get; set; }
        public EstadoDocumento? Estado { get; set; }
        public bool SoloPendientes { get; set; }
        public bool SoloVencidos { get; set; }

        // Flujo de retorno a crédito de venta
        public int? ReturnToVentaId { get; set; }

        // Paginacin
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        // Resultados
        public List<DocumentoClienteViewModel> Documentos { get; set; } = new();
        public int TotalResultados { get; set; }

        // Propiedades calculadas para paginacin
        public int TotalPages => TotalResultados == 0 ? 1 : (int)Math.Ceiling((double)TotalResultados / PageSize);
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;

        // Formulario embebido para carga rápida
        public DocumentoClienteViewModel UploadModel { get; set; } = new();
    }
}
