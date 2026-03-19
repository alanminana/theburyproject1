using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Representa un m�dulo del sistema con sus acciones disponibles
/// </summary>
public class ModuloSistema  : AuditableEntity
{
    /// <summary>
    /// Nombre del m�dulo (Productos, Ventas, Clientes, etc.)
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Clave �nica del m�dulo (productos, ventas, clientes)
    /// Usar min�sculas sin espacios
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Clave { get; set; } = string.Empty;

    /// <summary>
    /// Descripci�n del m�dulo
    /// </summary>
    [StringLength(500)]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Icono del m�dulo (Bootstrap Icons)
    /// Ejemplo: "bi-box-seam", "bi-cart"
    /// </summary>
    [StringLength(50)]
    public string? Icono { get; set; }

    /// <summary>
    /// Orden de visualizaci�n en el men�
    /// </summary>
    public int Orden { get; set; }

    /// <summary>
    /// Indica si el m�dulo est� activo
    /// </summary>
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Categor�a del m�dulo (Cat�logo, Ventas, Compras, Configuraci�n, etc.)
    /// </summary>
    [StringLength(50)]
    public string? Categoria { get; set; }

    // Navegaci�n
    public virtual ICollection<AccionModulo> Acciones { get; set; } = new List<AccionModulo>();
    public virtual ICollection<RolPermiso> Permisos { get; set; } = new List<RolPermiso>();
}