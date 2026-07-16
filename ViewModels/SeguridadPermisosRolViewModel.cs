using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;

public class SeguridadPermisosRolViewModel
{
    public string? RolSeleccionadoId { get; set; }
    public string? RolSeleccionadoNombre { get; set; }
    public string? BuscarModulo { get; set; }
    public string? GrupoSeleccionado { get; set; }
    public List<SeguridadRolSelectorItemViewModel> Roles { get; set; } = new();
    public List<string> Grupos { get; set; } = new();

    /// <summary>
    /// Permisos agrupados por categoría; cada módulo expone únicamente las
    /// acciones que realmente posee (con su nombre en español).
    /// </summary>
    public List<SeguridadPermisosRolGrupoViewModel> GruposModulos { get; set; } = new();

    public bool TieneRolSeleccionado => !string.IsNullOrWhiteSpace(RolSeleccionadoId);

    public int TotalModulos => GruposModulos.Sum(g => g.Modulos.Count);

    public int TotalAccionesSeleccionadas =>
        GruposModulos.Sum(g => g.Modulos.Sum(m => m.Acciones.Count(a => a.Seleccionado)));
}

public class SeguridadRolSelectorItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public class SeguridadPermisosRolGrupoViewModel
{
    public string Nombre { get; set; } = string.Empty;
    public List<SeguridadPermisosRolModuloViewModel> Modulos { get; set; } = new();

    public int TotalAcciones => Modulos.Sum(m => m.Acciones.Count);

    public int TotalSeleccionadas => Modulos.Sum(m => m.Acciones.Count(a => a.Seleccionado));
}

public class SeguridadPermisosRolModuloViewModel
{
    public int ModuloId { get; set; }
    public string ModuloNombre { get; set; } = string.Empty;
    public string ModuloClave { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public List<SeguridadPermisosRolAccionViewModel> Acciones { get; set; } = new();
}

public class SeguridadPermisosRolAccionViewModel
{
    public int AccionId { get; set; }
    public string AccionClave { get; set; } = string.Empty;
    public string AccionNombre { get; set; } = string.Empty;
    public bool Seleccionado { get; set; }
}

public class CopiarPermisosRolViewModel
{
    [Required]
    public string RolDestinoId { get; set; } = string.Empty;

    public string RolDestinoNombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "Debés seleccionar un rol origen.")]
    public string RolOrigenId { get; set; } = string.Empty;

    public List<SeguridadRolSelectorItemViewModel> RolesDisponibles { get; set; } = new();
}
