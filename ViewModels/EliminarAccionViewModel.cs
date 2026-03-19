using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;



/// <summary>
/// ViewModel para eliminar una acción
/// </summary>
public class EliminarAccionViewModel
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string ModuloNombre { get; set; } = string.Empty;
}