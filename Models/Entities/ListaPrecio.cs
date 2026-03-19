using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Representa una lista de precios del sistema
/// Ejemplos: "Contado", "Tarjeta 3 cuotas", "Mayorista"
/// </summary>
public class ListaPrecio  : AuditableEntity
{
    /// <summary>
    /// Nombre de la lista de precios
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// C�digo �nico de la lista
    /// </summary>
    [Required]
    [StringLength(20)]
    public string Codigo { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de lista de precios
    /// </summary>
    [Required]
    public TipoListaPrecio Tipo { get; set; }

    /// <summary>
    /// Descripci�n de la lista y sus reglas
    /// </summary>
    [StringLength(500)]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Porcentaje de margen sobre costo (para c�lculo autom�tico)
    /// </summary>
    public decimal? MargenPorcentaje { get; set; }

    /// <summary>
    /// Margen m�nimo permitido en porcentaje (validaci�n)
    /// </summary>
    public decimal? MargenMinimoPorcentaje { get; set; }

    /// <summary>
    /// Recargo adicional sobre precio base (ej: 10% para tarjeta)
    /// </summary>
    public decimal? RecargoPorcentaje { get; set; }

    /// <summary>
    /// Cantidad de cuotas (para listas de tarjeta)
    /// </summary>
    public int? CantidadCuotas { get; set; }

    /// <summary>
    /// Regla de redondeo: "ninguno", "decena", "centena", "unidad"
    /// </summary>
    [StringLength(20)]
    public string? ReglaRedondeo { get; set; }

    /// <summary>
    /// Indica si esta lista est� activa
    /// </summary>
    public bool Activa { get; set; } = true;

    /// <summary>
    /// Indica si es la lista predeterminada del sistema
    /// </summary>
    public bool EsPredeterminada { get; set; } = false;

    /// <summary>
    /// Orden de visualizaci�n en el sistema
    /// </summary>
    public int Orden { get; set; }

    /// <summary>
    /// Reglas adicionales en formato JSON (flexible para futuras extensiones)
    /// Ejemplo: {"redondeo": "centena", "margenMinimo": 25}
    /// </summary>
    public string? ReglasJson { get; set; }

    /// <summary>
    /// Notas o comentarios adicionales sobre la lista
    /// </summary>
    [StringLength(1000)]
    public string? Notas { get; set; }

    // Navegaci�n
    public virtual ICollection<ProductoPrecioLista> Precios { get; set; } = new List<ProductoPrecioLista>();
}