namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Estado de conexión mostrado en MercadoLibre/Index.
    /// </summary>
    public class MercadoLibreConexionViewModel
    {
        public bool ModuloConfigurado { get; set; }

        public List<MercadoLibreCuentaViewModel> Cuentas { get; set; } = new();

        public bool HayCuentaConectada => Cuentas.Any(c => c.Activa);
    }

    public class MercadoLibreCuentaViewModel
    {
        public int Id { get; set; }
        public long MeliUserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string SiteId { get; set; } = string.Empty;
        public bool Activa { get; set; }
        public DateTime AccessTokenExpiresAtUtc { get; set; }
        public DateTime? UltimaPruebaConexionUtc { get; set; }
        public bool? UltimaPruebaConexionOk { get; set; }
        public DateTime? UltimaImportacionListingsUtc { get; set; }
        public int TotalListings { get; set; }
        public int ListingsVinculadas { get; set; }
    }

    /// <summary>
    /// Fila de la grilla de publicaciones.
    /// </summary>
    public class MercadoLibreListingViewModel
    {
        public int Id { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string CurrencyId { get; set; } = string.Empty;
        public int AvailableQuantity { get; set; }
        public int SoldQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? SubStatus { get; set; }
        public string? Permalink { get; set; }
        public string? CategoryId { get; set; }
        public string? ListingTypeId { get; set; }
        public string? SellerSku { get; set; }
        public bool TieneVariaciones { get; set; }
        public int CantidadVariaciones { get; set; }
        public DateTime? LastSyncUtc { get; set; }

        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public decimal? ProductoStockActual { get; set; }
        public decimal? ProductoPrecioVenta { get; set; }

        /// <summary>
        /// Producto sugerido por coincidencia exacta SellerSku == Producto.Codigo
        /// (solo sugerencia; la vinculación es siempre explícita).
        /// </summary>
        public int? ProductoSugeridoId { get; set; }
        public string? ProductoSugeridoNombre { get; set; }

        public bool Vinculada => ProductoId.HasValue;
    }

    /// <summary>
    /// Resumen de una corrida de importación de publicaciones.
    /// </summary>
    public class MercadoLibreImportResultViewModel
    {
        public int TotalEncontradas { get; set; }
        public int Creadas { get; set; }
        public int Actualizadas { get; set; }
        public int ConVariaciones { get; set; }
        public int Errores { get; set; }
        public long DuracionMs { get; set; }
        public List<string> Mensajes { get; set; } = new();
    }
}
