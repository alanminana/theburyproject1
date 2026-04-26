namespace TheBuryProject.Models.Constants;

/// <summary>
/// Constantes para el módulo de tickets internos del ERP.
/// </summary>
public static class TicketConstants
{
    public const string Modulo = "tickets";
    public const string UploadFolder = "uploads/tickets";

    /// <summary>Tamaño máximo de adjunto: 10 MB.</summary>
    public const long MaxAdjuntoBytes = 10 * 1024 * 1024;

    /// <summary>Extensiones permitidas para adjuntos.</summary>
    public static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".zip"
    };

    public static class Acciones
    {
        public const string Ver = "view";
        public const string Crear = "create";
        public const string Editar = "update";
        public const string CambiarEstado = "changestatus";
        public const string Resolver = "resolve";
        public const string Eliminar = "delete";

        public static string[] Todas => new[]
            { Ver, Crear, Editar, CambiarEstado, Resolver, Eliminar };
    }
}
