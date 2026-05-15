using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    public class ProductoUnidadesGlobalFiltros
    {
        public int? ProductoId { get; set; }
        public EstadoUnidad? Estado { get; set; }
        public string? Texto { get; set; }
        public bool SoloDisponibles { get; set; }
        public bool SoloSinNumeroSerie { get; set; }
        public bool SoloVendidas { get; set; }
        public bool SoloFaltantes { get; set; }
        public bool SoloBaja { get; set; }
        public bool SoloDevueltas { get; set; }
    }
}
