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

    // --- CAJA-06: contramovimiento (H-CAJA-2 fuera de alcance): se serializa con el cierre sin cambiar su regla.

    [SqlServerConcFact(DisplayName = "FIN-CAJA-P0 [CAJA-06] contramovimiento de venta en vuelo: el cierre espera y lo incluye (regla de H-CAJA-2 intacta)")]
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
}
