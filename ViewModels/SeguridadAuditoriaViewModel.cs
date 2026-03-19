namespace TheBuryProject.ViewModels;

public class SeguridadAuditoriaViewModel
{
    public string? UsuarioSeleccionado { get; set; }
    public string? ModuloSeleccionado { get; set; }
    public string? AccionSeleccionada { get; set; }
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

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
