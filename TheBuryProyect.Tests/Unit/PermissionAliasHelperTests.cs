using System.Security.Claims;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para PermissionAliasHelper.
/// Función pura — no requiere base de datos ni infraestructura.
/// Cubre Canon, ExpandActionAliases, ExpandPermissionClaims,
/// MatchesPermissionClaim y HasPermissionClaim.
/// </summary>
public class PermissionAliasHelperTests
{
    // =========================================================================
    // Canon
    // =========================================================================

    [Fact]
    public void Canon_ConEspacios_RetornaTrimeado()
    {
        Assert.Equal("view", PermissionAliasHelper.Canon("  view  "));
    }

    [Fact]
    public void Canon_Mayusculas_RetornaMinuscula()
    {
        Assert.Equal("create", PermissionAliasHelper.Canon("CREATE"));
    }

    [Fact]
    public void Canon_Null_RetornaVacio()
    {
        Assert.Equal(string.Empty, PermissionAliasHelper.Canon(null!));
    }

    // =========================================================================
    // ExpandActionAliases
    // =========================================================================

    [Fact]
    public void ExpandActionAliases_AccionSinAlias_RetornaSoloEsaAccion()
    {
        var resultado = PermissionAliasHelper.ExpandActionAliases("view");
        Assert.Single(resultado);
        Assert.Contains("view", resultado);
    }

    [Fact]
    public void ExpandActionAliases_EditExpande_AUpdateYEdit()
    {
        var resultado = PermissionAliasHelper.ExpandActionAliases("edit");

        Assert.Contains("update", resultado);
        Assert.Contains("edit", resultado);
        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void ExpandActionAliases_UpdateExpande_AUpdateYEdit()
    {
        var resultado = PermissionAliasHelper.ExpandActionAliases("update");

        Assert.Contains("update", resultado);
        Assert.Contains("edit", resultado);
    }

    [Fact]
    public void ExpandActionAliases_ApproveExpande_AApproveYAuthorize()
    {
        var resultado = PermissionAliasHelper.ExpandActionAliases("approve");

        Assert.Contains("approve", resultado);
        Assert.Contains("authorize", resultado);
    }

    [Fact]
    public void ExpandActionAliases_ResetPasswordExpande_ABothVariants()
    {
        var resultado = PermissionAliasHelper.ExpandActionAliases("resetpassword");

        Assert.Contains("resetpassword", resultado);
        Assert.Contains("resetpass", resultado);
    }

    [Fact]
    public void ExpandActionAliases_AccionDesconocida_RetornaSoloEsaAccion()
    {
        var resultado = PermissionAliasHelper.ExpandActionAliases("unknownaction");

        Assert.Single(resultado);
        Assert.Contains("unknownaction", resultado);
    }

    [Fact]
    public void ExpandActionAliases_CaseInsensitive_ExpandeCorrectamente()
    {
        var resultado = PermissionAliasHelper.ExpandActionAliases("EDIT");

        Assert.Contains("update", resultado);
        Assert.Contains("edit", resultado);
    }

    // =========================================================================
    // ExpandPermissionClaims
    // =========================================================================

    [Fact]
    public void ExpandPermissionClaims_ModuloYAccionSimple_RetornaUnClaim()
    {
        var resultado = PermissionAliasHelper.ExpandPermissionClaims("ventas", "view");

        Assert.Single(resultado);
        Assert.Contains("ventas.view", resultado);
    }

    [Fact]
    public void ExpandPermissionClaims_AccionConAlias_RetornaMultiplesClaims()
    {
        var resultado = PermissionAliasHelper.ExpandPermissionClaims("Clientes", "Edit");

        Assert.Contains("clientes.update", resultado);
        Assert.Contains("clientes.edit", resultado);
        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void ExpandPermissionClaims_ModuloEnMayusculas_NormalizaAMinuscula()
    {
        var resultado = PermissionAliasHelper.ExpandPermissionClaims("PRODUCTOS", "view");

        Assert.All(resultado, c => Assert.StartsWith("productos.", c));
    }

    // =========================================================================
    // MatchesPermissionClaim
    // =========================================================================

    [Fact]
    public void MatchesPermissionClaim_ClaimExacto_RetornaTrue()
    {
        Assert.True(PermissionAliasHelper.MatchesPermissionClaim("ventas.view", "ventas", "view"));
    }

    [Fact]
    public void MatchesPermissionClaim_ClaimPorAlias_RetornaTrue()
    {
        // "edit" is an alias for "update"
        Assert.True(PermissionAliasHelper.MatchesPermissionClaim("ventas.edit", "ventas", "update"));
        Assert.True(PermissionAliasHelper.MatchesPermissionClaim("ventas.update", "ventas", "edit"));
    }

    [Fact]
    public void MatchesPermissionClaim_ModuloDistinto_RetornaFalse()
    {
        Assert.False(PermissionAliasHelper.MatchesPermissionClaim("clientes.view", "ventas", "view"));
    }

    [Fact]
    public void MatchesPermissionClaim_AccionDistinta_RetornaFalse()
    {
        Assert.False(PermissionAliasHelper.MatchesPermissionClaim("ventas.delete", "ventas", "view"));
    }

    [Fact]
    public void MatchesPermissionClaim_CaseInsensitive_RetornaTrue()
    {
        Assert.True(PermissionAliasHelper.MatchesPermissionClaim("VENTAS.VIEW", "ventas", "view"));
    }

    // =========================================================================
    // HasPermissionClaim
    // =========================================================================

    [Fact]
    public void HasPermissionClaim_UserNull_RetornaFalse()
    {
        Assert.False(PermissionAliasHelper.HasPermissionClaim(null!, "ventas", "view"));
    }

    [Fact]
    public void HasPermissionClaim_UserSinClaims_RetornaFalse()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(PermissionAliasHelper.HasPermissionClaim(user, "ventas", "view"));
    }

    [Fact]
    public void HasPermissionClaim_UserConClaimCorrecto_RetornaTrue()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("Permission", "ventas.view")
        });
        var user = new ClaimsPrincipal(identity);

        Assert.True(PermissionAliasHelper.HasPermissionClaim(user, "ventas", "view"));
    }

    [Fact]
    public void HasPermissionClaim_UserConClaimAlias_RetornaTrue()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("Permission", "ventas.edit")
        });
        var user = new ClaimsPrincipal(identity);

        // "edit" is an alias for "update"
        Assert.True(PermissionAliasHelper.HasPermissionClaim(user, "ventas", "update"));
    }

    [Fact]
    public void HasPermissionClaim_ClaimTypoDistinto_RetornaFalse()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("Role", "ventas.view") // wrong claim type
        });
        var user = new ClaimsPrincipal(identity);

        Assert.False(PermissionAliasHelper.HasPermissionClaim(user, "ventas", "view"));
    }
}
