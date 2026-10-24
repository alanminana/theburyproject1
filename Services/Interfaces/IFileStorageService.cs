namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Abstracción de almacenamiento de archivos.
/// Desacopla la lógica de negocio del mecanismo concreto (disco, blob, etc.).
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Persiste el archivo y devuelve la ruta relativa desde wwwroot.
    /// </summary>
    Task<string> SaveAsync(IFormFile archivo, string subFolder);

    /// <summary>
    /// Elimina el archivo indicado por su ruta relativa desde wwwroot.
    /// No lanza excepción si el archivo no existe.
    /// </summary>
    Task DeleteAsync(string rutaRelativa);

    /// <summary>
    /// Abre para lectura el archivo indicado por su ruta relativa desde wwwroot, solo si resuelve
    /// dentro de <paramref name="subFolder"/>. Devuelve null si no existe, la ruta es inválida o
    /// intenta salir de la carpeta autorizada (traversal, rutas absolutas).
    /// </summary>
    Stream? OpenRead(string rutaRelativa, string subFolder);
}
