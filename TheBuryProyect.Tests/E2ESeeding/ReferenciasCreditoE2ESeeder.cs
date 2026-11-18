using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// D-02 (2026-11-15): minimo de referencias verificadas para otorgar credito personal que SOLO necesita
    /// <c>e2e/cliente-referencias-credito.spec.js</c> (tag <c>@config-referencias</c>). El dataset comun lo deja en 0
    /// (<see cref="ClienteAptitudPunitorioE2ESeeder"/>) para que el resto de la suite no exija referencias a sus clientes
    /// sinteticos; este seeder lo sube a 1 solo alrededor del paso aislado de Playwright y lo restaura despues, igual que
    /// <see cref="ConfiguracionAisladaE2ESeeder"/>. Editarlo por la UI exige <c>configuracion.update</c> (solo SuperAdmin), que el
    /// usuario E2E (Administrador) no tiene: por eso se usa el mismo mecanismo que los otros seeders.
    /// </summary>
    public static class ReferenciasCreditoE2ESeeder
    {
        public const int MinimoActivado = 1;
        public const int MinimoDatasetComun = 0;

        public static async Task ActivarAsync(AppDbContext db)
        {
            var config = await db.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("No existe ConfiguracionCredito: ejecutar ClienteAptitudPunitorioE2ESeedRunner.Sembrar antes.");
            config.ReferenciasVerificadasMinimas = MinimoActivado;
            await db.SaveChangesAsync();
        }

        public static async Task RestaurarAsync(AppDbContext db)
        {
            var config = await db.ConfiguracionesCredito.OrderByDescending(c => c.Id).FirstOrDefaultAsync();
            if (config != null)
            {
                config.ReferenciasVerificadasMinimas = MinimoDatasetComun;
                await db.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// Punto de entrada de <see cref="ReferenciasCreditoE2ESeeder"/>. Misma convencion que los otros runners: exige
    /// <c>E2E_SEED_CONNECTION</c> explicita (nunca la base de desarrollo). El workflow ejecuta <c>Activar</c> antes del paso de
    /// Playwright de <c>@config-referencias</c> y <c>Restaurar</c> despues (<c>if: always()</c>).
    /// </summary>
    public class ReferenciasCreditoE2ESeedRunner
    {
        private const string ConnectionEnvVar = "E2E_SEED_CONNECTION";

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Activar()
        {
            await using var db = CrearContexto();
            await ReferenciasCreditoE2ESeeder.ActivarAsync(db);
            Console.WriteLine($"[referencias credito] minimo de referencias verificadas = {ReferenciasCreditoE2ESeeder.MinimoActivado}");
        }

        [RequiresEnvVarFact(ConnectionEnvVar)]
        public async Task Restaurar()
        {
            await using var db = CrearContexto();
            await ReferenciasCreditoE2ESeeder.RestaurarAsync(db);
            Console.WriteLine($"[referencias credito] minimo restaurado a {ReferenciasCreditoE2ESeeder.MinimoDatasetComun}");
        }

        private static AppDbContext CrearContexto()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)
                ?? throw new InvalidOperationException($"{ConnectionEnvVar} no configurada.");
            return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
        }
    }
}
