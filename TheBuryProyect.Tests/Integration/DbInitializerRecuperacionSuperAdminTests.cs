using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PROMPT 56-C: la única vía de recuperación cuando la instalación perdió TODOS sus SuperAdmin activos es el
/// flujo de inicialización (DbInitializer) para la cuenta administradora configurada; no se crean cuentas ni
/// contraseñas, y con al menos un SuperAdmin activo no se revierte una baja deliberada.
/// </summary>
[Collection("HttpIntegration")]
public class DbInitializerRecuperacionSuperAdminTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];

    public DbInitializerRecuperacionSuperAdminTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        await RolesPermisosSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SinNingunSuperAdminActivo_ReactivaYDesbloqueaLaCuentaConfigurada()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var admin = await CrearSuperAdminAsync(userManager, $"cfg_{_sfx}", activo: false, bloqueado: true);

        var recuperado = await DbInitializer.RecuperarSuperAdminConfiguradoAsync(
            context, userManager, admin, NullLogger.Instance);
        await userManager.UpdateAsync(admin);

        Assert.True(recuperado);
        var db = await context.Users.AsNoTracking().SingleAsync(u => u.Id == admin.Id);
        Assert.True(db.Activo);
        Assert.Null(db.LockoutEnd);
    }

    [Fact]
    public async Task ConOtroSuperAdminActivo_NoRevierteUnaBajaDeliberada()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        await CrearSuperAdminAsync(userManager, $"vigente_{_sfx}", activo: true, bloqueado: false);
        var dadoDeBaja = await CrearSuperAdminAsync(userManager, $"baja_{_sfx}", activo: false, bloqueado: false);

        var recuperado = await DbInitializer.RecuperarSuperAdminConfiguradoAsync(
            context, userManager, dadoDeBaja, NullLogger.Instance);

        Assert.False(recuperado);
        Assert.False((await context.Users.AsNoTracking().SingleAsync(u => u.Id == dadoDeBaja.Id)).Activo);
    }

    [Fact]
    public async Task UnaCuentaSinElRolSuperAdmin_NuncaSeReactivaComoSuperAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var comun = new ApplicationUser { UserName = $"comun_{_sfx}", Email = $"comun_{_sfx}@t.test", Activo = false };
        Assert.True((await userManager.CreateAsync(comun, "Test123!")).Succeeded);

        var recuperado = await DbInitializer.RecuperarSuperAdminConfiguradoAsync(
            context, userManager, comun, NullLogger.Instance);

        Assert.False(recuperado);
        Assert.False((await context.Users.AsNoTracking().SingleAsync(u => u.Id == comun.Id)).Activo);
    }

    private static async Task DesactivarTodosLosSuperAdminAsync(AppDbContext context)
    {
        var ids = await (from ur in context.UserRoles
                         join r in context.Roles on ur.RoleId equals r.Id
                         where r.NormalizedName == "SUPERADMIN"
                         select ur.UserId).ToListAsync();
        foreach (var user in await context.Users.Where(u => ids.Contains(u.Id)).ToListAsync())
            user.Activo = false;
        await context.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> CrearSuperAdminAsync(
        UserManager<ApplicationUser> userManager, string userName, bool activo, bool bloqueado)
    {
        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@t.test", EmailConfirmed = true, Activo = activo };
        Assert.True((await userManager.CreateAsync(user, "Test123!")).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, Roles.SuperAdmin)).Succeeded);
        if (bloqueado)
        {
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        }

        return user;
    }
}
