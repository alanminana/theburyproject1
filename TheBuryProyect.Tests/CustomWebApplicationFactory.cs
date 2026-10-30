using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        typeof(AcuerdoIncumplimientoBackgroundService),
        typeof(AlertaStockBackgroundService),
        typeof(DocumentoVencidoBackgroundService),
        typeof(CotizacionVencimientoBackgroundService),
    ];

    /// <summary>Activa el bloqueo total de TerminosCondicionesMiddleware (producción). Debe fijarse antes del primer uso.</summary>
    public bool ExigirTerminosEnTodasLasPeticiones { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");

        // SEG-BAJA: la suite usa usuarios que no aceptaron los términos; el bloqueo total de TerminosCondicionesMiddleware
        // se prueba de forma explícita con ExigirTerminosEnTodasLasPeticiones = true.
        builder.UseSetting(TheBuryProject.Middleware.TerminosCondicionesMiddleware.ClaveBloqueoTotal,
            ExigirTerminosEnTodasLasPeticiones ? "true" : "false");

        builder.ConfigureServices(services =>
        {
            // Eliminar TODOS los descriptores relacionados con AppDbContext:
            // - IDbContextFactory<AppDbContext>  (servicios del negocio)
            // - AppDbContext scoped               (registrado en Program.cs)
            // - DbContextOptions<AppDbContext>    (registrado internamente por AddEntityFrameworkStores)
            // - IDbContextOptionsConfiguration<AppDbContext> (EF Core 9+: AddDbContext* acumula acá el UseSqlServer de
            //   Program.cs; si no se quita, coexiste con UseSqlite y EF rechaza dos proveedores)
            var dbDescriptors = services
                .Where(d =>
                    d.ServiceType == typeof(IDbContextFactory<AppDbContext>) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions))
                .ToList();

            foreach (var d in dbDescriptors)
                services.Remove(d);

            // Registrar SQLite en memoria para todos los paths de resolución.
            // Se reutiliza la misma SqliteConnection para que todos los scopes compartan la BD.
            // AddInterceptors: ver SqliteRowVersionRotationInterceptor — SQL Server regenera
            // rowversion en cada UPDATE, SQLite no; sin esto los tests de concurrencia HTTP
            // (mismo token en dos requests) no podrían detectar la segunda escritura como stale.
            void SqliteOptions(DbContextOptionsBuilder options) =>
                options.UseSqlite(_connection).AddInterceptors(new SqliteRowVersionRotationInterceptor());

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

    /// <summary>Todo cliente de test lleva el token antiforgery hacia /api/ (ver AntiforgeryApiHandler).</summary>
    public new HttpClient CreateClient() => CreateClient(new WebApplicationFactoryClientOptions());

    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options)
    {
        // CreateDefaultClient no es virtual: se envuelve el cliente de la factory (que conserva cookies y redirecciones)
        // con el handler de antiforgery como capa externa.
        var interno = base.CreateClient(options);
        interno.Timeout = Timeout.InfiniteTimeSpan;
        var antiforgery = new Infrastructure.AntiforgeryApiHandler { InnerHandler = new Infrastructure.ClienteComoHandler(interno) };
        var externo = new HttpClient(antiforgery) { BaseAddress = interno.BaseAddress };
        antiforgery.Cliente = externo;
        return externo;
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

    /// <summary>
    /// Usuario de integración PUN-ML9-B2 con el permiso exacto creditos.view y sin bypass
    /// SuperAdmin. Permite verificar el endpoint read-only con la autorización real.
    /// </summary>
    public const string CreditoViewPermsUserId = "test-credito-view-perms-id";

    public async Task SeedUserWithCreditoViewPermissionAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == CreditoViewPermsUserId))
            return;

        const string roleId = "test-credito-view-role-id";
        const int testModuloId = 9988;
        const int accionCreditoView = 9988;

        context.Roles.Add(new IdentityRole
        {
            Id = roleId,
            Name = "TestCreditoViewRole",
            NormalizedName = "TESTCREDITOVIEWROLE"
        });
        context.Users.Add(new ApplicationUser
        {
            Id = CreditoViewPermsUserId,
            UserName = "testuser-credito-view",
            NormalizedUserName = "TESTUSER-CREDITO-VIEW",
            Email = "testcreditoview@test.com",
            NormalizedEmail = "TESTCREDITOVIEW@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = CreditoViewPermsUserId,
            RoleId = roleId
        });
        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = testModuloId,
            Nombre = "Test Crédito View",
            Clave = "credito-view-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.AccionesModulo.Add(new AccionModulo
        {
            Id = accionCreditoView,
            ModuloId = testModuloId,
            Nombre = "Test Crédito View",
            Clave = "view-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId,
            ModuloId = testModuloId,
            AccionId = accionCreditoView,
            ClaimValue = "creditos.view",
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    public const string CreditoPunitorioBothPermsUserId = "test-credito-punitorio-both-perms-id";

    /// <summary>
    /// Usuario HTTP PUN-ML9-C con creditos.view, cobranzas.applyfine y
    /// cobranzas.revertfine, sin bypass SuperAdmin.
    /// </summary>
    public async Task SeedUserWithCreditoPunitorioBothPermissionsAsync()
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == CreditoPunitorioBothPermsUserId))
            return;

        const string roleId = "test-credito-punitorio-both-role-id";
        const int testModuloId = 9987;
        const int accionCreditoView = 9987;
        const int accionApplyFine = 9986;
        const int accionRevertFine = 9985;

        context.Roles.Add(new IdentityRole
        {
            Id = roleId,
            Name = "TestCreditoPunitorioBothRole",
            NormalizedName = "TESTCREDITOPUNITORIOBOTHROLE"
        });
        context.Users.Add(new ApplicationUser
        {
            Id = CreditoPunitorioBothPermsUserId,
            UserName = "testuser-credito-punitorio-both",
            NormalizedUserName = "TESTUSER-CREDITO-PUNITORIO-BOTH",
            Email = "testcreditopunitorioboth@test.com",
            NormalizedEmail = "TESTCREDITOPUNITORIOBOTH@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string>
        {
            UserId = CreditoPunitorioBothPermsUserId,
            RoleId = roleId
        });
        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = testModuloId,
            Nombre = "Test Credito Punitorio",
            Clave = "credito-punitorio-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.AccionesModulo.AddRange(
            new AccionModulo
            {
                Id = accionCreditoView,
                ModuloId = testModuloId,
                Nombre = "Test Credito View",
                Clave = "view-test",
                Orden = 0,
                RowVersion = new byte[8]
            },
            new AccionModulo
            {
                Id = accionApplyFine,
                ModuloId = testModuloId,
                Nombre = "Test Apply Fine",
                Clave = "applyfine-test",
                Orden = 1,
                RowVersion = new byte[8]
            },
            new AccionModulo
            {
                Id = accionRevertFine,
                ModuloId = testModuloId,
                Nombre = "Test Revert Fine",
                Clave = "revertfine-test",
                Orden = 2,
                RowVersion = new byte[8]
            });
        context.RolPermisos.AddRange(
            new RolPermiso
            {
                RoleId = roleId,
                ModuloId = testModuloId,
                AccionId = accionCreditoView,
                ClaimValue = "creditos.view",
                IsDeleted = false,
                RowVersion = new byte[8]
            },
            new RolPermiso
            {
                RoleId = roleId,
                ModuloId = testModuloId,
                AccionId = accionApplyFine,
                ClaimValue = "cobranzas.applyfine",
                IsDeleted = false,
                RowVersion = new byte[8]
            },
            new RolPermiso
            {
                RoleId = roleId,
                ModuloId = testModuloId,
                AccionId = accionRevertFine,
                ClaimValue = "cobranzas.revertfine",
                IsDeleted = false,
                RowVersion = new byte[8]
            });
        await context.SaveChangesAsync();
    }

    public const string CreditoPayBothPermsUserId = "test-credito-pay-both-perms-id";
    public const string CreditoPayOnlyPermsUserId = "test-credito-pay-only-perms-id";

    public Task SeedUserWithCreditoPayBothPermissionsAsync() =>
        SeedPermissionUserAsync(
            CreditoPayBothPermsUserId,
            "test-credito-pay-both-role-id",
            "TestCreditoPayBothRole",
            9984,
            "creditos.view",
            "cobranzas.payinstallment");

    public Task SeedUserWithCreditoPayOnlyPermissionAsync() =>
        SeedPermissionUserAsync(
            CreditoPayOnlyPermsUserId,
            "test-credito-pay-only-role-id",
            "TestCreditoPayOnlyRole",
            9982,
            "cobranzas.payinstallment");

    public const string ProductoEditSinCostoEnvioUserId = "test-producto-edit-sin-envio-id";
    public const string ProductoEditConCostoEnvioUserId = "test-producto-edit-con-envio-id";

    /// <summary>Puede ver y editar productos, pero NO tiene productos.editshippingcost.</summary>
    public Task SeedUserWithProductoEditSinCostoEnvioAsync() =>
        SeedPermissionUserAsync(
            ProductoEditSinCostoEnvioUserId,
            "test-producto-edit-sin-envio-role-id",
            "TestProductoEditSinEnvioRole",
            9970,
            "productos.view",
            "productos.update");

    /// <summary>Puede ver y editar productos y además tiene productos.editshippingcost.</summary>
    public Task SeedUserWithProductoEditConCostoEnvioAsync() =>
        SeedPermissionUserAsync(
            ProductoEditConCostoEnvioUserId,
            "test-producto-edit-con-envio-role-id",
            "TestProductoEditConEnvioRole",
            9960,
            "productos.view",
            "productos.update",
            "productos.editshippingcost");

    public const string VentaSinDescuentoUserId = "test-venta-sin-descuento-id";
    public const string VentaConDescuentoUserId = "test-venta-con-descuento-id";
    public const string VentaDescuentoRetiradoUserId = "test-venta-descuento-retirado-id";

    public const string ReporteSoloVerUserId = "test-reporte-solo-ver-id";
    public const string TicketsVerUserId = "test-tickets-ver-id";

    /// <summary>COM-S-018: consulta reportes (reportes.view) pero NO puede exportarlos (sin reportes.export).</summary>
    public Task SeedUserWithReporteSoloVerAsync() =>
        SeedPermissionUserAsync(
            ReporteSoloVerUserId, "test-reporte-solo-ver-role-id", "TestReporteSoloVerRole", 9640,
            "reportes.view", "ventas.view");

    /// <summary>COM-S-003: consulta tickets (tickets.view).</summary>
    public Task SeedUserWithTicketsVerAsync() =>
        SeedPermissionUserAsync(
            TicketsVerUserId, "test-tickets-ver-role-id", "TestTicketsVerRole", 9630,
            "tickets.view");

    public const string OrdenCompraSinRecepcionUserId = "test-oc-sin-recepcion-id";
    public const string OrdenCompraConRecepcionUserId = "test-oc-con-recepcion-id";

    /// <summary>COM-S-007: edita y cambia el estado de órdenes de compra (update) SIN poder recepcionar (receive).</summary>
    public Task SeedUserWithOrdenCompraSinRecepcionAsync() =>
        SeedPermissionUserAsync(
            OrdenCompraSinRecepcionUserId, "test-oc-sin-recepcion-role-id", "TestOcSinRecepcionRole", 9620,
            "ordenescompra.view", "ordenescompra.update");

    /// <summary>COM-S-007: igual que el anterior más ordenescompra.receive.</summary>
    public Task SeedUserWithOrdenCompraConRecepcionAsync() =>
        SeedPermissionUserAsync(
            OrdenCompraConRecepcionUserId, "test-oc-con-recepcion-role-id", "TestOcConRecepcionRole", 9610,
            "ordenescompra.view", "ordenescompra.update", "ordenescompra.receive");

    public const string OrdenCompraCancelaUserId = "test-oc-cancela-id";

    /// <summary>H9-M6: ordenescompra.view + update + cancel (puede cancelar y eliminar órdenes).</summary>
    public Task SeedUserWithOrdenCompraCancelaAsync() =>
        SeedPermissionUserAsync(
            OrdenCompraCancelaUserId, "test-oc-cancela-role-id", "TestOcCancelaRole", 9660,
            "ordenescompra.view", "ordenescompra.update", "ordenescompra.cancel");

    public const string ContratoSoloVentasUserId = "test-contrato-solo-ventas-id";
    public const string ContratoConfigurarUserId = "test-contrato-configurar-id";

    /// <summary>FIN-S-004: puede ver ventas pero NO configurar crédito. Debe recibir 403 al generar el contrato.</summary>
    public Task SeedUserWithContratoSoloVentasAsync() =>
        SeedPermissionUserAsync(
            ContratoSoloVentasUserId, "test-contrato-solo-ventas-role-id", "TestContratoSoloVentasRole", 9520,
            "ventas.view");

    /// <summary>FIN-S-004: ventas.view + creditos.configure. Llega a la lógica de generación del contrato.</summary>
    public Task SeedUserWithContratoConfigurarAsync() =>
        SeedPermissionUserAsync(
            ContratoConfigurarUserId, "test-contrato-configurar-role-id", "TestContratoConfigurarRole", 9580,
            "ventas.view", "creditos.view", "creditos.configure");

    public const string PreciosSoloAplicarUserId = "test-precios-solo-aplicar-id";
    public const string PreciosCambioRapidoUserId = "test-precios-cambio-rapido-id";

    /// <summary>Cambio rápido de precios: solo precios.apply (no puede aprobar). Debe recibir 403 en AplicarRapido.</summary>
    public Task SeedUserWithPreciosSoloAplicarAsync() =>
        SeedPermissionUserAsync(
            PreciosSoloAplicarUserId, "test-precios-solo-aplicar-role-id", "TestPreciosSoloAplicarRole", 9540,
            "precios.view", "precios.apply", "productos.view");

    /// <summary>Cambio rápido de precios completo: simular + aprobar + aplicar.</summary>
    public Task SeedUserWithPreciosCambioRapidoCompletoAsync() =>
        SeedPermissionUserAsync(
            PreciosCambioRapidoUserId, "test-precios-cambio-rapido-role-id", "TestPreciosCambioRapidoRole", 9560,
            "precios.view", "precios.simulate", "precios.approve", "precios.apply", "productos.view");

    public const string ConfiguracionActualizaUserId = "test-configuracion-actualiza-id";

    /// <summary>FIN-S-017: edita la configuración (precios de envíos y armados). configuracion.view + configuracion.update.</summary>
    public Task SeedUserWithConfiguracionActualizaAsync() =>
        SeedPermissionUserAsync(
            ConfiguracionActualizaUserId, "test-configuracion-actualiza-role-id", "TestConfiguracionActualizaRole", 9600,
            "configuracion.view", "configuracion.update");

    public const string VentaSoloActualizarUserId = "test-venta-solo-actualizar-id";

    /// <summary>FIN-S-005: edita ventas (ventas.update) pero NO puede confirmarlas (sin ventas.create).</summary>
    public Task SeedUserWithVentaSoloActualizarAsync() =>
        SeedPermissionUserAsync(
            VentaSoloActualizarUserId, "test-venta-solo-actualizar-role-id", "TestVentaSoloActualizarRole", 9650,
            "ventas.view", "ventas.update");

    /// <summary>DESCUENTO-PERMISO-01: opera ventas y cotizaciones (ver/crear/editar/convertir) SIN ventas.discount.</summary>
    public Task SeedUserWithVentaSinDescuentoAsync() =>
        SeedPermissionUserAsync(
            VentaSinDescuentoUserId, "test-venta-sin-descuento-role-id", "TestVentaSinDescuentoRole", 9800,
            "ventas.view", "ventas.create", "ventas.update", "ventas.invoice",
            "cotizaciones.view", "cotizaciones.create", "cotizaciones.convert");

    /// <summary>DESCUENTO-PERMISO-01: mismo perfil que el anterior más el permiso independiente ventas.discount.</summary>
    public Task SeedUserWithVentaConDescuentoAsync() =>
        SeedPermissionUserAsync(
            VentaConDescuentoUserId, "test-venta-con-descuento-role-id", "TestVentaConDescuentoRole", 9780,
            "ventas.view", "ventas.create", "ventas.update", "ventas.invoice", "ventas.discount",
            "cotizaciones.view", "cotizaciones.create", "cotizaciones.convert");

    /// <summary>Igual que ConDescuento, pero luego el test le retira ventas.discount (RetirarPermisoDescuentoAsync).</summary>
    public Task SeedUserWithVentaDescuentoRetirableAsync() =>
        SeedPermissionUserAsync(
            VentaDescuentoRetiradoUserId, "test-venta-descuento-retirado-role-id", "TestVentaDescuentoRetiradoRole", 9760,
            "ventas.view", "ventas.create", "ventas.update", "ventas.discount",
            "cotizaciones.view", "cotizaciones.create");

    /// <summary>Marca como eliminado el RolPermiso ventas.discount del rol de <see cref="VentaDescuentoRetiradoUserId"/>.</summary>
    public Task RetirarPermisoDescuentoAsync() => FijarPermisoDescuentoRetirableAsync(retirado: true);

    /// <summary>
    /// Deja a <see cref="VentaDescuentoRetiradoUserId"/> CON ventas.discount (siembra el usuario si falta). El usuario es
    /// compartido por los tests de la clase: cada test que retira el permiso debe partir de esto para no depender del orden.
    /// </summary>
    public async Task PrepararUsuarioConDescuentoRetirableAsync()
    {
        await SeedUserWithVentaDescuentoRetirableAsync();
        await FijarPermisoDescuentoRetirableAsync(retirado: false);
    }

    private async Task FijarPermisoDescuentoRetirableAsync(bool retirado)
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var permiso = await context.RolPermisos.SingleAsync(rp =>
            rp.RoleId == "test-venta-descuento-retirado-role-id" && rp.ClaimValue == "ventas.discount");
        permiso.IsDeleted = retirado;
        await context.SaveChangesAsync();
    }

    public const string ReporteSinDescuentoUserId = "test-reporte-sin-descuento-id";
    public const string ReporteConDescuentoUserId = "test-reporte-con-descuento-id";

    /// <summary>DESCUENTO-PERMISO-02: consulta y exporta reportes (reportes.view/export) SIN ventas.discount.</summary>
    public Task SeedUserWithReporteSinDescuentoAsync() =>
        SeedPermissionUserAsync(
            ReporteSinDescuentoUserId, "test-reporte-sin-descuento-role-id", "TestReporteSinDescuentoRole", 9740,
            "reportes.view", "reportes.export", "ventas.view");

    /// <summary>DESCUENTO-PERMISO-02: mismo perfil más ventas.discount.</summary>
    public Task SeedUserWithReporteConDescuentoAsync() =>
        SeedPermissionUserAsync(
            ReporteConDescuentoUserId, "test-reporte-con-descuento-role-id", "TestReporteConDescuentoRole", 9720,
            "reportes.view", "reportes.export", "ventas.view", "ventas.discount");

    public const string DocumentosSinDescuentoUserId = "test-docs-sin-descuento-id";
    public const string DocumentosConDescuentoUserId = "test-docs-con-descuento-id";

    /// <summary>DESCUENTO-PERMISO-03: ve ventas, facturas y documentos emitidos (ver/reimprimir) SIN ventas.discount.</summary>
    public Task SeedUserWithDocumentosSinDescuentoAsync() =>
        SeedPermissionUserAsync(
            DocumentosSinDescuentoUserId, "test-docs-sin-descuento-role-id", "TestDocsSinDescuentoRole", 9700,
            "ventas.view", "ventas.invoice", "documentos.view", "documentos.reprint", "cotizaciones.view");

    /// <summary>DESCUENTO-PERMISO-03: mismo perfil más ventas.discount.</summary>
    public Task SeedUserWithDocumentosConDescuentoAsync() =>
        SeedPermissionUserAsync(
            DocumentosConDescuentoUserId, "test-docs-con-descuento-role-id", "TestDocsConDescuentoRole", 9680,
            "ventas.view", "ventas.invoice", "ventas.discount", "documentos.view", "documentos.reprint", "cotizaciones.view");

    public const string StockLectorUserId = "test-stock-lector-id";
    public const string StockOperadorUserId = "test-stock-operador-id";
    public const string ProductoEditConPreciosUserId = "test-producto-edit-con-precios-id";
    public const string ProductoSoloLecturaUserId = "test-producto-solo-lectura-id";

    /// <summary>Consulta stock y Kardex (movimientos.view), sin movimientos.create.</summary>
    public Task SeedUserWithStockLectorAsync() =>
        SeedPermissionUserAsync(
            StockLectorUserId, "test-stock-lector-role-id", "TestStockLectorRole", 9940,
            "movimientos.view", "productos.view");

    /// <summary>Consulta y registra movimientos de stock.</summary>
    public Task SeedUserWithStockOperadorAsync() =>
        SeedPermissionUserAsync(
            StockOperadorUserId, "test-stock-operador-role-id", "TestStockOperadorRole", 9930,
            "movimientos.view", "movimientos.create", "productos.view");

    /// <summary>Edita productos y además puede aplicar cambios de precio (precios.apply).</summary>
    public Task SeedUserWithProductoEditConPreciosAsync() =>
        SeedPermissionUserAsync(
            ProductoEditConPreciosUserId, "test-producto-edit-con-precios-role-id", "TestProductoEditConPreciosRole", 9920,
            "productos.view", "productos.update", "precios.apply");

    /// <summary>Solo consulta productos: no crea, no edita, no toca precios.</summary>
    public Task SeedUserWithProductoSoloLecturaAsync() =>
        SeedPermissionUserAsync(
            ProductoSoloLecturaUserId, "test-producto-solo-lectura-role-id", "TestProductoSoloLecturaRole", 9910,
            "productos.view");

    public const string ProductoSoloCrearUserId = "test-producto-solo-crear-id";

    /// <summary>Consulta y crea productos (productos.create), sin editar, eliminar ni tocar precios.</summary>
    public Task SeedUserWithProductoSoloCrearAsync() =>
        SeedPermissionUserAsync(
            ProductoSoloCrearUserId, "test-producto-solo-crear-role-id", "TestProductoSoloCrearRole", 9900,
            "productos.view", "productos.create");

    public const string ProductoMarcaCompletoUserId = "test-producto-marca-completo-id";

    /// <summary>Opera productos (ver/crear/editar/eliminar/precios) y marcas (ver/editar/eliminar).</summary>
    public Task SeedUserWithProductoYMarcaCompletoAsync() =>
        SeedPermissionUserAsync(
            ProductoMarcaCompletoUserId, "test-producto-marca-completo-role-id", "TestProductoMarcaCompletoRole", 9940,
            "productos.view", "productos.create", "productos.update", "productos.delete", "precios.apply",
            "marcas.view", "marcas.update", "marcas.delete");

    public const string ProductoMarcaSoloLecturaUserId = "test-producto-marca-solo-lectura-id";

    /// <summary>Solo consulta productos y marcas.</summary>
    public Task SeedUserWithProductoYMarcaSoloLecturaAsync() =>
        SeedPermissionUserAsync(
            ProductoMarcaSoloLecturaUserId, "test-producto-marca-solo-lectura-role-id", "TestProductoMarcaSoloLecturaRole", 9960,
            "productos.view", "marcas.view");

    // FIN-S-006: usuario NO administrador con permisos de caja (acreditar/mover/ver). Su acceso a cada caja
    // depende exclusivamente del padron CajaVendedores que sembre el test.
    public const string CajaEditPermsUserId = "test-caja-edit-perms-id";
    public const string CajaEditOtroPermsUserId = "test-caja-edit-otro-perms-id";

    public const string CajaCierrePermsUserId = "test-caja-cierre-perms-id";

    /// <summary>Cajero con caja.close/movements (FIN-S-006: POST con modelo inválido). Sin padrón: lo agrega cada test.</summary>
    public Task SeedUserWithCajaCierrePermissionAsync(string userId = CajaCierrePermsUserId) =>
        SeedPermissionUserAsync(
            userId,
            "role-" + userId,
            "TestCajaCierre" + userId.GetHashCode().ToString("X"),
            9970 + (Math.Abs(userId.GetHashCode()) % 20) * 10 + 5,
            "caja.close",
            "caja.movements",
            "caja.view");

    public Task SeedUserWithCajaEditPermissionAsync(string userId = CajaEditPermsUserId) =>
        SeedPermissionUserAsync(
            userId,
            "role-" + userId,
            "TestCajaEdit" + userId.GetHashCode().ToString("X"),
            9970 + (Math.Abs(userId.GetHashCode()) % 20) * 10,
            "caja.edit",
            "caja.movements",
            "caja.view");

    private async Task SeedPermissionUserAsync(
        string userId,
        string roleId,
        string roleName,
        int moduloId,
        params string[] claims)
    {
        using var scope = Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.Users.AnyAsync(u => u.Id == userId))
            return;

        context.Roles.Add(new IdentityRole
        {
            Id = roleId,
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant()
        });
        context.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = userId,
            NormalizedUserName = userId.ToUpperInvariant(),
            Email = $"{userId}@test.local",
            NormalizedEmail = $"{userId}@test.local".ToUpperInvariant(),
            Activo = true,
            RowVersion = new byte[8]
        });
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = moduloId,
            Nombre = roleName,
            Clave = $"{roleName.ToLowerInvariant()}-test",
            Orden = 0,
            RowVersion = new byte[8]
        });

        for (var index = 0; index < claims.Length; index++)
        {
            var accionId = moduloId - index;
            context.AccionesModulo.Add(new AccionModulo
            {
                Id = accionId,
                ModuloId = moduloId,
                Nombre = claims[index],
                Clave = $"permission-{index}",
                Orden = index,
                RowVersion = new byte[8]
            });
            context.RolPermisos.Add(new RolPermiso
            {
                RoleId = roleId,
                ModuloId = moduloId,
                AccionId = accionId,
                ClaimValue = claims[index],
                IsDeleted = false,
                RowVersion = new byte[8]
            });
        }

        await context.SaveChangesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
