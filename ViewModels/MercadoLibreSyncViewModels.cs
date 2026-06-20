using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Preview de sincronización stock/precio ERP → ML: qué se enviaría.
    /// </summary>
    public class MercadoLibreSyncPreviewViewModel
    {
        public bool ModoSimulacion { get; set; }

        public string Tipo { get; set; } = string.Empty;

        public List<MercadoLibreSyncPreviewItemViewModel> Items { get; set; } = new();

        public int TotalConCambios => Items.Count(i => i.TieneCambios && !i.Excluida);
        public int TotalExcluidas => Items.Count(i => i.Excluida);
        public int TotalAdvertencias => Items.Count(i => i.Advertencias.Count > 0);
    }

    public class MercadoLibreSyncPreviewItemViewModel
    {
        public int ListingId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool TieneVariaciones { get; set; }
        public int CantidadVariaciones { get; set; }
        public long? VariationId { get; set; }
        public string? VariationAtributos { get; set; }
        public string? VariationSellerSku { get; set; }
        public bool EsVariacion => VariationId.HasValue;

        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }

        // Stock
        public int StockMl { get; set; }
        public int? StockObjetivo { get; set; }
        public bool CambiaStock => StockObjetivo.HasValue && StockObjetivo.Value != StockMl;

        /// <summary>Origen efectivo del stock objetivo (lógico, físico, unidad específica).</summary>
        public MercadoLibreOrigenStock? OrigenStock { get; set; }

        // Precio
        public decimal PrecioMl { get; set; }
        public decimal? PrecioObjetivo { get; set; }
        public bool CambiaPrecio => PrecioObjetivo.HasValue && PrecioObjetivo.Value != PrecioMl;

        public bool TieneCambios => CambiaStock || CambiaPrecio;

        /// <summary>Publicación excluida del push (sin vincular, multi-variación para stock, etc.).</summary>
        public bool Excluida { get; set; }

        public string? MotivoExclusion { get; set; }

        public List<string> Advertencias { get; set; } = new();

        public string EtiquetaOperacion => VariationId.HasValue
            ? $"{ItemId} / var {VariationId}"
            : ItemId;
    }

    /// <summary>
    /// Resultado de aplicar (o simular) una sincronización.
    /// </summary>
    public class MercadoLibreSyncResultViewModel
    {
        public bool FueSimulado { get; set; }
        public int Exitosos { get; set; }
        public int Fallidos { get; set; }
        public int Omitidos { get; set; }
        public List<string> Mensajes { get; set; } = new();
    }
}
