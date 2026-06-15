using System.ComponentModel.DataAnnotations;
using TheBuryProject.Modules.MercadoLibre.Entities;

namespace TheBuryProject.Modules.MercadoLibre.ViewModels
{
    /// <summary>
    /// Solicitud de simulación de un aumento masivo ML.
    /// </summary>
    public class MercadoLibrePriceBatchRequest
    {
        [Required(ErrorMessage = "El nombre del lote es obligatorio")]
        [StringLength(200)]
        public string Nombre { get; set; } = string.Empty;

        public MercadoLibrePriceBatchOrigen Origen { get; set; }

        [Range(-99, 999, ErrorMessage = "El ajuste debe estar entre -99% y 999%")]
        public decimal ValorAjustePorcentaje { get; set; }

        // Filtros
        public int? CategoriaId { get; set; }
        public int? MarcaId { get; set; }

        /// <summary>active | paused | (vacío = cualquiera no finalizada).</summary>
        public string? Estado { get; set; }

        /// <summary>Solo publicaciones vinculadas a producto (obligatorio con origen DesdePrecioErp).</summary>
        public bool SoloVinculadas { get; set; } = true;

        public decimal? PrecioDesde { get; set; }
        public decimal? PrecioHasta { get; set; }

        /// <summary>Solo publicaciones con stock ML &gt; 0.</summary>
        public bool SoloConStock { get; set; }
    }

    public class MercadoLibrePriceBatchListViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public MercadoLibrePriceBatchEstado Estado { get; set; }
        public MercadoLibrePriceBatchOrigen Origen { get; set; }
        public decimal ValorAjustePorcentaje { get; set; }
        public int CantidadPublicaciones { get; set; }
        public int CantidadItems { get; set; }
        public int CantidadErrores { get; set; }
        public bool AplicadoEnSimulacion { get; set; }
        public string SolicitadoPor { get; set; } = string.Empty;
        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaAplicacion { get; set; }
        public DateTime? FechaReversion { get; set; }
    }

    public class MercadoLibrePriceBatchDetalleViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public MercadoLibrePriceBatchEstado Estado { get; set; }
        public MercadoLibrePriceBatchOrigen Origen { get; set; }
        public decimal ValorAjustePorcentaje { get; set; }
        public bool AplicadoEnSimulacion { get; set; }
        public bool FiltroSoloVinculadas { get; set; }
        public bool ModoSimulacion { get; set; }
        public string SolicitadoPor { get; set; } = string.Empty;
        public DateTime FechaSolicitud { get; set; }
        public string? AplicadoPor { get; set; }
        public DateTime? FechaAplicacion { get; set; }
        public string? RevertidoPor { get; set; }
        public DateTime? FechaReversion { get; set; }
        public string? MotivoReversion { get; set; }

        public List<MercadoLibrePriceBatchItemViewModel> Items { get; set; } = new();

        public int ConAdvertencia => Items.Count(i => i.TieneAdvertencia);
        public int Aplicados => Items.Count(i => i.Aplicado);
        public int ConError => Items.Count(i => !string.IsNullOrEmpty(i.Error));

        public bool PuedeAplicar => Estado == MercadoLibrePriceBatchEstado.Simulado && Items.Count > 0;
        public bool PuedeRevertir => Estado is MercadoLibrePriceBatchEstado.Aplicado or MercadoLibrePriceBatchEstado.AplicadoParcial
                                     && !AplicadoEnSimulacion;
        public bool PuedeCancelar => Estado == MercadoLibrePriceBatchEstado.Simulado;
    }

    public class MercadoLibrePriceBatchItemViewModel
    {
        public int ListingId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public long? VariationId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public string OrigenPrecio { get; set; } = string.Empty;
        public decimal? MargenEstimadoPorcentaje { get; set; }
        public decimal PrecioAnterior { get; set; }
        public decimal PrecioNuevo { get; set; }
        public decimal DiferenciaPorcentaje { get; set; }
        public string? PayloadAplicacionJson { get; set; }
        public bool TieneAdvertencia { get; set; }
        public string? MensajeAdvertencia { get; set; }
        public bool Aplicado { get; set; }
        public bool Revertido { get; set; }
        public string? Error { get; set; }
    }
}
