// Services/RolService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <summary>
/// Implementación del servicio de roles y permisos
/// </summary>
public class RolService : IRolService
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _context;

    public RolService(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        AppDbContext context)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
    }

    private static string Canon(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string CanonClaim(string moduloClave, string accionClave)
        => $"{Canon(moduloClave)}.{Canon(accionClave)}";

    #region Gestión de Roles

    public async Task<List<IdentityRole>> GetAllRolesAsync()
    {
        return await _roleManager.Roles
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public Task<IdentityRole?> GetRoleByIdAsync(string roleId) => _roleManager.FindByIdAsync(roleId);

    public Task<IdentityRole?> GetRoleByNameAsync(string roleName) => _roleManager.FindByNameAsync(roleName);

    public Task<IdentityResult> CreateRoleAsync(string roleName)
        => _roleManager.CreateAsync(new IdentityRole(roleName));

    public async Task<IdentityResult> UpdateRoleAsync(string roleId, string newRoleName)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null)
            return IdentityResult.Failed(new IdentityError { Description = "Rol no encontrado" });

        role.Name = newRoleName;
        role.NormalizedName = newRoleName.ToUpperInvariant();
        return await _roleManager.UpdateAsync(role);
    }

    public async Task<IdentityResult> DeleteRoleAsync(string roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null)
            return IdentityResult.Failed(new IdentityError { Description = "Rol no encontrado" });

        // Obtener todos los usuarios con este rol
        var allUsersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        
        // Filtrar solo usuarios activos
        var activeUsersInRole = allUsersInRole.Where(u => u.Activo).ToList();
        
        if (activeUsersInRole.Any())
        {
            return IdentityResult.Failed(new IdentityError
            {
                Description = $"No se puede eliminar el rol porque tiene {activeUsersInRole.Count} usuario(s) activo(s) asignado(s)"
            });
        }
        
        // Si solo hay usuarios inactivos, permitir eliminación
        // Los usuarios inactivos mantendrán el rol asignado pero no se consideran un impedimento

        await ClearPermissionsForRoleAsync(roleId);
        return await _roleManager.DeleteAsync(role);
    }

    public Task<bool> RoleExistsAsync(string roleName) => _roleManager.RoleExistsAsync(roleName);

    #endregion

    #region Gestión de Permisos

    public async Task<List<RolPermiso>> GetPermissionsForRoleAsync(string roleId)
    {
        return await _context.RolPermisos
            .Include(rp => rp.Modulo)
            .Include(rp => rp.Accion)
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .OrderBy(rp => rp.Modulo.Orden)
            .ThenBy(rp => rp.Accion.Orden)
            .ToListAsync();
    }

    public async Task<RolPermiso> AssignPermissionToRoleAsync(string roleId, int moduloId, int accionId)
    {
        // Traer TODOS (activos + soft-deleted) para evitar duplicados históricos por QueryFilter
        var existentes = await _context.RolPermisos
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == roleId && rp.ModuloId == moduloId && rp.AccionId == accionId)
            .ToListAsync();

        // Validar módulo/acción (y obtener claves canonizadas)
        var modulo = await _context.ModulosSistema
            .FirstOrDefaultAsync(m => m.Id == moduloId && !m.IsDeleted);

        var accion = await _context.AccionesModulo
            .FirstOrDefaultAsync(a => a.Id == accionId && !a.IsDeleted);

        if (modulo == null || accion == null)
            throw new InvalidOperationException("Módulo o acción no encontrados o eliminados");

        var claimValue = CanonClaim(modulo.Clave, accion.Clave);

        // Si ya hay activo, asegurar ClaimValue canonizado (por datos legacy) y devolver
        var activo = existentes.FirstOrDefault(rp => !rp.IsDeleted);
        if (activo != null)
        {
            if (!string.Equals(Canon(activo.ClaimValue), claimValue, StringComparison.Ordinal))
            {
                activo.ClaimValue = claimValue;
                await _context.SaveChangesAsync();
                await SyncRoleClaimsAsync(roleId);
            }
            return activo;
        }

        // Si hay soft-deleted, revivir
        var revivable = existentes.FirstOrDefault();
        if (revivable != null)
        {
            revivable.IsDeleted = false;
            revivable.ClaimValue = claimValue;

            await _context.SaveChangesAsync();
            await SyncRoleClaimsAsync(roleId);
            return revivable;
        }

        // Crear nuevo
        var rolPermiso = new RolPermiso
        {
            RoleId = roleId,
            ModuloId = moduloId,
            AccionId = accionId,
            ClaimValue = claimValue,
            IsDeleted = false
        };

        _context.RolPermisos.Add(rolPermiso);
        await _context.SaveChangesAsync();
        await SyncRoleClaimsAsync(roleId);

        return rolPermiso;
    }

    public async Task<bool> RemovePermissionFromRoleAsync(string roleId, int moduloId, int accionId)
    {
        var permiso = await _context.RolPermisos
            .FirstOrDefaultAsync(rp => rp.RoleId == roleId &&
                                      rp.ModuloId == moduloId &&
                                      rp.AccionId == accionId &&
                                      !rp.IsDeleted);

        if (permiso == null) return false;

        permiso.IsDeleted = true;
        await _context.SaveChangesAsync();

        await SyncRoleClaimsAsync(roleId);
        return true;
    }

    public async Task ClearPermissionsForRoleAsync(string roleId)
    {
        var now = DateTime.UtcNow;
        await _context.RolPermisos
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(rp => rp.IsDeleted, true)
                .SetProperty(rp => rp.UpdatedAt, now));

        await SyncRoleClaimsAsync(roleId);
    }

    public async Task<bool> RoleHasPermissionAsync(string roleId, string moduloClave, string accionClave)
    {
        var claims = await _context.RolPermisos
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .Select(rp => rp.ClaimValue)
            .ToListAsync();

        return claims.Any(claimValue =>
            PermissionAliasHelper.MatchesPermissionClaim(claimValue, moduloClave, accionClave));
    }

    public async Task<List<RolPermiso>> AssignMultiplePermissionsAsync(string roleId, List<(int moduloId, int accionId)> permisos)
    {
        var result = new List<RolPermiso>(permisos.Count);

        foreach (var (moduloId, accionId) in permisos)
            result.Add(await AssignPermissionToRoleAsync(roleId, moduloId, accionId));

        return result;
    }

    public async Task SyncRoleClaimsAsync(string roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null) return;

        var currentClaims = await _roleManager.GetClaimsAsync(role);
        var permisos = await GetPermissionsForRoleAsync(roleId);

        var permisoClaims = permisos
            .Select(p => Canon(p.ClaimValue))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var claim in currentClaims.Where(c => c.Type == "Permission"))
        {
            if (!permisoClaims.Contains(Canon(claim.Value)))
                await _roleManager.RemoveClaimAsync(role, claim);
        }

        var existingClaimValues = currentClaims
            .Where(c => c.Type == "Permission")
            .Select(c => Canon(c.Value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var cv in permisoClaims)
        {
            if (!existingClaimValues.Contains(cv))
                await _roleManager.AddClaimAsync(role, new Claim("Permission", cv));
        }
    }

    #endregion

    #region Gestión de Usuarios en Roles

    public async Task<List<ApplicationUser>> GetUsersInRoleAsync(string roleName, bool includeInactive = false)
    {
        var allUsers = await _userManager.GetUsersInRoleAsync(roleName);
        
        if (includeInactive)
        {
            return allUsers.ToList();
        }
        
        // Por defecto, solo devolver usuarios activos
        return allUsers.Where(u => u.Activo).ToList();
    }

    public async Task<IdentityResult> AssignRoleToUserAsync(string userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "Usuario no encontrado" });

        return await _userManager.AddToRoleAsync(user, roleName);
    }

    public async Task<IdentityResult> RemoveRoleFromUserAsync(string userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return IdentityResult.Failed(new IdentityError { Description = "Usuario no encontrado" });

        return await _userManager.RemoveFromRoleAsync(user, roleName);
    }

    public async Task<List<string>> GetUserRolesAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return new List<string>();

        return (await _userManager.GetRolesAsync(user)).ToList();
    }

    public async Task<List<string>> GetUserActiveRolesAsync(string userId)
    {
        var roles = await (
            from ur in _context.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join role in _context.Roles.AsNoTracking() on ur.RoleId equals role.Id
            join meta in _context.RolMetadatas.AsNoTracking() on role.Id equals meta.RoleId into metadataJoin
            from metadata in metadataJoin.DefaultIfEmpty()
            where metadata == null || metadata.Activo
            select role.Name
        ).ToListAsync();

        return roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r)
            .ToList();
    }

    public async Task<bool> UserIsInRoleAsync(string userId, string roleName)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        return await _userManager.IsInRoleAsync(user, roleName);
    }

    // MEJOR OPCIÓN: 1 query a DB + normalización en memoria (evita romper IQueryable con métodos no traducibles)
    public async Task<List<string>> GetUserEffectivePermissionsAsync(string userId)
    {
        var raw = await (
            from ur in _context.UserRoles.AsNoTracking()
            where ur.UserId == userId
            join role in _context.Roles.AsNoTracking() on ur.RoleId equals role.Id
            join meta in _context.RolMetadatas.AsNoTracking() on role.Id equals meta.RoleId into metadataJoin
            from metadata in metadataJoin.DefaultIfEmpty()
            where metadata == null || metadata.Activo
            join rp in _context.RolPermisos.AsNoTracking().Where(rp => !rp.IsDeleted)
                on role.Id equals rp.RoleId
            select rp.ClaimValue
        )
            .Distinct()
            .ToListAsync();

        return raw
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(Canon)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    #endregion

    #region Módulos y Acciones

    public async Task<List<ModuloSistema>> GetAllModulosAsync()
    {
        return await _context.ModulosSistema
            .Include(m => m.Acciones)
            .Where(m => !m.IsDeleted && m.Activo)
            .OrderBy(m => m.Orden)
            .ToListAsync();
    }

    public async Task<ModuloSistema?> GetModuloByIdAsync(int id)
    {
        return await _context.ModulosSistema
            .Include(m => m.Acciones)
            .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted);
    }

    public async Task<ModuloSistema?> GetModuloByClaveAsync(string clave)
    {
        var claveNorm = Canon(clave);

        return await _context.ModulosSistema
            .Include(m => m.Acciones)
            .FirstOrDefaultAsync(m => m.Clave == claveNorm && !m.IsDeleted);
    }

    public async Task<List<AccionModulo>> GetAccionesForModuloAsync(int moduloId)
    {
        return await _context.AccionesModulo
            .Where(a => a.ModuloId == moduloId && !a.IsDeleted && a.Activa)
            .OrderBy(a => a.Orden)
            .ToListAsync();
    }

    public async Task<AccionModulo?> GetAccionByIdAsync(int id)
    {
        return await _context.AccionesModulo
            .Include(a => a.Modulo)
            .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
    }

    public async Task<ModuloSistema> CreateModuloAsync(ModuloSistema modulo)
    {
        modulo.Clave = Canon(modulo.Clave);
        _context.ModulosSistema.Add(modulo);
        await _context.SaveChangesAsync();
        return modulo;
    }

    public async Task<bool> UpdateModuloAsync(ModuloSistema modulo, string? updatedBy = null)
    {
        var existing = await _context.ModulosSistema
            .FirstOrDefaultAsync(m => m.Id == modulo.Id && !m.IsDeleted);

        if (existing == null) return false;

        existing.Nombre = modulo.Nombre;
        existing.Clave = Canon(modulo.Clave);
        existing.Descripcion = modulo.Descripcion;
        existing.Categoria = modulo.Categoria;
        existing.Icono = modulo.Icono;
        existing.Orden = modulo.Orden;
        existing.Activo = modulo.Activo;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<AccionModulo> CreateAccionAsync(AccionModulo accion)
    {
        accion.Clave = Canon(accion.Clave);
        _context.AccionesModulo.Add(accion);
        await _context.SaveChangesAsync();
        return accion;
    }

    public async Task<bool> UpdateAccionAsync(AccionModulo accion, string? updatedBy = null)
    {
        var existing = await _context.AccionesModulo
            .FirstOrDefaultAsync(a => a.Id == accion.Id && !a.IsDeleted);

        if (existing == null) return false;

        existing.Nombre = accion.Nombre;
        existing.Clave = Canon(accion.Clave);
        existing.Descripcion = accion.Descripcion;
        existing.ModuloId = accion.ModuloId;
        existing.Activa = accion.Activa;
        existing.Orden = accion.Orden;
        existing.Icono = accion.Icono;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAccionAsync(int id, string? deletedBy = null)
    {
        var now = DateTime.UtcNow;

        var accionExists = await _context.AccionesModulo
            .AsNoTracking()
            .AnyAsync(a => a.Id == id && !a.IsDeleted);

        if (!accionExists) return false;

        var affectedRoleIds = await _context.RolPermisos
            .AsNoTracking()
            .Where(p => p.AccionId == id && !p.IsDeleted)
            .Select(p => p.RoleId)
            .Distinct()
            .ToListAsync();

        await _context.AccionesModulo
            .Where(a => a.Id == id && !a.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.IsDeleted, true)
                .SetProperty(a => a.UpdatedAt, now)
                .SetProperty(a => a.UpdatedBy, deletedBy));

        await _context.RolPermisos
            .Where(p => p.AccionId == id && !p.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.IsDeleted, true)
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.UpdatedBy, deletedBy));

        foreach (var roleId in affectedRoleIds)
            await SyncRoleClaimsAsync(roleId);

        return true;
    }

    public async Task<bool> DeleteModuloAsync(int id, string? deletedBy = null)
    {
        var now = DateTime.UtcNow;

        var moduloExists = await _context.ModulosSistema
            .AsNoTracking()
            .AnyAsync(m => m.Id == id && !m.IsDeleted);

        if (!moduloExists) return false;

        var affectedRoleIds = await _context.RolPermisos
            .AsNoTracking()
            .Where(p => p.ModuloId == id && !p.IsDeleted)
            .Select(p => p.RoleId)
            .Distinct()
            .ToListAsync();

        await _context.ModulosSistema
            .Where(m => m.Id == id && !m.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.IsDeleted, true)
                .SetProperty(m => m.UpdatedAt, now)
                .SetProperty(m => m.UpdatedBy, deletedBy));

        await _context.AccionesModulo
            .Where(a => a.ModuloId == id && !a.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.IsDeleted, true)
                .SetProperty(a => a.UpdatedAt, now)
                .SetProperty(a => a.UpdatedBy, deletedBy));

        await _context.RolPermisos
            .Where(p => p.ModuloId == id && !p.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.IsDeleted, true)
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.UpdatedBy, deletedBy));

        foreach (var roleId in affectedRoleIds)
            await SyncRoleClaimsAsync(roleId);

        return true;
    }

    #endregion

    #region Reportes y Estadísticas

    public async Task<Dictionary<string, Dictionary<string, List<string>>>> GetPermissionsMatrixAsync()
    {
        var roles = await GetAllRolesAsync();
        var modulos = await GetAllModulosAsync();
        var matrix = new Dictionary<string, Dictionary<string, List<string>>>();

        foreach (var role in roles)
        {
            var permisos = await GetPermissionsForRoleAsync(role.Id);
            var rolePermisos = new Dictionary<string, List<string>>();

            foreach (var modulo in modulos)
            {
                var acciones = permisos
                    .Where(p => p.ModuloId == modulo.Id)
                    .Select(p => p.Accion.Clave)
                    .ToList();

                rolePermisos[modulo.Clave] = acciones;
            }

            matrix[role.Name!] = rolePermisos;
        }

        return matrix;
    }

    public async Task<Dictionary<string, int>> GetRoleUsageStatsAsync()
    {
        var roles = await GetAllRolesAsync();
        var stats = new Dictionary<string, int>();

        foreach (var role in roles)
        {
            var users = await GetUsersInRoleAsync(role.Name!);
            stats[role.Name!] = users.Count;
        }

        return stats;
    }

    #endregion
}
