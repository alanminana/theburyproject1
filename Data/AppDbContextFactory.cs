using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TheBuryProject.Data;

/// <summary>
/// Factory para crear AppDbContext en tiempo de diseño (para migraciones de EF).
/// Esta clase es necesaria para que dotnet ef migrations funcione correctamente.
///
/// Resuelve la cadena de conexión desde configuración (appsettings + variables de
/// entorno) para permitir apuntar las migraciones a la base correcta en CI o en
/// otros entornos sin editar código. Si no hay configuración explícita, cae al
/// LocalDB de desarrollo para preservar el comportamiento histórico.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackDevConnection =
        "Server=(localdb)\\mssqllocaldb;Database=TheBuryProjectDb;Trusted_Connection=true;MultipleActiveResultSets=true";

    public AppDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Sin configuración explícita: fallback a LocalDB de desarrollo.
            connectionString = FallbackDevConnection;
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        // Crear el contexto sin IHttpContextAccessor (no está disponible en tiempo de diseño)
        return new AppDbContext(optionsBuilder.Options, null);
    }
}
