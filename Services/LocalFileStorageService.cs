using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <summary>
/// Implementación de IFileStorageService que almacena archivos en disco bajo wwwroot.
/// Sigue el mismo patrón que DocumentoClienteService pero extraído como abstracción.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(IWebHostEnvironment environment, ILogger<LocalFileStorageService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<string> SaveAsync(IFormFile archivo, string subFolder)
    {
        var uploadPath = Path.Combine(_environment.WebRootPath, subFolder);
        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        var nombreArchivo = $"{Guid.NewGuid()}{extension}";
        var rutaCompleta = Path.Combine(uploadPath, nombreArchivo);

        await using var stream = new FileStream(rutaCompleta, FileMode.Create);
        await archivo.CopyToAsync(stream);

        // Retorna ruta relativa con separadores forward-slash (consistente con DocumentoCliente)
        return $"{subFolder}/{nombreArchivo}".Replace('\\', '/');
    }

    public Stream? OpenRead(string rutaRelativa, string subFolder)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativa) || string.IsNullOrWhiteSpace(subFolder))
            return null;

        try
        {
            // La DB puede traer rutas guardadas con separadores de Windows: ambos son el mismo directorio.
            static string Normalizar(string ruta) =>
                ruta.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

            var carpeta = Path.GetFullPath(Path.Combine(_environment.WebRootPath, Normalizar(subFolder)))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var rutaCompleta = Path.GetFullPath(Path.Combine(_environment.WebRootPath, Normalizar(rutaRelativa)));

            if (!rutaCompleta.StartsWith(carpeta, StringComparison.OrdinalIgnoreCase))
                return null;

            // La contención textual no alcanza: un symlink/junction dentro de la carpeta apuntaría fuera de ella.
            if (!File.Exists(rutaCompleta) || AtraviesaReparsePoint(rutaCompleta, carpeta))
                return null;

            return new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 4096, useAsync: true);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException
                                       or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "No se pudo abrir el archivo en ruta '{Ruta}'.", rutaRelativa);
            return null;
        }
    }

    // true si el archivo o algún directorio entre la carpeta autorizada y el archivo es un enlace (symlink/junction).
    private static bool AtraviesaReparsePoint(string rutaCompleta, string carpetaAutorizada)
    {
        var actual = new FileInfo(rutaCompleta);
        if (actual.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return true;
        var dir = actual.Directory;
        var raiz = carpetaAutorizada.TrimEnd(Path.DirectorySeparatorChar);
        while (dir != null && dir.FullName.Length > raiz.Length)
        {
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return true;
            dir = dir.Parent;
        }
        return false;
    }

    public Task DeleteAsync(string rutaRelativa)
    {
        try
        {
            var rutaCompleta = Path.Combine(_environment.WebRootPath, rutaRelativa.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(rutaCompleta))
                File.Delete(rutaCompleta);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo eliminar el archivo en ruta '{Ruta}'.", rutaRelativa);
        }

        return Task.CompletedTask;
    }
}
