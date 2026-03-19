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
    public List<SeguridadPermisosRolColumnViewModel> Columnas { get; set; } = new();
    public List<SeguridadPermisosRolRowViewModel> Filas { get; set; } = new();

    public bool TieneRolSeleccionado => !string.IsNullOrWhiteSpace(RolSeleccionadoId);
}

public class SeguridadRolSelectorItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}

public class SeguridadPermisosRolColumnViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
}

public class SeguridadPermisosRolRowViewModel
{
    public int ModuloId { get; set; }
    public string ModuloNombre { get; set; } = string.Empty;
    public string ModuloClave { get; set; } = string.Empty;
    public string Grupo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public List<SeguridadPermisosRolCellViewModel> Celdas { get; set; } = new();
}

public class SeguridadPermisosRolCellViewModel
{
    public string ColumnKey { get; set; } = string.Empty;
    public int? AccionId { get; set; }
    public string AccionClave { get; set; } = string.Empty;
    public string AccionNombre { get; set; } = string.Empty;
    public bool Disponible { get; set; }
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
