namespace TheBuryProject.Services.Interfaces;

public interface ITerminosCondicionesService
{
    /// <summary>
    /// Identificador de la versión vigente de los Términos y Condiciones.
    /// Cambiarlo (junto con el texto en la vista) obliga a todos los usuarios a re-aceptar.
    /// </summary>
    string VersionActual { get; }

    Task<bool> UsuarioAceptoVersionActualAsync(string usuarioId);

    Task RegistrarAceptacionAsync(string usuarioId, string usuarioNombreUsuario, string nombreIngresado);

    /// <summary>
    /// Vía alternativa de aceptación ("desafiar a los dioses"). Idempotente: si el usuario
    /// ya tiene la versión actual aceptada (por esta vía o la normal), no crea un registro
    /// duplicado y devuelve true. Devuelve false únicamente si falla la persistencia.
    /// </summary>
    Task<bool> ActivarDesafioALosDiosesAsync(string usuarioId, string usuarioNombreUsuario);
}
