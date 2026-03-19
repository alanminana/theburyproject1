namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para mostrar el historial de precios
    /// </summary>
    public class PrecioHistoricoViewModel
    {
        public int Id { get; set; }
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;

        public decimal PrecioCompraAnterior { get; set; }
        public decimal PrecioCompraNuevo { get; set; }
        public decimal PrecioVentaAnterior { get; set; }
        public decimal PrecioVentaNuevo { get; set; }

        public string? MotivoCambio { get; set; }
        public bool PuedeRevertirse { get; set; }
        public DateTime FechaCambio { get; set; }
        public string UsuarioModificacion { get; set; } = string.Empty;

        // Propiedades calculadas
        public decimal PorcentajeCambioCompra { get; set; }
        public decimal PorcentajeCambioVenta { get; set; }
        public decimal MargenAnterior { get; set; }
        public decimal MargenNuevo { get; set; }
        public decimal DiferenciaCompra => PrecioCompraNuevo - PrecioCompraAnterior;
        public decimal DiferenciaVenta => PrecioVentaNuevo - PrecioVentaAnterior;

        // Para UI
        public string EstiloCambioCompra => PorcentajeCambioCompra >= 0 ? "text-danger" : "text-success";
        public string EstiloCambioVenta => PorcentajeCambioVenta >= 0 ? "text-success" : "text-danger";
        public string IconoCambioCompra => PorcentajeCambioCompra >= 0 ? "↑" : "↓";
        public string IconoCambioVenta => PorcentajeCambioVenta >= 0 ? "↑" : "↓";
    }

    /// <summary>
    /// ViewModel para filtrar historial de precios
    /// </summary>
    public class PrecioHistoricoFiltroViewModel : PaginationViewModel
    {
        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string? UsuarioModificacion { get; set; }
        public bool? SoloPuedeRevertirse { get; set; }
        public decimal? PorcentajeMinimoAumento { get; set; }
    }

    /// <summary>
    /// ViewModel para estadísticas de cambios de precios
    /// </summary>
    public class PrecioHistoricoEstadisticasViewModel
    {
        public int TotalCambios { get; set; }
        public int CambiosConAumento { get; set; }
        public int CambiosConDisminucion { get; set; }
        public int CambiosReversibles { get; set; }

        public decimal PromedioAumentoCompra { get; set; }
        public decimal PromedioAumentoVenta { get; set; }
        public decimal PromedioDisminucionCompra { get; set; }
        public decimal PromedioDisminucionVenta { get; set; }

        public decimal MayorAumentoVenta { get; set; }
        public string? ProductoMayorAumentoVenta { get; set; }
        public decimal MayorDisminucionVenta { get; set; }
        public string? ProductoMayorDisminucionVenta { get; set; }

        public List<PrecioHistoricoViewModel> UltimosCambios { get; set; } = new();
        public List<ProductoConMasCambiosViewModel> ProductosConMasCambios { get; set; } = new();
    }

    /// <summary>
    /// ViewModel para productos con más cambios de precio
    /// </summary>
    public class ProductoConMasCambiosViewModel
    {
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public int TotalCambios { get; set; }
        public decimal PrecioActualVenta { get; set; }
        public decimal PrecioActualCompra { get; set; }
    }

    /// <summary>
    /// ViewModel para simular cambios de precio
    /// </summary>
    public class PrecioSimulacionViewModel
    {
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;

        // Precios actuales
        public decimal PrecioCompraActual { get; set; }
        public decimal PrecioVentaActual { get; set; }
        public decimal MargenActual { get; set; }

        // Precios propuestos
        public decimal PrecioCompraPropuesto { get; set; }
        public decimal PrecioVentaPropuesto { get; set; }
        public decimal MargenPropuesto { get; set; }

        // Cambios
        public decimal DiferenciaCompra { get; set; }
        public decimal DiferenciaVenta { get; set; }
        public decimal PorcentajeCambioCompra { get; set; }
        public decimal PorcentajeCambioVenta { get; set; }
        public decimal DiferenciaMargen { get; set; }

        // Alertas
        public List<string> Alertas { get; set; } = new();
        public bool EsRecomendable { get; set; }
        public string? Recomendacion { get; set; }
    }
}