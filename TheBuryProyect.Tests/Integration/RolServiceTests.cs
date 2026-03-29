using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para RolService.
/// Cubre: GetAllRolesAsync, GetRoleByIdAsync, GetRoleByNameAsync, RoleExistsAsync,
/// CreateRoleAsync, UpdateRoleAsync, DeleteRoleAsync, GetRolesInvalidosAsync,
/// GetPermissionsForRoleAsync, AssignPermissionToRoleAsync, RemovePermissionFromRoleAsync,
/// RoleHasPermissionAsync, ClearPermissionsForRoleAsync,
/// GetUsersInRoleAsync, AssignRoleToUserAsync, RemoveRoleFromUserAsync,
/// GetUserRolesAsync, GetUserActiveRolesAsync, UserIsInRoleAsync,
/// GetUserEffectivePermissionsAsync,
/// GetAllModulosAsync, GetModuloByIdAsync, GetModuloByClaveAsync, GetAccionesForModuloAsync,
/// CreateModuloAsync, UpdateModuloAsync, CreateAccionAsync, UpdateAccionAsync,
/// GetRoleMetadataAsync, EnsureRoleMetadataAsync, ToggleRoleActivoAsync,
/// GetRoleUsageStatsAsync.
/// </summary>
public class RolServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly RolService _service;

    public RolServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(dbOptions);
        _context.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequiredLength = 4;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        var provider = services.BuildServiceProvider();
        _userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();

        _service = new RolService(_roleManager, _userManager, _context, NullLogger<RolService>.Instance);
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<IdentityRole> SeedRoleAsync(string roleName)
    {
        var role = new IdentityRole(roleName);
        await _roleManager.CreateAsync(role);
        return role;
    }

    private async Task<ApplicationUser> SeedUserAsync(string userName, bool activo = true)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.com",
            NormalizedEmail = $"{userName}@test.com".ToUpperInvariant(),
            Activo = activo,
            RowVersion = Array.Empty<byte>()
        };
        await _userManager.CreateAsync(user, "Test1234");
        return user;
    }

    private async Task<(ModuloSistema modulo, AccionModulo accion)> SeedModuloConAccionAsync(
        string moduloClave = "ventas", string accionClave = "view")
    {
        var modulo = new ModuloSistema { Nombre = moduloClave, Clave = moduloClave, Activo = true, Orden = 1 };
        _context.ModulosSistema.Add(modulo);
        await _context.SaveChangesAsync();

        var accion = new AccionModulo { Nombre = accionClave, Clave = accionClave, ModuloId = modulo.Id, Activa = true, Orden = 1 };
        _context.AccionesModulo.Add(accion);
        await _context.SaveChangesAsync();

        return (modulo, accion);
    }

    // =========================================================================
    // GetAllRolesAsync
    // =========================================================================

    [Fact]
    public async Task GetAllRolesAsync_SinRoles_RetornaListaVacia()
    {
        var result = await _service.GetAllRolesAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllRolesAsync_ConRoles_RetornaOrdenados()
    {
        await SeedRoleAsync("Zebra");
        await SeedRoleAsync("Admin");

        var result = await _service.GetAllRolesAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Admin", result[0].Name);
        Assert.Equal("Zebra", result[1].Name);
    }

    // =========================================================================
    // GetRoleByIdAsync / GetRoleByNameAsync / RoleExistsAsync
    // =========================================================================

    [Fact]
    public async Task GetRoleByIdAsync_Existente_RetornaRol()
    {
        var rol = await SeedRoleAsync("Admin");
        var result = await _service.GetRoleByIdAsync(rol.Id);
        Assert.NotNull(result);
        Assert.Equal("Admin", result!.Name);
    }

    [Fact]
    public async Task GetRoleByIdAsync_Inexistente_RetornaNull()
    {
        var result = await _service.GetRoleByIdAsync("no-existe");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRoleByNameAsync_Existente_RetornaRol()
    {
        await SeedRoleAsync("Vendedor");
        var result = await _service.GetRoleByNameAsync("Vendedor");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RoleExistsAsync_Existente_RetornaTrue()
    {
        await SeedRoleAsync("Supervisor");
        Assert.True(await _service.RoleExistsAsync("Supervisor"));
    }

    [Fact]
    public async Task RoleExistsAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _service.RoleExistsAsync("NoExiste"));
    }

    // =========================================================================
    // CreateRoleAsync
    // =========================================================================

    [Fact]
    public async Task CreateRoleAsync_NuevoRol_Succeeded()
    {
        var result = await _service.CreateRoleAsync("NuevoRol");

        Assert.True(result.Succeeded);
        Assert.True(await _roleManager.RoleExistsAsync("NuevoRol"));
    }

    [Fact]
    public async Task CreateRoleAsync_RolDuplicado_Falla()
    {
        await SeedRoleAsync("Duplicado");

        var result = await _service.CreateRoleAsync("Duplicado");

        Assert.False(result.Succeeded);
    }

    // =========================================================================
    // UpdateRoleAsync
    // =========================================================================

    [Fact]
    public async Task UpdateRoleAsync_RolExistente_ActualizaNombre()
    {
        var rol = await SeedRoleAsync("NombreViejo");

        var result = await _service.UpdateRoleAsync(rol.Id, "NombreNuevo");

        Assert.True(result.Succeeded);
        var actualizado = await _roleManager.FindByIdAsync(rol.Id);
        Assert.Equal("NombreNuevo", actualizado!.Name);
    }

    [Fact]
    public async Task UpdateRoleAsync_IdInexistente_RetornaError()
    {
        var result = await _service.UpdateRoleAsync("id-que-no-existe", "Nombre");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Description.Contains("no encontrado"));
    }

    // =========================================================================
    // DeleteRoleAsync
    // =========================================================================

    [Fact]
    public async Task DeleteRoleAsync_SinUsuariosActivos_Elimina()
    {
        var rol = await SeedRoleAsync("ParaEliminar");

        var result = await _service.DeleteRoleAsync(rol.Id);

        Assert.True(result.Succeeded);
        Assert.False(await _roleManager.RoleExistsAsync("ParaEliminar"));
    }

    [Fact]
    public async Task DeleteRoleAsync_ConUsuarioActivo_RetornaError()
    {
        var rol = await SeedRoleAsync("ConUsuario");
        var user = await SeedUserAsync("user_activo");
        await _userManager.AddToRoleAsync(user, "ConUsuario");

        var result = await _service.DeleteRoleAsync(rol.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Description.Contains("activo"));
    }

    [Fact]
    public async Task DeleteRoleAsync_IdInexistente_RetornaError()
    {
        var result = await _service.DeleteRoleAsync("no-existe");
        Assert.False(result.Succeeded);
    }

    // =========================================================================
    // GetRolesInvalidosAsync
    // =========================================================================

    [Fact]
    public async Task GetRolesInvalidosAsync_ListaVacia_RetornaVacia()
    {
        var result = await _service.GetRolesInvalidosAsync(Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRolesInvalidosAsync_TodosValidos_RetornaVacia()
    {
        await SeedRoleAsync("Admin");
        await SeedRoleAsync("Vendedor");

        var result = await _service.GetRolesInvalidosAsync(new[] { "Admin", "Vendedor" });

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRolesInvalidosAsync_AlgunosInvalidos_RetornaInvalidos()
    {
        await SeedRoleAsync("Admin");

        var result = await _service.GetRolesInvalidosAsync(new[] { "Admin", "NoExiste" });

        Assert.Single(result);
        Assert.Equal("NoExiste", result[0]);
    }

    // =========================================================================
    // Permisos: AssignPermissionToRoleAsync / GetPermissionsForRoleAsync /
    //           RemovePermissionFromRoleAsync / RoleHasPermissionAsync
    // =========================================================================

    [Fact]
    public async Task GetPermissionsForRoleAsync_SinPermisos_RetornaVacia()
    {
        var rol = await SeedRoleAsync("SinPermisos");
        var result = await _service.GetPermissionsForRoleAsync(rol.Id);
        Assert.Empty(result);
    }

    [Fact]
    public async Task AssignPermissionToRoleAsync_NuevoPermiso_Asigna()
    {
        var rol = await SeedRoleAsync("AdminPermisos");
        var (modulo, accion) = await SeedModuloConAccionAsync("productos", "view");

        var permiso = await _service.AssignPermissionToRoleAsync(rol.Id, modulo.Id, accion.Id);

        Assert.NotNull(permiso);
        Assert.Equal("productos.view", permiso.ClaimValue);

        var permisos = await _service.GetPermissionsForRoleAsync(rol.Id);
        Assert.Single(permisos);
    }

    [Fact]
    public async Task AssignPermissionToRoleAsync_PermisoYaExiste_RetornaExistente()
    {
        var rol = await SeedRoleAsync("AdminPermisosDup");
        var (modulo, accion) = await SeedModuloConAccionAsync("ventas2", "create");

        await _service.AssignPermissionToRoleAsync(rol.Id, modulo.Id, accion.Id);
        var segunda = await _service.AssignPermissionToRoleAsync(rol.Id, modulo.Id, accion.Id);

        var permisos = await _service.GetPermissionsForRoleAsync(rol.Id);
        Assert.Single(permisos); // no duplicates
    }

    [Fact]
    public async Task RemovePermissionFromRoleAsync_PermisoExistente_RetornaTrue()
    {
        var rol = await SeedRoleAsync("RolRemover");
        var (modulo, accion) = await SeedModuloConAccionAsync("clientes", "delete");

        await _service.AssignPermissionToRoleAsync(rol.Id, modulo.Id, accion.Id);
        var removed = await _service.RemovePermissionFromRoleAsync(rol.Id, modulo.Id, accion.Id);

        Assert.True(removed);
        var permisos = await _service.GetPermissionsForRoleAsync(rol.Id);
        Assert.Empty(permisos);
    }

    [Fact]
    public async Task RemovePermissionFromRoleAsync_PermisoInexistente_RetornaFalse()
    {
        var rol = await SeedRoleAsync("RolSinPermiso");
        var (modulo, accion) = await SeedModuloConAccionAsync("reportes", "view");

        var removed = await _service.RemovePermissionFromRoleAsync(rol.Id, modulo.Id, accion.Id);

        Assert.False(removed);
    }

    [Fact]
    public async Task RoleHasPermissionAsync_ConPermiso_RetornaTrue()
    {
        var rol = await SeedRoleAsync("RolConPermiso");
        var (modulo, accion) = await SeedModuloConAccionAsync("inventario", "view");

        await _service.AssignPermissionToRoleAsync(rol.Id, modulo.Id, accion.Id);

        Assert.True(await _service.RoleHasPermissionAsync(rol.Id, "inventario", "view"));
    }

    [Fact]
    public async Task RoleHasPermissionAsync_SinPermiso_RetornaFalse()
    {
        var rol = await SeedRoleAsync("RolSinPermiso2");

        Assert.False(await _service.RoleHasPermissionAsync(rol.Id, "inventario", "view"));
    }

    // =========================================================================
    // Usuarios en roles
    // =========================================================================

    [Fact]
    public async Task GetUsersInRoleAsync_SoloActivos_ExcluyeInactivos()
    {
        await SeedRoleAsync("TeamA");
        var activo = await SeedUserAsync("user_activo_a", activo: true);
        var inactivo = await SeedUserAsync("user_inactivo_a", activo: false);
        await _userManager.AddToRoleAsync(activo, "TeamA");
        await _userManager.AddToRoleAsync(inactivo, "TeamA");

        var result = await _service.GetUsersInRoleAsync("TeamA", includeInactive: false);

        Assert.Single(result);
        Assert.Equal("user_activo_a", result[0].UserName);
    }

    [Fact]
    public async Task GetUsersInRoleAsync_IncluirInactivos_RetornaTodos()
    {
        await SeedRoleAsync("TeamB");
        var activo = await SeedUserAsync("team_b_activo");
        var inactivo = await SeedUserAsync("team_b_inactivo", activo: false);
        await _userManager.AddToRoleAsync(activo, "TeamB");
        await _userManager.AddToRoleAsync(inactivo, "TeamB");

        var result = await _service.GetUsersInRoleAsync("TeamB", includeInactive: true);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task AssignRoleToUserAsync_UserExistente_Asigna()
    {
        await SeedRoleAsync("RolParaAsignar");
        var user = await SeedUserAsync("user_assign");

        var result = await _service.AssignRoleToUserAsync(user.Id, "RolParaAsignar");

        Assert.True(result.Succeeded);
        Assert.True(await _userManager.IsInRoleAsync(user, "RolParaAsignar"));
    }

    [Fact]
    public async Task AssignRoleToUserAsync_UserInexistente_RetornaError()
    {
        await SeedRoleAsync("RolX");

        var result = await _service.AssignRoleToUserAsync("user-no-existe", "RolX");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RemoveRoleFromUserAsync_UserConRol_Quita()
    {
        await SeedRoleAsync("RolParaQuitar");
        var user = await SeedUserAsync("user_remove_role");
        await _userManager.AddToRoleAsync(user, "RolParaQuitar");

        var result = await _service.RemoveRoleFromUserAsync(user.Id, "RolParaQuitar");

        Assert.True(result.Succeeded);
        Assert.False(await _userManager.IsInRoleAsync(user, "RolParaQuitar"));
    }

    [Fact]
    public async Task GetUserRolesAsync_ConRoles_RetornaRoles()
    {
        await SeedRoleAsync("Rol1");
        await SeedRoleAsync("Rol2");
        var user = await SeedUserAsync("user_roles");
        await _userManager.AddToRoleAsync(user, "Rol1");
        await _userManager.AddToRoleAsync(user, "Rol2");

        var roles = await _service.GetUserRolesAsync(user.Id);

        Assert.Equal(2, roles.Count);
        Assert.Contains("Rol1", roles);
    }

    [Fact]
    public async Task GetUserRolesAsync_UserInexistente_RetornaVacio()
    {
        var roles = await _service.GetUserRolesAsync("no-existe");
        Assert.Empty(roles);
    }

    [Fact]
    public async Task UserIsInRoleAsync_UserEnRol_RetornaTrue()
    {
        await SeedRoleAsync("Checkeado");
        var user = await SeedUserAsync("user_check");
        await _userManager.AddToRoleAsync(user, "Checkeado");

        Assert.True(await _service.UserIsInRoleAsync(user.Id, "Checkeado"));
    }

    [Fact]
    public async Task UserIsInRoleAsync_UserSinRol_RetornaFalse()
    {
        await SeedRoleAsync("OtroRol");
        var user = await SeedUserAsync("user_no_rol");

        Assert.False(await _service.UserIsInRoleAsync(user.Id, "OtroRol"));
    }

    [Fact]
    public async Task UserIsInRoleAsync_UserInexistente_RetornaFalse()
    {
        Assert.False(await _service.UserIsInRoleAsync("no-existe", "Admin"));
    }

    // =========================================================================
    // GetUserEffectivePermissionsAsync
    // =========================================================================

    [Fact]
    public async Task GetUserEffectivePermissionsAsync_UserSinRoles_RetornaVacio()
    {
        var user = await SeedUserAsync("user_sin_permisos");

        var result = await _service.GetUserEffectivePermissionsAsync(user.Id);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserEffectivePermissionsAsync_ConPermisos_RetornaPermisos()
    {
        var rol = await SeedRoleAsync("RolConPermisos");
        var (modulo, accion) = await SeedModuloConAccionAsync("caja", "view");
        await _service.AssignPermissionToRoleAsync(rol.Id, modulo.Id, accion.Id);

        var user = await SeedUserAsync("user_con_permisos");
        await _userManager.AddToRoleAsync(user, "RolConPermisos");

        var result = await _service.GetUserEffectivePermissionsAsync(user.Id);

        Assert.Contains("caja.view", result);
    }

    // =========================================================================
    // Módulos y Acciones
    // =========================================================================

    [Fact]
    public async Task GetAllModulosAsync_SinModulos_RetornaVacio()
    {
        var result = await _service.GetAllModulosAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllModulosAsync_ConModulos_RetornaActivos()
    {
        var (modulo, _) = await SeedModuloConAccionAsync("activo_m", "view");

        // Seed inactivo
        var inactivo = new ModuloSistema { Nombre = "Inactivo", Clave = "inactivo", Activo = false, Orden = 99 };
        _context.ModulosSistema.Add(inactivo);
        await _context.SaveChangesAsync();

        var result = await _service.GetAllModulosAsync();

        Assert.DoesNotContain(result, m => m.Clave == "inactivo");
        Assert.Contains(result, m => m.Clave == "activo_m");
    }

    [Fact]
    public async Task GetModuloByIdAsync_Existente_RetornaConAcciones()
    {
        var (modulo, accion) = await SeedModuloConAccionAsync("modulox", "create");

        var result = await _service.GetModuloByIdAsync(modulo.Id);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Acciones);
    }

    [Fact]
    public async Task GetModuloByClaveAsync_ClaveCanonicaMinusculas_Encuentra()
    {
        await SeedModuloConAccionAsync("pagos", "view");

        var result = await _service.GetModuloByClaveAsync("PAGOS");

        Assert.NotNull(result);
        Assert.Equal("pagos", result!.Clave);
    }

    [Fact]
    public async Task GetAccionesForModuloAsync_ConAcciones_RetornaActivas()
    {
        var (modulo, accion) = await SeedModuloConAccionAsync("comp", "edit");

        // Seed inactiva
        var inactiva = new AccionModulo { Nombre = "Inactiva", Clave = "delete", ModuloId = modulo.Id, Activa = false, Orden = 99 };
        _context.AccionesModulo.Add(inactiva);
        await _context.SaveChangesAsync();

        var result = await _service.GetAccionesForModuloAsync(modulo.Id);

        Assert.All(result, a => Assert.True(a.Activa));
        Assert.DoesNotContain(result, a => a.Clave == "delete");
    }

    [Fact]
    public async Task CreateModuloAsync_NuevoModulo_Persiste()
    {
        var modulo = new ModuloSistema { Nombre = "Nuevo", Clave = "NUEVO", Activo = true, Orden = 10 };

        var result = await _service.CreateModuloAsync(modulo);

        Assert.True(result.Id > 0);
        Assert.Equal("nuevo", result.Clave); // canonizado
    }

    [Fact]
    public async Task UpdateModuloAsync_ModuloExistente_ActualizaCampos()
    {
        var (modulo, _) = await SeedModuloConAccionAsync("para_update", "view");

        var update = new ModuloSistema
        {
            Id = modulo.Id,
            Nombre = "Nombre Actualizado",
            Clave = "para_update",
            Activo = true,
            Orden = 5
        };

        var ok = await _service.UpdateModuloAsync(update);

        Assert.True(ok);
        var actualizado = await _context.ModulosSistema.FindAsync(modulo.Id);
        Assert.Equal("Nombre Actualizado", actualizado!.Nombre);
    }

    [Fact]
    public async Task CreateAccionAsync_NuevaAccion_Persiste()
    {
        var (modulo, _) = await SeedModuloConAccionAsync("mod_accion", "view");

        var accion = new AccionModulo
        {
            ModuloId = modulo.Id,
            Nombre = "Exportar",
            Clave = "EXPORT",
            Activa = true,
            Orden = 5
        };

        var result = await _service.CreateAccionAsync(accion);

        Assert.True(result.Id > 0);
        Assert.Equal("export", result.Clave); // canonizado
    }

    [Fact]
    public async Task UpdateAccionAsync_AccionExistente_ActualizaCampos()
    {
        var (modulo, accion) = await SeedModuloConAccionAsync("mod_update_ac", "read");

        var update = new AccionModulo
        {
            Id = accion.Id,
            ModuloId = modulo.Id,
            Nombre = "Leer Todo",
            Clave = "read",
            Activa = true,
            Orden = 2
        };

        var ok = await _service.UpdateAccionAsync(update);

        Assert.True(ok);
        var actualizada = await _context.AccionesModulo.FindAsync(accion.Id);
        Assert.Equal("Leer Todo", actualizada!.Nombre);
    }

    // =========================================================================
    // Metadata de Roles
    // =========================================================================

    [Fact]
    public async Task GetRoleMetadataAsync_SinMetadata_RetornaNull()
    {
        var rol = await SeedRoleAsync("SinMeta");

        var result = await _service.GetRoleMetadataAsync(rol.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task EnsureRoleMetadataAsync_SinMetadata_Crea()
    {
        var rol = await SeedRoleAsync("ConMeta");

        var metadata = await _service.EnsureRoleMetadataAsync(rol.Id, rol.Name);
        await _context.SaveChangesAsync();

        Assert.True(metadata.Activo);
        Assert.Equal(rol.Id, metadata.RoleId);
    }

    [Fact]
    public async Task ToggleRoleActivoAsync_RolExistente_CambiaActivo()
    {
        var rol = await SeedRoleAsync("ToggleMe");

        var nombre = await _service.ToggleRoleActivoAsync(rol.Id, false);

        Assert.Equal("ToggleMe", nombre);
        var metadata = await _service.GetRoleMetadataAsync(rol.Id);
        Assert.NotNull(metadata);
        Assert.False(metadata!.Activo);
    }

    [Fact]
    public async Task ToggleRoleActivoAsync_RolInexistente_RetornaNull()
    {
        var result = await _service.ToggleRoleActivoAsync("no-existe", false);
        Assert.Null(result);
    }

    // =========================================================================
    // GetRoleUsageStatsAsync
    // =========================================================================

    [Fact]
    public async Task GetRoleUsageStatsAsync_SinUsuarios_RetornaCeroParaTodos()
    {
        await SeedRoleAsync("RolStats");

        var stats = await _service.GetRoleUsageStatsAsync();

        Assert.True(stats.ContainsKey("RolStats"));
        Assert.Equal(0, stats["RolStats"]);
    }

    [Fact]
    public async Task GetRoleUsageStatsAsync_ConUsuarios_ContaCorrectamente()
    {
        await SeedRoleAsync("RolConUso");
        var u1 = await SeedUserAsync("stats_user1");
        var u2 = await SeedUserAsync("stats_user2");
        await _userManager.AddToRoleAsync(u1, "RolConUso");
        await _userManager.AddToRoleAsync(u2, "RolConUso");

        var stats = await _service.GetRoleUsageStatsAsync();

        Assert.Equal(2, stats["RolConUso"]);
    }

    // =========================================================================
    // GetUserActiveRolesAsync
    // =========================================================================

    [Fact]
    public async Task GetUserActiveRolesAsync_RolActivo_RetornaRol()
    {
        var rol = await SeedRoleAsync("Activo1");
        var user = await SeedUserAsync("user_active_rol");
        await _userManager.AddToRoleAsync(user, "Activo1");

        var roles = await _service.GetUserActiveRolesAsync(user.Id);

        Assert.Contains("Activo1", roles);
    }
}
