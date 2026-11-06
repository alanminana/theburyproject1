using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// H-11 / H-05 (auditoría funcional 2026-11-05) — concurrencia REAL en SQL Server (efímero) sobre la APERTURA de caja.
//
// Reglas de negocio (propietario):
//   * una misma caja física no puede tener dos aperturas activas;
//   * un usuario solo puede mantener UNA apertura activa a la vez.
//
// Antes de la corrección, AbrirCajaAsync comprobaba («¿la caja ya tiene apertura?») y recién después insertaba, sin
// lock ni transacción: dos aperturas simultáneas pasaban la comprobación y quedaban dos aperturas activas.
//
// Técnica (idéntica a FinCajaSqlServerTests): una compuerta detiene la operación A justo antes de INSERTAR la apertura
// (comprobación ya hecha), se lanza la operación B y se observa. Sin la corrección B completa y luego A inserta
// igualmente -> dos aperturas activas. Con la corrección A ya tomó el lock de aplicación: B queda BLOQUEADA hasta que A
// confirma y, al entrar, ve la apertura y se rechaza.
//
// Mismo gate que los demás harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL (catálogo "qa_fin_conc*"). Se corre con
// QA_FIN_CONC_RCSI = 0 y 1.
// ---------------------------------------------------------------------------------------------

public sealed class CajaAperturaSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_apertura";
}

public class CajaAperturaUnicaSqlServerTests : IClassFixture<CajaAperturaSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(7);

    private readonly CajaAperturaSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public CajaAperturaUnicaSqlServerTests(CajaAperturaSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private static GatePlan AntesDeInsertarApertura() =>
        new("antes-del-insert-apertura", sql => sql.Contains("INSERT INTO [AperturasCaja]", StringComparison.Ordinal));

    private async Task<int> CrearCajaAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var caja = new Caja
        {
            Codigo = "AU" + Guid.NewGuid().ToString("N")[..6],
            Nombre = "Caja apertura única",
            Estado = EstadoCaja.Cerrada,
            Activa = true,
            RowVersion = new byte[8]
        };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();
        return caja.Id;
    }

    private async Task<int> ContarAperturasActivasAsync(int? cajaId = null, string? usuario = null)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.AperturasCaja.CountAsync(a =>
            !a.Cerrada && !a.IsDeleted
            && (cajaId == null || a.CajaId == cajaId)
            && (usuario == null || a.UsuarioApertura == usuario));
    }

    private Task<string> Abrir(int cajaId, string usuario, GatePlan? plan = null) =>
        Task.Run(async () =>
        {
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                using var scope = _fx.Services.CreateScope();
                var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
                var ap = await caja.AbrirCajaAsync(new AbrirCajaViewModel { CajaId = cajaId, MontoInicial = 100m }, usuario);
                return $"OK({ap.Id})";
            }
            catch (InvalidOperationException ex)
            {
                return "RECHAZADA: " + ex.Message;
            }
            catch (Exception ex)
            {
                for (Exception? e = ex; e != null; e = e.InnerException)
                    if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number == 1205))
                        return "DEADLOCK_VICTIMA";
                return $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                ConcGateInterceptor.Plan.Value = null;
            }
        });

    private async Task<bool> LlegoALaCompuertaAsync(GatePlan plan, Task<string> operacion)
    {
        var llego = await Task.WhenAny(plan.Reached.Task, operacion, Task.Delay(EsperaCompuerta)) == plan.Reached.Task;
        if (!llego)
        {
            _out.WriteLine($"--- no llegó a la compuerta {plan.Nombre} ---");
            foreach (var l in plan.Trace) _out.WriteLine("  " + l);
        }
        return llego;
    }

    /// <summary>
    /// true si la operación quedó BLOQUEADA esperando un lock de aplicación: se observa en el motor
    /// (sys.dm_tran_locks, request_status = WAIT) en vez de depender de una espera fija. false si terminó sin bloquearse.
    /// </summary>
    private async Task<bool> QuedaBloqueadaAsync(Task<string> operacion)
    {
        var limite = DateTime.UtcNow + EsperaObservacion * 2;
        while (DateTime.UtcNow < limite)
        {
            if (operacion.IsCompleted) return false;
            if (await HayEsperaDeLockDeAplicacionAsync()) return true;
            await Task.Delay(100);
        }
        return false;
    }

    private async Task<bool> HayEsperaDeLockDeAplicacionAsync()
    {
        await using var cn = new SqlConnection(_fx.ConnectionString);
        await cn.OpenAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT' AND resource_database_id = DB_ID()";
        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }

    /// <summary>
    /// A se detiene justo antes de insertar; se lanza B. Devuelve (resultado de A, resultado de B, B quedó bloqueada mientras A esperaba).
    /// </summary>
    private async Task<(string a, string b, bool bBloqueada)> CarreraAsync(
        int cajaA, string usuarioA, int cajaB, string usuarioB)
    {
        var plan = AntesDeInsertarApertura();
        var opA = Abrir(cajaA, usuarioA, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, opA), "A debía llegar a la compuerta (comprobación hecha, sin insertar)");

        var opB = Abrir(cajaB, usuarioB);
        var bloqueada = await QuedaBloqueadaAsync(opB);

        plan.Release.TrySetResult();
        var a = await opA;
        var b = await opB;
        _out.WriteLine($"A: {a}");
        _out.WriteLine($"B: {b} (bloqueada mientras A esperaba: {bloqueada})");
        return (a, b, bloqueada);
    }

    [SqlServerConcFact(DisplayName = "H-11 [APERTURA-00] el catálogo corre con el modo de aislamiento pedido (RCSI)")]
    public void ModoDeAislamientoEsElPedido()
    {
        var pedido = Environment.GetEnvironmentVariable("QA_FIN_CONC_RCSI") == "1";
        Assert.Equal(pedido, _fx.RcsiActivo);
    }

    [SqlServerConcFact(DisplayName = "H-11 [APERTURA-01] dos aperturas simultáneas de la MISMA caja: queda una sola activa")]
    public async Task MismaCaja_DosUsuarios_SoloUnaApertura()
    {
        var caja = await CrearCajaAsync();

        var (a, b, bloqueada) = await CarreraAsync(caja, "usuario.a", caja, "usuario.b");

        Assert.True(bloqueada, "la segunda apertura debía esperar el lock de la primera (no resolverse por RowVersion)");
        Assert.Equal(1, await ContarAperturasActivasAsync(cajaId: caja));
        Assert.Equal(1, new[] { a, b }.Count(r => r.StartsWith("OK(", StringComparison.Ordinal)));
        Assert.Equal(1, new[] { a, b }.Count(r => r.StartsWith("RECHAZADA", StringComparison.Ordinal)));
    }

    [SqlServerConcFact(DisplayName = "H-11 [APERTURA-02] dos aperturas simultáneas de la misma caja por el MISMO usuario: queda una sola")]
    public async Task MismaCaja_MismoUsuario_SoloUnaApertura()
    {
        var caja = await CrearCajaAsync();

        var (a, b, bloqueada) = await CarreraAsync(caja, "usuario.dup", caja, "usuario.dup");

        Assert.True(bloqueada, "la segunda apertura debía esperar el lock de la primera");
        Assert.Equal(1, new[] { a, b }.Count(r => r.StartsWith("OK(", StringComparison.Ordinal)));
        Assert.Equal(1, new[] { a, b }.Count(r => r.StartsWith("RECHAZADA", StringComparison.Ordinal)));
        Assert.Equal(1, await ContarAperturasActivasAsync(cajaId: caja));
        Assert.Equal(1, await ContarAperturasActivasAsync(usuario: "usuario.dup"));
    }

    [SqlServerConcFact(DisplayName = "H-05 [APERTURA-03] un usuario no puede abrir dos cajas distintas a la vez (carrera)")]
    public async Task MismoUsuario_DosCajasDistintas_SoloUnaApertura()
    {
        var cajaA = await CrearCajaAsync();
        var cajaB = await CrearCajaAsync();

        var (a, b, bloqueada) = await CarreraAsync(cajaA, "usuario.multi", cajaB, "usuario.multi");

        Assert.True(bloqueada, "la segunda apertura del mismo usuario debía esperar el lock del usuario");
        Assert.Equal(1, await ContarAperturasActivasAsync(usuario: "usuario.multi"));
        Assert.Equal(1, new[] { a, b }.Count(r => r.StartsWith("OK(", StringComparison.Ordinal)));
    }

    [SqlServerConcFact(DisplayName = "H-05 [APERTURA-04] secuencial: con una apertura activa el usuario no abre otra caja; sí tras cerrar la primera")]
    public async Task MismoUsuario_Secuencial_SegundaCajaRechazada()
    {
        var cajaA = await CrearCajaAsync();
        var cajaB = await CrearCajaAsync();

        var primera = await Abrir(cajaA, "usuario.seq");
        Assert.StartsWith("OK(", primera);

        var segunda = await Abrir(cajaB, "usuario.seq");
        Assert.StartsWith("RECHAZADA", segunda);
        Assert.Equal(1, await ContarAperturasActivasAsync(usuario: "usuario.seq"));

        // Otro usuario sí puede abrir la otra caja.
        Assert.StartsWith("OK(", await Abrir(cajaB, "otro.usuario"));
    }

    [SqlServerConcFact(DisplayName = "H-11 [APERTURA-05] cajas y usuarios distintos no se bloquean entre sí")]
    public async Task CajasYUsuariosDistintos_NoSeBloquean()
    {
        var cajaA = await CrearCajaAsync();
        var cajaB = await CrearCajaAsync();

        var (a, b, bloqueada) = await CarreraAsync(cajaA, "indep.a", cajaB, "indep.b");

        Assert.False(bloqueada, "una apertura independiente no debe esperar a la otra");
        Assert.StartsWith("OK(", a);
        Assert.StartsWith("OK(", b);
    }
}
