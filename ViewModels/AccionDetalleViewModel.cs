using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;


/// <summary>
/// ViewModel para mostrar detalles de una acción
/// </summary>
public class AccionDetalleViewModel
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int ModuloId { get; set; }
    public string ModuloNombre { get; set; } = string.Empty;
    public string ModuloClave { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public DateTime CreatedAt { get; set; }
}

