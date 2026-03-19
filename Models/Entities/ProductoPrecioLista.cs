using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Representa el precio de un producto en una lista espec�fica con vigencia
/// Esta tabla mantiene el historial completo de precios (append-only)
/// Clave compuesta: ProductoId + ListaId + VigenciaDesde
/// </summary>
public class ProductoPrecioLista  : AuditableEntity
{
    /// <summary>
    /// ID del producto
    /// </summary>
    [Required]
    public int ProductoId { get; set; }

    /// <summary>
    /// ID de la lista de precios
    /// </summary>
    [Required]
    public int ListaId { get; set; }

    /// <summary>
    /// Fecha desde la cual este precio es v�lido
    /// Permite tener historial completo de cambios
    /// </summary>
    [Required]
    public DateTime VigenciaDesde { get; set; }

    /// <summary>
    /// Fecha hasta la cual este precio es v�lido (null = vigente)
    /// Se calcula autom�ticamente al insertar un nuevo registro
    /// </summary>
    public DateTime? VigenciaHasta { get; set; }

    /// <summary>
    /// Costo del producto en el momento de este precio
    /// </summary>
    [Required]
    public decimal Costo { get; set; }

    /// <summary>
    /// Precio de venta calculado o asignado
    /// </summary>
    [Required]
    public decimal Precio { get; set; }

    /// <summary>
    /// Margen de ganancia en porcentaje
    /// </summary>
    public decimal MargenPorcentaje { get; set; }

    /// <summary>
    /// Margen de ganancia en valor absoluto
    /// </summary>
    public decimal MargenValor { get; set; }

    /// <summary>
    /// Indica si este precio fue calculado autom�ticamente o es manual
    /// </summary>
    public bool EsManual { get; set; } = false;

    /// <summary>
    /// ID del batch de cambio que gener� este precio (si aplica)
    /// </summary>
    public int? BatchId { get; set; }

    /// <summary>
    /// Usuario que cre� o autoriz� este precio
    /// </summary>
    [StringLength(50)]
    public string? CreadoPor { get; set; }

    /// <summary>
    /// Notas o justificaci�n del cambio de precio
    /// </summary>
    [StringLength(500)]
    public string? Notas { get; set; }

    /// <summary>
    /// Indica si este es el precio actualmente vigente
    /// </summary>
    public bool EsVigente { get; set; } = true;

    // Navegaci�n
    public virtual Producto Producto { get; set; } = null!;
    public virtual ListaPrecio Lista { get; set; } = null!;
    public virtual PriceChangeBatch? Batch { get; set; }
}