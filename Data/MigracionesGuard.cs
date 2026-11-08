using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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

            try
            {
                // La base todavía no existe: no hay nada real que proteger. Una base inaccesible NO es "no existe": ExistsAsync lanza
                // y se falla cerrado más abajo (antes CanConnectAsync devolvía false ante un fallo transitorio y dejaba migrar).
                if (!await context.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>().ExistsAsync())
                    return;

                var pendientes = (await context.Database.GetPendingMigrationsAsync()).ToList();
                if (pendientes.Count == 0)
                    return;

                var aplicadas = (await context.Database.GetAppliedMigrationsAsync()).Any();
                if (!aplicadas && !await TieneTablasDeUsuarioAsync(context))
                    return; // base realmente vacía: crear el esquema inicial no es tocar una base real

                var detalle = aplicadas
                    ? $"tiene {pendientes.Count} migración(es) pendiente(s) ({string.Join(", ", pendientes.Take(5))}{(pendientes.Count > 5 ? ", …" : "")})"
                    : "tiene tablas pero NO tiene historial de migraciones";
                throw new MigracionesNoAutorizadasException(
                    $"La base {detalle} y la migración NO está habilitada. El arranque no modificó el esquema ni sembró datos. " +
                    $"Para aplicarla: hacer respaldo y habilitar explícitamente con {ClaveConfiguracion}=true (env Database__AllowMigrations=true) " +
                    "o ejecutar TheBuryProyect.dll --migrate.");
            }
            catch (MigracionesNoAutorizadasException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Si no se puede verificar el estado del esquema tampoco se arranca (falla cerrada).
                throw new MigracionesNoAutorizadasException(
                    "No se pudo verificar el estado de las migraciones de la base; el arranque se detiene sin modificar nada: " + ex.Message);
            }
        }

        private static async Task<bool> TieneTablasDeUsuarioAsync(AppDbContext context)
        {
            var sql = context.Database.IsSqlServer()
                ? "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'"
                : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name <> '__EFMigrationsHistory' AND name NOT LIKE 'sqlite_%'";
            var conn = context.Database.GetDbConnection();
            var abierta = conn.State == System.Data.ConnectionState.Open;
            if (!abierta) await conn.OpenAsync();
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
            }
            finally
            {
                if (!abierta) await conn.CloseAsync();
            }
        }
    }
}
