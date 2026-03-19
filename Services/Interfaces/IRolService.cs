using Microsoft.AspNetCore.Identity;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Interfaces;

/// <summary>
/// Servicio para gestión de roles y permisos del sistema
/// </summary>
public interface IRolService
{
    // ============================================
    // GESTIÓN DE ROLES
    // ============================================

    /// <summary>
    /// Obtiene todos los roles del sistema
    /// </summary>
    Task<List<IdentityRole>> GetAllRolesAsync();

    /// <summary>
    /// Obtiene un rol por su ID
    /// </summary>
    Task<IdentityRole?> GetRoleByIdAsync(string roleId);

    /// <summary>
    /// Obtiene un rol por su nombre
    /// </summary>
    Task<IdentityRole?> GetRoleByNameAsync(string roleName);

    /// <summary>
    /// Crea un nuevo rol
    /// </summary>
    Task<IdentityResult> CreateRoleAsync(string roleName);

    /// <summary>
    /// Actualiza el nombre de un rol
    /// </summary>
    Task<IdentityResult> UpdateRoleAsync(string roleId, string newRoleName);

    /// <summary>
    /// Elimina un rol del sistema
    /// </summary>
    Task<IdentityResult> DeleteRoleAsync(string roleId);

    /// <summary>
    /// Verifica si un rol existe
    /// </summary>
    Task<bool> RoleExistsAsync(string roleName);

    // ============================================
    // GESTIÓN DE PERMISOS
    // ============================================

    /// <summary>
    /// Obtiene todos los permisos asignados a un rol
    /// </summary>
    Task<List<RolPermiso>> GetPermissionsForRoleAsync(string roleId);

    /// <summary>
    /// Asigna un permiso a un rol
    /// </summary>
    /// <param name="roleId">ID del rol</param>
    /// <param name="moduloId">ID del módulo</param>
    /// <param name="accionId">ID de la acción</param>
    Task<RolPermiso> AssignPermissionToRoleAsync(string roleId, int moduloId, int accionId);

    /// <summary>
    /// Remueve un permiso de un rol
    /// </summary>
    Task<bool> RemovePermissionFromRoleAsync(string roleId, int moduloId, int accionId);

    /// <summary>
    /// Remueve todos los permisos de un rol
    /// </summary>
    Task ClearPermissionsForRoleAsync(string roleId);

    /// <summary>
    /// Verifica si un rol tiene un permiso específico
    /// </summary>
    Task<bool> RoleHasPermissionAsync(string roleId, string moduloClave, string accionClave);

    /// <summary>
    /// Asigna múltiples permisos a un rol en una sola operación
    /// </summary>
    Task<List<RolPermiso>> AssignMultiplePermissionsAsync(string roleId, List<(int moduloId, int accionId)> permisos);

    /// <summary>
    /// Sincroniza los claims del rol con los permisos en BD
    /// Útil después de cambiar permisos
    /// </summary>
    Task SyncRoleClaimsAsync(string roleId);

    // ============================================
    // GESTIÓN DE USUARIOS EN ROLES
    // ============================================

    /// <summary>
    /// Obtiene todos los usuarios que tienen un rol específico
    /// </summary>
    /// <param name="roleName">Nombre del rol</param>
    /// <param name="includeInactive">Si es true, incluye usuarios inactivos. Por defecto false (solo activos)</param>
    Task<List<ApplicationUser>> GetUsersInRoleAsync(string roleName, bool includeInactive = false);

    /// <summary>
    /// Asigna un rol a un usuario
    /// </summary>
    Task<IdentityResult> AssignRoleToUserAsync(string userId, string roleName);

    /// <summary>
    /// Remueve un rol de un usuario
    /// </summary>
    Task<IdentityResult> RemoveRoleFromUserAsync(string userId, string roleName);

    /// <summary>
    /// Obtiene todos los roles de un usuario
    /// </summary>
    Task<List<string>> GetUserRolesAsync(string userId);

    /// <summary>
    /// Verifica si un usuario tiene un rol específico
    /// </summary>
    Task<bool> UserIsInRoleAsync(string userId, string roleName);

    /// <summary>
    /// Obtiene los roles activos de un usuario.
    /// </summary>
    Task<List<string>> GetUserActiveRolesAsync(string userId);

    /// <summary>
    /// Obtiene todos los permisos efectivos de un usuario (combinando todos sus roles)
    /// </summary>
    Task<List<string>> GetUserEffectivePermissionsAsync(string userId);

    // ============================================
    // MÓDULOS Y ACCIONES
    // ============================================

    /// <summary>
    /// Obtiene todos los módulos del sistema
    /// </summary>
    Task<List<ModuloSistema>> GetAllModulosAsync();

    /// <summary>
    /// Obtiene un módulo por su ID
    /// </summary>
    Task<ModuloSistema?> GetModuloByIdAsync(int id);

    /// <summary>
    /// Obtiene un módulo por su clave
    /// </summary>
    Task<ModuloSistema?> GetModuloByClaveAsync(string clave);

    /// <summary>
    /// Obtiene todas las acciones de un módulo específico
    /// </summary>
    Task<List<AccionModulo>> GetAccionesForModuloAsync(int moduloId);

    /// <summary>
    /// Obtiene una acción por su ID
    /// </summary>
    Task<AccionModulo?> GetAccionByIdAsync(int id);

    /// <summary>
    /// Crea un nuevo módulo del sistema
    /// </summary>
    Task<ModuloSistema> CreateModuloAsync(ModuloSistema modulo);

    /// <summary>
    /// Actualiza un módulo existente (soft updates)
    /// </summary>
    Task<bool> UpdateModuloAsync(ModuloSistema modulo, string? updatedBy = null);

    /// <summary>
    /// Crea una nueva acción para un módulo
    /// </summary>
    Task<AccionModulo> CreateAccionAsync(AccionModulo accion);

    /// <summary>
    /// Actualiza una acción existente
    /// </summary>
    Task<bool> UpdateAccionAsync(AccionModulo accion, string? updatedBy = null);

    /// <summary>
    /// Elimina (soft delete) una acción y sus permisos relacionados
    /// </summary>
    Task<bool> DeleteAccionAsync(int id, string? deletedBy = null);

    /// <summary>
    /// Elimina (soft delete) un módulo y todas sus acciones y permisos relacionados
    /// </summary>
    Task<bool> DeleteModuloAsync(int id, string? deletedBy = null);

    // ============================================
    // REPORTES Y ESTADÍSTICAS
    // ============================================

    /// <summary>
    /// Obtiene la matriz de permisos: Roles x Módulos.Acciones
    /// </summary>
    Task<Dictionary<string, Dictionary<string, List<string>>>> GetPermissionsMatrixAsync();

    /// <summary>
    /// Obtiene estadísticas de uso de roles
    /// </summary>
    Task<Dictionary<string, int>> GetRoleUsageStatsAsync();
}
