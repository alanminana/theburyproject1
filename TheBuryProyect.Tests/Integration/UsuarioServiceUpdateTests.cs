using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

public class UsuarioServiceUpdateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UsuarioService _service;

    public UsuarioServiceUpdateTests()
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

        _service = new UsuarioService(_userManager, _context, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.UsuarioService>.Instance);
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _roleManager.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<ApplicationUser> SeedUser(string userName, string email, bool activo = true)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            Activo = activo,
            Nombre = "Test",
            Apellido = "User"
        };
        var result = await _userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task SeedRole(string roleName)
    {
        var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
        Assert.True(result.Succeeded);
    }

    private UsuarioUpdateRequest BuildRequest(ApplicationUser user, List<string>? roles = null)
    {
        return new UsuarioUpdateRequest
        {
            UserId = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            Nombre = user.Nombre,
            Apellido = user.Apellido,
            Telefono = user.Telefono,
            SucursalId = user.SucursalId,
            SucursalNombre = user.Sucursal,
            Activo = user.Activo,
            RolesDeseados = roles ?? [],
            RowVersion = user.RowVersion,
            EditadoPor = "admin-test"
        };
    }

    // -------------------------------------------------------------------------
    // 1. Actualiza datos básicos del usuario
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsuario_ActualizaDatosBasicos()
    {
        var user = await SeedUser("juan", "juan@test.com");

        var request = BuildRequest(user);
        request = request with
        {
            UserName = "juan_updated",
            Email = "juan_new@test.com",
            Nombre = "Juan",
            Apellido = "Pérez"
        };

        var result = await _service.UpdateUsuarioAsync(request);

        Assert.True(result.Ok);

        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.Equal("juan_updated", updated!.UserName);
        Assert.Equal("juan_new@test.com", updated.Email);
        Assert.Equal("Juan", updated.Nombre);
        Assert.Equal("Pérez", updated.Apellido);
    }

    // -------------------------------------------------------------------------
    // 2. Asigna roles correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsuario_AsignaRoles()
    {
        var user = await SeedUser("maria", "maria@test.com");
        await SeedRole("Admin");
        await SeedRole("Vendedor");

        var request = BuildRequest(user, ["Admin", "Vendedor"]);

        var result = await _service.UpdateUsuarioAsync(request);

        Assert.True(result.Ok);

        var roles = await _userManager.GetRolesAsync(user);
        Assert.Contains("Admin", roles);
        Assert.Contains("Vendedor", roles);
    }

    // -------------------------------------------------------------------------
    // 3. Remueve roles que ya no están en la lista
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsuario_RemueveRolesNoDeseados()
    {
        var user = await SeedUser("pedro", "pedro@test.com");
        await SeedRole("Admin");
        await SeedRole("Cajero");
        await _userManager.AddToRoleAsync(user, "Admin");
        await _userManager.AddToRoleAsync(user, "Cajero");

        // Refresh RowVersion after role changes
        await _context.Entry(user).ReloadAsync();

        var request = BuildRequest(user, ["Admin"]); // Solo Admin, sin Cajero

        var result = await _service.UpdateUsuarioAsync(request);

        Assert.True(result.Ok);

        var roles = await _userManager.GetRolesAsync(user);
        Assert.Contains("Admin", roles);
        Assert.DoesNotContain("Cajero", roles);
    }

    // -------------------------------------------------------------------------
    // 4. Tracking de desactivación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsuario_DesactivaConTracking()
    {
        var user = await SeedUser("carlos", "carlos@test.com", activo: true);

        var request = BuildRequest(user) with { Activo = false };

        var result = await _service.UpdateUsuarioAsync(request);

        Assert.True(result.Ok);

        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.False(updated!.Activo);
        Assert.NotNull(updated.FechaDesactivacion);
        Assert.Equal("admin-test", updated.DesactivadoPor);
    }

    // -------------------------------------------------------------------------
    // 5. Tracking de reactivación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsuario_ReactivaLimpiaTracking()
    {
        var user = await SeedUser("lucia", "lucia@test.com", activo: false);
        user.FechaDesactivacion = DateTime.UtcNow.AddDays(-5);
        user.DesactivadoPor = "otro-admin";
        user.MotivoDesactivacion = "motivo previo";
        await _userManager.UpdateAsync(user);
        await _context.Entry(user).ReloadAsync();

        var request = BuildRequest(user) with { Activo = true };

        var result = await _service.UpdateUsuarioAsync(request);

        Assert.True(result.Ok);

        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.True(updated!.Activo);
        Assert.Null(updated.FechaDesactivacion);
        Assert.Null(updated.DesactivadoPor);
        Assert.Null(updated.MotivoDesactivacion);
    }

    // -------------------------------------------------------------------------
    // 6. Usuario no encontrado retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsuario_UsuarioNoExiste_RetornaError()
    {
        var request = new UsuarioUpdateRequest
        {
            UserId = "inexistente",
            UserName = "x",
            Email = "x@test.com",
            RolesDeseados = [],
            RowVersion = [1, 2, 3],
            EditadoPor = "admin"
        };

        var result = await _service.UpdateUsuarioAsync(request);

        Assert.False(result.Ok);
        Assert.NotEmpty(result.Errors);
    }

    // -------------------------------------------------------------------------
    // 7. Atomicidad: update + roles en misma transacción
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsuario_ActualizaDatosYRolesAtomicamente()
    {
        var user = await SeedUser("atomico", "atomico@test.com");
        await SeedRole("Gerente");

        var request = BuildRequest(user, ["Gerente"]) with
        {
            UserName = "atomico_v2",
            Nombre = "Atómico",
            Activo = true
        };

        var result = await _service.UpdateUsuarioAsync(request);

        Assert.True(result.Ok);

        var updated = await _userManager.FindByIdAsync(user.Id);
        Assert.Equal("atomico_v2", updated!.UserName);
        Assert.Equal("Atómico", updated.Nombre);

        var roles = await _userManager.GetRolesAsync(updated);
        Assert.Single(roles);
        Assert.Equal("Gerente", roles[0]);
    }
}
