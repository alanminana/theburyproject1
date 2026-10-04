using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Documentos;

namespace TheBuryProject.Services.Interfaces
{
    public sealed class TipoDocumentoInput
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public CategoriaDocumento Categoria { get; set; } = CategoriaDocumento.Otro;
        public bool Activo { get; set; } = true;
        public bool PermiteMultiples { get; set; }
        public bool RequiereFirma { get; set; }
        public string Prefijo { get; set; } = "DOC";
    }

    public sealed class PlantillaDocumentoInput
    {
        public int Id { get; set; }
        public int TipoDocumentoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public bool Activa { get; set; } = true;
        public DateTime VigenteDesde { get; set; } = DateTime.Today;
        public DateTime? VigenteHasta { get; set; }
        public bool RequiereFirma { get; set; }
        public string? FirmantesRequeridos { get; set; }
        public int Copias { get; set; } = 1;
        public string Contenido { get; set; } = string.Empty;
        public string? VariablesRequeridas { get; set; }
        public string? ComentarioVersion { get; set; }
    }

    public sealed class ReglaDocumentoInput
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string EventoCodigo { get; set; } = string.Empty;
        public string? CondicionJson { get; set; }
        public int? PlantillaDocumentoId { get; set; }
        public int? PaqueteDocumentalId { get; set; }
        public int Prioridad { get; set; } = 100;
        public bool Activa { get; set; } = true;
        public bool Obligatoria { get; set; }
        public string? GrupoExclusion { get; set; }
        public bool ExigeFirmaParaContinuar { get; set; }
    }

    public sealed class PaqueteDocumentalInput
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public bool Activo { get; set; } = true;

        /// <summary>Plantillas del paquete, en el orden de impresión.</summary>
        public List<int> PlantillaIds { get; set; } = new();
    }

    public sealed class DocumentoPreviewResultado
    {
        public string Texto { get; init; } = string.Empty;
        public IReadOnlyList<string> ErroresPlantilla { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> VariablesVacias { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> VariablesNoResueltas { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RequeridasFaltantes { get; init; } = Array.Empty<string>();
        public bool UsaDatosReales { get; init; }
        public bool EsGenerable => ErroresPlantilla.Count == 0 && RequeridasFaltantes.Count == 0;
    }

    public interface IDocumentoConfiguracionService
    {
        Task<List<TipoDocumento>> ListarTiposAsync();
        Task<TipoDocumento> GuardarTipoAsync(TipoDocumentoInput input);

        Task<List<PlantillaDocumento>> ListarPlantillasAsync(int? tipoDocumentoId = null);
        Task<PlantillaDocumento?> ObtenerPlantillaAsync(int id);
        Task<PlantillaDocumentoVersion?> ObtenerVersionVigenteAsync(int plantillaId);
        Task<PlantillaDocumento> GuardarPlantillaAsync(PlantillaDocumentoInput input);
        Task SetPlantillaActivaAsync(int id, bool activa);

        /// <summary>Crea una versión nueva copiando el contenido de una anterior (las versiones nunca se modifican).</summary>
        Task<PlantillaDocumento> RestaurarVersionAsync(int plantillaId, int numeroVersion);

        Task<List<ReglaDocumento>> ListarReglasAsync();
        Task<ReglaDocumento?> ObtenerReglaAsync(int id);
        Task<ReglaDocumento> GuardarReglaAsync(ReglaDocumentoInput input);
        Task SetReglaActivaAsync(int id, bool activa);
        Task EliminarReglaAsync(int id);

        Task<List<PaqueteDocumental>> ListarPaquetesAsync();
        Task<PaqueteDocumental?> ObtenerPaqueteAsync(int id);
        Task<PaqueteDocumental> GuardarPaqueteAsync(PaqueteDocumentalInput input);

        Task<DocumentoPreviewResultado> PrevisualizarAsync(
            string contenido, string? variablesRequeridas, string evento,
            int? ventaId = null, int? pagoCuotaId = null, int? cotizacionId = null);
    }
}
