using System.Security.Claims;

namespace TheBuryProject.Helpers;

public static class PermissionAliasHelper
{
    private static readonly string[][] ActionGroups =
    [
        ["view"],
        ["create"],
        ["update", "edit"],
        ["delete"],
        ["approve", "authorize"],
        ["export"],
        ["print"],
        ["duplicate"],
        ["assignpermissions", "assignroles", "assign"],
        ["revoke"],
        ["block"],
        ["resetpassword", "resetpass"]
    ];

    public static string Canon(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public static IReadOnlyCollection<string> ExpandActionAliases(string accion)
    {
        var canon = Canon(accion);
        var group = ActionGroups.FirstOrDefault(g => g.Contains(canon, StringComparer.OrdinalIgnoreCase));
        return group ?? [canon];
    }

    public static IReadOnlyCollection<string> ExpandPermissionClaims(string modulo, string accion)
    {
        var moduloCanon = Canon(modulo);
        return ExpandActionAliases(accion)
            .Select(alias => $"{moduloCanon}.{alias}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool MatchesPermissionClaim(string claimValue, string modulo, string accion)
    {
        var normalizedClaim = Canon(claimValue);
        return ExpandPermissionClaims(modulo, accion)
            .Any(expected => string.Equals(expected, normalizedClaim, StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasPermissionClaim(ClaimsPrincipal user, string modulo, string accion)
    {
        if (user == null)
            return false;

        return user.Claims.Any(claim =>
            claim.Type == "Permission" &&
            MatchesPermissionClaim(claim.Value, modulo, accion));
    }
}
