using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public class UsuarioService : IUsuarioService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _context;
    private readonly ILogger<UsuarioService> _logger;

    public UsuarioService(
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        ILogger<UsuarioService> logger)
    {
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    public async Task<UsuarioDashboardStats> GetDashboardStatsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var activos = await _context.Users.AsNoTracking().CountAsync(u => u.Activo);
        var bloqueados = await _context.Users.AsNoTracking().CountAsync(u =>
            u.LockoutEnabled &&
            u.LockoutEnd.HasValue &&
            u.LockoutEnd > now);

        return new UsuarioDashboardStats(activos, bloqueados);
    }

    public async Task<List<UsuarioResumen>> GetUsuariosConRolesAsync(bool incluirInactivos = false)
    {
        var query = _context.Users.AsQueryable();

        if (!incluirInactivos)
            query = query.Where(u => u.Activo);

        var users = await query
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var sucursales = await _context.GetSucursalOptionsAsync();
        var sucursalesLookup = sucursales.ToDictionary(s => s.Id, s => s.Nombre);

        var rolesLookup = await _context.UserRoles
            .Join(_context.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, r.Name })
            .GroupBy(x => x.UserId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Name!).ToList());

        return users.Select(user => new UsuarioResumen
        {
            Id = user.Id,
            Email = user.Email!,
            UserName = user.UserName!,
            EmailConfirmed = user.EmailConfirmed,
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd,
            Roles = rolesLookup.GetValueOrDefault(user.Id, []),
            Activo = user.Activo,
            NombreCompleto = user.NombreCompleto,
            SucursalId = user.SucursalId,
            Sucursal = user.SucursalId.HasValue && sucursalesLookup.TryGetValue(user.SucursalId.Value, out var sucursalNombre)
                ? sucursalNombre
                : user.Sucursal,
            UltimoAcceso = user.UltimoAcceso,
            FechaCreacion = user.FechaCreacion
        }).ToList();
    }

    public Task<List<SucursalOptionViewModel>> GetSucursalOptionsAsync()
        => _context.GetSucursalOptionsAsync();

    public async Task<bool> ExistsSucursalActivaAsync(int sucursalId)
    {
        return await _context.Sucursales
            .AsNoTracking()
            .AnyAsync(s => s.Id == sucursalId && s.Activa);
    }

    public Task<Sucursal?> GetSucursalByIdAsync(int? sucursalId)
        => _context.GetSucursalAsync(sucursalId);

    public async Task<List<ApplicationUser>> GetUsuariosByIdsAsync(IEnumerable<string> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return new List<ApplicationUser>();

        return await _userManager.Users
            .Where(u => idList.Contains(u.Id))
            .ToListAsync();
    }

    public async Task<UsuarioUpdateResult> UpdateUsuarioAsync(UsuarioUpdateRequest request)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
            return UsuarioUpdateResult.Failed("Usuario no encontrado.");

        // Mapear campos
        var wasActive = user.Activo;
        user.UserName = request.UserName;
        user.Email = request.Email;
        user.Nombre = string.IsNullOrWhiteSpace(request.Nombre) ? null : request.Nombre.Trim();
        user.Apellido = string.IsNullOrWhiteSpace(request.Apellido) ? null : request.Apellido.Trim();
        user.Telefono = string.IsNullOrWhiteSpace(request.Telefono) ? null : request.Telefono.Trim();
        user.PhoneNumber = user.Telefono;
        user.SucursalId = request.SucursalId;
        user.Sucursal = request.SucursalNombre;
        user.Activo = request.Activo;

        // Tracking de activación/desactivación
        if (wasActive && !request.Activo)
        {
            user.FechaDesactivacion = DateTime.UtcNow;
            user.DesactivadoPor = request.EditadoPor;
            user.MotivoDesactivacion = "Desactivado desde la edición de Seguridad.";
        }
        else if (!wasActive && request.Activo)
        {
            user.FechaDesactivacion = null;
            user.DesactivadoPor = null;
            user.MotivoDesactivacion = null;
        }

        // Concurrencia optimista
        _context.Entry(user).Property(u => u.RowVersion).OriginalValue = request.RowVersion;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // 1. Update Identity user
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                if (updateResult.Errors.Any(e =>
                    string.Equals(e.Code, "ConcurrencyFailure", StringComparison.OrdinalIgnoreCase)))
                {
                    return UsuarioUpdateResult.Conflict();
                }

                return UsuarioUpdateResult.Failed(updateResult.Errors.Select(e => e.Description));
            }

            // 2. Sync roles
            var currentRoles = await _userManager.GetRolesAsync(user);
            var rolesToRemove = currentRoles
                .Except(request.RolesDeseados, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rolesToAdd = request.RolesDeseados
                .Except(currentRoles, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rolesToRemove.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                    return UsuarioUpdateResult.Failed(removeResult.Errors.Select(e => e.Description));
            }

            if (rolesToAdd.Count > 0)
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                    return UsuarioUpdateResult.Failed(addResult.Errors.Select(e => e.Description));
            }

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Usuario actualizado - Id {UserId} - Activo {Activo} - RolesAgregados {Added} - RolesQuitados {Removed}",
                user.Id, user.Activo, rolesToAdd.Count, rolesToRemove.Count);

            return UsuarioUpdateResult.Success();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<UsuarioEdicionData?> GetUsuarioParaEdicionAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);

        return new UsuarioEdicionData
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Nombre = user.Nombre,
            Apellido = user.Apellido,
            Telefono = user.Telefono ?? user.PhoneNumber,
            Roles = roles.ToList(),
            SucursalId = user.SucursalId,
            Activo = user.Activo,
            RowVersion = user.RowVersion
        };
    }

    public async Task<List<UsuarioValidacionError>> ValidarUnicidadUsuarioAsync(string userId, string userName, string email)
    {
        var errors = new List<UsuarioValidacionError>();

        var existingByUserName = await _userManager.FindByNameAsync(userName);
        if (existingByUserName != null && existingByUserName.Id != userId)
        {
            errors.Add(new UsuarioValidacionError("UserName", $"El nombre de usuario '{userName}' ya está en uso."));
        }

        var existingByEmail = await _userManager.FindByEmailAsync(email);
        if (existingByEmail != null && existingByEmail.Id != userId)
        {
            errors.Add(new UsuarioValidacionError("Email", $"El email '{email}' ya está en uso."));
        }

        return errors;
    }

    public async Task<List<UsuarioSelectItem>> GetUsuarioSelectListAsync()
    {
        return await _context.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new UsuarioSelectItem(u.Id, u.UserName ?? u.Email ?? ""))
            .ToListAsync();
    }

    public async Task<List<UsuarioSelectItem>> GetUsuariosPorRolAsync(string roleName)
    {
        var users = await _userManager.GetUsersInRoleAsync(roleName);
        return users
            .OrderBy(u => u.UserName)
            .Select(u => new UsuarioSelectItem(u.Id, u.UserName ?? u.Email ?? ""))
            .ToList();
    }
}
