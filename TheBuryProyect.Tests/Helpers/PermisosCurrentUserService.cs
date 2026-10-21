using TheBuryProject.Models.Constants;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Helpers;

/// <summary>
/// ICurrentUserService de test con permisos "modulo.accion" mutables: permite simular un usuario con o
/// sin ventas.discount y retirarle el permiso a mitad de la "sesión" (el servicio real lee los claims
/// del request actual, que PermissionClaimsTransformation reconstruye en cada request).
/// </summary>
internal sealed class PermisosCurrentUserService : ICurrentUserService
{
    private readonly HashSet<string> _permisos;

    public PermisosCurrentUserService(params string[] permisos)
    {
        _permisos = new HashSet<string>(permisos.Select(p => p.ToLowerInvariant()), StringComparer.Ordinal);
    }

    public static PermisosCurrentUserService ConDescuentos() =>
        new(VentaConstants.PermisoDescuento.Claim);

    public static PermisosCurrentUserService SinDescuentos() => new();

    public void Conceder(string permiso) => _permisos.Add(permiso.ToLowerInvariant());

    public void Retirar(string permiso) => _permisos.Remove(permiso.ToLowerInvariant());

    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) =>
        _permisos.Contains($"{modulo}.{accion}".ToLowerInvariant());
    public string? GetIpAddress() => null;
}
