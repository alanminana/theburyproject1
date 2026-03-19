using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Representa un lote de cambios de precios masivos
/// Soporta workflow: Simulación → Autorización → Aplicación → Reversión
/// </summary>
public class PriceChangeBatch  : AuditableEntity
{
    /// <summary>
    /// Nombre descriptivo del batch
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de cambio que se aplicará
    /// </summary>
    [Required]
    public TipoCambio TipoCambio { get; set; }

    /// <summary>
    /// Tipo de aplicación del cambio
    /// </summary>
    [Required]
    public TipoAplicacion TipoAplicacion { get; set; }

    /// <summary>
    /// Valor del cambio (porcentaje o valor absoluto)
    /// Ejemplo: 15.0 para +15%, -10.0 para -10%, 5000 para +$5000
    /// </summary>
    [Required]
    public decimal ValorCambio { get; set; }

    /// <summary>
    /// Alcance del cambio en formato JSON
    /// Ejemplo: {"categorias": [1,2,3], "marcas": [5], "productos": null}
    /// </summary>
    [Required]
    public string AlcanceJson { get; set; } = string.Empty;

    /// <summary>
    /// IDs de listas de precios afectadas
    /// </summary>
    [Required]
    public string ListasAfectadasJson { get; set; } = string.Empty;

    /// <summary>
    /// Estado actual del batch
    /// </summary>
    [Required]
    public EstadoBatch Estado { get; set; } = EstadoBatch.Simulado;

    /// <summary>
    /// Resumen de la simulación en formato JSON
    /// Ejemplo: {"totalProductos": 150, "aumentoPromedio": 15.5, "rangoPrecios": {...}}
    /// </summary>
    public string? SimulacionJson { get; set; }

    /// <summary>
    /// Cantidad de productos afectados
    /// </summary>
    public int CantidadProductos { get; set; }

    /// <summary>
    /// Usuario que solicitó el cambio
    /// </summary>
    [Required]
    [StringLength(50)]
    public string SolicitadoPor { get; set; } = string.Empty;

    /// <summary>
    /// Fecha de solicitud
    /// </summary>
    [Required]
    public DateTime FechaSolicitud { get; set; }

    /// <summary>
    /// Usuario que autorizó el cambio
    /// </summary>
    [StringLength(50)]
    public string? AprobadoPor { get; set; }

    /// <summary>
    /// Fecha de aprobación
    /// </summary>
    public DateTime? FechaAprobacion { get; set; }

    /// <summary>
    /// Usuario que aplicó el cambio
    /// </summary>
    [StringLength(50)]
    public string? AplicadoPor { get; set; }

    /// <summary>
    /// Fecha de aplicación
    /// </summary>
    public DateTime? FechaAplicacion { get; set; }

    /// <summary>
    /// Fecha de vigencia para los nuevos precios
    /// </summary>
    public DateTime? FechaVigencia { get; set; }

    /// <summary>
    /// Usuario que revirtió el cambio
    /// </summary>
    [StringLength(50)]
    public string? RevertidoPor { get; set; }

    /// <summary>
    /// Fecha de reversión
    /// </summary>
    public DateTime? FechaReversion { get; set; }

    /// <summary>
    /// Usuario que canceló el batch
    /// </summary>
    [StringLength(50)]
    public string? CanceladoPor { get; set; }

    /// <summary>
    /// Fecha de cancelación
    /// </summary>
    public DateTime? FechaCancelacion { get; set; }

    /// <summary>
    /// Motivo de rechazo
    /// </summary>
    [StringLength(500)]
    public string? MotivoRechazo { get; set; }

    /// <summary>
    /// Motivo de cancelación
    /// </summary>
    [StringLength(500)]
    public string? MotivoCancelacion { get; set; }

    /// <summary>
    /// Motivo de reversión
    /// </summary>
    [StringLength(500)]
    public string? MotivoReversion { get; set; }

    /// <summary>
    /// Notas adicionales sobre el batch
    /// </summary>
    [StringLength(1000)]
    public string? Notas { get; set; }

    /// <summary>
    /// Indica si este cambio requiere autorización
    /// </summary>
    public bool RequiereAutorizacion { get; set; }

    /// <summary>
    /// ID del batch padre (para reversiones)
    /// </summary>
    public int? BatchPadreId { get; set; }

    /// <summary>
    /// Batch padre - usado cuando este batch es una reversión
    /// </summary>
    public virtual PriceChangeBatch? BatchPadre { get; set; }

    /// <summary>
    /// Batch de reversión - si este batch fue revertido, referencia al batch de reversión
    /// </summary>
    public virtual PriceChangeBatch? BatchReversion { get; set; }

    /// <summary>
    /// Porcentaje promedio de cambio (para estadísticas)
    /// </summary>
    public decimal? PorcentajePromedioCambio { get; set; }

    // Navegación
    public virtual ICollection<PriceChangeItem> Items { get; set; } = new List<PriceChangeItem>();
}