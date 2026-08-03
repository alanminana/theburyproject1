using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Tests.Infrastructure;

namespace TheBuryProject.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Conexión SQLite en memoria compartida: debe permanecer abierta durante toda la vida
    // del factory para que la BD no sea descartada entre requests / scopes.
    // SQLite soporta ExecuteUpdate/ExecuteDelete y transacciones, a diferencia de InMemory.
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    private static readonly Type[] BackgroundServicesToRemove =
    [
        typeof(MoraBackgroundService),
        typeof(AlertaStockBackgroundService),
        typeof(DocumentoVencidoBackgroundService),
        typeof(CotizacionVencimientoBackgroundService),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Eliminar TODOS los descriptores relacionados con AppDbContext:
            // - IDbContextFactory<AppDbContext>  (servicios del negocio)
            // - AppDbContext scoped               (registrado en Program.cs)
            // - DbContextOptions<AppDbContext>    (registrado internamente por AddEntityFrameworkStores)
            var dbDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(IDbContextFactory<AppDbContext>) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions))
                .ToList();

            foreach (var d in dbDescriptors)
                services.Remove(d);

            // Registrar SQLite en memoria para todos los paths de resolución.
            // Se reutiliza la misma SqliteConnection para que todos los scopes compartan la BD.
            void SqliteOptions(DbContextOptionsBuilder options) =>
                options.UseSqlite(_connection);

            services.AddDbContext<AppDbContext>(SqliteOptions);
            services.AddDbContextFactory<AppDbContext>(SqliteOptions, ServiceLifetime.Scoped);

            // Remover background services que intentan conectar a SQL Server.
            // IHostedService se registra por tipo de implementación, no por interfaz,
            // por lo que hay que buscar por ImplementationType.
            var hostedToRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType != null
                            && BackgroundServicesToRemove.Contains(d.ImplementationType))
                .ToList();

            foreach (var descriptor in hostedToRemove)
                services.Remove(descriptor);

            // Registrar esquema de autenticación fake para tests.
            // Se reconfiguran explícitamente todos los defaults porque AddDefaultIdentity
            // ya registró Identity.Application como scheme por defecto y tiene precedencia.
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Crear esquema SQLite al iniciar el host.
        // EnsureCreated() aplica el modelo EF sin migrations — adecuado para tests.
        using var scope = host.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var context = contextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        return host;
    }

    /// <summary>
    /// Siembra en la BD de test el usuario y su rol SuperAdmin.
    /// PermissionClaimsTransformation consulta UserRoles en BD — sin este seed
    /// elimina el claim SuperAdmin del principal al no encontrarlo en BD.
    /// </summary>
    public async Task SeedTestUserAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == TestAuthHandler.UserId))
            return;

        var roleId = "test-role-id";
        context.Roles.Add(new IdentityRole { Id = roleId, Name = "SuperAdmin", NormalizedName = "SUPERADMIN" });
        context.Users.Add(new ApplicationUser
        {
            Id = TestAuthHandler.UserId,
            UserName = "testuser",
            NormalizedUserName = "TESTUSER",
            Email = "test@test.com",
            NormalizedEmail = "TEST@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = TestAuthHandler.UserId, RoleId = roleId });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// User ID para tests que necesitan un usuario autenticado sin permisos en DB.
    /// Al no estar seedeado, PermissionClaimsTransformation devuelve roles/permisos vacíos → 403 en endpoints con permiso requerido.
    /// </summary>
    public const string NoPermsUserId = "test-no-perms-id";

    /// <summary>
    /// User ID para tests que necesitan un usuario con cotizaciones.convert pero sin SuperAdmin.
    /// Seedear con SeedUserWithConvertPermissionAsync antes de usar.
    /// </summary>
    public const string ConvertPermsUserId = "test-convert-perms-id";

    /// <summary>
    /// User ID para tests que necesitan un usuario con cotizaciones.cancel pero sin SuperAdmin.
    /// Seedear con SeedUserWithCancelPermissionAsync antes de usar.
    /// </summary>
    public const string CancelPermsUserId = "test-cancel-perms-id";

    /// <summary>
    /// User ID para tests que necesitan un usuario con cotizaciones.expire pero sin SuperAdmin.
    /// Seedear con SeedUserWithExpirePermissionAsync antes de usar.
    /// </summary>
    public const string ExpirePermsUserId = "test-expire-perms-id";

    /// <summary>
    /// HttpClient.Timeout por defecto (100s) es insuficiente en suite completa: bajo contención de CPU
    /// del proceso de test (colecciones xUnit en paralelo) el round-trip in-process contra TestServer
    /// puede superarlo aunque el request esté procesándose correctamente (ver docs/fase-kira-fix-testhost-flakiness.md).
    /// No es un límite de negocio — es un default heredado de HttpClient sin relación con este dominio.
    /// </summary>
    private static readonly TimeSpan HttpTestClientTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Crea un HttpClient con el usuario de test autenticado (SuperAdmin).
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.Timeout = HttpTestClientTimeout;
        return client;
    }

    /// <summary>
    /// Crea un HttpClient autenticado con un userId específico (sobreescribe el default SuperAdmin via header).
    /// Útil para tests de seguridad: si el userId no está en DB, PermissionClaimsTransformation vacía los permisos.
    /// </summary>
    public HttpClient CreateClientWithUserId(string userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.Timeout = HttpTestClientTimeout;
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    /// <summary>
    /// Siembra en la BD de test un usuario con permiso cotizaciones.convert (sin SuperAdmin).
    /// Diseñado para tests que verifican acceso por permiso específico, no por rol SuperAdmin.
    /// Idempotente: no duplica si el usuario ya existe.
    /// </summary>
    public async Task SeedUserWithConvertPermissionAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == ConvertPermsUserId))
            return;

        const string roleId = "test-convert-role-id";
        const int testModuloId = 9999;
        const int testAccionId = 9999;

        context.Roles.Add(new IdentityRole { Id = roleId, Name = "TestConvertRole", NormalizedName = "TESTCONVERTROLE" });
        context.Users.Add(new ApplicationUser
        {
            Id = ConvertPermsUserId,
            UserName = "testuser-convert",
            NormalizedUserName = "TESTUSER-CONVERT",
            Email = "testconvert@test.com",
            NormalizedEmail = "TESTCONVERT@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = ConvertPermsUserId, RoleId = roleId });
        await context.SaveChangesAsync();

        // EF Core SQLite activa PRAGMA foreign_keys = ON: los registros padre son obligatorios.
        // Se usan IDs de test (9999) que no colisionan con el seed de producción.
        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = testModuloId,
            Nombre = "Test Cotizaciones",
            Clave = "cotizaciones-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        // AccionModulo para "convert" (id=9999) y "view" (id=9998)
        // CotizacionApiController requiere cotizaciones.view a nivel clase + cotizaciones.convert a nivel método.
        context.AccionesModulo.Add(new AccionModulo
        {
            Id = testAccionId,
            ModuloId = testModuloId,
            Nombre = "Test Convert",
            Clave = "convert-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.AccionesModulo.Add(new AccionModulo
        {
            Id = testAccionId - 1,
            ModuloId = testModuloId,
            Nombre = "Test View",
            Clave = "view-test",
            Orden = 1,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = testAccionId,
            ClaimValue = "cotizaciones.convert",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = testAccionId - 1,
            ClaimValue = "cotizaciones.view",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Siembra en la BD de test un usuario con permiso cotizaciones.cancel (sin SuperAdmin).
    /// Diseñado para tests que verifican acceso por permiso específico, no por rol SuperAdmin.
    /// Idempotente: no duplica si el usuario ya existe.
    /// </summary>
    public async Task SeedUserWithCancelPermissionAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == CancelPermsUserId))
            return;

        const string roleId = "test-cancel-role-id";
        const int testModuloId = 9997;
        const int testAccionId = 9997;

        context.Roles.Add(new IdentityRole { Id = roleId, Name = "TestCancelRole", NormalizedName = "TESTCANCELROLE" });
        context.Users.Add(new ApplicationUser
        {
            Id = CancelPermsUserId,
            UserName = "testuser-cancel",
            NormalizedUserName = "TESTUSER-CANCEL",
            Email = "testcancel@test.com",
            NormalizedEmail = "TESTCANCEL@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = CancelPermsUserId, RoleId = roleId });
        await context.SaveChangesAsync();

        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = testModuloId,
            Nombre = "Test Cotizaciones Cancel",
            Clave = "cotizaciones-cancel-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.AccionesModulo.Add(new AccionModulo
        {
            Id = testAccionId,
            ModuloId = testModuloId,
            Nombre = "Test Cancel",
            Clave = "cancel-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.AccionesModulo.Add(new AccionModulo
        {
            Id = testAccionId - 1,
            ModuloId = testModuloId,
            Nombre = "Test View Cancel",
            Clave = "view-cancel-test",
            Orden = 1,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = testAccionId,
            ClaimValue = "cotizaciones.cancel",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = testAccionId - 1,
            ClaimValue = "cotizaciones.view",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Siembra en la BD de test un usuario con permiso cotizaciones.expire (sin SuperAdmin).
    /// Diseñado para tests que verifican acceso por permiso específico, no por rol SuperAdmin.
    /// Idempotente: no duplica si el usuario ya existe.
    /// </summary>
    public async Task SeedUserWithExpirePermissionAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == ExpirePermsUserId))
            return;

        const string roleId = "test-expire-role-id";
        const int testModuloId = 9995;
        const int testAccionId = 9995;

        context.Roles.Add(new IdentityRole { Id = roleId, Name = "TestExpireRole", NormalizedName = "TESTEXPIREROLE" });
        context.Users.Add(new ApplicationUser
        {
            Id = ExpirePermsUserId,
            UserName = "testuser-expire",
            NormalizedUserName = "TESTUSER-EXPIRE",
            Email = "testexpire@test.com",
            NormalizedEmail = "TESTEXPIRE@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = ExpirePermsUserId, RoleId = roleId });
        await context.SaveChangesAsync();

        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = testModuloId,
            Nombre = "Test Cotizaciones Expire",
            Clave = "cotizaciones-expire-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.AccionesModulo.Add(new AccionModulo
        {
            Id = testAccionId,
            ModuloId = testModuloId,
            Nombre = "Test Expire",
            Clave = "expire-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.AccionesModulo.Add(new AccionModulo
        {
            Id = testAccionId - 1,
            ModuloId = testModuloId,
            Nombre = "Test View Expire",
            Clave = "view-expire-test",
            Orden = 1,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = testAccionId,
            ClaimValue = "cotizaciones.expire",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = testAccionId - 1,
            ClaimValue = "cotizaciones.view",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// User ID para tests PUN-ML8 que necesitan un usuario con acceso completo a la pestaña de
    /// punitorios (configuracion.view + viewpunitorio + managepunitorio + retroactivepunitorio),
    /// sin SuperAdmin. Seedear con <see cref="SeedUserWithPunitorioManagePermissionAsync"/>.
    /// </summary>
    public const string PunitorioManagePermsUserId = "test-punitorio-manage-perms-id";

    /// <summary>
    /// User ID para tests PUN-ML8 que necesitan un usuario que puede ver la página de crédito
    /// personal (configuracion.view) pero NO tiene ningún permiso específico de punitorios.
    /// Seedear con <see cref="SeedUserWithConfiguracionViewOnlyPermissionAsync"/>.
    /// </summary>
    public const string PunitorioNoAccessUserId = "test-punitorio-no-access-id";

    /// <summary>
    /// Siembra en la BD de test un usuario con configuracion.view + viewpunitorio + managepunitorio +
    /// retroactivepunitorio (sin SuperAdmin). Módulo/acciones de test — no colisionan con el seed de
    /// producción real (que no corre en el entorno "Testing", ver Program.cs). Idempotente.
    /// </summary>
    public async Task SeedUserWithPunitorioManagePermissionAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == PunitorioManagePermsUserId))
            return;

        const string roleId = "test-punitorio-manage-role-id";
        const int testModuloId = 9993;
        const int accionManage = 9993;
        const int accionView = 9992;
        const int accionRetro = 9991;
        const int accionConfigView = 9990;

        context.Roles.Add(new IdentityRole { Id = roleId, Name = "TestPunitorioManageRole", NormalizedName = "TESTPUNITORIOMANAGEROLE" });
        context.Users.Add(new ApplicationUser
        {
            Id = PunitorioManagePermsUserId,
            UserName = "testuser-punitorio-manage",
            NormalizedUserName = "TESTUSER-PUNITORIO-MANAGE",
            Email = "testpunitoriomanage@test.com",
            NormalizedEmail = "TESTPUNITORIOMANAGE@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = PunitorioManagePermsUserId, RoleId = roleId });
        await context.SaveChangesAsync();

        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = testModuloId,
            Nombre = "Test Configuracion Punitorio",
            Clave = "configuracion-punitorio-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.AccionesModulo.AddRange(
            new AccionModulo { Id = accionManage, ModuloId = testModuloId, Nombre = "Test Manage Punitorio", Clave = "managepunitorio-test", Orden = 0, RowVersion = new byte[8] },
            new AccionModulo { Id = accionView, ModuloId = testModuloId, Nombre = "Test View Punitorio", Clave = "viewpunitorio-test", Orden = 1, RowVersion = new byte[8] },
            new AccionModulo { Id = accionRetro, ModuloId = testModuloId, Nombre = "Test Retroactive Punitorio", Clave = "retroactivepunitorio-test", Orden = 2, RowVersion = new byte[8] },
            new AccionModulo { Id = accionConfigView, ModuloId = testModuloId, Nombre = "Test Configuracion View", Clave = "view-configuracion-test", Orden = 3, RowVersion = new byte[8] });
        await context.SaveChangesAsync();

        context.RolPermisos.AddRange(
            new RolPermiso { RoleId = roleId, ModuloId = testModuloId, AccionId = accionManage, ClaimValue = "configuracion.managepunitorio", IsDeleted = false, RowVersion = new byte[8] },
            new RolPermiso { RoleId = roleId, ModuloId = testModuloId, AccionId = accionView, ClaimValue = "configuracion.viewpunitorio", IsDeleted = false, RowVersion = new byte[8] },
            new RolPermiso { RoleId = roleId, ModuloId = testModuloId, AccionId = accionRetro, ClaimValue = "configuracion.retroactivepunitorio", IsDeleted = false, RowVersion = new byte[8] },
            new RolPermiso { RoleId = roleId, ModuloId = testModuloId, AccionId = accionConfigView, ClaimValue = "configuracion.view", IsDeleted = false, RowVersion = new byte[8] });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Siembra en la BD de test un usuario con únicamente configuracion.view (sin ningún permiso
    /// de punitorios, sin SuperAdmin) — simula un usuario que puede abrir /ConfiguracionPago/CreditoPersonal
    /// pero no tiene acceso a la pestaña "Punitorios por mora". Idempotente.
    /// </summary>
    public async Task SeedUserWithConfiguracionViewOnlyPermissionAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == PunitorioNoAccessUserId))
            return;

        const string roleId = "test-punitorio-no-access-role-id";
        const int testModuloId = 9989;
        const int accionConfigView = 9989;

        context.Roles.Add(new IdentityRole { Id = roleId, Name = "TestPunitorioNoAccessRole", NormalizedName = "TESTPUNITORIONOACCESSROLE" });
        context.Users.Add(new ApplicationUser
        {
            Id = PunitorioNoAccessUserId,
            UserName = "testuser-punitorio-noaccess",
            NormalizedUserName = "TESTUSER-PUNITORIO-NOACCESS",
            Email = "testpunitorionoaccess@test.com",
            NormalizedEmail = "TESTPUNITORIONOACCESS@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = PunitorioNoAccessUserId, RoleId = roleId });
        await context.SaveChangesAsync();

        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = testModuloId,
            Nombre = "Test Configuracion View Only",
            Clave = "configuracion-view-only-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.AccionesModulo.Add(new AccionModulo
        {
            Id = accionConfigView,
            ModuloId = testModuloId,
            Nombre = "Test Configuracion View",
            Clave = "view-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = accionConfigView,
            ClaimValue = "configuracion.view",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
