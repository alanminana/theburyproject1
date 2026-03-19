using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TheBuryProject.Data;

/// <summary>
/// Factory para crear AppDbContext en tiempo de diseño (para migraciones de EF).
/// Esta clase es necesaria para que dotnet ef migrations funcione correctamente.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Usar la cadena de conexión directamente para migraciones
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=TheBuryProjectDb;Trusted_Connection=true;MultipleActiveResultSets=true"
        );

        // Crear el contexto sin IHttpContextAccessor (no está disponible en tiempo de diseño)
        return new AppDbContext(optionsBuilder.Options, null);
    }
}