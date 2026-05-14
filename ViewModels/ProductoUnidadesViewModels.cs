using TheBuryProject.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels
{
    public class ProductoUnidadesViewModel
    {
        public int ProductoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public bool RequiereNumeroSerie { get; set; }
        public decimal StockActual { get; set; }
        public ProductoUnidadesFiltroViewModel Filtros { get; set; } = new();
        public ProductoUnidadCrearViewModel CrearUnidad { get; set; } = new();
        public ProductoUnidadCargaMasivaViewModel CargaMasiva { get; set; } = new();
        public List<ProductoUnidadEstadoResumenViewModel> ResumenEstados { get; set; } = new();
        public List<ProductoUnidadItemViewModel> Unidades { get; set; } = new();
    }

    public class ProductoUnidadCrearViewModel
    {
        public int ProductoId { get; set; }

        [StringLength(100, ErrorMessage = "El numero de serie no puede superar los 100 caracteres.")]
        public string? NumeroSerie { get; set; }

        [StringLength(200, ErrorMessage = "La ubicacion actual no puede superar los 200 caracteres.")]
        public string? UbicacionActual { get; set; }

        [StringLength(500, ErrorMessage = "Las observaciones no pueden superar los 500 caracteres.")]
        public string? Observaciones { get; set; }
    }

    public class ProductoUnidadCargaMasivaViewModel
    {
        public int ProductoId { get; set; }

        [Range(0, 200, ErrorMessage = "La cantidad sin serie debe estar entre 0 y 200.")]
        public int CantidadSinSerie { get; set; }

        public string? NumerosSerieTexto { get; set; }

        [StringLength(200, ErrorMessage = "La ubicacion actual no puede superar los 200 caracteres.")]
        public string? UbicacionActual { get; set; }

        [StringLength(500, ErrorMessage = "Las observaciones no pueden superar los 500 caracteres.")]
        public string? Observaciones { get; set; }

        public bool Confirmar { get; set; }
        public bool PreviewListo { get; set; }
        public int TotalUnidades => Preview.Count;
        public List<ProductoUnidadCargaMasivaPreviewItemViewModel> Preview { get; set; } = new();
    }

    public class ProductoUnidadCargaMasivaPreviewItemViewModel
    {
        public int Orden { get; set; }
        public string? NumeroSerie { get; set; }
        public bool TieneNumeroSerie => !string.IsNullOrWhiteSpace(NumeroSerie);
    }

    public class ProductoUnidadesFiltroViewModel
    {
        public EstadoUnidad? Estado { get; set; }
        public string? Texto { get; set; }
        public bool SoloDisponibles { get; set; }
        public bool SoloVendidas { get; set; }
        public bool SoloSinNumeroSerie { get; set; }
    }

    public class ProductoUnidadEstadoResumenViewModel
    {
        public EstadoUnidad Estado { get; set; }
        public int Cantidad { get; set; }
    }

    public class ProductoUnidadItemViewModel
    {
        public int Id { get; set; }
        public string CodigoInternoUnidad { get; set; } = string.Empty;
        public string? NumeroSerie { get; set; }
        public EstadoUnidad Estado { get; set; }
        public string? UbicacionActual { get; set; }
        public DateTime FechaIngreso { get; set; }
        public string? ClienteAsociado { get; set; }
        public int? VentaDetalleId { get; set; }
        public DateTime? FechaVenta { get; set; }
        public string? Observaciones { get; set; }
    }

    public class ProductoUnidadHistorialViewModel
    {
        public int UnidadId { get; set; }
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public string CodigoInternoUnidad { get; set; } = string.Empty;
        public string? NumeroSerie { get; set; }
        public EstadoUnidad EstadoActual { get; set; }
        public List<ProductoUnidadMovimientoItemViewModel> Movimientos { get; set; } = new();
    }

    public class ProductoUnidadMovimientoItemViewModel
    {
        public DateTime FechaCambio { get; set; }
        public EstadoUnidad EstadoAnterior { get; set; }
        public EstadoUnidad EstadoNuevo { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public string? OrigenReferencia { get; set; }
        public string? UsuarioResponsable { get; set; }
    }
}
