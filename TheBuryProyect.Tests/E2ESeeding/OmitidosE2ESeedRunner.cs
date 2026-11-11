using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// Punto de entrada de <see cref="OmitidosE2ESeeder"/> (C-03). Misma convención que los otros runners:
    /// exige <c>E2E_SEED_CONNECTION</c> explícita (nunca la base de desarrollo) y escribe
    /// <c>e2e/.auth/omitidos-seed-ids.json</c> con las variables <c>E2E_*</c> que consume el workflow.
    /// </summary>
    public class OmitidosE2ESeedRunner
    {
        private const string ConnectionEnvVar = "E2E_SEED_CONNECTION";

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Sembrar()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                ?? throw new InvalidOperationException($"{ConnectionEnvVar} no configurada.");
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);

            var salida = await OmitidosE2ESeeder.SembrarAsync(db);

            var output = Path.Combine(FindRepoRoot(), "e2e", ".auth", "omitidos-seed-ids.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(salida, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[omitidos seed] ids escritos en {output}");
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null && !File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                current = current.Parent;
            return current?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }
}
