using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Servicio para operaciones compuestas sobre usuarios del sistema.
/// </summary>
public interface IUsuarioService
{
    /// <summary>
    /// Actualiza un usuario (datos + roles) en una sola transacción.
    /// Las validaciones de unicidad (username, email) y existencia de roles
    /// deben hacerse ANTES de llamar a este método.
    /// </summary>
    Task<UsuarioUpdateResult> UpdateUsuarioAsync(UsuarioUpdateRequest request);
}

/// <summary>
/// Datos necesarios para actualizar un usuario.
/// </summary>
public sealed record UsuarioUpdateRequest
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string Email { get; init; }
    public string? Nombre { get; init; }
    public string? Apellido { get; init; }
    public string? Telefono { get; init; }
    public int? SucursalId { get; init; }
    public string? SucursalNombre { get; init; }
    public bool Activo { get; init; }
    public required List<string> RolesDeseados { get; init; }
    public required byte[] RowVersion { get; init; }
    public string? EditadoPor { get; init; }
}

/// <summary>
/// Resultado de la operación de actualización de usuario.
/// </summary>
public sealed class UsuarioUpdateResult
{
    public bool Ok { get; init; }
    public bool ConcurrencyConflict { get; init; }
    public List<string> Errors { get; init; } = [];

    public static UsuarioUpdateResult Success() => new() { Ok = true };
    public static UsuarioUpdateResult Conflict() => new() { ConcurrencyConflict = true, Errors = ["El usuario fue modificado por otro usuario. Recargá los datos antes de guardar nuevamente."] };
    public static UsuarioUpdateResult Failed(params string[] errors) => new() { Errors = [.. errors] };
    public static UsuarioUpdateResult Failed(IEnumerable<string> errors) => new() { Errors = [.. errors] };
}
