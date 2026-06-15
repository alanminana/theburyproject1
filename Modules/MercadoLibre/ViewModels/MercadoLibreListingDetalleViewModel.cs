using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre.ViewModels
{
    /// <summary>
    /// Detalle completo de una publicación para el ABM (Fase E) +
    /// calculadora de precio del canal (Fase G).
    /// </summary>
    public class MercadoLibreListingDetalleViewModel
    {
        public int Id { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string CurrencyId { get; set; } = "ARS";
        public int AvailableQuantity { get; set; }
        public int SoldQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? SubStatus { get; set; }
        public string? Permalink { get; set; }
        public string? CategoryId { get; set; }
        public string? ListingTypeId { get; set; }
        public string? Condition { get; set; }
        public string? SellerSku { get; set; }
        public bool TieneVariaciones { get; set; }
        public DateTime? LastSyncUtc { get; set; }

        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public decimal? ProductoStockActual { get; set; }
        public decimal? ProductoPrecioVenta { get; set; }

        /// <summary>Descripción actual en ML (texto plano). Null si no se pudo consultar.</summary>
        public string? Descripcion { get; set; }
        public bool DescripcionConsultada { get; set; }

        public bool ModoSimulacion { get; set; }

        // Origen de stock (Checkpoint 4)
        public MercadoLibreOrigenStock OrigenStockGlobal { get; set; }
        public MercadoLibreOrigenStock? OrigenStockOverride { get; set; }
        public MercadoLibreOrigenStock OrigenStockEfectivo => OrigenStockOverride ?? OrigenStockGlobal;
        public int? ProductoUnidadId { get; set; }
        public int? StockDisponibleSegunOrigen { get; set; }
        public string? AdvertenciaStock { get; set; }

        /// <summary>Unidades físicas del producto vinculado (selector de unidad específica).</summary>
        public List<MercadoLibreUnidadOptionViewModel> UnidadesDisponibles { get; set; } = new();

        public List<MercadoLibreVariacionViewModel> Variaciones { get; set; } = new();

        /// <summary>Desglose de la calculadora (solo si está vinculada a un Producto).</summary>
        public MercadoLibreDesglosePrecio? Desglose { get; set; }

        public List<MercadoLibreSyncLogViewModel> UltimosLogs { get; set; } = new();

        public bool Vinculada => ProductoId.HasValue;
        public bool EstaActiva => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
        public bool EstaPausada => string.Equals(Status, "paused", StringComparison.OrdinalIgnoreCase);
        public bool EstaFinalizada => string.Equals(Status, "closed", StringComparison.OrdinalIgnoreCase);
    }

    public class MercadoLibreVariacionViewModel
    {
        public long VariationId { get; set; }
        public decimal Precio { get; set; }
        public int AvailableQuantity { get; set; }
        public int SoldQuantity { get; set; }
        public string? SellerSku { get; set; }
        public string? Atributos { get; set; }
        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public MercadoLibreOrigenStock? OrigenStockOverride { get; set; }
        public MercadoLibreOrigenStock OrigenStockEfectivo { get; set; }
        public int? ProductoUnidadId { get; set; }
        public int? StockDisponibleSegunOrigen { get; set; }
        public string? AdvertenciaStock { get; set; }
        public bool UsaProductoDePublicacion { get; set; }
        public bool RequiereVinculoParaStock { get; set; }
        public List<MercadoLibreUnidadOptionViewModel> UnidadesDisponibles { get; set; } = new();
    }

    public class MercadoLibreSyncLogViewModel
    {
        public DateTime Fecha { get; set; }
        public string Operacion { get; set; } = string.Empty;
        public bool Exito { get; set; }
        public string? Detalle { get; set; }
    }
}
