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
    /// Crea un HttpClient con el usuario de test autenticado (SuperAdmin).
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
