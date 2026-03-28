using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class RolServiceStatsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;
    private readonly RoleManager<IdentityRole> _roleManager;

    public RolServiceStatsTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var roleStore = new RoleStore<IdentityRole>(_context);
        _roleManager = new RoleManager<IdentityRole>(
            roleStore,
            new[] { new RoleValidator<IdentityRole>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

        _service = new RolService(
            _roleManager,
            null!,
            _context,
            NullLogger<RolService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<IdentityRole> SeedRole(string roleName)
    {
        var role = new IdentityRole(roleName);
        await _roleManager.CreateAsync(role);
        return role;
    }

    private async Task<ApplicationUser> SeedUser(string userId)
    {
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"user_{userId}",
            NormalizedUserName = $"USER_{userId.ToUpperInvariant()}",
            Email = $"{userId}@test.com",
            NormalizedEmail = $"{userId.ToUpperInvariant()}@TEST.COM",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task AssignUserToRole(string userId, string roleId)
    {
        _context.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
        await _context.SaveChangesAsync();
    }

    private async Task<(ModuloSistema modulo, AccionModulo accion)> SeedModuloConAccion(
        string moduloClave, string accionClave)
    {
        var modulo = new ModuloSistema
        {
            Nombre = moduloClave,
            Clave = moduloClave,
            Orden = 1,
            Activo = true
        };
        _context.ModulosSistema.Add(modulo);
        await _context.SaveChangesAsync();

        var accion = new AccionModulo
        {
            ModuloId = modulo.Id,
            Nombre = accionClave,
            Clave = accionClave,
            Activa = true
        };
        _context.AccionesModulo.Add(accion);
        await _context.SaveChangesAsync();

        return (modulo, accion);
    }

    private async Task SeedRolPermiso(string roleId, int moduloId, int accionId, string claimValue)
    {
        _context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = moduloId,
            AccionId = accionId,
            ClaimValue = claimValue
        });
        await _context.SaveChangesAsync();
    }

    // =========================================================================
    // GetRoleUsageStatsAsync
    // =========================================================================

    // -------------------------------------------------------------------------
    // 1. Sin roles — devuelve diccionario vacío
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleUsageStats_SinRoles_RetornaDiccionarioVacio()
    {
        var result = await _service.GetRoleUsageStatsAsync();

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // 2. Rol sin usuarios asignados — cuenta 0
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleUsageStats_RolSinUsuarios_RetornaCeroPara()
    {
        await SeedRole("Admin");

        var result = await _service.GetRoleUsageStatsAsync();

        Assert.True(result.ContainsKey("Admin"));
        Assert.Equal(0, result["Admin"]);
    }

    // -------------------------------------------------------------------------
    // 3. Un rol con dos usuarios — cuenta 2
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleUsageStats_RolConDosUsuarios_RetornaDos()
    {
        var role = await SeedRole("Admin");
        await SeedUser("u1");
        await SeedUser("u2");
        await AssignUserToRole("u1", role.Id);
        await AssignUserToRole("u2", role.Id);

        var result = await _service.GetRoleUsageStatsAsync();

        Assert.Equal(2, result["Admin"]);
    }

    // -------------------------------------------------------------------------
    // 4. Dos roles — conteos independientes por rol
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleUsageStats_DosRolesConDiferentesUsuarios_ConteosSeparados()
    {
        var roleAdmin = await SeedRole("Admin");
        var roleVendedor = await SeedRole("Vendedor");
        await SeedUser("u1");
        await SeedUser("u2");
        await SeedUser("u3");
        await AssignUserToRole("u1", roleAdmin.Id);
        await AssignUserToRole("u2", roleVendedor.Id);
        await AssignUserToRole("u3", roleVendedor.Id);

        var result = await _service.GetRoleUsageStatsAsync();

        Assert.Equal(1, result["Admin"]);
        Assert.Equal(2, result["Vendedor"]);
    }

    // -------------------------------------------------------------------------
    // 5. Resultado indexado por nombre de rol (no por ID)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleUsageStats_ClavesDelDiccionarioSonNombres()
    {
        var role = await SeedRole("Supervisor");

        var result = await _service.GetRoleUsageStatsAsync();

        Assert.True(result.ContainsKey("Supervisor"));
        Assert.False(result.ContainsKey(role.Id));
    }

    // =========================================================================
    // GetPermissionsMatrixAsync
    // =========================================================================

    // -------------------------------------------------------------------------
    // 6. Sin roles — devuelve matrix vacía
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPermissionsMatrix_SinRoles_RetornaMatrixVacia()
    {
        var result = await _service.GetPermissionsMatrixAsync();

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // 7. Rol sin permisos — aparece en matrix con listas vacías por módulo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPermissionsMatrix_RolSinPermisos_ApareceConListasVacias()
    {
        await SeedRole("Vendedor");
        await SeedModuloConAccion("ventas", "ver");

        var result = await _service.GetPermissionsMatrixAsync();

        Assert.True(result.ContainsKey("Vendedor"));
        var moduleMap = result["Vendedor"];
        Assert.True(moduleMap.ContainsKey("ventas"));
        Assert.Empty(moduleMap["ventas"]);
    }

    // -------------------------------------------------------------------------
    // 8. Rol con permiso asignado — aparece en la acción correcta del módulo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPermissionsMatrix_RolConPermiso_MuestraAccionEnModulo()
    {
        var role = await SeedRole("Admin");
        var (modulo, accion) = await SeedModuloConAccion("productos", "create");
        await SeedRolPermiso(role.Id, modulo.Id, accion.Id, "productos.create");

        var result = await _service.GetPermissionsMatrixAsync();

        Assert.True(result.ContainsKey("Admin"));
        var acciones = result["Admin"]["productos"];
        Assert.Contains("create", acciones);
    }

    // -------------------------------------------------------------------------
    // 9. Permiso soft-deleted — no aparece en la matrix
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPermissionsMatrix_PermisoSoftDeleted_NoAparece()
    {
        var role = await SeedRole("Admin");
        var (modulo, accion) = await SeedModuloConAccion("productos", "delete");
        await SeedRolPermiso(role.Id, modulo.Id, accion.Id, "productos.delete");

        var permiso = await _context.RolPermisos.FirstAsync();
        permiso.IsDeleted = true;
        await _context.SaveChangesAsync();

        var result = await _service.GetPermissionsMatrixAsync();

        var acciones = result["Admin"]["productos"];
        Assert.DoesNotContain("delete", acciones);
    }

    // -------------------------------------------------------------------------
    // 10. Dos roles — cada uno solo ve sus propios permisos (sin contaminación cruzada)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPermissionsMatrix_DosRoles_PermisosAisladosPorRol()
    {
        var roleAdmin = await SeedRole("Admin");
        var roleVendedor = await SeedRole("Vendedor");
        var (modulo, accionVer) = await SeedModuloConAccion("ventas", "ver");
        var accionCrear = new AccionModulo { ModuloId = modulo.Id, Nombre = "crear", Clave = "crear", Activa = true };
        _context.AccionesModulo.Add(accionCrear);
        await _context.SaveChangesAsync();

        await SeedRolPermiso(roleAdmin.Id, modulo.Id, accionVer.Id, "ventas.ver");
        await SeedRolPermiso(roleVendedor.Id, modulo.Id, accionCrear.Id, "ventas.crear");

        var result = await _service.GetPermissionsMatrixAsync();

        Assert.Contains("ver", result["Admin"]["ventas"]);
        Assert.DoesNotContain("crear", result["Admin"]["ventas"]);

        Assert.Contains("crear", result["Vendedor"]["ventas"]);
        Assert.DoesNotContain("ver", result["Vendedor"]["ventas"]);
    }

    // -------------------------------------------------------------------------
    // 11. Módulo inactivo — no aparece en la matrix
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPermissionsMatrix_ModuloInactivo_NoApareceEnMatrix()
    {
        await SeedRole("Admin");
        var (modulo, _) = await SeedModuloConAccion("pagos", "ver");

        modulo.Activo = false;
        await _context.SaveChangesAsync();

        var result = await _service.GetPermissionsMatrixAsync();

        Assert.True(result.ContainsKey("Admin"));
        Assert.False(result["Admin"].ContainsKey("pagos"));
    }

    // -------------------------------------------------------------------------
    // 12. Claves del diccionario externo son nombres de rol (no IDs)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPermissionsMatrix_ClavesExternas_SonNombresDeRol()
    {
        var role = await SeedRole("Cajero");

        var result = await _service.GetPermissionsMatrixAsync();

        Assert.True(result.ContainsKey("Cajero"));
        Assert.False(result.ContainsKey(role.Id));
    }
}
