using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace TheBuryProject.Middleware
{
    /// <summary>
    /// Durante un reinicio/caída transitoria de SQL Server, cualquier código que dependa de la DB
    /// dentro del pipeline (por ejemplo PermissionClaimsTransformation vía RolService, en
    /// autenticación) puede lanzar SqlException antes de que el pool de conexiones se recupere.
    /// EF Core (sin EnableRetryOnFailure) envuelve ese SqlException dentro de un
    /// InvalidOperationException ("...enabling transient error resiliency...") en vez de dejarlo
    /// propagar directo, así que hay que revisar la cadena de InnerException, no sólo el tipo del
    /// catch. Sin este middleware, UseExceptionHandler lo convierte en un 500 genérico
    /// indistinguible de un bug real. Acá se responde 503 + Retry-After sólo cuando en la cadena
    /// aparece un SqlException con un código de error conocido como transitorio; cualquier otra
    /// excepción (SQL no-transitoria o no-SQL) se re-lanza intacta y sigue el manejo existente.
    /// </summary>
    public class TransientDbUnavailableMiddleware
    {
        // Mismo criterio de "transitorio" que EF Core (SqlServerTransientExceptionDetector):
        // errores de timeout de login, recurso no disponible o base en transición de estado.
        private static readonly int[] TransientErrorNumbers =
        {
            4060,  // Cannot open database "X" requested by the login (DB aún no disponible tras restart)
            40197, 40501, 40613, // Azure SQL: throttling / servicio ocupado
            49918, 49919, 49920, // Azure SQL: recursos insuficientes
            4221,  // login redirect en progreso
            10928, 10929, // Azure SQL: límite de recursos
            10053, 10054, 10060, // conexión cortada por el server/red
            233, 64, 20, // conexión rechazada / handshake fallido
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<TransientDbUnavailableMiddleware> _logger;

        public TransientDbUnavailableMiddleware(RequestDelegate next, ILogger<TransientDbUnavailableMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex) when (FindTransientSqlException(ex) is { } sqlEx && !context.Response.HasStarted)
            {
                _logger.LogWarning(ex,
                    "SQL transitoriamente no disponible (error {SqlErrorNumber}) durante {Method} {Path}; respondiendo 503.",
                    FindTransientSqlException(ex)!.Number, context.Request.Method, context.Request.Path);

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = "2";
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("Servicio temporalmente no disponible (base de datos en recuperación). Reintente en unos segundos.");
            }
        }

        // Recorre la cadena de InnerException (EF Core envuelve el SqlException real dentro de un
        // InvalidOperationException cuando no hay EnableRetryOnFailure configurado) buscando un
        // SqlException con un código conocido como transitorio.
        internal static SqlException? FindTransientSqlException(Exception? ex)
        {
            var current = ex;
            while (current is not null)
            {
                if (current is SqlException sqlEx && IsTransient(sqlEx.Number))
                    return sqlEx;
                current = current.InnerException;
            }
            return null;
        }

        internal static bool IsTransient(SqlException ex) => IsTransient(ex.Number);

        internal static bool IsTransient(int errorNumber)
        {
            foreach (var number in TransientErrorNumbers)
            {
                if (errorNumber == number)
                    return true;
            }
            return false;
        }
    }
}
