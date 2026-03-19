using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <summary>
/// Agrega/remueve dinámicamente los claims "Permission" efectivos del usuario en cada autenticación.
/// Debe ser idempotente.
/// </summary>
public sealed class PermissionClaimsTransformation : IClaimsTransformation
{
    private readonly IRolService _rolService;

    public PermissionClaimsTransformation(IRolService rolService)
    {
        _rolService = rolService;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Evita CS8603: nunca devolvemos null
        if (principal is null)
            return new ClaimsPrincipal(new ClaimsIdentity());

        if (principal.Identity is not ClaimsIdentity identity)
            return principal;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return principal;

        var activeRoles = await _rolService.GetUserActiveRolesAsync(userId);
        var normalizedRoles = activeRoles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingRoleClaims = identity.FindAll(identity.RoleClaimType).ToList();
        foreach (var claim in existingRoleClaims)
        {
            if (!normalizedRoles.Contains(claim.Value))
                identity.RemoveClaim(claim);
        }

        var currentRoles = identity.FindAll(identity.RoleClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in activeRoles)
        {
            if (currentRoles.Add(roleName))
                identity.AddClaim(new Claim(identity.RoleClaimType, roleName));
        }

        var effectivePermissions = await _rolService.GetUserEffectivePermissionsAsync(userId);

        var normalized = effectivePermissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        // Quitar claims que ya no correspondan
        var existingPermissionClaims = identity.FindAll("Permission").ToList();
        foreach (var claim in existingPermissionClaims)
        {
            var claimValue = (claim.Value ?? string.Empty).Trim().ToLowerInvariant();
            if (!normalized.Contains(claimValue))
                identity.RemoveClaim(claim);
        }

        // Agregar faltantes
        var current = identity.FindAll("Permission")
            .Select(c => (c.Value ?? string.Empty).Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permiso in normalized)
        {
            if (current.Add(permiso))
                identity.AddClaim(new Claim("Permission", permiso));
        }

        return principal;
    }
}
