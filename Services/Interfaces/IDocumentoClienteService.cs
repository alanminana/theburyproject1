using System.Collections.Generic;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services.Interfaces
{
    public interface IDocumentoClienteService
    {
        Task<List<DocumentoClienteViewModel>> GetAllAsync();
        Task<DocumentoClienteViewModel?> GetByIdAsync(int id);
        Task<List<DocumentoClienteViewModel>> GetByClienteIdAsync(int clienteId);
        Task<DocumentoClienteViewModel> UploadAsync(DocumentoClienteViewModel viewModel);
        Task<DocumentacionClienteEstadoViewModel> ValidarDocumentacionObligatoriaAsync(
            int clienteId,
            IEnumerable<TipoDocumentoCliente>? requeridos = null);
        Task<bool> VerificarAsync(int id, string verificadoPor, string? observaciones = null);
        Task<bool> RechazarAsync(int id, string motivo, string rechazadoPor);
        Task<bool> DeleteAsync(int id);
        Task<byte[]> DescargarArchivoAsync(int id);
        /// <summary>
        /// Busca documentos con filtros y paginacin
        /// </summary>
        Task<(List<DocumentoClienteViewModel> Documentos, int Total)> BuscarAsync(DocumentoClienteFilterViewModel filtro);
        /// <summary>
        /// Verifica todos los documentos pendientes de un cliente
        /// </summary>
        Task<int> VerificarTodosAsync(int clienteId, string verificadoPor, string? observaciones = null);
        /// <summary>
        /// Marca documentos vencidos automticamente (ejecutado por BackgroundService)
        /// </summary>
        Task MarcarVencidosAsync();
        /// <summary>
        /// Verifica una lista de documentos por sus IDs (batch)
        /// </summary>
        Task<BatchOperacionResultado> VerificarBatchAsync(IEnumerable<int> ids, string verificadoPor, string? observaciones = null);
        /// <summary>
        /// Rechaza una lista de documentos por sus IDs (batch)
        /// </summary>
        Task<BatchOperacionResultado> RechazarBatchAsync(IEnumerable<int> ids, string motivo, string rechazadoPor);
    }

    /// <summary>
    /// Resultado de operación batch sobre documentos
    /// </summary>
    public class BatchOperacionResultado
    {
        public int Exitosos { get; set; }
        public int Fallidos { get; set; }
        public List<BatchItemError> Errores { get; set; } = new();
    }

    public class BatchItemError
    {
        public int Id { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }
}
