using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Representa el detalle de un cambio de precio individual dentro de un batch
/// Mantiene el estado antes/despu�s para auditor�a y posible reversi�n
/// </summary>
public class PriceChangeItem  : AuditableEntity
{
    /// <summary>
    /// ID del batch al que pertenece este item
    /// </summary>
    [Required]
    public int BatchId { get; set; }

    /// <summary>
    /// ID del producto afectado
    /// </summary>
    [Required]
    public int ProductoId { get; set; }

    /// <summary>
    /// ID de la lista de precios afectada
    /// </summary>
    [Required]
    public int ListaId { get; set; }

    /// <summary>
    /// C�digo del producto (desnormalizado para reporting)
    /// </summary>
    [Required]
    [StringLength(50)]
    public string ProductoCodigo { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del producto (desnormalizado para reporting)
    /// </summary>
    [Required]
    [StringLength(200)]
    public string ProductoNombre { get; set; } = string.Empty;

    /// <summary>
    /// Precio anterior (antes del cambio)
    /// </summary>
    [Required]
    public decimal PrecioAnterior { get; set; }

    /// <summary>
    /// Precio nuevo (despu�s del cambio)
    /// </summary>
    [Required]
    public decimal PrecioNuevo { get; set; }

    /// <summary>
    /// Diferencia en valor absoluto
    /// </summary>
    [Required]
    public decimal DiferenciaValor { get; set; }

    /// <summary>
    /// Diferencia en porcentaje
    /// </summary>
    [Required]
    public decimal DiferenciaPorcentaje { get; set; }

    /// <summary>
    /// Costo del producto en el momento del cambio
    /// </summary>
    public decimal? Costo { get; set; }

    /// <summary>
    /// Margen anterior en porcentaje
    /// </summary>
    public decimal? MargenAnterior { get; set; }

    /// <summary>
    /// Margen nuevo en porcentaje
    /// </summary>
    public decimal? MargenNuevo { get; set; }

    /// <summary>
    /// Indica si hubo alguna advertencia en este item
    /// Ejemplo: margen por debajo del m�nimo, precio negativo, etc.
    /// </summary>
    public bool TieneAdvertencia { get; set; } = false;

    /// <summary>
    /// Mensaje de advertencia si existe
    /// </summary>
    [StringLength(500)]
    public string? MensajeAdvertencia { get; set; }

    /// <summary>
    /// Indica si este item fue aplicado exitosamente
    /// </summary>
    public bool Aplicado { get; set; } = false;

    /// <summary>
    /// Indica si este item fue revertido
    /// </summary>
    public bool Revertido { get; set; } = false;

    // Navegaci�n
    public virtual PriceChangeBatch Batch { get; set; } = null!;
    public virtual Producto Producto { get; set; } = null!;
    public virtual ListaPrecio Lista { get; set; } = null!;
}