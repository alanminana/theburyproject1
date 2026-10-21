using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// DESCUENTO-PERMISO-02: punto de entrada del seed de <see cref="DescuentoPermisoE2ESeeder"/>. Exige
    /// <c>E2E_SEED_CONNECTION</c> explícita (nunca el connection string por defecto de la app) y escribe las variables
    /// que consume el spec en <c>e2e/.auth/descuento-permiso-seed-ids.json</c> (gitignorado). Correr DESPUÉS de
    /// <see cref="GenericE2ESeedRunner"/>.
    /// </summary>
    public class DescuentoPermisoE2ESeedRunner
    {
        private const string ConnectionEnvVar = "E2E_SEED_CONNECTION";

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Sembrar()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                ?? throw new InvalidOperationException($"{ConnectionEnvVar} no configurada.");
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
            await using var db = new AppDbContext(options);

            var ventaId = await DescuentoPermisoE2ESeeder.SembrarAsync(db);

            var salida = new
            {
                E2E_SIN_DESCUENTO_USER = DescuentoPermisoE2ESeeder.UsuarioSinPermiso,
                E2E_VENTA_CON_DESCUENTO_ID = ventaId,
                E2E_VENTA_CON_DESCUENTO_TOTAL = DescuentoPermisoE2ESeeder.TotalVenta.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            var output = Path.Combine(FindRepoRoot(), "e2e", ".auth", "descuento-permiso-seed-ids.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(salida, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[descuento-permiso seed] ids escritos en {output}");
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
