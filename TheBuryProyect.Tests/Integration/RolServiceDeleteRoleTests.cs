using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class RolServiceDeleteRoleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;
    private readonly UserManager<ApplicationUser> _userManager;

    public RolServiceDeleteRoleTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var roleStore = new RoleStore<IdentityRole>(_context);
        var roleManager = new RoleManager<IdentityRole>(
            roleStore,
            new[] { new RoleValidator<IdentityRole>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

        var userStore = new UserStore<ApplicationUser>(_context);
        _userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        _service = new RolService(roleManager, _userManager, _context, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.RolService>.Instance);
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

    private async Task<string> SeedRoleViaService(string nombre, string? descripcion = null, bool activo = true)
    {
        var (_, _, roleId, _) = await _service.CreateRoleWithMetadataAsync(nombre, descripcion, activo);
        return roleId!;
    }

    private async Task<(int moduloId, int accionId)> SeedModuloYAccion(string moduloClave, string accionClave)
    {
        var modulo = new ModuloSistema
        {
            Nombre = moduloClave,
            Clave = moduloClave,
            Orden = 1
        };
        _context.ModulosSistema.Add(modulo);
        await _context.SaveChangesAsync();

        var accion = new AccionModulo
        {
            ModuloId = modulo.Id,
            Nombre = accionClave,
            Clave = accionClave,
            Orden = 1
        };
        _context.AccionesModulo.Add(accion);
        await _context.SaveChangesAsync();

        return (modulo.Id, accion.Id);
    }

    private async Task<ApplicationUser> SeedActiveUser(string userName, string roleName)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@test.com",
            NormalizedUserName = userName.ToUpperInvariant(),
            NormalizedEmail = $"{userName}@test.com".ToUpperInvariant(),
            Activo = true,
            EmailConfirmed = true
        };
        var result = await _userManager.CreateAsync(user, "Test123!");
        if (!result.Succeeded)
            throw new Exception($"Failed to create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        await _userManager.AddToRoleAsync(user, roleName);
        return user;
    }

    // -------------------------------------------------------------------------
    // 1. Delete exitoso: role eliminado, metadata y permisos limpiados
    //    FK cascade hard-deletes related rows after IdentityRole removal.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteRole_Exitoso_EliminaRoleYMetadataYPermisos()
    {
        var roleId = await SeedRoleViaService("Temporal", "Rol temporal");
        var (modId, accId) = await SeedModuloYAccion("ventas", "view");
        await _service.AssignPermissionToRoleAsync(roleId, modId, accId);

        var result = await _service.DeleteRoleAsync(roleId);

        Assert.True(result.Succeeded);

        // IdentityRole hard-deleted
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        Assert.Null(role);

        // Metadata gone (cascade)
        var metadata = await _context.RolMetadatas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.Null(metadata);

        // Permisos gone (cascade)
        var permisos = await _context.RolPermisos
            .IgnoreQueryFilters()
            .CountAsync(rp => rp.RoleId == roleId);
        Assert.Equal(0, permisos);
    }

    // -------------------------------------------------------------------------
    // 2. Con usuarios activos: no elimina nada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteRole_ConUsuariosActivos_NoEliminaNada()
    {
        var roleId = await SeedRoleViaService("Protegido");
        var (modId, accId) = await SeedModuloYAccion("clientes", "create");
        await _service.AssignPermissionToRoleAsync(roleId, modId, accId);

        await SeedActiveUser("testuser", "Protegido");

        var result = await _service.DeleteRoleAsync(roleId);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Description.Contains("usuario(s) activo(s)"));

        // Role sigue existiendo
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        Assert.NotNull(role);

        // Metadata intacta
        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.False(metadata!.IsDeleted);

        // Permisos intactos
        var permisos = await _context.RolPermisos
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .CountAsync();
        Assert.Equal(1, permisos);
    }

    // -------------------------------------------------------------------------
    // 3. Sin metadata previa: no falla
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteRole_SinMetadata_NoFalla()
    {
        // Crear role directamente sin metadata (bypassing CreateRoleWithMetadataAsync)
        _context.Roles.Add(new IdentityRole
        {
            Id = "raw-role-001",
            Name = "RawRole",
            NormalizedName = "RAWROLE",
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
        await _context.SaveChangesAsync();

        var result = await _service.DeleteRoleAsync("raw-role-001");

        Assert.True(result.Succeeded);

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == "raw-role-001");
        Assert.Null(role);
    }

    // -------------------------------------------------------------------------
    // 4. Atomicidad: role + metadata + permisos consistentes al éxito
    //    Nota: FK cascade hard-deletes metadata y permisos cuando se elimina
    //    el IdentityRole. El soft-delete previo es un safety net.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteRole_Atomicidad_TodoConsistenteAlExito()
    {
        var roleId = await SeedRoleViaService("Auditor", "Rol auditor", true);
        var (modId1, accId1) = await SeedModuloYAccion("reportes", "view");
        var (modId2, accId2) = await SeedModuloYAccion("dashboard", "view");
        await _service.AssignPermissionToRoleAsync(roleId, modId1, accId1);
        await _service.AssignPermissionToRoleAsync(roleId, modId2, accId2);

        var result = await _service.DeleteRoleAsync(roleId);

        Assert.True(result.Succeeded);

        // Role gone
        Assert.Equal(0, await _context.Roles.CountAsync(r => r.Id == roleId));

        // Metadata gone (FK cascade hard-deletes after IdentityRole removal)
        var metadataCount = await _context.RolMetadatas
            .IgnoreQueryFilters()
            .CountAsync(m => m.RoleId == roleId);
        Assert.Equal(0, metadataCount);

        // Permisos gone (FK cascade hard-deletes after IdentityRole removal)
        var permisosCount = await _context.RolPermisos
            .IgnoreQueryFilters()
            .CountAsync(rp => rp.RoleId == roleId);
        Assert.Equal(0, permisosCount);
    }
}
