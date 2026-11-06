using System.Security.Claims;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>Regla aprobada: la baja de clientes esta reservada a Administrador y SuperAdmin, aunque otro rol tenga el permiso asignado.</summary>
public class ClientesEliminarSoloAdminTests
{
    private static ClaimsPrincipal Usuario(string? rol, bool conPermisoDelete)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "u") };
        if (rol != null) claims.Add(new Claim(ClaimTypes.Role, rol));
        if (conPermisoDelete) claims.Add(new Claim("Permission", "clientes.delete"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Theory]
    [InlineData("Administrador", true, true)]
    [InlineData("SuperAdmin", false, true)]      // SuperAdmin siempre
    [InlineData("Gerente", true, false)]         // tiene el permiso (bases anteriores a la regla) pero no el rol
    [InlineData("Vendedor", true, false)]
    [InlineData("Administrador", false, false)]  // el rol solo no alcanza: tambien hace falta el permiso
    [InlineData(null, false, false)]
    public void PuedeEliminarClientes_RequierePermisoYRolAdministrador(string? rol, bool permiso, bool esperado)
        => Assert.Equal(esperado, Usuario(rol, permiso).PuedeEliminarClientes());
}
