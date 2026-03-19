using System.ComponentModel.DataAnnotations;
using TheBuryProject.Models.Base;

namespace TheBuryProject.Models.Entities;

/// <summary>
/// Representa una acci�n disponible en un m�dulo del sistema
/// </summary>
public class AccionModulo  : AuditableEntity
{
    /// <summary>
    /// ID del m�dulo al que pertenece esta acci�n
    /// </summary>
    [Required]
    public int ModuloId { get; set; }

    /// <summary>
    /// Nombre de la acci�n (Ver, Crear, Editar, Eliminar, etc.)
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>
    /// Clave �nica de la acci�n (view, create, update, delete, authorize, etc.)
    /// Usar min�sculas sin espacios
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Clave { get; set; } = string.Empty;

    /// <summary>
    /// Descripci�n de la acci�n
    /// </summary>
    [StringLength(500)]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Icono de la acci�n (Bootstrap Icons)
    /// </summary>
    [StringLength(50)]
    public string? Icono { get; set; }

    /// <summary>
    /// Orden de visualizaci�n
    /// </summary>
    public int Orden { get; set; }

    /// <summary>
    /// Indica si la acci�n est� activa
    /// </summary>
    public bool Activa { get; set; } = true;

    // Navegaci�n
    public virtual ModuloSistema Modulo { get; set; } = null!;
    public virtual ICollection<RolPermiso> Permisos { get; set; } = new List<RolPermiso>();
}