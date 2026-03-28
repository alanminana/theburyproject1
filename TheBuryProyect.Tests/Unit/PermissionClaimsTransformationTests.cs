using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para PermissionClaimsTransformation.
/// Cubren: principal null retorna principal vacío, identity no ClaimsIdentity
/// retorna original, userId vacío retorna original, happy path agrega roles
/// y permisos, idempotencia (doble llamada no duplica claims), sincronización
/// (rol/permiso removido del servicio se quita del principal), normalización
/// (permisos en minúscula/trim), principal sin roles previos funciona.
/// </summary>
public class PermissionClaimsTransformationTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PermissionClaimsTransformation Build(
        List<string>? roles = null,
        List<string>? permisos = null)
    {
        var stub = new StubRolServiceTransformation(
            roles ?? new List<string>(),
            permisos ?? new List<string>());
        return new PermissionClaimsTransformation(stub);
    }

    private static ClaimsPrincipal PrincipalConUserId(string userId = "user-1")
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
            "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    // -------------------------------------------------------------------------
    // Guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transform_PrincipalNull_RetornaPrincipalVacio()
    {
        var sut = Build();

        var result = await sut.TransformAsync(null!);

        Assert.NotNull(result);
        Assert.False(result.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Transform_IdentityNoEsClaimsIdentity_RetornaOriginal()
    {
        var sut = Build();
        // ClaimsPrincipal con IIdentity genérica (no ClaimsIdentity)
        var principal = new ClaimsPrincipal(new System.Security.Principal.GenericIdentity("user"));

        var result = await sut.TransformAsync(principal);

        Assert.Same(principal, result);
    }

    [Fact]
    public async Task Transform_UserIdVacio_RetornaOriginal()
    {
        var sut = Build(roles: new List<string> { "Admin" });
        // Principal sin claim NameIdentifier
        var identity = new ClaimsIdentity(Array.Empty<Claim>(), "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var result = await sut.TransformAsync(principal);

        // No agrega claims
        Assert.Empty(result.FindAll("Permission"));
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transform_AgregaRolesDelServicio()
    {
        var sut = Build(roles: new List<string> { "Admin", "Vendedor" });
        var principal = PrincipalConUserId();

        var result = await sut.TransformAsync(principal);

        Assert.True(result.IsInRole("Admin"));
        Assert.True(result.IsInRole("Vendedor"));
    }

    [Fact]
    public async Task Transform_AgregaPermisosDelServicio()
    {
        var sut = Build(permisos: new List<string> { "ventas.crear", "clientes.ver" });
        var principal = PrincipalConUserId();

        var result = await sut.TransformAsync(principal);

        var permClaims = result.FindAll("Permission").Select(c => c.Value).ToList();
        Assert.Contains("ventas.crear", permClaims);
        Assert.Contains("clientes.ver", permClaims);
    }

    // -------------------------------------------------------------------------
    // Idempotencia
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transform_DobleLlamada_NoDuplicaRoles()
    {
        var sut = Build(roles: new List<string> { "Admin" });
        var principal = PrincipalConUserId();

        var result1 = await sut.TransformAsync(principal);
        var result2 = await sut.TransformAsync(result1);

        var roleClaims = result2.FindAll(ClaimTypes.Role).ToList();
        Assert.Single(roleClaims);
    }

    [Fact]
    public async Task Transform_DobleLlamada_NoDuplicaPermisos()
    {
        var sut = Build(permisos: new List<string> { "ventas.crear" });
        var principal = PrincipalConUserId();

        var result1 = await sut.TransformAsync(principal);
        var result2 = await sut.TransformAsync(result1);

        var permClaims = result2.FindAll("Permission").ToList();
        Assert.Single(permClaims);
    }

    // -------------------------------------------------------------------------
    // Sincronización — quitado del servicio se remueve del principal
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transform_RolEliminadoDelServicio_SeRemoveDelPrincipal()
    {
        // Primero el usuario tiene Admin + Vendedor
        var sut1 = Build(roles: new List<string> { "Admin", "Vendedor" });
        var principal = PrincipalConUserId();
        var resultConAmbos = await sut1.TransformAsync(principal);

        // Ahora Vendedor fue revocado
        var sut2 = Build(roles: new List<string> { "Admin" });
        var resultActualizado = await sut2.TransformAsync(resultConAmbos);

        Assert.True(resultActualizado.IsInRole("Admin"));
        Assert.False(resultActualizado.IsInRole("Vendedor"));
    }

    [Fact]
    public async Task Transform_PermisoEliminadoDelServicio_SeRemoveDelPrincipal()
    {
        var sut1 = Build(permisos: new List<string> { "ventas.crear", "clientes.eliminar" });
        var principal = PrincipalConUserId();
        var resultConAmbos = await sut1.TransformAsync(principal);

        // clientes.eliminar fue revocado
        var sut2 = Build(permisos: new List<string> { "ventas.crear" });
        var resultActualizado = await sut2.TransformAsync(resultConAmbos);

        var permisos = resultActualizado.FindAll("Permission").Select(c => c.Value).ToList();
        Assert.Contains("ventas.crear", permisos);
        Assert.DoesNotContain("clientes.eliminar", permisos);
    }

    // -------------------------------------------------------------------------
    // Normalización de permisos (trim + lowercase)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transform_PermisosConEspaciosYMayusculas_SeNormalizan()
    {
        var sut = Build(permisos: new List<string> { "  Ventas.Crear  ", "CLIENTES.VER" });
        var principal = PrincipalConUserId();

        var result = await sut.TransformAsync(principal);

        var permisos = result.FindAll("Permission").Select(c => c.Value).ToList();
        Assert.Contains("ventas.crear", permisos);
        Assert.Contains("clientes.ver", permisos);
        // No deben existir versiones con mayúsculas
        Assert.DoesNotContain("Ventas.Crear", permisos);
    }

    // -------------------------------------------------------------------------
    // Permisos vacíos o whitespace — se ignoran
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transform_PermisosVaciosOWhitespace_SeIgnoran()
    {
        var sut = Build(permisos: new List<string> { "", "   ", "ventas.ver" });
        var principal = PrincipalConUserId();

        var result = await sut.TransformAsync(principal);

        var permisos = result.FindAll("Permission").Select(c => c.Value).ToList();
        Assert.Single(permisos);
        Assert.Equal("ventas.ver", permisos[0]);
    }

    // -------------------------------------------------------------------------
    // Sin roles ni permisos — no agrega nada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transform_SinRolesNiPermisos_NoAgregaClaims()
    {
        var sut = Build();
        var principal = PrincipalConUserId();

        var result = await sut.TransformAsync(principal);

        Assert.Empty(result.FindAll(ClaimTypes.Role));
        Assert.Empty(result.FindAll("Permission"));
    }
}

// ---------------------------------------------------------------------------
// Stub mínimo de IRolService
// ---------------------------------------------------------------------------

file sealed class StubRolServiceTransformation : IRolService
{
    private readonly List<string> _roles;
    private readonly List<string> _permisos;

    public StubRolServiceTransformation(List<string> roles, List<string> permisos)
    {
        _roles = roles;
        _permisos = permisos;
    }

    // Solo estos dos métodos son usados por PermissionClaimsTransformation
    public Task<List<string>> GetUserActiveRolesAsync(string userId) => Task.FromResult(_roles);
    public Task<List<string>> GetUserEffectivePermissionsAsync(string userId) => Task.FromResult(_permisos);

    // Resto no usado — implementación mínima para satisfacer la interfaz
    private static T NI<T>() => throw new NotImplementedException();

    // Metadata
    public Task<RolMetadata?> GetRoleMetadataAsync(string roleId) => Task.FromResult<RolMetadata?>(null);
    public Task<RolMetadata> EnsureRoleMetadataAsync(string roleId, string? roleName) => throw new NotImplementedException();
    public Task<string?> ToggleRoleActivoAsync(string roleId, bool activo) => Task.FromResult<string?>(null);
    public Task<(bool Ok, string? Error, string? RoleId, string? RoleName)> CreateRoleWithMetadataAsync(string nombre, string? descripcion, bool activo) => Task.FromResult((true, (string?)null, (string?)null, (string?)null));
    public Task<(bool Ok, string? Error, string? RoleName)> UpdateRoleWithMetadataAsync(string roleId, string nombre, string? descripcion, bool activo) => Task.FromResult((true, (string?)null, (string?)null));
    public Task<(bool Ok, string? Error, string? RoleId, string? RoleName, int PermisosCopiados)> DuplicateRoleAsync(string sourceRoleId, string nombre, string? descripcion, bool activo) => Task.FromResult((true, (string?)null, (string?)null, (string?)null, 0));
    // Gestión de roles (IdentityRole)
    public Task<List<IdentityRole>> GetAllRolesAsync() => Task.FromResult(new List<IdentityRole>());
    public Task<IdentityRole?> GetRoleByIdAsync(string roleId) => Task.FromResult<IdentityRole?>(null);
    public Task<IdentityRole?> GetRoleByNameAsync(string roleName) => Task.FromResult<IdentityRole?>(null);
    public Task<IdentityResult> CreateRoleAsync(string roleName) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> UpdateRoleAsync(string roleId, string newRoleName) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> DeleteRoleAsync(string roleId) => Task.FromResult(IdentityResult.Success);
    public Task<bool> RoleExistsAsync(string roleName) => Task.FromResult(false);
    public Task<List<string>> GetRolesInvalidosAsync(IEnumerable<string> roleNames) => Task.FromResult(new List<string>());
    // Permisos
    public Task<List<RolPermiso>> GetPermissionsForRoleAsync(string roleId) => Task.FromResult(new List<RolPermiso>());
    public Task<RolPermiso> AssignPermissionToRoleAsync(string roleId, int moduloId, int accionId) => throw new NotImplementedException();
    public Task<bool> RemovePermissionFromRoleAsync(string roleId, int moduloId, int accionId) => Task.FromResult(false);
    public Task ClearPermissionsForRoleAsync(string roleId) => Task.CompletedTask;
    public Task<bool> RoleHasPermissionAsync(string roleId, string moduloClave, string accionClave) => Task.FromResult(false);
    public Task<List<RolPermiso>> AssignMultiplePermissionsAsync(string roleId, List<(int moduloId, int accionId)> permisos) => Task.FromResult(new List<RolPermiso>());
    public Task SyncRoleClaimsAsync(string roleId) => Task.CompletedTask;
    public Task<(bool Ok, string? Error, int PermisosAsignados)> SyncPermisosForRoleAsync(string roleId, List<int> accionIds) => Task.FromResult((true, (string?)null, 0));
    public Task<(bool Ok, string? Error, int PermisosCopiados)> CopyPermisosFromRoleAsync(string sourceRoleId, string targetRoleId) => Task.FromResult((true, (string?)null, 0));
    public Task<List<ApplicationUser>> GetUsersInRoleAsync(string roleName, bool includeInactive = false) => Task.FromResult(new List<ApplicationUser>());
    public Task<IdentityResult> AssignRoleToUserAsync(string userId, string roleName) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> RemoveRoleFromUserAsync(string userId, string roleName) => Task.FromResult(IdentityResult.Success);
    public Task<List<string>> GetUserRolesAsync(string userId) => Task.FromResult(new List<string>());
    public Task<bool> UserIsInRoleAsync(string userId, string roleName) => Task.FromResult(false);
    public Task<List<ModuloSistema>> GetAllModulosAsync() => Task.FromResult(new List<ModuloSistema>());
    public Task<ModuloSistema?> GetModuloByIdAsync(int id) => Task.FromResult<ModuloSistema?>(null);
    public Task<ModuloSistema?> GetModuloByClaveAsync(string clave) => Task.FromResult<ModuloSistema?>(null);
    public Task<List<AccionModulo>> GetAccionesForModuloAsync(int moduloId) => Task.FromResult(new List<AccionModulo>());
    public Task<AccionModulo?> GetAccionByIdAsync(int id) => Task.FromResult<AccionModulo?>(null);
    public Task<ModuloSistema> CreateModuloAsync(ModuloSistema modulo) => throw new NotImplementedException();
    public Task<bool> UpdateModuloAsync(ModuloSistema modulo, string? updatedBy = null) => Task.FromResult(false);
    public Task<AccionModulo> CreateAccionAsync(AccionModulo accion) => throw new NotImplementedException();
    public Task<bool> UpdateAccionAsync(AccionModulo accion, string? updatedBy = null) => Task.FromResult(false);
    public Task<bool> DeleteAccionAsync(int id, string? deletedBy = null) => Task.FromResult(false);
    public Task<bool> DeleteModuloAsync(int id, string? deletedBy = null) => Task.FromResult(false);
    public Task<Dictionary<string, Dictionary<string, List<string>>>> GetPermissionsMatrixAsync() => Task.FromResult(new Dictionary<string, Dictionary<string, List<string>>>());
    public Task<Dictionary<string, int>> GetRoleUsageStatsAsync() => Task.FromResult(new Dictionary<string, int>());
    public Task<Dictionary<string, RolMetadata>> GetAllRoleMetadataAsync() => Task.FromResult(new Dictionary<string, RolMetadata>());
    public Task<RoleAggregateStats> GetRoleAggregateStatsAsync() => Task.FromResult(new RoleAggregateStats());
    public Task<HashSet<string>> GetAllRoleNamesAsync() => Task.FromResult(new HashSet<string>());
}
