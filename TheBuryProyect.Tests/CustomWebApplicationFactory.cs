using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TheBuryProject.Data;
using TheBuryProject.Services;
using TheBuryProject.Tests.Infrastructure;

namespace TheBuryProject.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly Type[] BackgroundServicesToRemove =
    [
        typeof(MoraBackgroundService),
        typeof(AlertaStockBackgroundService),
        typeof(DocumentoVencidoBackgroundService),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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

            // Registrar InMemory para todos los paths de resolución
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("TestDb"));

            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseInMemoryDatabase("TestDb"), ServiceLifetime.Scoped);

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

            // Registrar esquema de autenticación fake para tests
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
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
}
