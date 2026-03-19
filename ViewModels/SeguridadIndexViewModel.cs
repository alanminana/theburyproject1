namespace TheBuryProject.ViewModels;

public class SeguridadIndexViewModel
{
    public string ActiveTab { get; set; } = "usuarios";
    public int UsuariosActivos { get; set; }
    public int UsuariosBloqueados { get; set; }
    public int RolesActivos { get; set; }
    public int PermisosAsignados { get; set; }
    public SeguridadUsuariosTabViewModel? UsuariosTab { get; set; }
    public SeguridadRolesTabViewModel? RolesTab { get; set; }
    public SeguridadPermisosRolViewModel? PermisosRolTab { get; set; }
    public SeguridadAuditoriaViewModel? AuditoriaTab { get; set; }
}
