using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
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
        await CrearCatalogoAsync();
        await using var context = NuevoContexto();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Crea el catálogo descartable con el modo de aislamiento pedido (QA_SEC_SUPERADMIN_RCSI = 0 / 1) fijado de forma
    /// explícita en ambos sentidos: no se confía en lo que herede de `model` ni en lo que haga el proveedor al crearlo.
    /// </summary>
    private static async Task CrearCatalogoAsync()
    {
        var csb = new SqlConnectionStringBuilder(SuperAdminSqlEnv.ConnectionString);
        var catalogo = csb.InitialCatalog;
        csb.InitialCatalog = "master";
        var rcsi = Environment.GetEnvironmentVariable("QA_SEC_SUPERADMIN_RCSI") == "1" ? "ON" : "OFF";
        await using var c = new SqlConnection(csb.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(
            $"IF DB_ID(N'{catalogo}') IS NOT NULL BEGIN ALTER DATABASE [{catalogo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalogo}]; END; " +
            $"CREATE DATABASE [{catalogo}]; ALTER DATABASE [{catalogo}] SET READ_COMMITTED_SNAPSHOT {rcsi} WITH ROLLBACK IMMEDIATE;", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (SuperAdminSqlEnv.SkipReason() != null) return;
        await using var context = NuevoContexto();
        await context.Database.EnsureDeletedAsync();
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-00] el catalogo corre con el modo de aislamiento pedido (RCSI)")]
    public async Task SA00_ModoDeAislamientoDelCatalogo_CoincideConElPedido()
    {
        var pedido = Environment.GetEnvironmentVariable("QA_SEC_SUPERADMIN_RCSI") == "1" ? 1 : 0;
        await using var c = new SqlConnection(SuperAdminSqlEnv.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()", c);
        var real = (int)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(pedido, real);
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-01] dos desactivaciones simultaneas de SuperAdmin distintos: solo una se aplica")]
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

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-02] desactivar y bloquear simultaneos de SuperAdmin distintos: solo una se aplica")]
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

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-03] operacion rechazada no deja escrituras")]
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

    // ───────────────────────── PROMPT 56: invariante estructural y atomicidad masiva en SQL Server real ─────────────────────────

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-04] unico SuperAdmin bloqueado temporalmente no se puede desactivar")]
    public async Task UnicoSuperAdminTemporalmenteBloqueado_NoSePuedeDesactivar()
    {
        var (a, b) = await SembrarDosSuperAdminAsync();
        await using (var context = NuevoContexto())
        {
            // b queda inactivo y a con un bloqueo de login vigente: a sigue siendo el último SuperAdmin activo.
            (await context.Users.SingleAsync(u => u.Id == b)).Activo = false;
            var ua = await context.Users.SingleAsync(u => u.Id == a);
            ua.LockoutEnabled = true;
            ua.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
            await context.SaveChangesAsync();
        }

        var rechazo = await Ejecutar(u => u.Activo = false, a);

        Assert.False(rechazo.Permitido);
        await using var verificacion = NuevoContexto();
        Assert.True(await verificacion.Users.AsNoTracking().Where(u => u.Id == a).Select(u => u.Activo).SingleAsync());
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-05] dos desactivaciones con uno bloqueado nunca dejan cero activos")]
    public async Task DosDesactivacionesSimultaneasConUnoBloqueado_NuncaQuedaCeroActivos()
    {
        for (var i = 0; i < Iteraciones; i++)
        {
            var (a, b) = await SembrarDosSuperAdminAsync();
            await using (var context = NuevoContexto())
            {
                var ub = await context.Users.SingleAsync(u => u.Id == b);
                ub.LockoutEnabled = true;
                ub.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
                await context.SaveChangesAsync();
            }

            await Task.WhenAll(Ejecutar(u => u.Activo = false, a), Ejecutar(u => u.Activo = false, b));

            Assert.True(await ContarActivosAsync() >= 1, "Quedaron cero SuperAdmin activos.");
        }
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-06] operacion masiva con conflicto de concurrencia real revierte todo")]
    public async Task OperacionMasivaConConflictoDeConcurrenciaReal_RevierteTodoYNoPersisteDespues()
    {
        var ids = await SembrarOrdinariosAsync(3);

        await using var context = NuevoContexto();
        var guardia = NuevaGuardia(context);
        var userManager = NuevoUserManager(context);

        var resultado = await guardia.EjecutarConservandoSuperAdminAsync(async () =>
        {
            var usuarios = await userManager.Users.Where(u => ids.Contains(u.Id)).OrderBy(u => u.UserName).ToListAsync();

            // Otra sesión modifica al tercero entre la lectura y el UPDATE (rowversion real de SQL Server).
            await using (var otra = NuevoContexto())
            {
                var fila = await otra.Users.SingleAsync(u => u.Id == usuarios[2].Id);
                fila.Telefono = "concurrente";
                await otra.SaveChangesAsync();
            }

            foreach (var user in usuarios)
            {
                user.Activo = false;
                var r = await userManager.UpdateAsync(user);
                if (!r.Succeeded)
                    return false;
            }

            return true;
        });

        Assert.True(resultado.Permitido);

        // Un SaveChanges posterior del mismo contexto no debe persistir lo revertido (tracker descartado).
        await context.SaveChangesAsync();

        await using var verificacion = NuevoContexto();
        var estados = await verificacion.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Activo, u.Telefono }).ToListAsync();
        Assert.All(estados, e => Assert.True(e.Activo));
        Assert.Equal(1, estados.Count(e => e.Telefono == "concurrente"));
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-07] excepcion en medio de la operacion masiva revierte todo y descarta el tracker")]
    public async Task ExcepcionEnMedioDeLaOperacionMasiva_RevierteTodoYDescartaElTracker()
    {
        var ids = await SembrarOrdinariosAsync(3);

        await using var context = NuevoContexto();
        var guardia = NuevaGuardia(context);
        var userManager = NuevoUserManager(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => guardia.EjecutarConservandoSuperAdminAsync(async () =>
        {
            var usuarios = await userManager.Users.Where(u => ids.Contains(u.Id)).OrderBy(u => u.UserName).ToListAsync();
            for (var i = 0; i < usuarios.Count; i++)
            {
                if (i == 2) throw new InvalidOperationException("fallo inyectado");
                usuarios[i].Activo = false;
                Assert.True((await userManager.UpdateAsync(usuarios[i])).Succeeded);
            }

            return true;
        }));

        await context.SaveChangesAsync();

        await using var verificacion = NuevoContexto();
        Assert.All(await verificacion.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Activo).ToListAsync(),
            activo => Assert.True(activo));
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 57 [SA-08] operacion masiva correcta confirma todo en SQL Server")]
    public async Task OperacionMasivaCorrecta_ConfirmaTodoEnSqlServer()
    {
        var ids = await SembrarOrdinariosAsync(3);

        await using var context = NuevoContexto();
        var guardia = NuevaGuardia(context);
        var userManager = NuevoUserManager(context);

        var resultado = await guardia.EjecutarConservandoSuperAdminAsync(async () =>
        {
            foreach (var user in await userManager.Users.Where(u => ids.Contains(u.Id)).ToListAsync())
            {
                user.Activo = false;
                if (!(await userManager.UpdateAsync(user)).Succeeded)
                    return false;
            }

            return true;
        });

        Assert.True(resultado.Permitido);
        await using var verificacion = NuevoContexto();
        Assert.All(await verificacion.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Activo).ToListAsync(),
            activo => Assert.False(activo));
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 58 [SA-09] dos arranques simultaneos sin SuperAdmin: se aprovisiona una sola cuenta privilegiada")]
    public async Task DosArranquesSimultaneosSinSuperAdmin_AprovisionanUnaSolaCuenta()
    {
        for (var i = 0; i < Iteraciones; i++)
        {
            await LimpiarUsuariosYAsegurarRolAsync();

            var resultados = await Task.WhenAll(Arrancar($"boot_a{i}"), Arrancar($"boot_b{i}"));

            Assert.Equal(1, resultados.Count(r => r.Creado));
            Assert.Equal(1, await ContarActivosAsync());
            await using var verificacion = NuevoContexto();
            Assert.Equal(1, await verificacion.Users.AsNoTracking().CountAsync());
        }
    }

    [SuperAdminSqlServerFact(DisplayName = "PROMPT 58 [SA-10] fallo al asignar el rol en SQL Server no deja la cuenta creada")]
    public async Task FalloAlAsignarElRol_EnSqlServer_RevierteLaCuenta()
    {
        await LimpiarUsuariosYAsegurarRolAsync();
        await using var context = NuevoContexto();
        var um = new UserManagerRolFallido(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DbInitializer.AprovisionarPrimerSuperAdminAsync(
            context, um, NuevoCandidato("boot_fallo"), "Aprov#Test123!", NullLogger.Instance));

        await using var verificacion = NuevoContexto();
        Assert.Equal(0, await verificacion.Users.AsNoTracking().CountAsync());
    }

    private static async Task<(bool Creado, IdentityResult Resultado)> Arrancar(string nombre)
    {
        await using var context = NuevoContexto();
        return await DbInitializer.AprovisionarPrimerSuperAdminAsync(
            context, new UserManagerLento(context), NuevoCandidato(nombre), "Aprov#Test123!", NullLogger.Instance);
    }

    private static ApplicationUser NuevoCandidato(string nombre) =>
        new() { UserName = nombre, Email = $"{nombre}@t.test", EmailConfirmed = true, Activo = true };

    private static async Task LimpiarUsuariosYAsegurarRolAsync()
    {
        await using var context = NuevoContexto();
        context.UserRoles.RemoveRange(context.UserRoles);
        context.Users.RemoveRange(context.Users);
        await context.SaveChangesAsync();
        if (!await context.Roles.AnyAsync(r => r.NormalizedName == "SUPERADMIN"))
        {
            context.Roles.Add(new IdentityRole { Id = Guid.NewGuid().ToString(), Name = Roles.SuperAdmin, NormalizedName = "SUPERADMIN" });
            await context.SaveChangesAsync();
        }
    }

    /// <summary>Ensancha la ventana entre la verificación y la confirmación para que la carrera sea observable.</summary>
    private sealed class UserManagerLento(AppDbContext context) : UserManager<ApplicationUser>(
        new UserStore<ApplicationUser>(context), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
        new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance)
    {
        public override async Task<IdentityResult> CreateAsync(ApplicationUser user, string password)
        {
            var r = await base.CreateAsync(user, password);
            await Task.Delay(400);
            return r;
        }
    }

    private sealed class UserManagerRolFallido(AppDbContext context) : UserManager<ApplicationUser>(
        new UserStore<ApplicationUser>(context), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
        new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance)
    {
        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "Fallo simulado." }));
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

    private static GuardiaPrivilegiosService NuevaGuardia(AppDbContext context) =>
        new(context, new SuperAdminActor(), new Microsoft.AspNetCore.Http.HttpContextAccessor(),
            new AuditoriaNula(), NullLogger<GuardiaPrivilegiosService>.Instance);

    private static UserManager<ApplicationUser> NuevoUserManager(AppDbContext context) =>
        new(new UserStore<ApplicationUser>(context), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, NullLogger<UserManager<ApplicationUser>>.Instance);

    private static async Task<List<string>> SembrarOrdinariosAsync(int cantidad)
    {
        await using var context = NuevoContexto();
        context.UserRoles.RemoveRange(context.UserRoles);
        context.Users.RemoveRange(context.Users);
        await context.SaveChangesAsync();

        var ids = new List<string>();
        for (var i = 0; i < cantidad; i++)
        {
            var id = Guid.NewGuid().ToString();
            ids.Add(id);
            context.Users.Add(new ApplicationUser
            {
                Id = id, UserName = $"ord_{i}", NormalizedUserName = $"ORD_{i}",
                Email = $"o{i}@t.test", NormalizedEmail = $"O{i}@T.TEST",
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), Activo = true
            });
        }

        await context.SaveChangesAsync();
        return ids;
    }

    private static async Task<int> ContarActivosAsync()
    {
        await using var context = NuevoContexto();
        return await (from u in context.Users.AsNoTracking()
                      join ur in context.UserRoles on u.Id equals ur.UserId
                      join r in context.Roles on ur.RoleId equals r.Id
                      where r.NormalizedName == "SUPERADMIN" && u.Activo
                      select u.Id).CountAsync();
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

        public void AgregarEvento(string modulo, string accion, string entidad, string? detalle = null) { }

        public Task<AuditoriaQueryResult> ConsultarEventosAsync(
            string? usuario = null, string? modulo = null, string? accion = null,
            DateOnly? desde = null, DateOnly? hasta = null, int skip = 0, int? take = null) =>
            Task.FromResult(new AuditoriaQueryResult());
    }
}
