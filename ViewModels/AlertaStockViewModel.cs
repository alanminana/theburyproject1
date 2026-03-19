using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// ViewModel para mostrar alertas de stock
    /// </summary>
    public class AlertaStockViewModel
    {
        public int Id { get; set; }
        public byte[]? RowVersion { get; set; }
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public string CategoriaNombre { get; set; } = string.Empty;
        public string MarcaNombre { get; set; } = string.Empty;

        public TipoAlertaStock Tipo { get; set; }
        public string TipoDescripcion => Tipo.ToString();
        public PrioridadAlerta Prioridad { get; set; }
        public string PrioridadDescripcion => Prioridad.ToString();
        public EstadoAlerta Estado { get; set; }
        public string EstadoDescripcion => Estado.ToString();

        public string Mensaje { get; set; } = string.Empty;
        public decimal StockActual { get; set; }
        public decimal StockMinimo { get; set; }
        public decimal? CantidadSugeridaReposicion { get; set; }

        public DateTime FechaAlerta { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string? UsuarioResolucion { get; set; }
        public string? Observaciones { get; set; }
        public bool NotificacionUrgente { get; set; }

        // Propiedades calculadas
        public decimal PorcentajeStockMinimo { get; set; }
        public int DiasDesdeAlerta { get; set; }
        public bool EstaVencida { get; set; }

        // Para UI
        public string BadgeTipo => Tipo switch
        {
            TipoAlertaStock.StockBajo => "bg-warning",
            TipoAlertaStock.StockCritico => "bg-danger",
            TipoAlertaStock.StockAgotado => "bg-dark",
            _ => "bg-secondary"
        };

        public string BadgePrioridad => Prioridad switch
        {
            PrioridadAlerta.Baja => "bg-info",
            PrioridadAlerta.Media => "bg-warning",
            PrioridadAlerta.Alta => "bg-danger",
            PrioridadAlerta.Critica => "bg-dark text-white",
            _ => "bg-secondary"
        };

        public string BadgeEstado => Estado switch
        {
            EstadoAlerta.Pendiente => "bg-warning",
            EstadoAlerta.EnProceso => "bg-info",
            EstadoAlerta.Resuelta => "bg-success",
            EstadoAlerta.Ignorada => "bg-secondary",
            _ => "bg-secondary"
        };

        public string IconoTipo => Tipo switch
        {
            TipoAlertaStock.StockBajo => "bi-exclamation-triangle",
            TipoAlertaStock.StockCritico => "bi-exclamation-octagon",
            TipoAlertaStock.StockAgotado => "bi-x-octagon",
            _ => "bi-info-circle"
        };
    }

    /// <summary>
    /// ViewModel para filtrar alertas de stock
    /// </summary>
    public class AlertaStockFiltroViewModel : PaginationViewModel
    {
        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public string? ProductoNombre { get; set; }
        public TipoAlertaStock? Tipo { get; set; }
        public PrioridadAlerta? Prioridad { get; set; }
        public EstadoAlerta? Estado { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public bool? SoloUrgentes { get; set; }
        public bool? SoloVencidas { get; set; }
    }

    /// <summary>
    /// ViewModel para estadísticas de alertas de stock
    /// </summary>
    public class AlertaStockEstadisticasViewModel
    {
        // Totales generales
        public int TotalAlertas { get; set; }
        public int AlertasPendientes { get; set; }
        public int AlertasResueltas { get; set; }
        public int AlertasIgnoradas { get; set; }

        // Alertas especiales
        public int AlertasUrgentes { get; set; }
        public int AlertasVencidas { get; set; }

        // Por prioridad
        public int AlertasCriticas { get; set; }
        public int AlertasAltas { get; set; }
        public int AlertasMedias { get; set; }

        // Por tipo de alerta
        public int AlertasStockAgotado { get; set; }
        public int AlertasStockCritico { get; set; }
        public int AlertasStockBajo { get; set; }
        public int AlertasSinMovimiento { get; set; }

        // Métricas de rendimiento
        public decimal PromedioResolucionDias { get; set; }
        public decimal TasaResolucionPorcentaje { get; set; }
        public int ProductosAfectados { get; set; }

        // Valores y promedios
        public decimal PromedioReposicionSugerida { get; set; }
        public decimal ValorTotalStockCritico { get; set; }

        // Listas detalladas (para vistas adicionales si se requieren)
        public List<AlertaStockViewModel> UltimasAlertas { get; set; } = new();
        public List<ProductoAlertaViewModel> ProductosMasAlertas { get; set; } = new();
        public List<CategoriaAlertaViewModel> CategoriasConMasAlertas { get; set; } = new();

        // Propiedades de compatibilidad (deprecated, usar las nuevas)
        [Obsolete("Use AlertasPendientes instead")]
        public int TotalAlertasPendientes => AlertasPendientes;
        [Obsolete("Use AlertasStockCritico instead")]
        public int ProductosCriticos => AlertasStockCritico;
        [Obsolete("Use AlertasStockBajo instead")]
        public int ProductosStockBajo => AlertasStockBajo;
        [Obsolete("Use AlertasStockAgotado instead")]
        public int ProductosStockAgotado => AlertasStockAgotado;
    }

    /// <summary>
    /// ViewModel para producto con alerta
    /// </summary>
    public class ProductoAlertaViewModel
    {
        public int ProductoId { get; set; }
        public string ProductoCodigo { get; set; } = string.Empty;
        public string ProductoNombre { get; set; } = string.Empty;
        public string CategoriaNombre { get; set; } = string.Empty;
        public string MarcaNombre { get; set; } = string.Empty;

        public decimal StockActual { get; set; }
        public decimal StockMinimo { get; set; }
        public decimal PrecioVenta { get; set; }
        public decimal PrecioCompra { get; set; }

        public int TotalAlertas { get; set; }
        public TipoAlertaStock? UltimoTipoAlerta { get; set; }
        public PrioridadAlerta? UltimaPrioridad { get; set; }
        public DateTime? FechaUltimaAlerta { get; set; }

        public decimal ValorStock => StockActual * PrecioCompra;
        public decimal PorcentajeStock => StockMinimo == 0 ? 0 : (StockActual / StockMinimo) * 100;
    }

    /// <summary>
    /// ViewModel para categoría con alertas
    /// </summary>
    public class CategoriaAlertaViewModel
    {
        public int CategoriaId { get; set; }
        public string CategoriaNombre { get; set; } = string.Empty;
        public int TotalAlertas { get; set; }
        public int ProductosAfectados { get; set; }
        public decimal ValorStockTotal { get; set; }
    }

    /// <summary>
    /// ViewModel para productos en estado crítico
    /// </summary>
    public class ProductoCriticoViewModel
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string CategoriaNombre { get; set; } = string.Empty;
        public string? MarcaNombre { get; set; }

        public decimal StockActual { get; set; }
        public decimal StockMinimo { get; set; }
        public decimal PrecioCompra { get; set; }
        public decimal PrecioVenta { get; set; }

        public decimal? CantidadSugeridaReposicion { get; set; }
        public int AlertasPendientes { get; set; }

        public DateTime? UltimaVenta { get; set; }
        public int DiasDesdeUltimaVenta { get; set; }

        // Propiedades calculadas
        public decimal PorcentajeStockMinimo => StockMinimo == 0 ? 0 : (StockActual / StockMinimo) * 100;
        public decimal ValorInventario => StockActual * PrecioCompra;
        public bool EstaAgotado => StockActual <= 0;
    }
}