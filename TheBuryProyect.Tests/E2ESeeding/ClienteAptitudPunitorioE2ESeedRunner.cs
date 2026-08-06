using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// PUN-ML10-G: punto de entrada para sembrar/limpiar los 9 escenarios de
    /// <c>e2e/cliente-aptitud-punitorio.spec.js</c> contra una LocalDB descartable explícita.
    ///
    /// Uso (PowerShell), NUNCA contra la base de desarrollo:
    /// <code>
    /// $env:E2E_SEED_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=TheBuryProjectDb_E2E_PUNML10G;Trusted_Connection=True;TrustServerCertificate=True"
    /// dotnet test TheBuryProyect.Tests --filter "FullyQualifiedName~ClienteAptitudPunitorioE2ESeedRunner.Sembrar"
    /// # ... correr Playwright ...
    /// dotnet test TheBuryProyect.Tests --filter "FullyQualifiedName~ClienteAptitudPunitorioE2ESeedRunner.Limpiar"
    /// </code>
    ///
    /// La base debe existir y tener el esquema migrado (correr la app una vez apuntada a ella, o
    /// <c>dotnet ef database update</c>) ANTES de sembrar — este runner no migra, sólo siembra datos
    /// de dominio.
    ///
    /// <see cref="Sembrar"/> escribe los ids resultantes en un JSON (por defecto
    /// <c>e2e/.auth/pun-ml10g-seed-ids.json</c>, gitignorado — configurable con
    /// <c>E2E_SEED_OUTPUT</c>) para que un script de PowerShell/CI pueda leerlo y exportar las
    /// variables <c>E2E_CLIENTE_*_ID</c> que consume el spec, sin hardcodear ningún id.
    /// </summary>
    public class ClienteAptitudPunitorioE2ESeedRunner
    {
        private const string ConnectionEnvVar = "E2E_SEED_CONNECTION";

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Sembrar()
        {
            await using var db = AbrirContexto();

            var resultado = await ClienteAptitudPunitorioE2ESeeder.SembrarAsync(db);

            var salida = new
            {
                E2E_CLIENTE_SIN_DEUDA_ID = resultado.ClienteSinDeudaId.ToString(),
                E2E_CLIENTE_MORA_CAPITAL_ID = resultado.ClienteMoraCapitalId.ToString(),
                E2E_CLIENTE_PUNITORIO_ID = resultado.ClientePunitorioId.ToString(),
                E2E_CLIENTE_MORA_Y_PUNITORIO_ID = resultado.ClienteMoraYPunitorioId.ToString(),
                E2E_CLIENTE_NOAPTO_PUNITORIO_ID = resultado.ClienteNoAptoPunitorioId.ToString(),
                E2E_CLIENTE_PUNITORIO_PARCIAL_ID = resultado.ClientePunitorioParcialId.ToString(),
                E2E_CLIENTE_PUNITORIO_PAGADO_ID = resultado.ClientePunitorioPagadoId.ToString(),
                E2E_CLIENTE_PUNITORIO_ANULADO_ID = resultado.ClientePunitorioAnuladoId.ToString(),
                E2E_CLIENTE_PUNITORIO_CALCULADO_NO_APLICADO_ID = resultado.ClientePunitorioCalculadoNoAplicadoId.ToString(),
                E2E_CLIENTE_MORA_Y_PUNITORIO_DOCUMENTO = resultado.ClienteMoraYPunitorioDocumento,

                // Legacy PUN-ML9-C (e2e/credito-punitorio-detalle.spec.js): crédito dedicado de
                // 6 cuotas vencidas (OPERATION_QUOTAS) + 1 séptima para "historial incompleto".
                E2E_CREDITO_PUN_ML9_ID = resultado.CreditoOperacionesId.ToString(),
                E2E_CREDITO_PUN_ML9_C_ID = resultado.CreditoOperacionesId.ToString(),
                E2E_CUOTAS_PUN_ML9_C = string.Join(",", resultado.CuotasOperacionesIds),
                E2E_CUOTA_HISTORIAL_INCOMPLETO = resultado.CuotaHistorialIncompletoId.ToString(),

                // PUN-ML10-G.1: cada uno de los otros 3 specs legacy PUN-ML9 tiene su propio crédito
                // dedicado — antes compartían "Operaciones" y se agotaban entre sí (ver seeder).
                E2E_PUNML9D_CREDITO = resultado.CreditoPagoIndividualId.ToString(),
                E2E_PUNML9D_CUOTA_SIN_APLICACION = resultado.CuotaPagoIndividualSinAplicacionId.ToString(),
                E2E_PUNML9D_CUOTA_CON_APLICACION = resultado.CuotaPagoIndividualConAplicacionId.ToString(),
                E2E_RETURNURL_CREDITO = resultado.CreditoReturnUrlId.ToString(),
                E2E_RETURNURL_CUOTA = resultado.CuotaReturnUrlId.ToString(),
                E2E_PUNML9E_CREDITO = resultado.CreditoAdelantoId.ToString(),
                E2E_PUNML9E_CLIENTE = resultado.ClienteAdelantoId.ToString()
            };

            var outputPath = ResolverOutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(salida, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine($"[PUN-ML10-G seed] ids escritos en {outputPath}");
            foreach (var linea in File.ReadAllLines(outputPath))
                Console.WriteLine(linea);
        }

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Limpiar()
        {
            await using var db = AbrirContexto();
            await ClienteAptitudPunitorioE2ESeeder.LimpiarAsync(db);
            Console.WriteLine("[PUN-ML10-G seed] limpieza completada (sólo clientes marcados E2EPUNML10G).");
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

        private static string ResolverOutputPath()
        {
            var configurado = Environment.GetEnvironmentVariable("E2E_SEED_OUTPUT");
            if (!string.IsNullOrWhiteSpace(configurado))
                return configurado;

            var repoRoot = FindRepoRoot();
            return Path.Combine(repoRoot, "e2e", ".auth", "pun-ml10g-seed-ids.json");
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

            throw new DirectoryNotFoundException("No se encontró la raíz del repositorio a partir de AppContext.BaseDirectory.");
        }
    }
}
