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
    /// Crea un HttpClient con el usuario de test autenticado (SuperAdmin).
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
