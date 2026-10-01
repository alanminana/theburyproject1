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

    /// <summary>Roles tildados en el selector de comparación (se conservan aunque haya menos de 2).</summary>
    public List<string> RolesComparadosIds { get; set; } = new();
    public bool SoloDiferencias { get; set; } = true;

    /// <summary>Solo se completa cuando hay al menos 2 roles a comparar.</summary>
    public SeguridadComparacionRolesViewModel? Comparacion { get; set; }

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

public class SeguridadComparacionRolesViewModel
{
    /// <summary>Roles comparados; el orden define las columnas de la matriz.</summary>
    public List<SeguridadComparacionRolColumnaViewModel> Roles { get; set; } = new();
    public List<SeguridadComparacionGrupoViewModel> Grupos { get; set; } = new();

    /// <summary>Acciones en las que los roles no coinciden (sobre el total, sin filtrar).</summary>
    public int TotalDiferencias { get; set; }
    public int TotalAcciones { get; set; }
}

public class SeguridadComparacionRolColumnaViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public int TotalPermisos { get; set; }
}

public class SeguridadComparacionGrupoViewModel
{
    public string Nombre { get; set; } = string.Empty;
    public List<SeguridadComparacionAccionFilaViewModel> Filas { get; set; } = new();
}

public class SeguridadComparacionAccionFilaViewModel
{
    public string ModuloNombre { get; set; } = string.Empty;
    public string ModuloClave { get; set; } = string.Empty;
    public string AccionNombre { get; set; } = string.Empty;
    public string AccionClave { get; set; } = string.Empty;

    /// <summary>Una entrada por rol, en el mismo orden que <see cref="SeguridadComparacionRolesViewModel.Roles"/>.</summary>
    public List<bool> TienePermiso { get; set; } = new();

    public bool EsDiferente => TienePermiso.Distinct().Count() > 1;
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
