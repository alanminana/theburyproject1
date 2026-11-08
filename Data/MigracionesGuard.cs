using Microsoft.EntityFrameworkCore;

namespace TheBuryProject.Data
{
    /// <summary>La base ya tiene historial de migraciones y hay pendientes sin habilitación explícita.</summary>
    public sealed class MigracionesNoAutorizadasException(string mensaje) : InvalidOperationException(mensaje);

    /// <summary>
    /// D-04 / protección de bases reales: el arranque normal de la app (y <c>--configurar-documentos</c>) NUNCA aplica migraciones
    /// pendientes sobre una base que ya tiene historial, salvo habilitación explícita: <c>Database:AllowMigrations=true</c>
    /// (env <c>Database__AllowMigrations</c>) o el modo <c>--migrate</c> (servicio one-shot de docker-compose, que es explícito por definición).
    /// Una base nueva o vacía (sin historial) se sigue creando sin pedir nada. Si no está autorizado se corta ANTES de migrar y de sembrar.
    /// </summary>
    public static class MigracionesGuard
    {
        public const string ClaveConfiguracion = "Database:AllowMigrations";

        public static async Task AsegurarAutorizadoAsync(AppDbContext context, IConfiguration configuration, bool explicitoPorComando = false)
        {
            if (explicitoPorComando || configuration.GetValue(ClaveConfiguracion, false))
                return;

            var pendientes = (await context.Database.GetPendingMigrationsAsync()).ToList();
            if (pendientes.Count == 0)
                return;

            var aplicadas = (await context.Database.GetAppliedMigrationsAsync()).Any();
            if (!aplicadas)
                return; // base nueva o vacía: crear el esquema inicial no es tocar una base real

            throw new MigracionesNoAutorizadasException(
                $"La base tiene {pendientes.Count} migración(es) pendiente(s) ({string.Join(", ", pendientes.Take(5))}" +
                $"{(pendientes.Count > 5 ? ", …" : "")}) y NO están habilitadas. El arranque no modificó el esquema ni sembró datos. " +
                $"Para aplicarlas: hacer respaldo y habilitar explícitamente con {ClaveConfiguracion}=true (env Database__AllowMigrations=true) " +
                "o ejecutar TheBuryProyect.dll --migrate.");
        }
    }
}
