using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    public class ProductoUnidadFiltros
    {
        public EstadoUnidad? Estado { get; set; }
        public string? Texto { get; set; }
        public bool SoloDisponibles { get; set; }
        public bool SoloVendidas { get; set; }
        public bool SoloSinNumeroSerie { get; set; }
    }
}
