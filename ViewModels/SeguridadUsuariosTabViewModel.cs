namespace TheBuryProject.ViewModels;

public class SeguridadUsuariosTabViewModel
{
    public List<UsuarioViewModel> Usuarios { get; set; } = new();
    public bool MostrarInactivos { get; set; }
    public List<string> AllRoles { get; set; } = new();
    public List<SucursalOptionViewModel> AllSucursales { get; set; } = new();
}
