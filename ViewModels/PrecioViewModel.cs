using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para lista de precios
/// </summary>
public class ListaPrecioViewModel
{
    public int Id { get; set; }
    public byte[]? RowVersion { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public TipoListaPrecio Tipo { get; set; }
    public string TipoDisplay { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public decimal? MargenPorcentaje { get; set; }
    public decimal? RecargoPorcentaje { get; set; }
    public decimal? MargenMinimoPorcentaje { get; set; }
    public int? CantidadCuotas { get; set; }
    public string? ReglaRedondeo { get; set; }
    public string? ReglasJson { get; set; }
    public string? Notas { get; set; }
    public bool Activa { get; set; }
    public bool EsPredeterminada { get; set; }
    public int CantidadProductos { get; set; }
}

/// <summary>
/// ViewModel para crear lista de precios
/// </summary>
public class CrearListaPrecioViewModel
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(100, ErrorMessage = "El nombre no puede exceder 100 caracteres")]
    [Display(Name = "Nombre de la Lista")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El código es requerido")]
    [StringLength(20, ErrorMessage = "El código no puede exceder 20 caracteres")]
    [Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El tipo es requerido")]
    [Display(Name = "Tipo de Lista")]
    public TipoListaPrecio Tipo { get; set; }

    [StringLength(500)]
    [Display(Name = "Descripción")]
    public string? Descripcion { get; set; }

    [Range(0, 999.99)]
    [Display(Name = "Margen % sobre Costo")]
    public decimal? MargenPorcentaje { get; set; }

    [Range(0, 999.99)]
    [Display(Name = "Recargo % Adicional")]
    public decimal? RecargoPorcentaje { get; set; }

    [Range(0, 999.99)]
    [Display(Name = "Margen Mínimo %")]
    public decimal? MargenMinimoPorcentaje { get; set; }

    [Range(1, 60)]
    [Display(Name = "Cantidad de Cuotas")]
    public int? CantidadCuotas { get; set; }

    [StringLength(20, ErrorMessage = "La regla de redondeo no puede exceder 20 caracteres")]
    [Display(Name = "Regla de Redondeo")]
    public string? ReglaRedondeo { get; set; }

    [Display(Name = "Reglas JSON")]
    public string? ReglasJson { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notas")]
    public string? Notas { get; set; }

    [Display(Name = "Activa")]
    public bool Activa { get; set; } = true;

    [Display(Name = "Predeterminada")]
    public bool EsPredeterminada { get; set; } = false;
}

/// <summary>
/// ViewModel para editar lista de precios
/// </summary>
public class EditarListaPrecioViewModel : CrearListaPrecioViewModel
{
    [Required]
    public int Id { get; set; }

    [Required]
    public byte[] RowVersion { get; set; } = default!;
}

/// <summary>
/// ViewModel para mostrar precio de un producto
/// </summary>
public class ProductoPrecioViewModel
{
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public string ListaNombre { get; set; } = string.Empty;
    public decimal Costo { get; set; }
    public decimal Precio { get; set; }
    public decimal MargenPorcentaje { get; set; }
    public decimal MargenValor { get; set; }
    public DateTime VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public bool EsVigente { get; set; }
    public bool EsManual { get; set; }
}

/// <summary>
/// ViewModel para el historial de precios de un producto
/// </summary>
public class HistorialPreciosViewModel
{
    public int ProductoId { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public int? ListaId { get; set; }
    public List<PrecioHistorialItemViewModel> Precios { get; set; } = new();
}

/// <summary>
/// ViewModel para item de historial de precio
/// </summary>
public class PrecioHistorialItemViewModel
{
    public int ListaId { get; set; }
    public string ListaNombre { get; set; } = string.Empty;
    public DateTime VigenciaDesde { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public decimal Costo { get; set; }
    public decimal Precio { get; set; }
    public decimal MargenPorcentaje { get; set; }
    public bool EsManual { get; set; }
    public bool EsVigente { get; set; }
    public string? CreadoPor { get; set; }
    public string? Notas { get; set; }
}

/// <summary>
/// ViewModel para simular cambio masivo de precios
/// </summary>
public class SimularCambioMasivoViewModel
{
    [Required(ErrorMessage = "El nombre es requerido")]
    [StringLength(200)]
    [Display(Name = "Nombre del Cambio")]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Tipo de Cambio")]
    public TipoCambio TipoCambio { get; set; }

    [Required]
    [Display(Name = "Tipo de Aplicación")]
    public TipoAplicacion TipoAplicacion { get; set; }

    [Required]
    [Display(Name = "Valor del Cambio")]
    public decimal ValorCambio { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Debe seleccionar al menos una lista")]
    [Display(Name = "Listas Afectadas")]
    public List<int> ListasIds { get; set; } = new();

    [Display(Name = "Categorías Específicas")]
    public List<int>? CategoriasIds { get; set; }

    [Display(Name = "Marcas Específicas")]
    public List<int>? MarcasIds { get; set; }

    [Display(Name = "Productos Específicos")]
    public List<int>? ProductosIds { get; set; }

    [StringLength(2000)]
    [Display(Name = "IDs de Productos (texto)")]
    public string? ProductoIdsText { get; set; }

    [Display(Name = "Fecha de Vigencia")]
    public DateTime? FechaVigencia { get; set; }

    [StringLength(1000)]
    [Display(Name = "Notas")]
    public string? Notas { get; set; }
}

/// <summary>
/// ViewModel para mostrar simulación de cambio
/// </summary>
public class SimulacionViewModel
{
    public int BatchId { get; set; }
    public byte[]? RowVersion { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public TipoCambio TipoCambio { get; set; }
    public TipoAplicacion TipoAplicacion { get; set; }
    public decimal ValorCambio { get; set; }
    public EstadoBatch Estado { get; set; }
    public int CantidadProductos { get; set; }
    public decimal? PorcentajePromedioCambio { get; set; }
    public string SolicitadoPor { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; }
    public bool RequiereAutorizacion { get; set; }
    public List<SimulacionItemViewModel> Items { get; set; } = new();
    public Dictionary<string, object>? Estadisticas { get; set; }
    public int PaginaActual { get; set; }
    public int TamanioPagina { get; set; }
}

/// <summary>
/// ViewModel para item de simulación
/// </summary>
public class SimulacionItemViewModel
{
    public int Id { get; set; }
    public string ProductoCodigo { get; set; } = string.Empty;
    public string ProductoNombre { get; set; } = string.Empty;
    public int ListaId { get; set; }
    public string ListaNombre { get; set; } = string.Empty;
    public decimal? Costo { get; set; }
    public decimal PrecioAnterior { get; set; }
    public decimal PrecioNuevo { get; set; }
    public decimal DiferenciaValor { get; set; }
    public decimal DiferenciaPorcentaje { get; set; }
    public decimal? MargenAnterior { get; set; }
    public decimal? MargenNuevo { get; set; }
    public bool TieneAdvertencia { get; set; }
    public string? MensajeAdvertencia { get; set; }
}

/// <summary>
/// ViewModel para aprobar/rechazar batch
/// </summary>
public class AutorizarBatchViewModel
{
    public int BatchId { get; set; }
    public byte[] RowVersion { get; set; } = default!;
    public string Nombre { get; set; } = string.Empty;
    public TipoCambio TipoCambio { get; set; }
    public decimal ValorCambio { get; set; }
    public int CantidadProductos { get; set; }
    public decimal? PorcentajePromedioCambio { get; set; }
    public string SolicitadoPor { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; }
}

/// <summary>
/// ViewModel para aplicar batch
/// </summary>
public class AplicarBatchViewModel
{
    public int BatchId { get; set; }
    public byte[] RowVersion { get; set; } = default!;
    public string Nombre { get; set; } = string.Empty;
    public int CantidadProductos { get; set; }
    public string? AprobadoPor { get; set; }
    public DateTime? FechaAprobacion { get; set; }

    [Display(Name = "Fecha de Vigencia")]
    public DateTime? FechaVigencia { get; set; }
}

/// <summary>
/// ViewModel para revertir batch
/// </summary>
public class RevertirBatchViewModel
{
    public int BatchId { get; set; }
    public byte[] RowVersion { get; set; } = default!;
    public string Nombre { get; set; } = string.Empty;
    public int CantidadProductos { get; set; }
    public string? AplicadoPor { get; set; }
    public DateTime? FechaAplicacion { get; set; }
    public DateTime? FechaVigencia { get; set; }

    [Required(ErrorMessage = "Debe indicar el motivo de la reversión")]
    [StringLength(500)]
    [Display(Name = "Motivo de Reversión")]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>
/// ViewModel para un batch individual
/// </summary>
public class BatchViewModel
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public TipoCambio TipoCambio { get; set; }
    public string TipoCambioDisplay { get; set; } = string.Empty;
    public TipoAplicacion TipoAplicacion { get; set; }
    public string TipoAplicacionDisplay { get; set; } = string.Empty;
    public decimal ValorCambio { get; set; }
    public EstadoBatch Estado { get; set; }
    public string EstadoDisplay { get; set; } = string.Empty;
    public int CantidadProductos { get; set; }
    public decimal? PorcentajePromedioCambio { get; set; }
    public string SolicitadoPor { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; }
    public string? AprobadoPor { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public string? AplicadoPor { get; set; }
    public DateTime? FechaAplicacion { get; set; }
    public bool RequiereAutorizacion { get; set; }
}

/// <summary>
/// ViewModel para lista de batches
/// </summary>
public class BatchListViewModel
{
    public List<BatchViewModel> Batches { get; set; } = new();
    public EstadoBatch? EstadoFiltro { get; set; }
    public int PaginaActual { get; set; } = 1;
    public int TamanioPagina { get; set; } = 20;
    public int TotalItems { get; set; }
}

/// <summary>
/// ViewModel para simular cambio de precios desde el Catálogo (modal de selección rápida)
/// Soporta dos modos:
/// 1. Seleccionados: ProductoIdsText con IDs específicos
/// 2. Filtrados: FiltrosJson con los filtros actuales del catálogo
/// </summary>
public class SimularDesdeCatalogoViewModel
{
    /// <summary>
    /// IDs de productos seleccionados separados por coma (modo Seleccionados)
    /// </summary>
    public string? ProductoIdsText { get; set; }

    /// <summary>
    /// JSON con los filtros actuales del catálogo (modo Filtrados)
    /// </summary>
    public string? FiltrosJson { get; set; }

    /// <summary>
    /// Alcance del cambio: "seleccionados" o "filtrados"
    /// </summary>
    public string Alcance { get; set; } = "seleccionados";

    /// <summary>
    /// Tipo de cambio: Porcentual o Fijo
    /// </summary>
    [Required]
    public string TipoCambio { get; set; } = "Porcentual";

    /// <summary>
    /// Valor del cambio (positivo = aumento, negativo = disminución)
    /// El signo se ajusta desde el frontend según la dirección seleccionada
    /// </summary>
    [Required(ErrorMessage = "El valor es requerido")]
    public decimal ValorInput { get; set; }

    /// <summary>
    /// IDs de listas de precios a afectar
    /// </summary>
    [Required(ErrorMessage = "Debe seleccionar al menos una lista de precios")]
    [MinLength(1, ErrorMessage = "Debe seleccionar al menos una lista de precios")]
    public List<int> ListasPrecioIds { get; set; } = new();

    /// <summary>
    /// Nota o justificación del cambio
    /// </summary>
    [StringLength(1000)]
    public string? Nota { get; set; }

    /// <summary>
    /// Indica si la solicitud viene desde el catálogo
    /// </summary>
    public bool OrigenCatalogo { get; set; } = true;

    /// <summary>
    /// Verifica si hay productos o filtros válidos para procesar
    /// </summary>
    public bool TieneDatosParaProcesar => 
        !string.IsNullOrWhiteSpace(ProductoIdsText) || 
        (!string.IsNullOrWhiteSpace(FiltrosJson) && Alcance == "filtrados");
}

/// <summary>
/// Request para el endpoint AJAX SimularCambioRapido.
/// Soporta dos modos: "seleccionados" (con ProductoIds) o "filtrados" (con filtros del catálogo).
/// </summary>
public class SimularCambioRapidoRequest
{
    /// <summary>
    /// Modo de operación: "seleccionados" o "filtrados"
    /// </summary>
    public string Modo { get; set; } = "seleccionados";

    /// <summary>
    /// Porcentaje de cambio (positivo = aumento, negativo = descuento)
    /// </summary>
    public decimal Porcentaje { get; set; }

    /// <summary>
    /// IDs de productos cuando modo = "seleccionados"
    /// </summary>
    public List<int>? ProductoIds { get; set; }

    /// <summary>
    /// IDs de listas de precios a afectar. Si está vacío, se usa la predeterminada
    /// </summary>
    public List<int>? ListasPrecioIds { get; set; }

    // ========================================
    // Filtros (cuando modo = "filtrados")
    // ========================================

    /// <summary>
    /// Filtrar por categoría
    /// </summary>
    public int? CategoriaId { get; set; }

    /// <summary>
    /// Filtrar por marca
    /// </summary>
    public int? MarcaId { get; set; }

    /// <summary>
    /// Buscar por texto en código/nombre/descripción
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Solo productos activos
    /// </summary>
    public bool? SoloActivos { get; set; }

    /// <summary>
    /// Solo productos con stock bajo
    /// </summary>
    public bool? StockBajo { get; set; }
}

/// <summary>
/// Request para aplicar cambio de precios directamente desde el Catálogo (AJAX)
/// </summary>
public class AplicarRapidoRequest
{
    /// <summary>
    /// Modo de operación: "seleccionados" o "filtrados"
    /// </summary>
    public string Modo { get; set; } = "seleccionados";

    /// <summary>
    /// Porcentaje de cambio (positivo = aumento, negativo = descuento)
    /// </summary>
    public decimal Porcentaje { get; set; }

    /// <summary>
    /// IDs de productos (modo seleccionados)
    /// </summary>
    public List<int>? ProductoIds { get; set; }

    /// <summary>
    /// IDs de listas de precios a afectar
    /// </summary>
    public List<int>? ListasPrecioIds { get; set; }

    /// <summary>
    /// Filtros del catálogo (modo filtrados)
    /// </summary>
    public FiltrosRapidoDto? Filtros { get; set; }
}

/// <summary>
/// DTO para filtros del catálogo en AplicarRapido
/// </summary>
public class FiltrosRapidoDto
{
    public int? CategoriaId { get; set; }
    public int? MarcaId { get; set; }
    public string? Busqueda { get; set; }
    public bool? SoloActivos { get; set; }
    public bool? StockBajo { get; set; }
    public int? ListaPrecioId { get; set; }
}

/// <summary>
/// Request para revertir un batch via AJAX
/// </summary>
public class RevertirApiRequest
{
    /// <summary>
    /// ID del batch a revertir
    /// </summary>
    public int BatchId { get; set; }

    /// <summary>
    /// RowVersion en Base64 para control de concurrencia
    /// </summary>
    public string RowVersion { get; set; } = string.Empty;

    /// <summary>
    /// Motivo de la reversión (requerido)
    /// </summary>
    [Required(ErrorMessage = "Debe indicar el motivo de la reversión")]
    [StringLength(500)]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>
/// Request para POST /CambiosPrecios/Revertir/{id}
/// </summary>
public class RevertirPostRequest
{
    /// <summary>
    /// Motivo de la reversión (requerido)
    /// </summary>
    [Required(ErrorMessage = "Debe indicar el motivo de la reversión")]
    [StringLength(500)]
    public string Motivo { get; set; } = string.Empty;

    /// <summary>
    /// RowVersion en Base64 para control de concurrencia (opcional)
    /// Si no se proporciona, se usa el rowVersion actual del batch
    /// </summary>
    public string? RowVersion { get; set; }
}