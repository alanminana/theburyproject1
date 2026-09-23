using System;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// Punto de entrada para sembrar/limpiar los clientes y productos genéricos de
    /// <see cref="GenericE2ESeeder"/> contra una base descartable explícita (misma convención que
    /// <see cref="ClienteAptitudPunitorioE2ESeedRunner"/>: exige <c>E2E_SEED_CONNECTION</c>, nunca
    /// usa el connection string por defecto de la app).
    ///
    /// Uso (PowerShell), NUNCA contra la base de desarrollo:
    /// <code>
    /// $env:E2E_SEED_CONNECTION = "Server=db,1433;Database=TheBuryProjectDb;..."
    /// dotnet test TheBuryProyect.Tests --filter "FullyQualifiedName~GenericE2ESeedRunner.Sembrar"
    /// </code>
    /// </summary>
    public class GenericE2ESeedRunner
    {
        private const string ConnectionEnvVar = "E2E_SEED_CONNECTION";

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async System.Threading.Tasks.Task Sembrar()
        {
            await using var db = AbrirContexto();
            await GenericE2ESeeder.SembrarAsync(db);
            Console.WriteLine("[E2E generic seed] clientes/productos genéricos sembrados.");
        }

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async System.Threading.Tasks.Task Limpiar()
        {
            await using var db = AbrirContexto();
            await GenericE2ESeeder.LimpiarAsync(db);
            Console.WriteLine("[E2E generic seed] limpieza completada.");
        }

        private static AppDbContext AbrirContexto()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                ?? throw new InvalidOperationException($"{ConnectionEnvVar} no configurada.");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new AppDbContext(options);
        }
    }
}
