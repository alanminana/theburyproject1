using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// PROMPT 54 — Invariante "siempre queda un SuperAdmin utilizable" ante pedidos CONCURRENTES.
//
// Ejecuta el GuardiaPrivilegiosService REAL contra SQL Server real (SQLite no sirve: un único
// escritor serializa todo y escondería la carrera). Dos pedidos desactivan/bloquean a SuperAdmin
// distintos al mismo tiempo; sin el lock del rol SuperAdmin ambos ven "hay dos" y quedan cero.
//
// NO corre por defecto. Requiere QA_SEC_SUPERADMIN_SQL con una cadena a una BD DESCARTABLE cuyo
// Initial Catalog empiece con "qa_sec_superadmin" (se crea aplicando las migraciones y se elimina al final).
// Nunca toca la BD de desarrollo.
// ---------------------------------------------------------------------------------------------

public sealed class SuperAdminSqlServerFactAttribute : FactAttribute
{
    public SuperAdminSqlServerFactAttribute()
    {
        var skip = SuperAdminSqlEnv.SkipReason();
        if (skip != null) Skip = skip;
    }
}

internal static class SuperAdminSqlEnv
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("QA_SEC_SUPERADMIN_SQL");

    public static string? SkipReason()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return "BLOCKED: falta QA_SEC_SUPERADMIN_SQL (BD SQL Server descartable).";
        var catalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        return catalog.StartsWith("qa_sec_superadmin", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"BLOCKED: Initial Catalog '{catalog}' no empieza con 'qa_sec_superadmin' (protección de la BD de desarrollo).";
    }
}

public class UltimoSuperAdminConcurrenciaSqlServerTests : IAsyncLifetime
{
    private const int Iteraciones = 12;

    public async Task InitializeAsync()
    {
        if (SuperAdminSqlEnv.SkipReason() != null) return;
        await using var context = NuevoContexto();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (SuperAdminSqlEnv.SkipReason() != null) return;
        await using var context = NuevoContexto();
        await context.Database.EnsureDeletedAsync();
    }

    [SuperAdminSqlServerFact]
    public async Task DosDesactivacionesSimultaneasDeSuperAdminDistintos_SoloUnaSeAplica()
    {
        for (var i = 0; i < Iteraciones; i++)
        {
            var (a, b) = await SembrarDosSuperAdminAsync();

            var resultados = await Task.WhenAll(
                Ejecutar(u => u.Activo = false, a),
                Ejecutar(u => u.Activo = false, b));

            Assert.Equal(1, resultados.Count(r => r.Permitido));
            Assert.Equal(1, resultados.Count(r => !r.Permitido));
            Assert.Equal(1, await ContarUtilizablesAsync());
        }
    }

    [SuperAdminSqlServerFact]
    public async Task DesactivarYBloquearSimultaneosDeSuperAdminDistintos_SoloUnaSeAplica()
    {
        for (var i = 0; i < Iteraciones; i++)
        {
            var (a, b) = await SembrarDosSuperAdminAsync();

            var resultados = await Task.WhenAll(
                Ejecutar(u => u.Activo = false, a),
                Ejecutar(u =>
                {
                    u.LockoutEnabled = true;
                    u.LockoutEnd = DateTimeOffset.MaxValue;
                }, b));

            Assert.Equal(1, resultados.Count(r => r.Permitido));
            Assert.Equal(1, await ContarUtilizablesAsync());
        }
    }

    [SuperAdminSqlServerFact]
    public async Task OperacionRechazadaNoDejaEscrituras()
    {
        var (a, b) = await SembrarDosSuperAdminAsync();
        await Ejecutar(u => u.Activo = false, a);

        var rechazo = await Ejecutar(u => { u.Activo = false; u.Nombre = "Escrito"; }, b);

        Assert.False(rechazo.Permitido);
        await using var context = NuevoContexto();
        var estadoB = await context.Users.AsNoTracking().Where(u => u.Id == b).Select(u => new { u.Activo, u.Nombre }).SingleAsync();
        Assert.True(estadoB.Activo);
        Assert.NotEqual("Escrito", estadoB.Nombre);
    }

    // ───────────────────────── infraestructura ─────────────────────────

    private static AppDbContext NuevoContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(SuperAdminSqlEnv.ConnectionString).Options);

    /// <summary>
    /// Pedido simulado: guardia real + una escritura con retardo deliberado entre el cambio y la
    /// verificación, para que ambos pedidos hayan escrito antes de que cualquiera cuente.
    /// </summary>
    private static async Task<GuardiaResultado> Ejecutar(Action<ApplicationUser> cambio, string userId)
    {
        await using var context = NuevoContexto();
        var guardia = new GuardiaPrivilegiosService(
            context, new SuperAdminActor(), new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            new AuditoriaNula(), NullLogger<GuardiaPrivilegiosService>.Instance);

        return await guardia.EjecutarConservandoSuperAdminAsync(async () =>
        {
            var user = await context.Users.SingleAsync(u => u.Id == userId);
            cambio(user);
            await context.SaveChangesAsync();
            await Task.Delay(400);
            return true;
        });
    }

    private static async Task<(string A, string B)> SembrarDosSuperAdminAsync()
    {
        await using var context = NuevoContexto();
        context.UserRoles.RemoveRange(context.UserRoles);
        context.Users.RemoveRange(context.Users);
        await context.SaveChangesAsync();

        var roleId = await context.Roles.Where(r => r.NormalizedName == "SUPERADMIN").Select(r => r.Id).SingleOrDefaultAsync();
        if (roleId == null)
        {
            roleId = Guid.NewGuid().ToString();
            context.Roles.Add(new IdentityRole { Id = roleId, Name = Roles.SuperAdmin, NormalizedName = "SUPERADMIN" });
        }

        var ids = new List<string>();
        foreach (var n in new[] { "a", "b" })
        {
            var id = Guid.NewGuid().ToString();
            ids.Add(id);
            context.Users.Add(new ApplicationUser
            {
                Id = id, UserName = $"super_{n}", NormalizedUserName = $"SUPER_{n.ToUpperInvariant()}",
                Email = $"{n}@t.test", NormalizedEmail = $"{n.ToUpperInvariant()}@T.TEST",
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), Activo = true
            });
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = roleId });
        }

        await context.SaveChangesAsync();
        return (ids[0], ids[1]);
    }

    private static async Task<int> ContarUtilizablesAsync()
    {
        await using var context = NuevoContexto();
        var ahora = DateTimeOffset.UtcNow;
        var filas = await (from u in context.Users.AsNoTracking()
                           join ur in context.UserRoles on u.Id equals ur.UserId
                           join r in context.Roles on ur.RoleId equals r.Id
                           where r.NormalizedName == "SUPERADMIN" && u.Activo
                           select new { u.LockoutEnabled, u.LockoutEnd }).ToListAsync();
        return filas.Count(f => !f.LockoutEnabled || f.LockoutEnd == null || f.LockoutEnd <= ahora);
    }

    private sealed class SuperAdminActor : ICurrentUserService
    {
        public string GetUsername() => "actor";
        public string GetUserId() => "actor-id";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => role == Roles.SuperAdmin;
        public bool HasPermission(string modulo, string accion) => true;
        public string? GetIpAddress() => null;
    }

    private sealed class AuditoriaNula : ISeguridadAuditoriaService
    {
        public Task RegistrarEventoAsync(string modulo, string accion, string entidad, string? detalle = null) => Task.CompletedTask;

        public Task<AuditoriaQueryResult> ConsultarEventosAsync(
            string? usuario = null, string? modulo = null, string? accion = null,
            DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null) =>
            Task.FromResult(new AuditoriaQueryResult());
    }
}
