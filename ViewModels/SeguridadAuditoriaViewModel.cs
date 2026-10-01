namespace TheBuryProject.ViewModels;

public class SeguridadAuditoriaViewModel
{
    public string? UsuarioSeleccionado { get; set; }
    public string? ModuloSeleccionado { get; set; }
    public string? AccionSeleccionada { get; set; }
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    public const int TamanoPaginaDefault = 25;

    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = TamanoPaginaDefault;

    /// <summary>Total de eventos que cumplen los filtros (todas las páginas).</summary>
    public int TotalRegistros { get; set; }

    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina));
    public int PrimerRegistro => Registros.Count == 0 ? 0 : (Pagina - 1) * TamanoPagina + 1;
    public int UltimoRegistro => Registros.Count == 0 ? 0 : PrimerRegistro + Registros.Count - 1;

    public List<string> Usuarios { get; set; } = new();
    public List<string> Modulos { get; set; } = new();
    public List<string> Acciones { get; set; } = new();
    public List<RegistroAuditoriaViewModel> Registros { get; set; } = new();
}

public class RegistroAuditoriaViewModel
{
    public DateTime FechaHora { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string Modulo { get; set; } = string.Empty;
    public string Entidad { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}
