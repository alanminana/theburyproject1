using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// PR #75: punto de entrada del seed sintético de Mora para la evidencia visual. Exige
    /// <c>E2E_SEED_CONNECTION</c> explícita (nunca usa el connection string por defecto de la app) y
    /// escribe los ids en <c>e2e/.auth/pr75-mora-seed-ids.json</c> (gitignorado) para el spec.
    /// </summary>
    public class MoraVisualE2ESeedRunner
    {
        private const string ConnectionEnvVar = "E2E_SEED_CONNECTION";

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Sembrar()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                ?? throw new InvalidOperationException($"{ConnectionEnvVar} no configurada.");
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
            await using var db = new AppDbContext(options);

            var r = await MoraVisualE2ESeeder.SembrarAsync(db);

            var salida = new
            {
                clienteFichaRicaId = r.ClienteFichaRicaId,
                clienteFichaRicaDocumento = r.ClienteFichaRicaDocumento,
                clienteSinContactosId = r.ClienteSinContactosId,
                clienteNombreLargoId = r.ClienteNombreLargoId,
                totalClientes = MoraVisualE2ESeeder.TotalClientes
            };

            var output = Path.Combine(FindRepoRoot(), "e2e", ".auth", "pr75-mora-seed-ids.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(salida, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[PR75 mora seed] ids escritos en {output}");
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
        }
    }
}
