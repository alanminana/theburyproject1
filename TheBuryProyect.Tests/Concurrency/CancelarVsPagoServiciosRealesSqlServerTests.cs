using System.Collections.Concurrent;
using System.Data.Common;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Infrastructure;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// QA-FIN-CONC-P0 — Fase 2: SERVICIOS REALES (VentaService.CancelarVentaAsync / CreditoService.*)
// contra SQL Server real, con el interleaving peligroso FORZADO de manera determinista.
//
// Técnica: un DbCommandInterceptor "compuerta" detiene una de las dos operaciones EN UN COMANDO SQL
// concreto (identificado por su texto) hasta que el test decide liberarla. Mientras una está detenida
// el test lanza la otra y observa (sesión SQL real) si avanza, si queda bloqueada y por quién.
// Cada operación corre en su propio scope DI = su propio DbContext = su propia conexión/transacción.
//
// No usa SQLite (un único escritor serializaría todo y no probaría nada). Gate de seguridad idéntico
// al del harness de modelo: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*".
// El test NO toca otro catálogo: la cadena la arma el propio DbContext del host de prueba.
// ---------------------------------------------------------------------------------------------

internal sealed class GatePlan
{
    public GatePlan(string nombre, Func<string, bool> trigger)
    {
        Nombre = nombre;
        Trigger = trigger;
    }

    public string Nombre { get; }
    public Func<string, bool> Trigger { get; }
    public bool Fired { get; set; }
    public int Spid { get; set; }
    public ConcurrentQueue<string> Trace { get; } = new();
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ConcGateInterceptor : DbCommandInterceptor
{
    public static readonly AsyncLocal<GatePlan?> Plan = new();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await PasarPorCompuertaAsync(command);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await PasarPorCompuertaAsync(command);
        return result;
    }

    private static async Task PasarPorCompuertaAsync(DbCommand command)
    {
        var plan = Plan.Value;
        if (plan == null) return;

        var sql = command.CommandText;
        plan.Trace.Enqueue(Resumir(sql));

        if (plan.Fired || !plan.Trigger(sql)) return;

        plan.Fired = true;
        plan.Spid = (command.Connection as SqlConnection)?.ServerProcessId ?? 0;
        plan.Trace.Enqueue("  >>> COMPUERTA: operación detenida ANTES de ejecutar el comando anterior <<<");
        plan.Reached.TrySetResult();
        // Tope duro de seguridad: si el test se cuelga, la operación no queda detenida para siempre.
        await plan.Release.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static string Resumir(string sql)
    {
        var plano = string.Join(' ', sql.Split(['\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries));
        return plano.Length <= 170 ? plano : plano[..170] + "…";
    }
}

/// <summary>Usuario fijo para que CajaService encuentre la apertura sembrada.</summary>
internal sealed class ConcCurrentUser : ICurrentUserService
{
    public const string Usuario = "qaconc";
    public string GetUsername() => Usuario;
    public string GetUserId() => "qaconc-id";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "qaconc@example.invalid";
    public bool IsInRole(string role) => true;
    public bool HasPermission(string modulo, string accion) => true;
    public string? GetIpAddress() => "127.0.0.1";
}

internal static class ConcSeed
{
    public static async Task<int> SembrarAperturaAsync(AppDbContext ctx, string usuario = ConcCurrentUser.Usuario)
    {
        var caja = new Caja
        {
            Codigo = "QC" + Guid.NewGuid().ToString("N")[..6],
            Nombre = "Caja QA conc",
            Estado = EstadoCaja.Abierta,
            RowVersion = new byte[8]
        };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();
        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = usuario,
            MontoInicial = 0m,
            FechaApertura = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        ctx.AperturasCaja.Add(apertura);
        await ctx.SaveChangesAsync();
        return apertura.Id;
    }

    /// <summary>Venta a crédito Confirmada con crédito Generado y 3 cuotas Pendientes de 300 (estado post ConfirmarVentaCredito).</summary>
    public static async Task<EscenarioSembrado> SembrarAsync(
        AppDbContext ctx, int aperturaCajaId, bool conIngresoDeVenta = true, string usuarioMovimiento = ConcCurrentUser.Usuario)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var cliente = new Cliente
        {
            TipoDocumento = "DNI",
            NumeroDocumento = sufijo,
            Nombre = "Conc",
            Apellido = "QA" + sufijo,
            Telefono = "0000000000",
            Domicilio = "QA 123",
            PuntajeCliente = 5,
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = "CRQC" + sufijo,
            Estado = EstadoCredito.Generado,
            MontoSolicitado = 900m,
            MontoAprobado = 900m,
            SaldoPendiente = 900m,
            TasaInteres = 0,
            CantidadCuotas = 3,
            MontoCuota = 300m,
            TotalAPagar = 900m,
            FechaSolicitud = DateTime.UtcNow,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1)
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();

        var cuotas = Enumerable.Range(1, 3).Select(n => new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = n,
            MontoCapital = 300m,
            MontoInteres = 0m,
            MontoTotal = 300m,
            FechaVencimiento = DateTime.UtcNow.AddMonths(n),
            Estado = EstadoCuota.Pendiente
        }).ToList();
        ctx.Cuotas.AddRange(cuotas);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Numero = "VQC" + sufijo,
            Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.CreditoPersonal,
            Total = 900m,
            Subtotal = 900m,
            FechaVenta = DateTime.UtcNow,
            FechaConfiguracionCredito = DateTime.UtcNow
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();

        if (conIngresoDeVenta)
        {
            // Ingreso propio de la venta: la cancelación debe contra-asentarlo UNA sola vez.
            ctx.MovimientosCaja.Add(new MovimientoCaja
            {
                AperturaCajaId = aperturaCajaId,
                Tipo = TipoMovimientoCaja.Ingreso,
                Concepto = ConceptoMovimientoCaja.VentaEfectivo,
                Monto = 50m,
                Descripcion = "Ingreso de venta " + venta.Numero,
                Referencia = venta.Numero,
                VentaId = venta.Id,
                Usuario = usuarioMovimiento,
                FechaMovimiento = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        return new EscenarioSembrado(venta.Id, credito.Id, cliente.Id, cuotas.Select(c => c.Id).ToArray(), venta.Numero);
    }
}

public sealed class ConcSqlHostFixture : IAsyncLifetime
{
    private static readonly Type[] BackgroundServicesToRemove =
    [
        typeof(MoraBackgroundService),
        typeof(AlertaStockBackgroundService),
        typeof(DocumentoVencidoBackgroundService),
        typeof(CotizacionVencimientoBackgroundService),
    ];

    private WebApplicationFactory<Program>? _factory;
    public IServiceProvider Services => _factory!.Services;
    public int AperturaCajaId { get; private set; }

    /// <summary>
    /// Catálogo PROPIO de esta clase ("&lt;catálogo base&gt;_svc"): EnsureCreated no crea nada si la BD ya tiene
    /// tablas (el harness de modelo crea el esquema [qaconc] en el catálogo base, en paralelo). Sigue
    /// empezando con "qa_fin_conc": hereda la protección contra apuntar a una BD real.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;
    private string _masterCs = string.Empty;
    private string _catalogoSvc = string.Empty;

    public async Task InitializeAsync()
    {
        if (ConcEnv.SkipReason() != null) return;

        var baseCs = new SqlConnectionStringBuilder(ConcEnv.ConnectionString!);
        _catalogoSvc = baseCs.InitialCatalog + "_svc";
        if (!_catalogoSvc.StartsWith("qa_fin_conc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Catálogo fuera de la zona autorizada: " + _catalogoSvc);
        baseCs.InitialCatalog = _catalogoSvc;
        var cs = ConnectionString = baseCs.ConnectionString;
        baseCs.InitialCatalog = "master";
        _masterCs = baseCs.ConnectionString;
        await EjecutarEnMasterAsync($"IF DB_ID(N'{_catalogoSvc}') IS NOT NULL BEGIN ALTER DATABASE [{_catalogoSvc}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_catalogoSvc}]; END; CREATE DATABASE [{_catalogoSvc}];");
        // Igual que el catálogo base: si el runner pidió RCSI, la BD de servicios lo hereda.
        if (Environment.GetEnvironmentVariable("QA_FIN_CONC_RCSI") == "1")
            await EjecutarEnMasterAsync($"ALTER DATABASE [{_catalogoSvc}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;");
        var gate = new ConcGateInterceptor();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d =>
                             d.ServiceType == typeof(IDbContextFactory<AppDbContext>) ||
                             d.ServiceType == typeof(AppDbContext) ||
                             d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                             d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>) ||
                             d.ServiceType == typeof(DbContextOptions)).ToList())
                    services.Remove(d);

                void SqlOptions(DbContextOptionsBuilder o) => o.UseSqlServer(cs).AddInterceptors(gate);
                services.AddDbContext<AppDbContext>(SqlOptions);
                services.AddDbContextFactory<AppDbContext>(SqlOptions, ServiceLifetime.Scoped);

                foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService)
                                                      && d.ImplementationType != null
                                                      && BackgroundServicesToRemove.Contains(d.ImplementationType)).ToList())
                    services.Remove(d);

                services.RemoveAll<ICurrentUserService>();
                services.AddScoped<ICurrentUserService, ConcCurrentUser>();

                services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        });

        using var scope = Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await ctx.Database.MigrateAsync();

        AperturaCajaId = await ConcSeed.SembrarAperturaAsync(ctx);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_masterCs.Length > 0 && _catalogoSvc.StartsWith("qa_fin_conc", StringComparison.OrdinalIgnoreCase))
        {
            SqlConnection.ClearAllPools();
            await EjecutarEnMasterAsync($"IF DB_ID(N'{_catalogoSvc}') IS NOT NULL BEGIN ALTER DATABASE [{_catalogoSvc}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_catalogoSvc}]; END;");
        }
    }

    private async Task EjecutarEnMasterAsync(string sql)
    {
        await using var c = new SqlConnection(_masterCs);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}

public sealed record EscenarioSembrado(int VentaId, int CreditoId, int ClienteId, int[] CuotaIds, string VentaNumero);

public class CancelarVsPagoServiciosRealesSqlServerTests : IClassFixture<ConcSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(7);

    private readonly ConcSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public CancelarVsPagoServiciosRealesSqlServerTests(ConcSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // ------------------------------------------------------------------ compuertas (qué comando detener)

    private static bool EsSelectDeCuotasConCredito(string sql)
    {
        var s = sql.TrimStart();
        return s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
               && s.Contains("[Cuotas]", StringComparison.Ordinal)
               && s.Contains("[Creditos]", StringComparison.Ordinal)
               && !s.Contains("EXISTS", StringComparison.OrdinalIgnoreCase)
               && !s.Contains("UPDLOCK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cancelación: justo ANTES de cargar las cuotas del crédito (RestaurarCreditoPersonall), es decir
    /// DESPUÉS de que la guardia de pagos ya corrió. Es la ventana I-4 de la auditoría.
    /// </summary>
    private static GatePlan CancelacionTrasGuardia() =>
        new("cancelacion/tras-guardia", EsSelectDeCuotasConCredito);

    /// <summary>
    /// Pago: justo ANTES de insertar el PagoCuota, con la cuota ya actualizada (X lock) y el
    /// movimiento de caja ya insertado, todo sin commit.
    /// </summary>
    private static GatePlan PagoEnVuelo() =>
        new("pago/en-vuelo", sql => sql.Contains("INSERT INTO [PagosCuota]", StringComparison.Ordinal));

    // ------------------------------------------------------------------ ejecución

    private async Task<string> EjecutarAsync(GatePlan? plan, Func<IServiceProvider, Task<object?>> operacion)
    {
        try
        {
            ConcGateInterceptor.Plan.Value = plan;
            using var scope = _fx.Services.CreateScope();
            var r = await operacion(scope.ServiceProvider);
            return $"OK({r})";
        }
        catch (Exception ex)
        {
            return Clasificar(ex);
        }
        finally
        {
            ConcGateInterceptor.Plan.Value = null;
        }
    }

    private static string Clasificar(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number == 1205))
                return $"DEADLOCK_VICTIMA({ex.GetType().Name})";
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private Task<string> CancelarAsync(EscenarioSembrado s, GatePlan? plan, string motivo = "QA conc") =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
            await sp.GetRequiredService<IVentaService>().CancelarVentaAsync(s.VentaId, motivo)));

    private Task<string> PagarAsync(EscenarioSembrado s, int indiceCuota, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
        {
            var ctx = sp.GetRequiredService<AppDbContext>();
            var cuota = await ctx.Cuotas.AsNoTracking().FirstAsync(c => c.Id == s.CuotaIds[indiceCuota]);
            return await sp.GetRequiredService<ICreditoService>().PagarCuotaAsync(new PagarCuotaViewModel
            {
                CreditoId = s.CreditoId,
                CuotaId = cuota.Id,
                MontoPagado = cuota.MontoTotal,
                MedioPago = "Efectivo"
            });
        }));

    private Task<string> PagarMultipleAsync(EscenarioSembrado s, int indiceCuota, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
        {
            var ctx = sp.GetRequiredService<AppDbContext>();
            var cuota = await ctx.Cuotas.AsNoTracking().FirstAsync(c => c.Id == s.CuotaIds[indiceCuota]);
            var r = await sp.GetRequiredService<ICreditoService>().PagarCuotasAsync(new PagoMultipleCuotasRequest
            {
                ClienteId = s.ClienteId,
                CuotaIds = [cuota.Id],
                RowVersionsPorCuota = new Dictionary<int, string> { [cuota.Id] = Convert.ToBase64String(cuota.RowVersion) },
                MedioPago = "Efectivo"
            });
            return r.CantidadCuotas;
        }));

    // ------------------------------------------------------------------ siembra

    public async Task<EscenarioSembrado> SembrarAsync(bool conIngresoDeVenta = true)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ConcSeed.SembrarAsync(ctx, _fx.AperturaCajaId, conIngresoDeVenta);
    }

    // ------------------------------------------------------------------ evidencia SQL real

    private async Task<SqlConnection> AbrirAsync()
    {
        var c = new SqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        return c;
    }

    private async Task<string> SnapshotSesionAsync(int spid, string etiqueta)
    {
        if (spid <= 0) return $"[{etiqueta}] spid desconocido";
        var sb = new StringBuilder();
        await using var c = await AbrirAsync();

        await using (var cmd = new SqlCommand(
            "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id=@s", c))
        {
            cmd.Parameters.AddWithValue("@s", spid);
            var lvl = await cmd.ExecuteScalarAsync();
            var nombre = lvl switch { (short)1 => "READ UNCOMMITTED", (short)2 => "READ COMMITTED", (short)3 => "REPEATABLE READ",
                (short)4 => "SERIALIZABLE", (short)5 => "SNAPSHOT", _ => "?" };
            sb.AppendLine($"[{etiqueta}] spid={spid} aislamiento={nombre}");
        }

        await using (var cmd = new SqlCommand(@"
SELECT l.resource_type, l.request_mode, l.request_status, COUNT(*) n,
       ISNULL(OBJECT_NAME(p.object_id), '') obj
FROM sys.dm_tran_locks l
LEFT JOIN sys.partitions p ON p.hobt_id = l.resource_associated_entity_id AND l.resource_type IN ('KEY','PAGE','RID','HOBT')
WHERE l.request_session_id=@s AND l.resource_database_id=DB_ID() AND l.resource_type <> 'DATABASE'
GROUP BY l.resource_type, l.request_mode, l.request_status, ISNULL(OBJECT_NAME(p.object_id), '')
ORDER BY obj, l.resource_type", c))
        {
            cmd.Parameters.AddWithValue("@s", spid);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                sb.AppendLine($"    lock {r.GetString(0),-6} {r.GetString(1),-5} {r.GetString(2),-7} x{r.GetInt32(3)} {r.GetString(4)}");
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<List<string>> BloqueosActualesAsync()
    {
        var lista = new List<string>();
        await using var c = await AbrirAsync();
        await using var cmd = new SqlCommand(@"
SELECT r.session_id, r.blocking_session_id, r.wait_type, r.wait_time
FROM sys.dm_exec_requests r
WHERE r.blocking_session_id <> 0 AND r.database_id = DB_ID()", c);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add($"sesion {rd.GetInt16(0)} bloqueada por {rd.GetInt16(1)} ({rd.GetString(2)}, {rd.GetInt32(3)} ms)");
        return lista;
    }

    private async Task<bool> EsperarAsync(Func<Task<bool>> condicion, TimeSpan max)
    {
        var fin = DateTime.UtcNow + max;
        while (DateTime.UtcNow < fin)
        {
            if (await condicion()) return true;
            await Task.Delay(100);
        }
        return await condicion();
    }

    // ------------------------------------------------------------------ invariantes

    private async Task<List<string>> ViolacionesAsync(EscenarioSembrado s)
    {
        var v = new List<string>();
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var venta = await ctx.Ventas.AsNoTracking().FirstAsync(x => x.Id == s.VentaId);
        var credito = await ctx.Creditos.AsNoTracking().FirstAsync(x => x.Id == s.CreditoId);
        var cuotas = await ctx.Cuotas.AsNoTracking().Where(x => x.CreditoId == s.CreditoId).OrderBy(x => x.NumeroCuota).ToListAsync();
        var pagos = await ctx.PagosCuota.AsNoTracking()
            .Where(p => p.Cuota.CreditoId == s.CreditoId).ToListAsync();
        var aplicados = pagos.Where(p => p.Estado == EstadoPagoCuota.Aplicado && !p.IsDeleted).ToList();
        var cobros = await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Concepto == ConceptoMovimientoCaja.CobroCuota
                        && m.ReferenciaId != null && s.CuotaIds.Contains(m.ReferenciaId.Value)).ToListAsync();
        var contra = await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => !m.IsDeleted && m.VentaId == s.VentaId && m.Tipo == TipoMovimientoCaja.Egreso).ToListAsync();

        var ventaCancelada = venta.Estado == EstadoVenta.Cancelada;
        var creditoCancelado = credito.Estado == EstadoCredito.Cancelado;

        if (ventaCancelada != creditoCancelado)
            v.Add($"Venta {venta.Estado} vs Crédito {credito.Estado}: deben cancelarse juntos.");
        if (creditoCancelado && aplicados.Count > 0)
            v.Add($"Crédito Cancelado con {aplicados.Count} PagoCuota Aplicado (pago huérfano).");
        if (creditoCancelado && cuotas.Any(c => c.MontoPagado > 0m))
            v.Add("Crédito Cancelado con cuotas con MontoPagado > 0.");
        if (ventaCancelada && cobros.Count > 0)
            v.Add($"Venta Cancelada con {cobros.Count} cobro(s) de cuota en caja (ingreso no revertido).");
        if (aplicados.Sum(p => p.ImporteAplicadoCuota) != cuotas.Sum(c => c.MontoPagado))
            v.Add($"Ledger ({aplicados.Sum(p => p.ImporteAplicadoCuota)}) != Σ Cuota.MontoPagado ({cuotas.Sum(c => c.MontoPagado)}).");
        if (aplicados.Count != cobros.Count)
            v.Add($"PagoCuota aplicados ({aplicados.Count}) != cobros en caja ({cobros.Count}).");
        if (aplicados.Any(p => p.MovimientoCajaId == null || cobros.All(m => m.Id != p.MovimientoCajaId)))
            v.Add("PagoCuota sin movimiento de caja que lo respalde.");
        if (contra.Count > 1)
            v.Add($"{contra.Count} contramovimientos de caja para la misma venta (debe ser ≤ 1).");
        if (ventaCancelada && contra.Count != 1 && contra.Count != 0)
            v.Add("Contramovimiento inconsistente.");
        if (contra.Any(m => m.ReferenciaId != null && s.CuotaIds.Contains(m.ReferenciaId.Value)))
            v.Add("Contramovimiento apunta a un cobro de cuota (operación ajena).");
        if (!creditoCancelado && cuotas.Any(c => c.Estado == EstadoCuota.Cancelada))
            v.Add("Cuotas Canceladas con crédito no cancelado.");
        return v;
    }

    private async Task AssertInvarianteAsync(EscenarioSembrado s, string contexto)
    {
        var v = await ViolacionesAsync(s);
        _out.WriteLine($"[INVARIANTE {contexto}] " + (v.Count == 0 ? "OK" : "VIOLADO: " + string.Join(" | ", v)));
        Assert.True(v.Count == 0, $"{contexto}: {string.Join(" | ", v)}");
    }

    private void Volcar(string titulo, GatePlan plan)
    {
        _out.WriteLine($"--- traza SQL {titulo} ({plan.Nombre}) ---");
        foreach (var l in plan.Trace) _out.WriteLine("  " + l);
    }

    // =====================================================================================
    // 0. Entorno: aislamiento real y READ_COMMITTED_SNAPSHOT
    // =====================================================================================

    [SqlServerConcFact]
    public async Task Entorno_VerificaCatalogoYRcsi()
    {
        await using var c = await AbrirAsync();
        await using var cmd = new SqlCommand(@"
SELECT DB_NAME(), d.is_read_committed_snapshot_on, d.snapshot_isolation_state_desc, SERVERPROPERTY('ProductVersion')
FROM sys.databases d WHERE d.database_id = DB_ID()", c);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        var catalogo = r.GetString(0);
        var rcsi = r.GetBoolean(1);
        _out.WriteLine($"catalogo={catalogo} READ_COMMITTED_SNAPSHOT={(rcsi ? "ON" : "OFF")} ALLOW_SNAPSHOT_ISOLATION={r.GetString(2)} SQL Server {r.GetValue(3)}");
        Assert.StartsWith("qa_fin_conc", catalogo, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================================
    // 1. I-4: la cancelación pasó la guardia; el pago confirma ANTES de que cargue las cuotas.
    // =====================================================================================

    [SqlServerConcFact]
    public async Task I4_PagoIntercaladoTrasLaGuardia_NoDejaVentaCanceladaConPagoAplicado()
    {
        var s = await SembrarAsync();
        var plan = CancelacionTrasGuardia();
        var cancelar = CancelarAsync(s, plan);

        var llego = await Task.WhenAny(plan.Reached.Task, cancelar) == plan.Reached.Task;
        Assert.True(llego, "La cancelación no llegó a la compuerta (¿rechazó antes?): " + (cancelar.IsCompleted ? cancelar.Result : "?"));
        _out.WriteLine(await SnapshotSesionAsync(plan.Spid, "cancelación detenida tras la guardia"));

        // Con la cancelación detenida EN la ventana peligrosa, se lanza el pago.
        var pagar = PagarAsync(s, 0);
        var avanzo = await EsperarAsync(
            async () => pagar.IsCompleted || (await BloqueosActualesAsync()).Count > 0, EsperaObservacion);
        var pagoConfirmoAntes = pagar.IsCompleted && pagar.Result.StartsWith("OK(");
        var bloqueos = await BloqueosActualesAsync();
        _out.WriteLine($"pago: terminó antes de liberar={pagar.IsCompleted} resultado={(pagar.IsCompleted ? pagar.Result : "(pendiente)")}");
        _out.WriteLine("bloqueos observados con la cancelación detenida: " + (bloqueos.Count == 0 ? "ninguno" : string.Join("; ", bloqueos)));
        _out.WriteLine($"=> secuencia forzada: pagoConfirmoDuranteLaVentana={pagoConfirmoAntes} pagoBloqueadoPorCancelacion={bloqueos.Count > 0}");
        Assert.True(avanzo || pagar.IsCompleted || bloqueos.Count > 0);

        plan.Release.TrySetResult();
        var resCancelar = await cancelar;
        var resPagar = await pagar;
        Volcar("cancelación", plan);
        _out.WriteLine($"resultado cancelación: {resCancelar}");
        _out.WriteLine($"resultado pago:        {resPagar}");

        await AssertInvarianteAsync(s, "I-4");
        Assert.DoesNotContain("DEADLOCK", resCancelar + resPagar);
    }

    [SqlServerConcFact]
    public async Task I4_PagoMultiple_IntercaladoTrasLaGuardia_NoDejaVentaCanceladaConPagoAplicado()
    {
        var s = await SembrarAsync();
        var plan = CancelacionTrasGuardia();
        var cancelar = CancelarAsync(s, plan);
        Assert.True(await Task.WhenAny(plan.Reached.Task, cancelar) == plan.Reached.Task, "no llegó a la compuerta");

        var pagar = PagarMultipleAsync(s, 1);
        await EsperarAsync(async () => pagar.IsCompleted || (await BloqueosActualesAsync()).Count > 0, EsperaObservacion);
        _out.WriteLine($"pago múltiple: terminó antes de liberar={pagar.IsCompleted}; bloqueos: {string.Join("; ", await BloqueosActualesAsync())}");

        plan.Release.TrySetResult();
        var resCancelar = await cancelar;
        var resPagar = await pagar;
        _out.WriteLine($"cancelación: {resCancelar}\npago múltiple: {resPagar}");

        await AssertInvarianteAsync(s, "I-4 pago múltiple");
        Assert.DoesNotContain("DEADLOCK", resCancelar + resPagar);
    }

    // =====================================================================================
    // 2. Pago EN VUELO (cuota actualizada, sin commit) y la cancelación llega durante el pago.
    // =====================================================================================

    [SqlServerConcFact]
    public async Task PagoEnVuelo_LaCancelacionPosteriorSeRechazaYNoHayPagoHuerfano()
    {
        var s = await SembrarAsync();
        var plan = PagoEnVuelo();
        var pagar = PagarAsync(s, 0, plan);
        Assert.True(await Task.WhenAny(plan.Reached.Task, pagar) == plan.Reached.Task, "el pago no llegó a la compuerta: " + (pagar.IsCompleted ? pagar.Result : "?"));
        _out.WriteLine(await SnapshotSesionAsync(plan.Spid, "pago en vuelo (cuota actualizada, sin commit)"));

        var cancelar = CancelarAsync(s, null);
        await EsperarAsync(async () => cancelar.IsCompleted || (await BloqueosActualesAsync()).Count > 0, EsperaObservacion);
        _out.WriteLine($"cancelación: terminó antes de liberar el pago={cancelar.IsCompleted}; bloqueos: {string.Join("; ", await BloqueosActualesAsync())}");

        plan.Release.TrySetResult();
        var resPagar = await pagar;
        var resCancelar = await cancelar;
        Volcar("pago", plan);
        _out.WriteLine($"pago: {resPagar}\ncancelación: {resCancelar}");

        Assert.StartsWith("OK(", resPagar);
        Assert.Contains("pagos aplicados", resCancelar, StringComparison.OrdinalIgnoreCase);
        await AssertInvarianteAsync(s, "pago en vuelo");

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(EstadoVenta.Confirmada, (await ctx.Ventas.AsNoTracking().FirstAsync(v => v.Id == s.VentaId)).Estado);
        Assert.NotEqual(EstadoCredito.Cancelado, (await ctx.Creditos.AsNoTracking().FirstAsync(c => c.Id == s.CreditoId)).Estado);
    }

    // =====================================================================================
    // 3. Antes / después (sin solapamiento).
    // =====================================================================================

    [SqlServerConcFact]
    public async Task PagoCompletoAntes_LaCancelacionSeRechazaSinEscribir()
    {
        var s = await SembrarAsync();
        Assert.StartsWith("OK(", await PagarAsync(s, 0));
        var res = await CancelarAsync(s, null);
        _out.WriteLine("cancelación tras pago: " + res);
        Assert.Contains("pagos aplicados", res, StringComparison.OrdinalIgnoreCase);
        await AssertInvarianteAsync(s, "pago antes");
    }

    [SqlServerConcFact]
    public async Task CancelacionCompletaAntes_ElPagoPosteriorSeRechazaSinCobrar()
    {
        var s = await SembrarAsync();
        Assert.StartsWith("OK(", await CancelarAsync(s, null));
        var res = await PagarAsync(s, 0);
        _out.WriteLine("pago tras cancelación: " + res);
        Assert.StartsWith("PagoCuotaRechazadoException", res);
        await AssertInvarianteAsync(s, "pago después");
    }

    // =====================================================================================
    // 4. Dos cancelaciones simultáneas (la primera detenida en la ventana, la segunda compite).
    // =====================================================================================

    [SqlServerConcFact]
    public async Task DosCancelacionesSimultaneas_UnaSolaEscribeYSoloUnContramovimiento()
    {
        var s = await SembrarAsync();
        var plan = CancelacionTrasGuardia();
        var primera = CancelarAsync(s, plan, "primera");
        Assert.True(await Task.WhenAny(plan.Reached.Task, primera) == plan.Reached.Task, "la primera no llegó a la compuerta");

        var segunda = CancelarAsync(s, null, "segunda");
        await EsperarAsync(async () => segunda.IsCompleted || (await BloqueosActualesAsync()).Count > 0, EsperaObservacion);
        _out.WriteLine($"segunda: terminó antes de liberar={segunda.IsCompleted}; bloqueos: {string.Join("; ", await BloqueosActualesAsync())}");

        plan.Release.TrySetResult();
        var r1 = await primera;
        var r2 = await segunda;
        _out.WriteLine($"primera: {r1}\nsegunda: {r2}");

        Assert.Equal(1, new[] { r1, r2 }.Count(r => r.StartsWith("OK(")));
        Assert.Equal(1, new[] { r1, r2 }.Count(r => r.StartsWith("InvalidOperationException")));
        await AssertInvarianteAsync(s, "doble cancelación");

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var venta = await ctx.Ventas.AsNoTracking().FirstAsync(v => v.Id == s.VentaId);
        Assert.Equal(EstadoVenta.Cancelada, venta.Estado);
        Assert.Equal("primera", venta.MotivoCancelacion);
        Assert.Equal(1, await ctx.MovimientosCaja.CountAsync(m => m.VentaId == s.VentaId && m.Tipo == TipoMovimientoCaja.Egreso && !m.IsDeleted));
    }

    // =====================================================================================
    // 5. RowVersion: la escritura concurrente se detecta (cuota y crédito).
    // =====================================================================================

    [SqlServerConcFact]
    public async Task RowVersion_DetectaEscrituraConcurrenteEnCuotaYCredito()
    {
        var s = await SembrarAsync(conIngresoDeVenta: false);

        using var a = _fx.Services.CreateScope();
        using var b = _fx.Services.CreateScope();
        var ctxA = a.ServiceProvider.GetRequiredService<AppDbContext>();
        var ctxB = b.ServiceProvider.GetRequiredService<AppDbContext>();

        var cuotaA = await ctxA.Cuotas.FirstAsync(c => c.Id == s.CuotaIds[0]);
        var creditoA = await ctxA.Creditos.FirstAsync(c => c.Id == s.CreditoId);
        var rvCuotaLeida = cuotaA.RowVersion.ToArray();

        var cuotaB = await ctxB.Cuotas.FirstAsync(c => c.Id == s.CuotaIds[0]);
        var creditoB = await ctxB.Creditos.FirstAsync(c => c.Id == s.CreditoId);
        cuotaB.MontoPagado = 1m;
        creditoB.SaldoPendiente = 899m;
        await ctxB.SaveChangesAsync();
        Assert.False(rvCuotaLeida.SequenceEqual(cuotaB.RowVersion), "SQL Server debió regenerar el rowversion de la cuota");

        cuotaA.Estado = EstadoCuota.Cancelada;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxA.SaveChangesAsync());

        ctxA.ChangeTracker.Clear();
        var creditoA2 = await ctxA.Creditos.FirstAsync(c => c.Id == s.CreditoId);
        ctxA.Entry(creditoA2).Property(c => c.RowVersion).OriginalValue = creditoA.RowVersion;
        creditoA2.Estado = EstadoCredito.Cancelado;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxA.SaveChangesAsync());
    }

    // =====================================================================================
    // 6. Estrés sin compuerta: pago y cancelación arrancan "a la vez" N veces. Sin deadlocks.
    // =====================================================================================

    [SqlServerConcFact]
    public async Task Estres_PagoVsCancelacionSimultaneos_ConservaInvarianteYNoHayDeadlocks()
    {
        const int iteraciones = 24;
        var deadlocks = 0;
        var cancelaciones = 0;
        var pagos = 0;
        var ambosRechazados = 0;

        for (var i = 0; i < iteraciones; i++)
        {
            var s = await SembrarAsync();
            using var barrera = new Barrier(2);
            var tareaPago = Task.Run(async () => { barrera.SignalAndWait(); return await PagarAsync(s, 0); });
            var tareaCancel = Task.Run(async () => { barrera.SignalAndWait(); return await CancelarAsync(s, null); });
            var rp = await tareaPago;
            var rc = await tareaCancel;

            if (rp.Contains("DEADLOCK") || rc.Contains("DEADLOCK")) deadlocks++;
            if (rp.StartsWith("OK(")) pagos++;
            if (rc.StartsWith("OK(")) cancelaciones++;
            if (!rp.StartsWith("OK(") && !rc.StartsWith("OK(")) ambosRechazados++;
            _out.WriteLine($"#{i:D2} pago={Corto(rp)} | cancelación={Corto(rc)}");

            Assert.False(rp.StartsWith("OK(") && rc.StartsWith("OK("), $"#{i}: pago Y cancelación exitosos sobre el mismo crédito ({rp} / {rc})");
            await AssertInvarianteAsync(s, $"estrés #{i}");
        }

        _out.WriteLine($"RESUMEN: pagos OK={pagos} cancelaciones OK={cancelaciones} ambos rechazados={ambosRechazados} deadlocks={deadlocks}");
        Assert.Equal(0, deadlocks);
    }

    private static string Corto(string s) => s.Length <= 90 ? s : s[..90] + "…";
}
