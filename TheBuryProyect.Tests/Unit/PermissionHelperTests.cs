using System.Security.Claims;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para PermissionHelper.
/// Función pura — no requiere base de datos ni infraestructura.
/// Cubre TienePermiso, TieneCualquierPermiso, TieneTodosLosPermisos,
/// ObtenerPermisos y EsSuperAdmin.
/// </summary>
public class PermissionHelperTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ClaimsPrincipal UserAutenticado(string? role = null, string? permissionClaim = null)
    {
        var claims = new List<Claim>();
        if (permissionClaim != null)
            claims.Add(new Claim("Permission", permissionClaim));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        if (role != null)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UserNoAutenticado()
        => new ClaimsPrincipal(new ClaimsIdentity()); // no authentication type = not authenticated

    // =========================================================================
    // TienePermiso
    // =========================================================================

    [Fact]
    public void TienePermiso_UserNull_RetornaFalse()
    {
        Assert.False(((ClaimsPrincipal)null!).TienePermiso("ventas", "view"));
    }

    [Fact]
    public void TienePermiso_UserNoAutenticado_RetornaFalse()
    {
        Assert.False(UserNoAutenticado().TienePermiso("ventas", "view"));
    }

    [Fact]
    public void TienePermiso_SuperAdmin_RetornaTrueSinClaim()
    {
        var user = UserAutenticado(role: "SuperAdmin");
        Assert.True(user.TienePermiso("ventas", "view"));
    }

    [Fact]
    public void TienePermiso_ConClaimCorrecto_RetornaTrue()
    {
        var user = UserAutenticado(permissionClaim: "ventas.view");
        Assert.True(user.TienePermiso("ventas", "view"));
    }

    [Fact]
    public void TienePermiso_SinClaimCorrecto_RetornaFalse()
    {
        var user = UserAutenticado(permissionClaim: "clientes.view");
        Assert.False(user.TienePermiso("ventas", "view"));
    }

    // =========================================================================
    // TieneCualquierPermiso
    // =========================================================================

    [Fact]
    public void TieneCualquierPermiso_UserNull_RetornaFalse()
    {
        Assert.False(((ClaimsPrincipal)null!).TieneCualquierPermiso(("ventas", "view")));
    }

    [Fact]
    public void TieneCualquierPermiso_SuperAdmin_RetornaTrue()
    {
        var user = UserAutenticado(role: "SuperAdmin");
        Assert.True(user.TieneCualquierPermiso(("ventas", "view"), ("clientes", "delete")));
    }

    [Fact]
    public void TieneCualquierPermiso_TieneUnoDeLosDos_RetornaTrue()
    {
        var user = UserAutenticado(permissionClaim: "clientes.view");
        Assert.True(user.TieneCualquierPermiso(("ventas", "view"), ("clientes", "view")));
    }

    [Fact]
    public void TieneCualquierPermiso_NoTieneNinguno_RetornaFalse()
    {
        var user = UserAutenticado(permissionClaim: "reportes.view");
        Assert.False(user.TieneCualquierPermiso(("ventas", "view"), ("clientes", "view")));
    }

    // =========================================================================
    // TieneTodosLosPermisos
    // =========================================================================

    [Fact]
    public void TieneTodosLosPermisos_UserNull_RetornaFalse()
    {
        Assert.False(((ClaimsPrincipal)null!).TieneTodosLosPermisos(("ventas", "view")));
    }

    [Fact]
    public void TieneTodosLosPermisos_SuperAdmin_RetornaTrue()
    {
        var user = UserAutenticado(role: "SuperAdmin");
        Assert.True(user.TieneTodosLosPermisos(("ventas", "view"), ("clientes", "delete")));
    }

    [Fact]
    public void TieneTodosLosPermisos_TieneTodos_RetornaTrue()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("Permission", "ventas.view"),
            new Claim("Permission", "clientes.view")
        }, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        Assert.True(user.TieneTodosLosPermisos(("ventas", "view"), ("clientes", "view")));
    }

    [Fact]
    public void TieneTodosLosPermisos_FaltaUno_RetornaFalse()
    {
        var user = UserAutenticado(permissionClaim: "ventas.view");
        Assert.False(user.TieneTodosLosPermisos(("ventas", "view"), ("clientes", "view")));
    }

    // =========================================================================
    // ObtenerPermisos
    // =========================================================================

    [Fact]
    public void ObtenerPermisos_UserNull_RetornaVacio()
    {
        var resultado = ((ClaimsPrincipal)null!).ObtenerPermisos();
        Assert.Empty(resultado);
    }

    [Fact]
    public void ObtenerPermisos_ConClaims_RetornaOrdenados()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("Permission", "ventas.view"),
            new Claim("Permission", "clientes.create"),
            new Claim("Permission", "ventas.view") // duplicate
        }, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var resultado = user.ObtenerPermisos().ToList();

        Assert.Equal(2, resultado.Count); // deduplicated
        Assert.Equal("clientes.create", resultado[0]); // ordered
        Assert.Equal("ventas.view", resultado[1]);
    }

    // =========================================================================
    // EsSuperAdmin
    // =========================================================================

    [Fact]
    public void EsSuperAdmin_UserNull_RetornaFalse()
    {
        Assert.False(((ClaimsPrincipal)null!).EsSuperAdmin());
    }

    [Fact]
    public void EsSuperAdmin_ConRolSuperAdmin_RetornaTrue()
    {
        var user = UserAutenticado(role: "SuperAdmin");
        Assert.True(user.EsSuperAdmin());
    }

    [Fact]
    public void EsSuperAdmin_ConOtroRol_RetornaFalse()
    {
        var user = UserAutenticado(role: "Admin");
        Assert.False(user.EsSuperAdmin());
    }
}
