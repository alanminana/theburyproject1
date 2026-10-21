using Microsoft.Extensions.Configuration;
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

    // ───────── PROMPT 57-C: ruta real del arranque (CreateAdminUserAsync) con la cuenta configurada ─────────

    [Fact]
    public async Task Arranque_ConOtroSuperAdminActivo_NoReasignaElRolAUnaCuentaConfiguradaDegradada()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        await CrearSuperAdminAsync(userManager, $"vigente_{_sfx}", activo: true, bloqueado: false);
        var cfg = new ApplicationUser { UserName = $"cfg_{_sfx}", Email = $"cfg_{_sfx}@t.test", EmailConfirmed = true, Activo = true };
        Assert.True((await userManager.CreateAsync(cfg, "Test123!")).Succeeded); // degradada: sin rol SuperAdmin

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfg.UserName!, cfg.Email!), NullLogger.Instance);

        Assert.False(await userManager.IsInRoleAsync(cfg, Roles.SuperAdmin));
    }

    [Fact]
    public async Task Arranque_ConOtroSuperAdminActivo_NoReactivaLaCuentaConfiguradaDeshabilitada()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        await CrearSuperAdminAsync(userManager, $"vigente_{_sfx}", activo: true, bloqueado: false);
        var cfg = await CrearSuperAdminAsync(userManager, $"cfg_{_sfx}", activo: false, bloqueado: false);

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfg.UserName!, cfg.Email!), NullLogger.Instance);

        Assert.False((await context.Users.AsNoTracking().SingleAsync(u => u.Id == cfg.Id)).Activo);
    }

    [Fact]
    public async Task Arranque_SinNingunSuperAdminActivo_RecuperaSoloLaCuentaConfigurada_NoOtraDeshabilitada()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var otra = await CrearSuperAdminAsync(userManager, $"otra_{_sfx}", activo: false, bloqueado: false);
        var cfg = await CrearSuperAdminAsync(userManager, $"cfg_{_sfx}", activo: false, bloqueado: true);

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfg.UserName!, cfg.Email!), NullLogger.Instance);

        Assert.True((await context.Users.AsNoTracking().SingleAsync(u => u.Id == cfg.Id)).Activo);
        Assert.False((await context.Users.AsNoTracking().SingleAsync(u => u.Id == otra.Id)).Activo);
    }

    [Fact]
    public async Task Arranque_SinNingunSuperAdminActivo_ReactivaTambienElRolSiEstabaInactivo()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var cfg = await CrearSuperAdminAsync(userManager, $"cfg_{_sfx}", activo: false, bloqueado: false);
        var roleId = await context.Roles.Where(r => r.NormalizedName == "SUPERADMIN").Select(r => r.Id).SingleAsync();
        var meta = await context.RolMetadatas.SingleOrDefaultAsync(m => m.RoleId == roleId);
        if (meta == null) context.RolMetadatas.Add(new RolMetadata { RoleId = roleId, Activo = false, RowVersion = new byte[8] });
        else meta.Activo = false;
        await context.SaveChangesAsync();

        try
        {
            await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfg.UserName!, cfg.Email!), NullLogger.Instance);

            context.ChangeTracker.Clear();
            var hayUtilizable = await (
                from u in context.Users.AsNoTracking()
                join ur in context.UserRoles.AsNoTracking() on u.Id equals ur.UserId
                join m in context.RolMetadatas.AsNoTracking() on ur.RoleId equals m.RoleId into mm
                from m in mm.DefaultIfEmpty()
                where ur.RoleId == roleId && u.Activo && (m == null || m.Activo)
                select u.Id).AnyAsync();
            Assert.True(hayUtilizable);
        }
        finally
        {
            context.ChangeTracker.Clear();
            foreach (var m in await context.RolMetadatas.Where(m => m.RoleId == roleId).ToListAsync()) m.Activo = true;
            await context.SaveChangesAsync();
        }
    }

    // ───────── PROMPT 58: el arranque no crea otra cuenta SuperAdmin si ya hay administración válida ─────────

    private const string PasswordAprovisionamiento = "Aprov#Test123!";

    private async Task<int> ContarSuperAdminAsync(AppDbContext context) =>
        await (from ur in context.UserRoles.AsNoTracking()
               join r in context.Roles.AsNoTracking() on ur.RoleId equals r.Id
               where r.NormalizedName == "SUPERADMIN"
               select ur.UserId).CountAsync();

    [Fact]
    public async Task Arranque_CuentaConfiguradaEliminada_ConOtroSuperAdminActivo_NoReapareceNingunaCuentaPrivilegiada()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        await CrearSuperAdminAsync(userManager, $"vigente_{_sfx}", activo: true, bloqueado: false);
        var antes = await ContarSuperAdminAsync(context);
        var cfgName = $"cfg_{_sfx}";

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfgName, $"{cfgName}@t.test"), NullLogger.Instance);

        Assert.Null(await userManager.FindByNameAsync(cfgName));
        Assert.Equal(antes, await ContarSuperAdminAsync(context));
    }

    [Fact]
    public async Task Arranque_CambioDeNombreOEmailConfigurado_ConAdministracionValida_NoCreaCuenta()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        await CrearSuperAdminAsync(userManager, $"vigente_{_sfx}", activo: true, bloqueado: false);
        var antes = await ContarSuperAdminAsync(context);

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, $"otro_{_sfx}", $"viejo_{_sfx}@t.test"), NullLogger.Instance);
        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, $"nuevo_{_sfx}", $"nuevo_{_sfx}@t.test"), NullLogger.Instance);

        Assert.Null(await userManager.FindByNameAsync($"otro_{_sfx}"));
        Assert.Null(await userManager.FindByNameAsync($"nuevo_{_sfx}"));
        Assert.Equal(antes, await ContarSuperAdminAsync(context));
    }

    [Fact]
    public async Task Arranque_UnicoSuperAdminBloqueadoTemporalmente_NoCreaOtraCuentaYConservaSuRol()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var bloqueado = await CrearSuperAdminAsync(userManager, $"bloq_{_sfx}", activo: true, bloqueado: true);
        var antes = await ContarSuperAdminAsync(context);
        var cfgName = $"cfg_{_sfx}";

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfgName, $"{cfgName}@t.test"), NullLogger.Instance);

        Assert.Null(await userManager.FindByNameAsync(cfgName));
        Assert.Equal(antes, await ContarSuperAdminAsync(context));
        var db = await context.Users.AsNoTracking().SingleAsync(u => u.Id == bloqueado.Id);
        Assert.True(db.Activo);
        Assert.NotNull(db.LockoutEnd);
        Assert.True(await userManager.IsInRoleAsync(bloqueado, Roles.SuperAdmin));
    }

    [Fact]
    public async Task Arranque_InstalacionSinSuperAdmin_AprovisionaLaPrimeraCuenta_YReejecutarNoDuplica()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var cfgName = $"cfg_{_sfx}";
        var activosAntes = await ContarSuperAdminActivosAsync(context);

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfgName, $"{cfgName}@t.test"), NullLogger.Instance);
        var creada = await userManager.FindByNameAsync(cfgName);
        Assert.NotNull(creada);
        Assert.True(creada!.Activo);
        Assert.True(await userManager.IsInRoleAsync(creada, Roles.SuperAdmin));
        Assert.Equal(activosAntes + 1, await ContarSuperAdminActivosAsync(context));

        await DbInitializer.CreateAdminUserAsync(ProveedorConAdmin(scope, cfgName, $"{cfgName}@t.test"), NullLogger.Instance);

        Assert.Equal(1, await context.Users.AsNoTracking().CountAsync(u => u.NormalizedUserName == cfgName.ToUpperInvariant()));
        Assert.Equal(activosAntes + 1, await ContarSuperAdminActivosAsync(context));
    }

    [Fact]
    public async Task Arranque_FalloAlAsignarElRol_NoDejaUnaCuentaSinRolNiSeInformaComoExito()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inner = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var falla = new UserManagerQueFallaAlAsignarRol(scope.ServiceProvider);
        var cfgName = $"cfg_{_sfx}";
        var candidato = new ApplicationUser { UserName = cfgName, Email = $"{cfgName}@t.test", EmailConfirmed = true, Activo = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DbInitializer.AprovisionarPrimerSuperAdminAsync(context, falla, candidato, PasswordAprovisionamiento, NullLogger.Instance));

        context.ChangeTracker.Clear();
        Assert.Null(await inner.FindByNameAsync(cfgName));
    }

    [Fact]
    public async Task Arranque_Produccion_SinSuperAdmin_ExigeLaConfiguracionDeBootstrap()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DesactivarTodosLosSuperAdminAsync(context);
        var cfgName = $"cfg_{_sfx}";

        await Assert.ThrowsAnyAsync<Exception>(() => DbInitializer.CreateAdminUserAsync(
            ProveedorConAdmin(scope, cfgName, $"{cfgName}@t.test", password: null, produccion: true), NullLogger.Instance));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(cfgName));
    }

    [Fact]
    public async Task Arranque_Produccion_ConSuperAdminActivo_NoExigeNiCreaBootstrap()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DesactivarTodosLosSuperAdminAsync(context);
        await CrearSuperAdminAsync(userManager, $"vigente_{_sfx}", activo: true, bloqueado: false);
        var cfgName = $"cfg_{_sfx}";

        await DbInitializer.CreateAdminUserAsync(
            ProveedorConAdmin(scope, cfgName, $"{cfgName}@t.test", password: null, produccion: true), NullLogger.Instance);

        Assert.Null(await userManager.FindByNameAsync(cfgName));
    }

    private static async Task<int> ContarSuperAdminActivosAsync(AppDbContext context) =>
        await (from u in context.Users.AsNoTracking()
               join ur in context.UserRoles.AsNoTracking() on u.Id equals ur.UserId
               join r in context.Roles.AsNoTracking() on ur.RoleId equals r.Id
               where r.NormalizedName == "SUPERADMIN" && u.Activo
               select u.Id).CountAsync();

    private sealed class UserManagerQueFallaAlAsignarRol(IServiceProvider sp) : UserManager<ApplicationUser>(
        sp.GetRequiredService<IUserStore<ApplicationUser>>(),
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
        sp.GetRequiredService<IPasswordHasher<ApplicationUser>>(),
        sp.GetServices<IUserValidator<ApplicationUser>>(),
        sp.GetServices<IPasswordValidator<ApplicationUser>>(),
        sp.GetRequiredService<ILookupNormalizer>(),
        sp.GetRequiredService<IdentityErrorDescriber>(),
        sp,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserManager<ApplicationUser>>>())
    {
        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "Simulado", Description = "Fallo simulado de rol." }));
    }

    private static IServiceProvider ProveedorConAdmin(
        IServiceScope scope, string userName, string email, string? password = PasswordAprovisionamiento, bool produccion = false)
    {
        var valores = new Dictionary<string, string?> { ["Admin:UserName"] = userName, ["Admin:Email"] = email, ["Admin:Password"] = password };
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddConfiguration(scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>())
            .AddInMemoryCollection(valores)
            .Build();
        return new ProveedorConfig(scope.ServiceProvider, cfg, produccion);
    }

    private sealed class ProveedorConfig(IServiceProvider inner, Microsoft.Extensions.Configuration.IConfiguration cfg, bool produccion = false) : IServiceProvider
    {
        public object? GetService(Type t)
        {
            if (t == typeof(Microsoft.Extensions.Configuration.IConfiguration)) return cfg;
            if (produccion && t == typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment)) return new EntornoProduccion();
            return inner.GetService(t);
        }
    }

    private sealed class EntornoProduccion : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
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
