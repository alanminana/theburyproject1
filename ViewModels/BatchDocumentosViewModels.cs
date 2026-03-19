using System.Collections.Generic;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Request para operaciones batch de documentos
    /// </summary>
    public class BatchDocumentosRequest
    {
        public List<int> Ids { get; set; } = new();
        public string? Observaciones { get; set; }
        public string? Motivo { get; set; }
    }

    /// <summary>
    /// Response para operaciones batch de documentos
    /// </summary>
    public class BatchDocumentosResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int Exitosos { get; set; }
        public int Fallidos { get; set; }
        public List<BatchItemErrorResponse> Errores { get; set; } = new();
    }

    /// <summary>
    /// Error individual en operación batch
    /// </summary>
    public class BatchItemErrorResponse
    {
        public int Id { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }
}
