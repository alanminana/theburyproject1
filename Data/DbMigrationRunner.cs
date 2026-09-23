using Microsoft.EntityFrameworkCore;

namespace TheBuryProject.Data
{
    /// <summary>
    /// Ejecuta la inicializacion de base de datos (migraciones + seeds) como proceso independiente (`TheBuryProyect.dll --migrate`),
    /// para que el proceso web no necesite permisos DDL. Reutiliza <see cref="DbInitializer"/> sin cambiar su logica.
    /// </summary>
    public static class DbMigrationRunner
    {
        /// <returns>0 si el esquema queda al dia; 1 ante cualquier fallo (el orquestador no debe arrancar la app).</returns>
        public static async Task<int> RunAsync(IHost app)
        {
            var services = app.Services;
            var logger = services.GetRequiredService<ILogger<Program>>();

            try
            {
                await DbInitializer.Initialize(services);

                if (app.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
                {
                    await DbInitializer.CreateTestUsersAsync(services);
                }

                // DbInitializer omite migrar (solo con warning) si encuentra tablas sin historial coherente: aqui eso es un fallo.
                using var scope = services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count > 0)
                {
                    logger.LogCritical("Quedan {Count} migraciones pendientes tras ejecutar migrate; la app no debe arrancar.", pending.Count);
                    Console.Error.WriteLine($"[migrate] FALLO: {pending.Count} migraciones pendientes.");
                    return 1;
                }

                var applied = (await context.Database.GetAppliedMigrationsAsync()).Count();
                logger.LogInformation("migrate OK: {Applied} migraciones aplicadas, 0 pendientes.", applied);
                // Salida directa: en Production el nivel de log por defecto oculta Information y este es el unico rastro del resultado.
                Console.WriteLine($"[migrate] OK: {applied} migraciones aplicadas, 0 pendientes.");
                return 0;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "migrate FALLO: no se pudo dejar la base de datos actualizada.");
                Console.Error.WriteLine("[migrate] FALLO: no se pudo dejar la base de datos actualizada (ver el error registrado arriba).");
                return 1;
            }
        }
    }
}
