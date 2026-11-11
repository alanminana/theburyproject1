using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// Punto de entrada de <see cref="ConfiguracionAisladaE2ESeeder"/> (C-03, 2026-11-11). Misma convencion que los otros runners:
    /// exige <c>E2E_SEED_CONNECTION</c> explicita (nunca la base de desarrollo). El workflow ejecuta <c>Activar</c> antes del paso de
    /// Playwright de los tests <c>@config-aislada</c> y <c>Restaurar</c> despues (<c>if: always()</c>).
    /// </summary>
    public class ConfiguracionAisladaE2ESeedRunner
    {
        private const string ConnectionEnvVar = "E2E_SEED_CONNECTION";

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Activar()
        {
            await using var db = CrearContexto();
            await ConfiguracionAisladaE2ESeeder.ActivarAsync(db);
            Console.WriteLine("[config aislada] activada (ValidarDocumentacion=true, Credito personal y planes de tarjeta)");
        }

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Restaurar()
        {
            await using var db = CrearContexto();
            await ConfiguracionAisladaE2ESeeder.RestaurarAsync(db);
            Console.WriteLine("[config aislada] restaurada al estado del dataset comun");
        }

        private static AppDbContext CrearContexto()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                ?? throw new InvalidOperationException($"{ConnectionEnvVar} no configurada.");
            return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
        }
    }
}
