// Services/RolService.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
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
    private readonly ILogger<RolService> _logger;

    public RolService(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        ILogger<RolService> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
        _logger = logger;
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

    public async Task<IdentityResult> CreateRoleAsync(string roleName)
    {
        var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
        if (result.Succeeded)
            _logger.LogInformation("Rol creado - Nombre {RoleName}", roleName);
        else
            _logger.LogWarning("Error al crear rol {RoleName}: {Errors}", roleName,
                string.Join(", ", result.Errors.Select(e => e.Description)));
        return result;
    }

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

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Soft-delete metadata (antes del cascade hard-delete del role)
            var metadata = await _context.RolMetadatas
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.RoleId == roleId);
            if (metadata != null)
            {
                metadata.IsDeleted = true;
                metadata.Activo = false;
                metadata.UpdatedAt = DateTime.UtcNow;
            }

            await ClearPermissionsForRoleAsync(roleId);

            var deleteResult = await _roleManager.DeleteAsync(role);
            if (!deleteResult.Succeeded)
                return deleteResult;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Rol eliminado - Id {RoleId} - Nombre {RoleName}", roleId, role.Name);

            return IdentityResult.Success;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public Task<bool> RoleExistsAsync(string roleName) => _roleManager.RoleExistsAsync(roleName);

    public async Task<List<string>> GetRolesInvalidosAsync(IEnumerable<string> roleNames)
    {
        var names = roleNames.ToList();
        if (names.Count == 0) return new List<string>();

        var existentes = await _roleManager.Roles
            .Where(r => names.Contains(r.Name!))
            .Select(r => r.Name!)
            .ToListAsync();

        return names.Except(existentes, StringComparer.OrdinalIgnoreCase).ToList();
    }

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

        _logger.LogInformation("Claims sincronizados - Rol {RoleName} - {Count} permisos activos",
            role.Name, permisoClaims.Count);
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

        // Batch: cargar todos los permisos de todos los roles en una sola query
        var roleIds = roles.Select(r => r.Id).ToList();
        var todosPermisos = await _context.RolPermisos
            .AsNoTracking()
            .Include(rp => rp.Modulo)
            .Include(rp => rp.Accion)
            .Where(rp => roleIds.Contains(rp.RoleId) && !rp.IsDeleted)
            .ToListAsync();

        var permisosPorRol = todosPermisos.GroupBy(p => p.RoleId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matrix = new Dictionary<string, Dictionary<string, List<string>>>();

        foreach (var role in roles)
        {
            var permisos = permisosPorRol.GetValueOrDefault(role.Id, new());
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

        var roleIds = roles.Select(r => r.Id).ToList();
        var countsByRoleId = await _context.UserRoles
            .AsNoTracking()
            .Where(ur => roleIds.Contains(ur.RoleId))
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        return roles.ToDictionary(
            r => r.Name!,
            r => countsByRoleId.GetValueOrDefault(r.Id, 0));
    }

    #endregion

    #region Metadata de Roles

    public async Task<RolMetadata?> GetRoleMetadataAsync(string roleId)
    {
        return await _context.RolMetadatas
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.RoleId == roleId);
    }

    public async Task<RolMetadata> EnsureRoleMetadataAsync(string roleId, string? roleName)
    {
        var metadata = await _context.RolMetadatas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.RoleId == roleId);

        if (metadata != null)
        {
            if (metadata.IsDeleted)
            {
                metadata.IsDeleted = false;
            }

            if (string.IsNullOrWhiteSpace(metadata.Descripcion))
            {
                metadata.Descripcion = RolMetadataDefaults.GetDescripcion(roleName);
            }

            return metadata;
        }

        metadata = new RolMetadata
        {
            RoleId = roleId,
            Descripcion = RolMetadataDefaults.GetDescripcion(roleName),
            Activo = true,
            IsDeleted = false
        };

        _context.RolMetadatas.Add(metadata);
        return metadata;
    }

    public async Task<string?> ToggleRoleActivoAsync(string roleId, bool activo)
    {
        var role = await GetRoleByIdAsync(roleId);
        if (role == null)
            return null;

        var metadata = await EnsureRoleMetadataAsync(role.Id, role.Name);
        metadata.Activo = activo;
        metadata.IsDeleted = false;

        await _context.SaveChangesAsync();
        return role.Name;
    }

    public async Task<(bool Ok, string? Error, int PermisosAsignados)> SyncPermisosForRoleAsync(
        string roleId, List<int> accionIds)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null)
            return (false, "Rol no encontrado.", 0);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Filtrar acciones válidas (activas, no eliminadas)
            var acciones = await _context.AccionesModulo
                .AsNoTracking()
                .Where(a => accionIds.Contains(a.Id) && !a.IsDeleted && a.Activa)
                .Select(a => new { a.Id, a.ModuloId })
                .ToListAsync();

            await ClearPermissionsForRoleAsync(roleId);

            if (acciones.Count > 0)
            {
                await AssignMultiplePermissionsAsync(
                    roleId,
                    acciones.Select(a => (a.ModuloId, a.Id)).ToList());
            }

            await transaction.CommitAsync();
            return (true, null, acciones.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Ok, string? Error, int PermisosCopiados)> CopyPermisosFromRoleAsync(
        string sourceRoleId, string targetRoleId)
    {
        var sourceRole = await _roleManager.FindByIdAsync(sourceRoleId);
        if (sourceRole == null)
            return (false, "Rol origen no encontrado.", 0);

        var targetRole = await _roleManager.FindByIdAsync(targetRoleId);
        if (targetRole == null)
            return (false, "Rol destino no encontrado.", 0);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var permisosOrigen = await GetPermissionsForRoleAsync(sourceRole.Id);
            await ClearPermissionsForRoleAsync(targetRole.Id);

            var permisos = permisosOrigen
                .Select(p => (p.ModuloId, p.AccionId))
                .Distinct()
                .ToList();

            if (permisos.Count > 0)
            {
                await AssignMultiplePermissionsAsync(targetRole.Id, permisos);
            }

            await transaction.CommitAsync();
            return (true, null, permisos.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Ok, string? Error, string? RoleName)> UpdateRoleWithMetadataAsync(
        string roleId, string nombre, string? descripcion, bool activo)
    {
        var role = await _roleManager.FindByIdAsync(roleId);
        if (role == null)
            return (false, "Rol no encontrado.", null);

        // Validar duplicado de nombre contra otro rol
        var existing = await _roleManager.FindByNameAsync(nombre);
        if (existing != null && existing.Id != roleId)
            return (false, $"El rol '{nombre}' ya existe.", null);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            role.Name = nombre;
            role.NormalizedName = nombre.ToUpperInvariant();
            var updateResult = await _roleManager.UpdateAsync(role);
            if (!updateResult.Succeeded)
            {
                var errors = string.Join(" ", updateResult.Errors.Select(e => e.Description));
                return (false, errors, null);
            }

            var metadata = await EnsureRoleMetadataAsync(role.Id, role.Name);
            metadata.Descripcion = string.IsNullOrWhiteSpace(descripcion)
                ? RolMetadataDefaults.GetDescripcion(role.Name)
                : descripcion;
            metadata.Activo = activo;
            metadata.IsDeleted = false;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, null, role.Name);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Ok, string? Error, string? RoleId, string? RoleName)> CreateRoleWithMetadataAsync(
        string nombre, string? descripcion, bool activo)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var createResult = await _roleManager.CreateAsync(new IdentityRole(nombre));
            if (!createResult.Succeeded)
            {
                var errors = string.Join(" ", createResult.Errors.Select(e => e.Description));
                return (false, errors, null, null);
            }

            var role = await _roleManager.FindByNameAsync(nombre);
            if (role == null)
                return (false, "No se pudo recuperar el rol creado.", null, null);

            var metadata = await EnsureRoleMetadataAsync(role.Id, role.Name);
            metadata.Descripcion = string.IsNullOrWhiteSpace(descripcion)
                ? RolMetadataDefaults.GetDescripcion(role.Name)
                : descripcion;
            metadata.Activo = activo;
            metadata.IsDeleted = false;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, null, role.Id, role.Name);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Ok, string? Error, string? RoleId, string? RoleName, int PermisosCopiados)> DuplicateRoleAsync(
        string sourceRoleId, string nombre, string? descripcion, bool activo)
    {
        var sourceRole = await _roleManager.FindByIdAsync(sourceRoleId);
        if (sourceRole == null)
            return (false, "Rol origen no encontrado.", null, null, 0);

        var existing = await _roleManager.FindByNameAsync(nombre);
        if (existing != null)
            return (false, $"El rol '{nombre}' ya existe.", null, null, 0);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var createResult = await _roleManager.CreateAsync(new IdentityRole(nombre));
            if (!createResult.Succeeded)
            {
                var errors = string.Join(" ", createResult.Errors.Select(e => e.Description));
                return (false, errors, null, null, 0);
            }

            var newRole = await _roleManager.FindByNameAsync(nombre);
            if (newRole == null)
                return (false, "No se pudo recuperar el rol duplicado.", null, null, 0);

            var metadata = await EnsureRoleMetadataAsync(newRole.Id, newRole.Name);
            metadata.Descripcion = string.IsNullOrWhiteSpace(descripcion)
                ? RolMetadataDefaults.GetDescripcion(newRole.Name)
                : descripcion;
            metadata.Activo = activo;
            metadata.IsDeleted = false;

            // Copiar permisos del rol origen
            var permisosOrigen = await GetPermissionsForRoleAsync(sourceRole.Id);
            var permisos = permisosOrigen
                .Select(p => (p.ModuloId, p.AccionId))
                .Distinct()
                .ToList();

            if (permisos.Count > 0)
                await AssignMultiplePermissionsAsync(newRole.Id, permisos);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, null, newRole.Id, newRole.Name, permisos.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    #endregion

    #region Queries de soporte

    public async Task<Dictionary<string, RolMetadata>> GetAllRoleMetadataAsync()
    {
        return await _context.RolMetadatas
            .AsNoTracking()
            .ToDictionaryAsync(m => m.RoleId);
    }

    public async Task<RoleAggregateStats> GetRoleAggregateStatsAsync()
    {
        var userCounts = await _context.UserRoles
            .AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        var activeUserCounts = await (
            from ur in _context.UserRoles.AsNoTracking()
            join u in _context.Users.AsNoTracking().Where(user => user.Activo)
                on ur.UserId equals u.Id
            group ur by ur.RoleId into grouped
            select new { RoleId = grouped.Key, Count = grouped.Count() }
        ).ToDictionaryAsync(x => x.RoleId, x => x.Count);

        var permissionCounts = await _context.RolPermisos
            .AsNoTracking()
            .Where(rp => !rp.IsDeleted)
            .GroupBy(rp => rp.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        return new RoleAggregateStats
        {
            UserCounts = userCounts,
            ActiveUserCounts = activeUserCounts,
            PermissionCounts = permissionCounts
        };
    }

    public async Task<HashSet<string>> GetAllRoleNamesAsync()
    {
        var names = await _roleManager.Roles
            .AsNoTracking()
            .Select(r => r.Name ?? string.Empty)
            .ToListAsync();

        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}
