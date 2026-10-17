using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// FIN-CAJA-P0 (H-CAJA-1) — concurrencia REAL en SQL Server (efímero, solo CI) entre el CIERRE de caja y cada ruta que
// inserta un MovimientoCaja: alta manual, ingreso de venta, anticipo, cobro de cuota y reembolso de devolución.
//
// Servicios reales (CajaService) con compuertas deterministas (ConcGateInterceptor): una operación se detiene en un comando
// SQL concreto y el test lanza la otra. Dos puntos de parada cubren los dos órdenes posibles:
//   * "antes del lock"   -> el movimiento ya resolvió la apertura pero aún no tomó el lock; el cierre se completa primero.
//   * "con el lock"      -> el movimiento tomó el lock y está por insertar; el cierre debe ESPERAR y luego incluirlo.
// Sin Task.Delay como única sincronización: la espera fija solo comprueba que una operación sigue BLOQUEADA por un lock.
//
// Mismo gate de seguridad que los demás harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*".
// El workflow corre esta clase con READ_COMMITTED_SNAPSHOT apagado y encendido (QA_FIN_CONC_RCSI = 0 / 1).
// ---------------------------------------------------------------------------------------------

public sealed class FinCajaSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_caja";
}

public class FinCajaSqlServerTests : IClassFixture<FinCajaSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(7);

    private const decimal Base = 100m;      // ingreso en efectivo ya existente en la apertura
    private const decimal Monto = 25m;      // monto del movimiento bajo prueba

    private enum Ruta { Manual, Venta, Anticipo, Cuota, Devolucion }

    private readonly FinCajaSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public FinCajaSqlServerTests(FinCajaSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // ------------------------------------------------------------------ compuertas

    private static GatePlan Compuerta(string nombre, string fragmentoSql) =>
        new(nombre, sql => sql.Contains(fragmentoSql, StringComparison.Ordinal));

    /// <summary>Justo ANTES de tomar el lock de la apertura (apertura ya resuelta, lock libre).</summary>
    private static GatePlan AntesDelLock() => Compuerta("antes-del-lock", "[AperturasCaja] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");

    /// <summary>Con el lock ya tomado y validada la apertura, justo ANTES de insertar el movimiento.</summary>
    private static GatePlan ConLockAntesDelInsert() => Compuerta("con-lock/antes-del-insert", "INSERT INTO [MovimientosCaja]");

    /// <summary>Cierre con el lock ya tomado, justo ANTES de leer los movimientos para calcular los totales.</summary>
    private static GatePlan CierreConLockAntesDeLeerMovimientos() => Compuerta("cierre/con-lock/antes-de-leer-movimientos", "[MovimientosCaja]");

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

    private async Task<bool> LlegoALaCompuertaAsync(GatePlan plan, Task<string> operacion)
    {
        var llego = await Task.WhenAny(plan.Reached.Task, operacion, Task.Delay(EsperaCompuerta)) == plan.Reached.Task;
        if (!llego) Volcar("no llegó a la compuerta", plan);
        return llego;
    }

    /// <summary>true si la operación NO terminó dentro de la ventana de observación (= quedó bloqueada por un lock).</summary>
    private static async Task<bool> QuedaBloqueadaAsync(Task<string> operacion) =>
        await Task.WhenAny(operacion, Task.Delay(EsperaObservacion)) != operacion;

    private void Volcar(string titulo, GatePlan plan)
    {
        _out.WriteLine($"--- traza SQL {titulo} ({plan.Nombre}) ---");
        foreach (var l in plan.Trace) _out.WriteLine("  " + l);
    }

    // ------------------------------------------------------------------ operaciones

    private sealed record Escenario(string Usuario, int AperturaId, int VentaId, string VentaNumero);

    private Task<string> Mover(Ruta ruta, Escenario e, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
        {
            var caja = sp.GetRequiredService<ICajaService>();
            switch (ruta)
            {
                case Ruta.Manual:
                    return (await caja.RegistrarMovimientoAsync(new MovimientoCajaViewModel
                    {
                        AperturaCajaId = e.AperturaId, Tipo = TipoMovimientoCaja.Ingreso,
                        Concepto = ConceptoMovimientoCaja.AjusteCaja, Descripcion = "FIN-CAJA-P0", Monto = Monto
                    }, e.Usuario)).Id;
                case Ruta.Venta:
                    return (await caja.RegistrarMovimientoVentaAsync(e.VentaId, e.VentaNumero, Monto, TipoPago.Efectivo, e.Usuario))?.Id;
                case Ruta.Anticipo:
                    return (await caja.RegistrarMovimientoAnticipoAsync(990001, "CR-FIN-CAJA", Monto, e.Usuario))?.Id;
                case Ruta.Cuota:
                    return (await caja.RegistrarMovimientoCuotaAsync(
                        990002, "CR-FIN-CAJA", 1, Monto, 0m, TipoPago.Efectivo, "Efectivo", e.Usuario))?.Id;
                case Ruta.Devolucion:
                    return (await caja.RegistrarMovimientoDevolucionAsync(
                        990003, e.VentaId, e.VentaNumero, "DEV-FIN-CAJA", Monto, e.Usuario)).Id;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ruta));
            }
        }));

    private Task<string> Cerrar(Escenario e, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
        {
            var c = await sp.GetRequiredService<ICajaService>().CerrarCajaAsync(
                new CerrarCajaViewModel { AperturaCajaId = e.AperturaId, EfectivoContado = 0m, JustificacionDiferencia = "FIN-CAJA-P0" },
                e.Usuario);
            return $"ingresos={c.TotalIngresosSistema};egresos={c.TotalEgresosSistema}";
        }));

    // ------------------------------------------------------------------ siembra

    /// <summary>Apertura PROPIA (usuario único: la ruta por usuario la resuelve como la activa), una venta y un ingreso de 100 ya asentado.</summary>
    private async Task<Escenario> SembrarAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var usuario = "qc" + sufijo;
        var aperturaId = await ConcSeed.SembrarAperturaAsync(ctx, usuario);

        var cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = sufijo, Nombre = "Caja", Apellido = "QA" + sufijo,
            Telefono = "0000000000", Domicilio = "QA 123", PuntajeCliente = 5, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id, Numero = "VCJ" + sufijo, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo,
            Total = Monto, Subtotal = Monto, FechaVenta = DateTime.UtcNow, AperturaCajaId = aperturaId
        };
        ctx.Ventas.Add(venta);
        ctx.MovimientosCaja.Add(new MovimientoCaja
        {
            AperturaCajaId = aperturaId, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.AjusteCaja,
            Monto = Base, Descripcion = "Ingreso base FIN-CAJA-P0", Usuario = usuario, FechaMovimiento = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return new Escenario(usuario, aperturaId, venta.Id, venta.Numero);
    }

    private sealed record Libro(
        bool Cerrada, int Cierres, CierreCaja? Cierre, List<MovimientoCaja> Movimientos, AperturaFisicoResumen Fisico)
    {
        public int NuevosMovimientos => Movimientos.Count(m => m.Descripcion != "Ingreso base FIN-CAJA-P0");
    }

    private async Task<Libro> LeerAsync(int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = await ctx.AperturasCaja.AsNoTracking()
            .Include(a => a.Movimientos).Include(a => a.Cierre)
            .FirstAsync(a => a.Id == aperturaId);
        var cierres = await ctx.CierresCaja.AsNoTracking().CountAsync(c => c.AperturaCajaId == aperturaId);
        var movimientos = apertura.Movimientos.Where(m => !m.IsDeleted).ToList();
        return new Libro(apertura.Cerrada, cierres, apertura.Cierre, movimientos, CajaService.CalcularResumenFisico(apertura));
    }

    /// <summary>Invariante central de H-CAJA-1: el snapshot del cierre coincide con el libro de la apertura (nada entró después).</summary>
    private static void AssertCierreCoincideConLibro(Libro l)
    {
        Assert.True(l.Cerrada);
        Assert.NotNull(l.Cierre);
        Assert.Equal(1, l.Cierres);
        Assert.Equal(l.Fisico.IngresosFisicos, l.Cierre!.TotalIngresosSistema);
        Assert.Equal(l.Fisico.EgresosFisicos, l.Cierre.TotalEgresosSistema);
    }

    /// <summary>
    /// Movimientos nuevos que OTRA sesión ve confirmados. READPAST salta filas con lock ajeno: no se bloquea con RCSI apagado
    /// (una lectura común esperaría la fila sin confirmar) y, con RCSI encendido, tampoco ve la versión no confirmada.
    /// </summary>
    private async Task<int> ContarNuevosVisiblesParaOtraSesionAsync(int aperturaId)
    {
        await using var c = new SqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM [MovimientosCaja] WITH (READPAST) WHERE [AperturaCajaId] = @a AND [IsDeleted] = 0 AND [Descripcion] <> N'Ingreso base FIN-CAJA-P0'", c);
        cmd.Parameters.AddWithValue("@a", aperturaId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    // ================================================================== escenarios

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-00] el catalogo corre con el modo de aislamiento pedido (RCSI)")]
    public async Task CAJA00_ModoDeAislamientoDelCatalogo_CoincideConElPedido()
    {
        var pedido = Environment.GetEnvironmentVariable("QA_FIN_CONC_RCSI") == "1" ? 1 : 0;
        await using var c = new SqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()", c);
        var real = (int)(await cmd.ExecuteScalarAsync())!;
        _out.WriteLine($"READ_COMMITTED_SNAPSHOT pedido={pedido} real={real}");
        Assert.Equal(pedido, real);
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-01] secuencial: movimiento nuevo sobre una apertura cerrada se rechaza sin efectos")]
    public async Task CAJA01_MovimientoSobreAperturaCerrada_SeRechazaSinEfectos()
    {
        foreach (var ruta in new[] { Ruta.Manual, Ruta.Venta, Ruta.Anticipo, Ruta.Cuota, Ruta.Devolucion })
        {
            var e = await SembrarAsync();
            Assert.StartsWith("OK(", await Cerrar(e));
            var antes = await LeerAsync(e.AperturaId);

            var r = await Mover(ruta, e);
            _out.WriteLine($"{ruta}: {r}");

            // Manual y Venta resuelven la apertura por id (la rechaza el lock); las otras la buscan por usuario y ya no hay caja abierta.
            if (ruta is Ruta.Manual or Ruta.Venta)
            {
                Assert.Contains("InvalidOperationException", r);
                Assert.Contains("cerrada", r, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.DoesNotContain("DEADLOCK", r);
            }

            var despues = await LeerAsync(e.AperturaId);
            Assert.Equal(antes.Movimientos.Count, despues.Movimientos.Count);
            Assert.Equal(0, despues.NuevosMovimientos);
            AssertCierreCoincideConLibro(despues);
            Assert.Equal(antes.Cierre!.TotalIngresosSistema, despues.Cierre!.TotalIngresosSistema);
        }
    }

    // --- CAJA-02: el cierre se completa ANTES de que el movimiento tome el lock -> el movimiento se rechaza sin efectos.

    private async Task CierreCompletadoAntesDelMovimientoAsync(Ruta ruta)
    {
        var e = await SembrarAsync();
        var plan = AntesDelLock();
        var mover = Mover(ruta, e, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, mover),
            "El movimiento no llegó a la compuerta: " + (mover.IsCompleted ? mover.Result : "?"));

        // Movimiento detenido con la apertura ya resuelta y abierta; el lock está libre: el cierre se completa.
        var cierre = await Cerrar(e);
        _out.WriteLine($"cierre con el movimiento detenido antes del lock: {cierre}");
        Assert.StartsWith("OK(", cierre);

        plan.Release.TrySetResult();
        var res = await mover;
        Volcar("movimiento", plan);
        _out.WriteLine($"{ruta}: {res}");

        Assert.Contains("InvalidOperationException", res);
        Assert.Contains("cerrada", res, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEADLOCK", res);

        var l = await LeerAsync(e.AperturaId);
        Assert.Equal(0, l.NuevosMovimientos);                 // el movimiento rechazado no dejó rastro
        AssertCierreCoincideConLibro(l);
        Assert.Equal(Base, l.Cierre!.TotalIngresosSistema);   // el cierre solo vio el ingreso base
        Assert.Equal(0m, l.Cierre.TotalEgresosSistema);
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-02-MANUAL] cierre completado antes del movimiento manual: rechazado sin efectos")]
    public Task CAJA02_Manual() => CierreCompletadoAntesDelMovimientoAsync(Ruta.Manual);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-02-VENTA] cierre completado antes del ingreso de venta: rechazado sin efectos")]
    public Task CAJA02_Venta() => CierreCompletadoAntesDelMovimientoAsync(Ruta.Venta);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-02-ANTICIPO] cierre completado antes del anticipo: rechazado sin efectos")]
    public Task CAJA02_Anticipo() => CierreCompletadoAntesDelMovimientoAsync(Ruta.Anticipo);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-02-CUOTA] cierre completado antes del cobro de cuota: rechazado sin efectos")]
    public Task CAJA02_Cuota() => CierreCompletadoAntesDelMovimientoAsync(Ruta.Cuota);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-02-DEVOLUCION] cierre completado antes del reembolso de devolucion: rechazado sin efectos")]
    public Task CAJA02_Devolucion() => CierreCompletadoAntesDelMovimientoAsync(Ruta.Devolucion);

    // --- CAJA-03: el movimiento está EN VUELO con el lock -> el cierre espera, lo incluye y calcula bien.

    private async Task MovimientoEnVueloAntesDelCierreAsync(Ruta ruta)
    {
        var e = await SembrarAsync();
        var plan = ConLockAntesDelInsert();
        var mover = Mover(ruta, e, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, mover),
            "El movimiento no llegó a la compuerta: " + (mover.IsCompleted ? mover.Result : "?"));

        // Movimiento detenido con el lock tomado y SIN commit: el cierre debe esperar, no calcular sobre un libro incompleto.
        var cierre = Cerrar(e);
        var bloqueado = await QuedaBloqueadaAsync(cierre);
        _out.WriteLine($"cierre bloqueado por el lock de la apertura mientras el movimiento está en vuelo: {bloqueado}");
        Assert.True(bloqueado, "El cierre NO debió avanzar mientras el movimiento retiene la apertura: " + (cierre.IsCompleted ? cierre.Result : "?"));

        plan.Release.TrySetResult();
        var resMover = await mover;
        var resCierre = await cierre;
        Volcar("movimiento", plan);
        _out.WriteLine($"{ruta}: {resMover}\ncierre: {resCierre}");

        Assert.StartsWith("OK(", resMover);
        Assert.StartsWith("OK(", resCierre);
        Assert.DoesNotContain("DEADLOCK", resMover + resCierre);

        var l = await LeerAsync(e.AperturaId);
        Assert.Equal(1, l.NuevosMovimientos);                 // el movimiento confirmado quedó en la apertura...
        AssertCierreCoincideConLibro(l);                      // ...y el cierre lo incluye (snapshot == libro)
        switch (ruta)
        {
            case Ruta.Devolucion:
                Assert.Equal(Base, l.Cierre!.TotalIngresosSistema);
                Assert.Equal(Monto, l.Cierre.TotalEgresosSistema);
                break;
            case Ruta.Anticipo:
                // El anticipo no declara medio de pago físico: lo exigible es que el cierre coincida con el libro (arriba).
                break;
            default:
                Assert.Equal(Base + Monto, l.Cierre!.TotalIngresosSistema);
                Assert.Equal(0m, l.Cierre.TotalEgresosSistema);
                break;
        }
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-03-MANUAL] movimiento manual en vuelo: el cierre espera y lo incluye")]
    public Task CAJA03_Manual() => MovimientoEnVueloAntesDelCierreAsync(Ruta.Manual);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-03-VENTA] ingreso de venta en vuelo: el cierre espera y lo incluye")]
    public Task CAJA03_Venta() => MovimientoEnVueloAntesDelCierreAsync(Ruta.Venta);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-03-ANTICIPO] anticipo en vuelo: el cierre espera y lo incluye")]
    public Task CAJA03_Anticipo() => MovimientoEnVueloAntesDelCierreAsync(Ruta.Anticipo);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-03-CUOTA] cobro de cuota en vuelo: el cierre espera y lo incluye")]
    public Task CAJA03_Cuota() => MovimientoEnVueloAntesDelCierreAsync(Ruta.Cuota);

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-03-DEVOLUCION] reembolso de devolucion en vuelo: el cierre espera y lo incluye")]
    public Task CAJA03_Devolucion() => MovimientoEnVueloAntesDelCierreAsync(Ruta.Devolucion);

    // --- CAJA-04: dos cierres simultáneos -> uno solo vale.

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-04] dos cierres simultaneos de la misma apertura: un solo cierre valido")]
    public async Task CAJA04_DosCierresSimultaneos_UnSoloCierreValido()
    {
        var e = await SembrarAsync();
        var plan = CierreConLockAntesDeLeerMovimientos();
        var primero = Cerrar(e, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, primero),
            "El primer cierre no llegó a la compuerta: " + (primero.IsCompleted ? primero.Result : "?"));

        var segundo = Cerrar(e);
        Assert.True(await QuedaBloqueadaAsync(segundo), "El segundo cierre debía esperar el lock de la apertura.");

        plan.Release.TrySetResult();
        var res1 = await primero;
        var res2 = await segundo;
        Volcar("primer cierre", plan);
        _out.WriteLine($"primero: {res1}\nsegundo: {res2}");

        Assert.StartsWith("OK(", res1);
        Assert.Contains("InvalidOperationException", res2);
        Assert.Contains("cerrada", res2, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEADLOCK", res1 + res2);

        var l = await LeerAsync(e.AperturaId);
        AssertCierreCoincideConLibro(l);                      // exactamente 1 CierreCaja
        Assert.Equal(Base, l.Cierre!.TotalIngresosSistema);
    }

    // --- CAJA-05: transacción ambiental -> el movimiento participa, no confirma por su cuenta y el rollback es total.

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-05] transaccion ambiental: el movimiento no confirma por su cuenta, retiene el lock y el rollback lo deshace")]
    public async Task CAJA05_TransaccionAmbiental_ParticipaSinCommitYRollbackEsTotal()
    {
        var e = await SembrarAsync();
        Task<string>? cierre = null;

        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
            await using var tx = await ctx.Database.BeginTransactionAsync();

            var mov = await caja.RegistrarMovimientoAsync(new MovimientoCajaViewModel
            {
                AperturaCajaId = e.AperturaId, Tipo = TipoMovimientoCaja.Ingreso,
                Concepto = ConceptoMovimientoCaja.AjusteCaja, Descripcion = "FIN-CAJA-P0 ambiental", Monto = Monto
            }, e.Usuario);
            Assert.True(mov.Id > 0);
            Assert.NotNull(ctx.Database.CurrentTransaction);  // el servicio no cerró ni reemplazó la transacción del llamador

            // Sin commit del llamador: otra sesión no ve el movimiento y el cierre queda esperando el lock.
            Assert.Equal(0, await ContarNuevosVisiblesParaOtraSesionAsync(e.AperturaId));
            cierre = Cerrar(e);
            Assert.True(await QuedaBloqueadaAsync(cierre), "El cierre debía esperar a la transacción ambiental que retiene la apertura.");

            await tx.RollbackAsync();
        }

        var res = await cierre!;
        _out.WriteLine($"cierre tras el rollback del llamador: {res}");
        Assert.StartsWith("OK(", res);

        var l = await LeerAsync(e.AperturaId);
        Assert.Equal(0, l.NuevosMovimientos);                 // el rollback deshizo el movimiento
        AssertCierreCoincideConLibro(l);
        Assert.Equal(Base, l.Cierre!.TotalIngresosSistema);
    }

    // --- CAJA-06 (escenario A de H-CAJA-2): reversión en vuelo y cierre; el cierre espera e incluye el egreso.

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-06] contramovimiento de venta en vuelo: el cierre espera y lo incluye (H-CAJA-2, escenario A)")]
    public async Task CAJA06_ContramovimientoEnVuelo_SeSerializaConElCierre()
    {
        var e = await SembrarAsync();
        int ventaId;
        string ventaNumero;
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var esc = await ConcSeed.SembrarAsync(ctx, e.AperturaId, conIngresoDeVenta: true, usuarioMovimiento: e.Usuario);
            ventaId = esc.VentaId;
            ventaNumero = esc.VentaNumero;
        }

        var plan = ConLockAntesDelInsert();
        var revertir = Task.Run(() => EjecutarAsync(plan, async sp =>
            (await sp.GetRequiredService<ICajaService>().RegistrarContramovimientoVentaAsync(ventaId, ventaNumero, "FIN-CAJA-P0", e.Usuario))?.Id));

        Assert.True(await LlegoALaCompuertaAsync(plan, revertir),
            "El contramovimiento no llegó a la compuerta: " + (revertir.IsCompleted ? revertir.Result : "?"));

        var cierre = Cerrar(e);
        Assert.True(await QuedaBloqueadaAsync(cierre), "El cierre NO debió avanzar mientras el contramovimiento retiene la apertura.");

        plan.Release.TrySetResult();
        var resRevertir = await revertir;
        var resCierre = await cierre;
        _out.WriteLine($"contramovimiento: {resRevertir}\ncierre: {resCierre}");

        Assert.StartsWith("OK(", resRevertir);
        Assert.StartsWith("OK(", resCierre);
        Assert.DoesNotContain("DEADLOCK", resRevertir + resCierre);

        var l = await LeerAsync(e.AperturaId);
        AssertCierreCoincideConLibro(l);
        Assert.Contains(l.Movimientos, m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta);
    }

    // --- CAJA-07 (escenario B de H-CAJA-2): el cierre se completa ANTES de que la cancelación tome el lock de apertura.

    private async Task<(Escenario Caja, int VentaId, string VentaNumero, int CreditoId)> SembrarVentaConIngresoAsync()
    {
        var e = await SembrarAsync();
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var esc = await ConcSeed.SembrarAsync(ctx, e.AperturaId, conIngresoDeVenta: true, usuarioMovimiento: e.Usuario);
        return (e, esc.VentaId, esc.VentaNumero, esc.CreditoId);
    }

    private Task<string> CancelarVenta(int ventaId, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
            await sp.GetRequiredService<IVentaService>().CancelarVentaAsync(ventaId, "FIN-CAJA-2-P0")));

    private sealed record EstadoVentaCredito(EstadoVenta Venta, DateTime? FechaCancelacion, EstadoCredito Credito, int Cuotas);

    private async Task<EstadoVentaCredito> LeerVentaAsync(int ventaId, int creditoId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var v = await ctx.Ventas.AsNoTracking().FirstAsync(x => x.Id == ventaId);
        var c = await ctx.Creditos.AsNoTracking().FirstAsync(x => x.Id == creditoId);
        var cuotas = await ctx.Cuotas.AsNoTracking().CountAsync(x => x.CreditoId == creditoId && !x.IsDeleted && x.Estado == EstadoCuota.Pendiente);
        return new EstadoVentaCredito(v.Estado, v.FechaCancelacion, c.Estado, cuotas);
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-2-P0 [CAJA-07] cierre completado antes de la reversion: la cancelacion se rechaza y revierte toda su transaccion (H-CAJA-2, escenario B)")]
    public async Task CAJA07_CierreAntesDeLaReversion_LaCancelacionSeRechazaConRollbackCompleto()
    {
        var s = await SembrarVentaConIngresoAsync();
        var estadoAntes = await LeerVentaAsync(s.VentaId, s.CreditoId);

        // La cancelación avanza (venta/crédito ya modificados en su transacción) y se detiene justo antes de tomar el lock de apertura.
        var plan = AntesDelLock();
        var cancelar = CancelarVenta(s.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, cancelar),
            "La cancelación no llegó a la compuerta: " + (cancelar.IsCompleted ? cancelar.Result : "?"));

        // El cierre toma el lock libre, termina y congela el arqueo.
        Assert.StartsWith("OK(", await Cerrar(s.Caja));
        var cerrado = await LeerAsync(s.Caja.AperturaId);
        AssertCierreCoincideConLibro(cerrado);
        var movimientosAlCierre = cerrado.Movimientos.Count;

        plan.Release.TrySetResult();
        var res = await cancelar;
        _out.WriteLine($"cancelación tras el cierre: {res}");

        Assert.Contains("InvalidOperationException", res);
        Assert.Contains("caja ya cerrada", res, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEADLOCK", res);

        var l = await LeerAsync(s.Caja.AperturaId);
        AssertCierreCoincideConLibro(l);
        Assert.Equal(movimientosAlCierre, l.Movimientos.Count);
        Assert.DoesNotContain(l.Movimientos, m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta);
        Assert.Equal(cerrado.Cierre!.TotalEgresosSistema, l.Cierre!.TotalEgresosSistema);

        Assert.Equal(estadoAntes, await LeerVentaAsync(s.VentaId, s.CreditoId)); // venta, crédito y cuotas intactos (rollback completo)
    }

    // --- CAJA-08 (escenario C de H-CAJA-2): llamada directa a CajaService sobre una apertura ya cerrada.

    [SqlServerConcFact(DisplayName = "FIN-CAJA-2-P0 [CAJA-08] reversion directa sobre apertura cerrada: no inserta el egreso ni altera el cierre (H-CAJA-2, escenario C)")]
    public async Task CAJA08_ReversionDirectaSobreAperturaCerrada_SeRechazaSinEgreso()
    {
        var s = await SembrarVentaConIngresoAsync();
        Assert.StartsWith("OK(", await Cerrar(s.Caja));
        var antes = await LeerAsync(s.Caja.AperturaId);

        var res = await Task.Run(() => EjecutarAsync(null, async sp =>
            (await sp.GetRequiredService<ICajaService>()
                .RegistrarContramovimientoVentaAsync(s.VentaId, s.VentaNumero, "FIN-CAJA-2-P0", s.Caja.Usuario))?.Id));
        _out.WriteLine($"reversión directa: {res}");

        Assert.Contains("InvalidOperationException", res);
        Assert.Contains("caja ya cerrada", res, StringComparison.OrdinalIgnoreCase);

        var despues = await LeerAsync(s.Caja.AperturaId);
        AssertCierreCoincideConLibro(despues);
        Assert.Equal(antes.Movimientos.Count, despues.Movimientos.Count);
        Assert.DoesNotContain(despues.Movimientos, m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta);
        Assert.Equal(antes.Cierre!.TotalEgresosSistema, despues.Cierre!.TotalEgresosSistema);
    }

    // --- CAJA-09 (escenario D): cancelación completa en apertura abierta sin deadlock (orden Crédito -> Venta -> Apertura).

    [SqlServerConcFact(DisplayName = "FIN-CAJA-2-P0 [CAJA-09] cancelacion con caja abierta: una sola reversion, cierre posterior coincide, sin deadlock (H-CAJA-2, escenario D)")]
    public async Task CAJA09_CancelacionConCajaAbierta_UnaReversion_SinDeadlock()
    {
        var s = await SembrarVentaConIngresoAsync();

        var res = await CancelarVenta(s.VentaId);
        _out.WriteLine($"cancelación: {res}");
        Assert.StartsWith("OK(True", res);
        Assert.DoesNotContain("DEADLOCK", res);

        // Segunda cancelación: ya cancelada, no duplica efectos.
        var repetida = await CancelarVenta(s.VentaId);
        Assert.Contains("InvalidOperationException", repetida);

        Assert.StartsWith("OK(", await Cerrar(s.Caja));
        var l = await LeerAsync(s.Caja.AperturaId);
        AssertCierreCoincideConLibro(l);
        Assert.Equal(1, l.Movimientos.Count(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
        Assert.Equal(EstadoVenta.Cancelada, (await LeerVentaAsync(s.VentaId, s.CreditoId)).Venta);
    }

    // ================================================================== FIN-CAJA-3-P0 (H-CAJA-3)
    // La caja puede cerrarse con ingresos pendientes de acreditar; cerrada, la acreditación directa se rechaza. Cierre y
    // acreditación compiten por el MISMO lock de la apertura: gana uno y el otro ve el estado definitivo.

    private const string MensajeCajaCerradaAcreditacion = "pertenece a una caja ya cerrada";

    /// <summary>Con el lock de la apertura tomado y el estado releído, justo ANTES de persistir la acreditación.</summary>
    private static GatePlan ConLockAntesDelUpdateDeMovimiento() => Compuerta("acreditacion/con-lock/antes-del-update", "UPDATE [MovimientosCaja]");

    private const decimal MontoPendiente = 70m;

    /// <summary>Ingreso por transferencia Pendiente de acreditar en la apertura (no es efectivo: el arqueo no lo cuenta).</summary>
    private async Task<int> SembrarPendienteAsync(Escenario e)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var m = new MovimientoCaja
        {
            AperturaCajaId = e.AperturaId, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaTransferencia,
            TipoPago = TipoPago.Transferencia, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente, Monto = MontoPendiente,
            Descripcion = "Pendiente FIN-CAJA-3-P0", Usuario = e.Usuario, FechaMovimiento = DateTime.UtcNow
        };
        ctx.MovimientosCaja.Add(m);
        await ctx.SaveChangesAsync();
        return m.Id;
    }

    private Task<string> Acreditar(Escenario e, int movimientoId, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
            (await sp.GetRequiredService<ICajaService>().AcreditarMovimientoAsync(movimientoId, e.Usuario)).EstadoAcreditacion));

    /// <summary>Todo lo que un rechazo no debe tocar: movimiento (con auditoría), cierre, totales y saldo real.</summary>
    private sealed record FotoCaja3(
        EstadoAcreditacionMovimientoCaja? Estado, DateTime? UpdatedAt, string? UpdatedBy, bool Cerrada, int Cierres,
        decimal? CierreIngresos, decimal? CierreEgresos, int Movimientos, decimal SaldoReal);

    private async Task<FotoCaja3> FotoAsync(Escenario e, int movimientoId)
    {
        var l = await LeerAsync(e.AperturaId);
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mov = await ctx.MovimientosCaja.AsNoTracking().FirstAsync(m => m.Id == movimientoId);
        var saldo = await scope.ServiceProvider.GetRequiredService<ICajaService>().CalcularSaldoRealAsync(e.AperturaId);
        return new FotoCaja3(mov.EstadoAcreditacion, mov.UpdatedAt, mov.UpdatedBy, l.Cerrada, l.Cierres,
            l.Cierre?.TotalIngresosSistema, l.Cierre?.TotalEgresosSistema, l.Movimientos.Count, saldo);
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-01] acreditacion valida con la apertura abierta: pasa a Acreditado y suma al saldo real")]
    public async Task CAJA3_01_AcreditacionConAperturaAbierta_Acredita()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);
        var antes = await FotoAsync(e, id);

        var res = await Acreditar(e, id);
        _out.WriteLine($"acreditación: {res}");

        Assert.StartsWith("OK(", res);
        var despues = await FotoAsync(e, id);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, despues.Estado);
        Assert.Equal(antes.SaldoReal + MontoPendiente, despues.SaldoReal);
        Assert.False(despues.Cerrada);
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-02] cierre con ingreso pendiente permitido; acreditacion posterior rechazada")]
    public async Task CAJA3_02_CierreConPendiente_AcreditacionPosteriorSeRechaza()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);

        Assert.StartsWith("OK(", await Cerrar(e));            // el cierre con pendientes SIGUE permitido
        var antes = await FotoAsync(e, id);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, antes.Estado);

        var res = await Acreditar(e, id);
        _out.WriteLine($"acreditación sobre caja cerrada: {res}");

        Assert.Contains("InvalidOperationException", res);
        Assert.Contains(MensajeCajaCerradaAcreditacion, res);
        Assert.Contains("conciliación auditada", res);
        Assert.DoesNotContain("DEADLOCK", res);
        Assert.Equal(antes, await FotoAsync(e, id));
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-03] acreditacion gana el lock: el cierre espera y procesa el estado definitivo")]
    public async Task CAJA3_03_AcreditacionGanaElLock_ElCierreEsperaYVeElEstadoDefinitivo()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);
        var plan = ConLockAntesDelUpdateDeMovimiento();
        var acreditar = Acreditar(e, id, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, acreditar),
            "La acreditación no llegó a la compuerta: " + (acreditar.IsCompleted ? acreditar.Result : "?"));

        // Acreditación con el lock tomado y SIN commit: el cierre debe esperar, no calcular sobre un estado provisorio.
        var cierre = Cerrar(e);
        Assert.True(await QuedaBloqueadaAsync(cierre),
            "El cierre NO debió avanzar mientras la acreditación retiene la apertura: " + (cierre.IsCompleted ? cierre.Result : "?"));

        plan.Release.TrySetResult();
        var resAcreditar = await acreditar;
        var resCierre = await cierre;
        Volcar("acreditación", plan);
        _out.WriteLine($"acreditación: {resAcreditar}\ncierre: {resCierre}");

        Assert.StartsWith("OK(", resAcreditar);
        Assert.StartsWith("OK(", resCierre);
        Assert.DoesNotContain("DEADLOCK", resAcreditar + resCierre);

        var f = await FotoAsync(e, id);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Estado);   // confirmada ANTES del cierre
        Assert.True(f.Cerrada);
        AssertCierreCoincideConLibro(await LeerAsync(e.AperturaId));
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-04] cierre gana el lock: la acreditacion espera y se rechaza sin efectos")]
    public async Task CAJA3_04_CierreGanaElLock_LaAcreditacionEsperaYSeRechaza()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);
        var plan = CierreConLockAntesDeLeerMovimientos();
        var cierre = Cerrar(e, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, cierre),
            "El cierre no llegó a la compuerta: " + (cierre.IsCompleted ? cierre.Result : "?"));

        var acreditar = Acreditar(e, id);
        Assert.True(await QuedaBloqueadaAsync(acreditar),
            "La acreditación debía esperar el lock de la apertura retenido por el cierre: " + (acreditar.IsCompleted ? acreditar.Result : "?"));

        plan.Release.TrySetResult();
        var resCierre = await cierre;
        var resAcreditar = await acreditar;
        Volcar("cierre", plan);
        _out.WriteLine($"cierre: {resCierre}\nacreditación: {resAcreditar}");

        Assert.StartsWith("OK(", resCierre);
        Assert.Contains("InvalidOperationException", resAcreditar);
        Assert.Contains(MensajeCajaCerradaAcreditacion, resAcreditar);
        Assert.DoesNotContain("DEADLOCK", resCierre + resAcreditar);

        var f = await FotoAsync(e, id);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, f.Estado);
        Assert.True(f.Cerrada);
        AssertCierreCoincideConLibro(await LeerAsync(e.AperturaId));
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-04B] cierre completado antes de que la acreditacion tome el lock: rechazada sin efectos")]
    public async Task CAJA3_04B_CierreCompletadoAntesDeLaAcreditacion_SeRechazaSinEfectos()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);
        var plan = AntesDelLock();
        var acreditar = Acreditar(e, id, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, acreditar),
            "La acreditación no llegó a la compuerta: " + (acreditar.IsCompleted ? acreditar.Result : "?"));

        // La acreditación leyó la apertura como abierta y se detuvo ANTES del lock: el cierre se completa.
        Assert.StartsWith("OK(", await Cerrar(e));
        var cerrado = await FotoAsync(e, id);

        plan.Release.TrySetResult();
        var res = await acreditar;
        Volcar("acreditación", plan);
        _out.WriteLine($"acreditación tras el cierre: {res}");

        Assert.Contains("InvalidOperationException", res);
        Assert.Contains(MensajeCajaCerradaAcreditacion, res);
        Assert.DoesNotContain("DEADLOCK", res);
        Assert.Equal(cerrado, await FotoAsync(e, id));
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, cerrado.Estado);
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-05] dos acreditaciones simultaneas del mismo pendiente: una sola realiza la transicion")]
    public async Task CAJA3_05_DosAcreditacionesSimultaneas_UnaSolaTransicion()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);
        var plan = ConLockAntesDelUpdateDeMovimiento();
        var primera = Acreditar(e, id, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, primera),
            "La primera acreditación no llegó a la compuerta: " + (primera.IsCompleted ? primera.Result : "?"));

        var segunda = Acreditar(e, id);
        Assert.True(await QuedaBloqueadaAsync(segunda), "La segunda acreditación debía esperar el lock de la apertura.");

        plan.Release.TrySetResult();
        var res1 = await primera;
        var res2 = await segunda;
        Volcar("primera acreditación", plan);
        _out.WriteLine($"primera: {res1}\nsegunda: {res2}");

        Assert.Equal(1, new[] { res1, res2 }.Count(r => r.StartsWith("OK(", StringComparison.Ordinal)));
        Assert.StartsWith("OK(", res1);
        Assert.Contains("InvalidOperationException", res2);
        Assert.Contains("Estado actual: Acreditado", res2);
        Assert.DoesNotContain("DEADLOCK", res1 + res2);

        var f = await FotoAsync(e, id);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Estado);
        Assert.False(f.Cerrada);
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-06] acreditacion rechazada: movimiento, cierre, totales y saldo real intactos")]
    public async Task CAJA3_06_AcreditacionRechazada_NoModificaNada()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);
        Assert.StartsWith("OK(", await Cerrar(e));
        var antes = await FotoAsync(e, id);

        for (var i = 0; i < 2; i++)                                  // también al reintentar
        {
            var res = await Acreditar(e, id);
            Assert.Contains(MensajeCajaCerradaAcreditacion, res);
            Assert.Equal(antes, await FotoAsync(e, id));
        }

        Assert.Equal(1, (await LeerAsync(e.AperturaId)).Cierres);
        AssertCierreCoincideConLibro(await LeerAsync(e.AperturaId));
    }

    [SqlServerConcFact(DisplayName = "FIN-CAJA-3-P0 [CAJA-3-07] acreditacion en transaccion ambiental: retiene el lock, no confirma sola y el rollback la deshace")]
    public async Task CAJA3_07_TransaccionAmbiental_RetieneElLockYElRollbackLaDeshace()
    {
        var e = await SembrarAsync();
        var id = await SembrarPendienteAsync(e);
        Task<string>? cierre = null;

        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
            await using var tx = await ctx.Database.BeginTransactionAsync();

            var mov = await caja.AcreditarMovimientoAsync(id, e.Usuario);
            Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, mov.EstadoAcreditacion);
            Assert.NotNull(ctx.Database.CurrentTransaction);        // el servicio no cerró ni reemplazó la transacción del llamador

            // Sin commit del llamador la apertura sigue bloqueada: el cierre debe esperar.
            cierre = Cerrar(e);
            Assert.True(await QuedaBloqueadaAsync(cierre), "El cierre debía esperar a la transacción ambiental que retiene la apertura.");

            await tx.RollbackAsync();
        }

        var res = await cierre!;
        _out.WriteLine($"cierre tras el rollback del llamador: {res}");
        Assert.StartsWith("OK(", res);

        var f = await FotoAsync(e, id);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, f.Estado);   // el rollback deshizo la acreditación
        AssertCierreCoincideConLibro(await LeerAsync(e.AperturaId));
    }
}
