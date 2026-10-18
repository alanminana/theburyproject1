using System.Collections.Concurrent;
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
// QA-FIN-RESIDUAL — interleavings peligrosos RESIDUALES contra SQL Server real, con servicios reales y la misma
// técnica de compuerta determinista del harness QA-FIN-CONC-P0 (ConcGateInterceptor: una operación se detiene
// EN UN COMANDO SQL concreto hasta que el test la libera; no hay Task.Delay como sincronización de correctitud,
// solo como ventana de observación de "queda bloqueada").
//
// ESTADO: BLOCKED. Compilan, no se ejecutaron: requieren QA_RELEASE_TURNO=1 (turno exclusivo del stack FIN) y
// QA_FIN_CONC_SQL con catálogo "qa_fin_conc*". Mismo gate de seguridad que el resto del harness: no pueden
// tocar otra base. Catálogo propio "<base>_res" (no choca con "<base>_svc" de la otra clase al correr en paralelo).
//
// Convención de resultado esperado HOY (sin corrección de producción), a confirmar en la ejecución:
//   [esperado: FAIL]  el invariante se viola → defecto a confirmar por ejecución (hoy es riesgo INFERIDO del código)
//   [esperado: PASS]  el invariante se conserva (por RowVersion / unique index / lock) → riesgo descartado al ejecutar
// Los tests afirman el INVARIANTE DESEADO: los "[esperado: FAIL]" fallan hoy y pasan cuando se corrija.
// ---------------------------------------------------------------------------------------------

/// <summary>Catálogo propio de esta clase para poder correr en paralelo con CancelarVsPagoServiciosRealesSqlServerTests.</summary>
public sealed class FinResidualSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_res";
}

public class FinResidualSqlServerTests : IClassFixture<FinResidualSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(7);

    private readonly FinResidualSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public FinResidualSqlServerTests(FinResidualSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
        if (ConcEnv.SkipReason() == null)
            _out.WriteLine($"[modo SQL] READ_COMMITTED_SNAPSHOT={(_fx.RcsiActivo ? "ON" : "OFF")} (verificado en sys.databases)");
    }

    // ------------------------------------------------------------------ compuertas (qué comando detener)

    private static GatePlan Compuerta(string nombre, string fragmentoSql) =>
        new(nombre, sql => sql.Contains(fragmentoSql, StringComparison.Ordinal));

    /// <summary>Cierre: justo ANTES de insertar CierreCaja, con movimientos y totales ya leídos (READ COMMITTED, sin locks retenidos).</summary>
    private static GatePlan CierreAntesDeInsertar() => Compuerta("cierre/antes-de-insertar-cierre", "INSERT INTO [CierresCaja]");

    /// <summary>Cobro: justo ANTES de insertar el PagoCuota (cuota y MovimientoCaja ya escritos, sin commit).</summary>
    private static GatePlan CobroEnVuelo() => Compuerta("cobro/en-vuelo", "INSERT INTO [PagosCuota]");

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
        {
            if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number == 1205))
                return $"DEADLOCK_VICTIMA({ex.GetType().Name})";
            if (e is SqlException dup && dup.Errors.Cast<SqlError>().Any(x => x.Number is 2601 or 2627))
                return $"UNIQUE_VIOLADO({ex.GetType().Name})";
            // -2 = timeout del comando (CommandTimeout): una operación que esperó un lock más de lo que el test
            // permite NO es un rechazo de negocio ni un PASS: es una espera que el test debió liberar.
            if (e is SqlException to && to.Errors.Cast<SqlError>().Any(x => x.Number == -2))
                return $"TIMEOUT_SQL({ex.GetType().Name})";
        }
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private Task<string> Lanzar(GatePlan? plan, Func<IServiceProvider, Task<object?>> op) =>
        Task.Run(() => EjecutarAsync(plan, op));

    private async Task<bool> LlegoALaCompuertaAsync(GatePlan plan, Task<string> operacion)
    {
        var llego = await Task.WhenAny(plan.Reached.Task, operacion, Task.Delay(EsperaCompuerta)) == plan.Reached.Task;
        if (!llego) Volcar("no llegó a la compuerta", plan);
        return llego;
    }

    /// <summary>true si la operación NO terminó dentro de la ventana de observación (= quedó bloqueada por un lock).</summary>
    private static async Task<bool> QuedaBloqueadaAsync(Task<string> operacion) =>
        await Task.WhenAny(operacion, Task.Delay(EsperaObservacion)) != operacion;

    private static readonly TimeSpan EsperaResultado = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Espera el resultado de una operación con tope. Si no termina se libera la compuerta (para no dejar
    /// conexiones detenidas) y el test falla: una operación colgada nunca es un PASS.
    /// </summary>
    private async Task<string> ResultadoAsync(string nombre, Task<string> operacion, GatePlan? plan = null)
    {
        if (await Task.WhenAny(operacion, Task.Delay(EsperaResultado)) != operacion)
        {
            plan?.Release.TrySetResult();
            Assert.Fail($"{nombre}: la operación no terminó dentro de {EsperaResultado.TotalSeconds:0}s tras liberar la compuerta (colgada).");
        }
        return await operacion;
    }

    /// <summary>Deadlock SQL o timeout del comando: fallos de infraestructura/coordinación, nunca rechazos de negocio válidos.</summary>
    private static void NoEsFalloDeInfraestructura(string nombre, string resultado)
    {
        Assert.False(resultado.StartsWith("DEADLOCK_VICTIMA", StringComparison.Ordinal), $"{nombre}: deadlock SQL real ({resultado}).");
        Assert.False(resultado.StartsWith("TIMEOUT_SQL", StringComparison.Ordinal), $"{nombre}: timeout del comando, no un rechazo de negocio ({resultado}).");
    }

    private void Volcar(string titulo, GatePlan plan)
    {
        _out.WriteLine($"--- traza SQL {titulo} ({plan.Nombre}) ---");
        foreach (var l in plan.Trace) _out.WriteLine("  " + l);
    }

    private void Informe(string escenario, params string[] lineas)
    {
        _out.WriteLine($"[{escenario}]");
        foreach (var l in lineas) _out.WriteLine("  " + l);
    }

    // ------------------------------------------------------------------ siembra

    /// <summary>Apertura PROPIA del test (no la compartida del fixture): el cierre no puede afectar a otros tests.</summary>
    private async Task<int> NuevaAperturaAsync()
    {
        using var scope = _fx.Services.CreateScope();
        return await ConcSeed.SembrarAperturaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<EscenarioSembrado> NuevoCreditoAsync(int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        return await ConcSeed.SembrarAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), aperturaId, conIngresoDeVenta: false);
    }

    private sealed record VentaContado(int VentaId, string Numero, int ClienteId, int ProductoId, int AperturaId);

    /// <summary>Venta de contado Confirmada, 2 × $50; opcionalmente con su ingreso VentaEfectivo $100 ya asentado.</summary>
    private async Task<VentaContado> NuevaVentaContadoAsync(int aperturaId, bool conIngreso = true, decimal stock = 10m)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var cliente = new Cliente
        {
            Nombre = "Fin", Apellido = "Res" + s, TipoDocumento = "DNI", NumeroDocumento = s, Email = $"{s}@t.com",
            Activo = true, Telefono = "0", Domicilio = "QA 1"
        };
        var cat = new Categoria { Codigo = s, Nombre = "C" + s, Activo = true };
        var marca = new Marca { Codigo = s, Nombre = "M" + s, Activo = true };
        ctx.AddRange(cliente, cat, marca);
        await ctx.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = s, Nombre = "P" + s, CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 10m, PrecioVenta = 50m,
            PorcentajeIVA = 21m, StockActual = stock, Activo = true
        };
        ctx.Productos.Add(prod);
        await ctx.SaveChangesAsync();
        var venta = new Venta
        {
            Numero = "VFR" + s, ClienteId = cliente.Id, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow, AperturaCajaId = aperturaId, Subtotal = 100m, Total = 100m,
            Detalles = new List<VentaDetalle>
            {
                new() { ProductoId = prod.Id, Cantidad = 2, PrecioUnitario = 50m, Subtotal = 100m, CostoUnitarioAlMomento = 17.5m, CostoTotalAlMomento = 35m }
            }
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        if (conIngreso)
        {
            ctx.MovimientosCaja.Add(new MovimientoCaja
            {
                AperturaCajaId = aperturaId, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaEfectivo,
                EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado, TipoPago = TipoPago.Efectivo, Monto = 100m,
                Descripcion = "Venta " + venta.Numero, Referencia = venta.Numero, ReferenciaId = venta.Id, VentaId = venta.Id,
                Usuario = ConcCurrentUser.Usuario, FechaMovimiento = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }
        return new VentaContado(venta.Id, venta.Numero, cliente.Id, prod.Id, aperturaId);
    }

    // ------------------------------------------------------------------ operaciones

    private Task<string> CerrarAsync(int aperturaId, decimal efectivo, GatePlan? plan = null) =>
        Lanzar(plan, async sp =>
        {
            var c = await sp.GetRequiredService<ICajaService>().CerrarCajaAsync(
                new CerrarCajaViewModel
                {
                    AperturaCajaId = aperturaId, EfectivoContado = efectivo,
                    JustificacionDiferencia = "QA conc (diferencia esperada por la carrera)"
                }, ConcCurrentUser.Usuario);
            return c.Id;
        });

    private Task<string> CobrarCuotaAsync(EscenarioSembrado s, int indice, GatePlan? plan = null) =>
        Lanzar(plan, async sp =>
        {
            var ctx = sp.GetRequiredService<AppDbContext>();
            var cuota = await ctx.Cuotas.AsNoTracking().FirstAsync(c => c.Id == s.CuotaIds[indice]);
            return await sp.GetRequiredService<ICreditoService>().PagarCuotaAsync(new PagarCuotaViewModel
            {
                CreditoId = s.CreditoId, CuotaId = cuota.Id, MontoPagado = cuota.MontoTotal, MedioPago = "Efectivo"
            });
        });

    private Task<string> CancelarVentaAsync(int ventaId, GatePlan? plan = null) =>
        Lanzar(plan, async sp => await sp.GetRequiredService<IVentaService>().CancelarVentaAsync(ventaId, "QA residual"));

    private Task<string> CrearDevolucionAsync(VentaContado v, int cantidad, GatePlan? plan = null) =>
        Lanzar(plan, async sp =>
        {
            var d = await sp.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
                new Devolucion { VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA" },
                new List<DevolucionDetalle> { new() { ProductoId = v.ProductoId, Cantidad = cantidad } });
            return d.NumeroDevolucion;
        });

    private async Task<(int id, byte[] rv)> DevolucionAprobadaConReembolsoAsync(VentaContado v, int cantidad)
    {
        using var scope = _fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDevolucionService>();
        var d = await svc.CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA",
                TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero, RegistrarEgresoCaja = true
            },
            new List<DevolucionDetalle> { new() { ProductoId = v.ProductoId, Cantidad = cantidad, AccionRecomendada = AccionProducto.ReintegrarStock } });
        var a = await svc.AprobarDevolucionAsync(d.Id, "qa", d.RowVersion);
        return (a.Id, a.RowVersion);
    }

    private Task<string> CompletarDevolucionAsync(int id, byte[] rv, GatePlan? plan = null) =>
        Lanzar(plan, async sp => (await sp.GetRequiredService<IDevolucionService>().CompletarDevolucionAsync(id, rv)).Estado);

    // ------------------------------------------------------------------ invariantes

    /// <summary>
    /// I-CAJA: una apertura cerrada tiene un libro EN EFECTIVO idéntico a su CierreCaja (nada entra o sale después del cierre).
    /// </summary>
    private async Task<List<string>> ViolacionesCierreAsync(int aperturaId)
    {
        var v = new List<string>();
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ap = await ctx.AperturasCaja.AsNoTracking().Include(a => a.Cierre).FirstAsync(a => a.Id == aperturaId);
        var movs = await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => m.AperturaCajaId == aperturaId && !m.IsDeleted && m.TipoPago == TipoPago.Efectivo).ToListAsync();
        var ing = movs.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto);
        var egr = movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto);

        if (ap.Cerrada && ap.Cierre == null) v.Add("Apertura Cerrada sin CierreCaja.");
        if (!ap.Cerrada && ap.Cierre != null) v.Add("CierreCaja existe pero la apertura sigue abierta.");
        if (ap.Cierre != null)
        {
            if (ap.Cierre.TotalIngresosSistema != ing)
                v.Add($"Ingresos en efectivo del libro ({ing}) ≠ CierreCaja.TotalIngresosSistema ({ap.Cierre.TotalIngresosSistema}): movimiento posterior al cierre.");
            if (ap.Cierre.TotalEgresosSistema != egr)
                v.Add($"Egresos en efectivo del libro ({egr}) ≠ CierreCaja.TotalEgresosSistema ({ap.Cierre.TotalEgresosSistema}).");
        }
        return v;
    }

    /// <summary>I-VENTA-CAJA: para una venta Cancelada, Σ egresos(venta) = Σ ingresos(venta) (ni más ni menos).</summary>
    private async Task<(decimal ingresos, decimal egresos)> CajaDeVentaAsync(int ventaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var num = await ctx.Ventas.AsNoTracking().Where(x => x.Id == ventaId).Select(x => x.Numero).FirstAsync();
        var movs = await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => !m.IsDeleted && (m.VentaId == ventaId
                        || (m.Concepto == ConceptoMovimientoCaja.DevolucionCliente
                            && ctx.Devoluciones.Any(d => d.Id == m.ReferenciaId && d.VentaId == ventaId)))).ToListAsync();
        _ = num;
        return (movs.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto),
                movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto));
    }

    private async Task<EstadoVenta> EstadoVentaAsync(int ventaId)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Ventas.AsNoTracking()
            .Where(x => x.Id == ventaId).Select(x => x.Estado).FirstAsync();
    }

    private async Task<int> DevolucionesAsync(int ventaId)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Devoluciones.AsNoTracking()
            .CountAsync(d => d.VentaId == ventaId && !d.IsDeleted);
    }

    private async Task<decimal> StockAsync(int productoId)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Productos.AsNoTracking()
            .Where(p => p.Id == productoId).Select(p => p.StockActual).FirstAsync();
    }

    // =====================================================================================
    // R-01 / R-02  Cobro vs. cierre de caja
    // =====================================================================================

    /// <summary>
    /// R-01 [esperado: FAIL]. El cierre ya leyó movimientos y calculó el arqueo (sin locks); un cobro entra y
    /// confirma en esa apertura; el cierre se libera y commitea. Violación: cobro en una caja cerrada fuera del arqueo.
    /// Invariante deseado: el cobro se rechaza (caja cerrada) o el cierre incorpora/rechaza (lock de la apertura).
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-01]: CobroConfirmaEntreLecturaDelCierreYSuCommit_NoDejaMovimientoPosteriorAlCierre")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "FAIL")]
    public async Task R01_CobroConfirmaEntreLecturaDelCierreYSuCommit_NoDejaMovimientoPosteriorAlCierre()
    {
        var aperturaId = await NuevaAperturaAsync();
        var s = await NuevoCreditoAsync(aperturaId);
        var plan = CierreAntesDeInsertar();

        var cierre = CerrarAsync(aperturaId, efectivo: 0m, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, cierre), "El cierre no llegó a la compuerta.");

        var cobro = await CobrarCuotaAsync(s, 0);           // la apertura sigue abierta para el cobro: confirma
        plan.Release.TrySetResult();
        var resCierre = await cierre;

        Informe("R-01", $"cobro={cobro}", $"cierre={resCierre}");
        var viol = await ViolacionesCierreAsync(aperturaId);
        Informe("R-01 invariante", viol.Count == 0 ? "OK" : string.Join(" | ", viol));
        Assert.True(viol.Count == 0, string.Join(" | ", viol));
    }

    /// <summary>
    /// R-02 [esperado: PASS sin RCSI / FAIL con RCSI]. El cobro está en vuelo (movimiento de caja insertado, sin commit)
    /// y arranca el cierre. En READ COMMITTED el SELECT de movimientos espera el X lock del insert y luego lo ve;
    /// con READ_COMMITTED_SNAPSHOT lee la versión anterior y cierra sin el cobro.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-02]: CierreMientrasUnCobroEstaEnVuelo_NoDejaMovimientoPosteriorAlCierre")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "DEPENDE-RCSI")]
    public async Task R02_CierreMientrasUnCobroEstaEnVuelo_NoDejaMovimientoPosteriorAlCierre()
    {
        var aperturaId = await NuevaAperturaAsync();
        var s = await NuevoCreditoAsync(aperturaId);
        var plan = CobroEnVuelo();

        var cobro = CobrarCuotaAsync(s, 0, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, cobro), "El cobro no llegó a la compuerta.");

        var cierre = CerrarAsync(aperturaId, efectivo: 300m);
        var bloqueado = await QuedaBloqueadaAsync(cierre);
        Informe("R-02", $"cierre bloqueado por el cobro en vuelo = {bloqueado}");
        plan.Release.TrySetResult();
        var resCobro = await cobro;
        var resCierre = await cierre;

        Informe("R-02", $"cobro={resCobro}", $"cierre={resCierre}");
        var viol = await ViolacionesCierreAsync(aperturaId);
        Informe("R-02 invariante", viol.Count == 0 ? "OK" : string.Join(" | ", viol));
        Assert.True(viol.Count == 0, string.Join(" | ", viol));
    }

    /// <summary>
    /// R-03 [esperado: FAIL]. Cierre detenido tras leer; la CANCELACIÓN de una venta de esa apertura confirma
    /// (contramovimiento en la apertura original); el cierre commitea sin verlo. Mismo invariante I-CAJA.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-03]: CancelacionConfirmaEntreLecturaDelCierreYSuCommit_NoDejaMovimientoPosteriorAlCierre")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "FAIL")]
    public async Task R03_CancelacionConfirmaEntreLecturaDelCierreYSuCommit_NoDejaMovimientoPosteriorAlCierre()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId);
        var plan = CierreAntesDeInsertar();

        var cierre = CerrarAsync(aperturaId, efectivo: 100m, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, cierre));

        var cancel = await CancelarVentaAsync(v.VentaId);
        plan.Release.TrySetResult();
        var resCierre = await cierre;

        Informe("R-03", $"cancelación={cancel}", $"cierre={resCierre}");
        var viol = await ViolacionesCierreAsync(aperturaId);
        Informe("R-03 invariante", viol.Count == 0 ? "OK" : string.Join(" | ", viol));
        Assert.True(viol.Count == 0, string.Join(" | ", viol));
    }

    /// <summary>
    /// R-04 [esperado: PASS (inseguro pero seguro)]. Dos cierres simultáneos de la misma apertura: el índice único
    /// de CierreCaja.AperturaCajaId y la RowVersion de AperturaCaja deben dejar exactamente un cierre. Se registra
    /// cómo falla el segundo (hoy llega al usuario como mensaje crudo de EF: TempData["Error"] = ex.Message).
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-04]: DosCierresSimultaneos_DejanExactamenteUnCierre")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "PASS")]
    public async Task R04_DosCierresSimultaneos_DejanExactamenteUnCierre()
    {
        var aperturaId = await NuevaAperturaAsync();
        var plan = CierreAntesDeInsertar();
        var primero = CerrarAsync(aperturaId, efectivo: 0m, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, primero));

        var segundo = await CerrarAsync(aperturaId, efectivo: 0m);   // este completa
        plan.Release.TrySetResult();
        var resPrimero = await primero;

        Informe("R-04", $"segundo(completo)={segundo}", $"primero(liberado)={resPrimero}");
        using var scope = _fx.Services.CreateScope();
        var n = await scope.ServiceProvider.GetRequiredService<AppDbContext>().CierresCaja.CountAsync(c => c.AperturaCajaId == aperturaId);
        Assert.Equal(1, n);
        Assert.Equal(1, new[] { segundo, resPrimero }.Count(x => x.StartsWith("OK(")));
    }

    // =====================================================================================
    // R-05  Abrir caja duplicado
    // =====================================================================================

    /// <summary>
    /// R-05 [esperado: PASS por RowVersion de Caja]. Dos aperturas simultáneas de la MISMA caja: no existe índice
    /// único filtrado (CajaId, Cerrada=0); la única defensa es que ambas actualizan Caja.Estado con RowVersion.
    /// Si el esquema deja de actualizar la fila de Caja, este test se pone en rojo.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-05]: DosAperturasSimultaneasDeLaMismaCaja_DejanUnaSolaAbierta")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "PASS")]
    public async Task R05_DosAperturasSimultaneasDeLaMismaCaja_DejanUnaSolaAbierta()
    {
        int cajaId;
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var caja = new Caja { Codigo = "QR" + Guid.NewGuid().ToString("N")[..6], Nombre = "QA res", Estado = EstadoCaja.Cerrada, RowVersion = new byte[8] };
            ctx.Cajas.Add(caja);
            await ctx.SaveChangesAsync();
            cajaId = caja.Id;
        }

        Task<string> Abrir(GatePlan? p) => Lanzar(p, async sp =>
            (await sp.GetRequiredService<ICajaService>().AbrirCajaAsync(new AbrirCajaViewModel { CajaId = cajaId, MontoInicial = 0m }, ConcCurrentUser.Usuario)).Id);

        var plan = Compuerta("apertura/antes-de-insertar", "INSERT INTO [AperturasCaja]");
        var primera = Abrir(plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, primera));
        var segunda = await Abrir(null);
        plan.Release.TrySetResult();
        var resPrimera = await primera;

        Informe("R-05", $"segunda(completa)={segunda}", $"primera(liberada)={resPrimera}");
        using var check = _fx.Services.CreateScope();
        var abiertas = await check.ServiceProvider.GetRequiredService<AppDbContext>().AperturasCaja
            .CountAsync(a => a.CajaId == cajaId && !a.Cerrada && !a.IsDeleted);
        Assert.Equal(1, abiertas);
    }

    // =====================================================================================
    // R-06 / R-07 / R-08  Devolución vs. cancelación (y vs. otra devolución)
    // =====================================================================================

    /// <summary>
    /// R-06 [esperado: FAIL]. Dos altas simultáneas de devolución sobre la misma venta (cada una por la cantidad
    /// total): ambas leen "ya devuelto = 0". No hay lock ni fila compartida escrita. Invariante: Σ devuelta ≤ vendida.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-06]: DosDevolucionesSimultaneasPorLaCantidadTotal_NoExcedenLoVendido")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "FAIL")]
    public async Task R06_DosDevolucionesSimultaneasPorLaCantidadTotal_NoExcedenLoVendido()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId);
        var plan = Compuerta("devolucion/antes-de-insertar", "INSERT INTO [Devoluciones]");

        var primera = CrearDevolucionAsync(v, cantidad: 2, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, primera));
        var segunda = await CrearDevolucionAsync(v, cantidad: 2);
        plan.Release.TrySetResult();
        var resPrimera = await primera;

        Informe("R-06", $"segunda(completa)={segunda}", $"primera(liberada)={resPrimera}");
        using var scope = _fx.Services.CreateScope();
        var devuelto = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DevolucionDetalles
            .Where(d => !d.IsDeleted && d.Devolucion.VentaId == v.VentaId && d.Devolucion.Estado != EstadoDevolucion.Rechazada)
            .SumAsync(d => d.Cantidad);
        Assert.True(devuelto <= 2, $"Devuelto acumulado {devuelto} > vendido 2.");
    }

    /// <summary>
    /// R-07 (FIN-DEV-P0). Completar devolución (reembolso en caja) en vuelo —estado Completada, stock ya
    /// reintegrado, egreso de caja sin insertar— y cancelación de la MISMA venta lanzada en ese momento.
    /// La cancelación debe quedar bloqueada por el lock de la venta que ya tomó Completar; al liberarla ve la
    /// devolución Completada y se rechaza. Lo que sale de caja no supera lo ingresado y el stock no supera
    /// lo existente más lo vendido (variante determinista de H-DEV-2/H-DEV-3).
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-07]: CompletarDevolucionYCancelarLaMismaVenta_NoSacaMasDineroNiMasStockQueLoVendido")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "FAIL")]
    public async Task R07_CompletarDevolucionYCancelarLaMismaVenta_NoSacaMasDineroNiMasStockQueLoVendido()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, stock: 10m);
        var (devId, rv) = await DevolucionAprobadaConReembolsoAsync(v, cantidad: 2);

        var plan = Compuerta("completar/antes-del-reembolso", "INSERT INTO [MovimientosCaja]");
        var completar = CompletarDevolucionAsync(devId, rv, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, completar));

        var cancel = CancelarVentaAsync(v.VentaId);
        var bloqueada = await QuedaBloqueadaAsync(cancel);
        Informe("R-07", $"cancelación bloqueada por el completar en vuelo = {bloqueada}");
        plan.Release.TrySetResult();
        var resCompletar = await completar;
        var resCancel = await cancel;

        var (ing, egr) = await CajaDeVentaAsync(v.VentaId);
        var stock = await StockAsync(v.ProductoId);
        Informe("R-07", $"completar={resCompletar}", $"cancelación={resCancel}", $"caja: ingresos={ing} egresos={egr}", $"stock={stock} (inicial 10 + vendido 2 = tope 12)");
        Assert.True(bloqueada, "La cancelación no esperó el lock de la venta tomado por Completar.");
        Assert.StartsWith("OK(", resCompletar);
        Assert.Contains("devoluciones", resCancel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoVenta.Confirmada, await EstadoVentaAsync(v.VentaId));
        Assert.Equal(100m, egr);
        Assert.True(egr <= ing, $"Egresos {egr} > ingresos {ing} para la venta.");
        Assert.Equal(12m, stock);
    }

    /// <summary>
    /// R-11 (FIN-DEV-P0, H-DEV-1). Cancelación en vuelo (venta bloqueada, stock devuelto, contramovimiento sin
    /// insertar) y alta de devolución sobre la misma venta: el alta espera el lock; al commitear la cancelación
    /// se rechaza por venta cancelada y no queda ninguna devolución. Dinero y stock no se duplican.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO (CI) [R-11]: CancelarEnVueloYCrearDevolucion_NoDejaDevolucionSobreVentaCancelada")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task R11_CancelarEnVueloYCrearDevolucion_NoDejaDevolucionSobreVentaCancelada()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, stock: 10m);

        var plan = Compuerta("cancelacion/antes-del-contramovimiento", "INSERT INTO [MovimientosCaja]");
        var cancel = CancelarVentaAsync(v.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, cancel));

        var crear = CrearDevolucionAsync(v, cantidad: 1);
        var bloqueada = await QuedaBloqueadaAsync(crear);
        plan.Release.TrySetResult();
        var resCancel = await cancel;
        var resCrear = await crear;

        var (ing, egr) = await CajaDeVentaAsync(v.VentaId);
        var stock = await StockAsync(v.ProductoId);
        Informe("R-11", $"alta bloqueada por la cancelación en vuelo = {bloqueada}", $"cancelación={resCancel}", $"crear={resCrear}",
            $"caja: ingresos={ing} egresos={egr}", $"stock={stock}");
        Assert.True(bloqueada, "El alta de devolución no esperó el lock de la venta tomado por la cancelación.");
        Assert.StartsWith("OK(", resCancel);
        Assert.Contains("venta cancelada", resCrear, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoVenta.Cancelada, await EstadoVentaAsync(v.VentaId));
        Assert.Equal(0, await DevolucionesAsync(v.VentaId));
        Assert.Equal(100m, egr);
        Assert.Equal(12m, stock);
    }

    /// <summary>
    /// R-12 (FIN-DEV-P0, H-DEV-2). Alta de devolución en vuelo (venta bloqueada, cabecera sin insertar) y
    /// cancelación de la misma venta: la cancelación espera; al commitear el alta ve la devolución Pendiente y
    /// se rechaza. La venta sigue Confirmada, sin contramovimiento ni stock devuelto.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO (CI) [R-12]: CrearDevolucionEnVueloYCancelar_RechazaLaCancelacion")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task R12_CrearDevolucionEnVueloYCancelar_RechazaLaCancelacion()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, stock: 10m);

        var plan = Compuerta("devolucion/antes-de-insertar", "INSERT INTO [Devoluciones]");
        var crear = CrearDevolucionAsync(v, cantidad: 1, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, crear));

        var cancel = CancelarVentaAsync(v.VentaId);
        var bloqueada = await QuedaBloqueadaAsync(cancel);
        plan.Release.TrySetResult();
        var resCrear = await crear;
        var resCancel = await cancel;

        var (ing, egr) = await CajaDeVentaAsync(v.VentaId);
        var stock = await StockAsync(v.ProductoId);
        Informe("R-12", $"cancelación bloqueada por el alta en vuelo = {bloqueada}", $"crear={resCrear}", $"cancelación={resCancel}",
            $"caja: ingresos={ing} egresos={egr}", $"stock={stock}");
        Assert.True(bloqueada, "La cancelación no esperó el lock de la venta tomado por el alta de devolución.");
        Assert.StartsWith("OK(", resCrear);
        Assert.Contains("devoluciones", resCancel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoVenta.Confirmada, await EstadoVentaAsync(v.VentaId));
        Assert.Equal(1, await DevolucionesAsync(v.VentaId));
        Assert.Equal(0m, egr);
        Assert.Equal(10m, stock);
    }

    // ------------------------------------------------------------------ FIN-DEV-P0: aprobación de devoluciones

    private async Task<(int id, byte[] rv)> DevolucionPendienteAsync(VentaContado v, TipoResolucionDevolucion tipo)
    {
        using var scope = _fx.Services.CreateScope();
        var d = await scope.ServiceProvider.GetRequiredService<IDevolucionService>().CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = v.VentaId, ClienteId = v.ClienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA",
                TipoResolucion = tipo, RegistrarEgresoCaja = tipo == TipoResolucionDevolucion.ReembolsoDinero
            },
            new List<DevolucionDetalle> { new() { ProductoId = v.ProductoId, Cantidad = 1, AccionRecomendada = AccionProducto.ReintegrarStock } });
        return (d.Id, d.RowVersion);
    }

    private Task<string> AprobarDevolucionAsync(int id, byte[] rv, GatePlan? plan = null) =>
        Lanzar(plan, async sp => (await sp.GetRequiredService<IDevolucionService>().AprobarDevolucionAsync(id, "qa", rv)).Estado);

    /// <summary>Simula el registro histórico inconsistente (anterior a FIN-DEV-P0) escribiendo directo sobre la fila de la venta.</summary>
    private async Task ForzarVentaAsync(int ventaId, EstadoVenta? estado = null, bool eliminada = false)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var q = ctx.Ventas.IgnoreQueryFilters().Where(x => x.Id == ventaId);
        var n = estado.HasValue
            ? await q.ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, estado.Value))
            : await q.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, eliminada));
        Assert.Equal(1, n);
    }

    private async Task<(EstadoDevolucion estado, int notasCredito, decimal ingresos, decimal egresos)> FotoDevolucionAsync(int devId, int ventaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estado = await ctx.Devoluciones.IgnoreQueryFilters().AsNoTracking().Where(d => d.Id == devId).Select(d => d.Estado).FirstAsync();
        var nc = await ctx.NotasCredito.IgnoreQueryFilters().AsNoTracking().CountAsync(n => n.DevolucionId == devId);
        var movs = await ctx.MovimientosCaja.AsNoTracking().Where(m => !m.IsDeleted && m.VentaId == ventaId).ToListAsync();
        return (estado, nc, movs.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto),
                movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto));
    }

    /// <summary>
    /// R-13 (FIN-DEV-P0). Aprobación de una devolución con nota de crédito en vuelo (venta bloqueada, nota sin insertar)
    /// y cancelación de la MISMA venta: la cancelación debe esperar el lock que tomó la aprobación y, al liberarla, ver la
    /// devolución Aprobada y rechazarse. Queda una sola nota vigente, la venta Confirmada, sin contramovimiento ni stock devuelto.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO (CI) [R-13]: AprobarDevolucionEnVueloYCancelar_RechazaLaCancelacion")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task R13_AprobarDevolucionEnVueloYCancelar_RechazaLaCancelacion()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, stock: 10m);
        var (devId, rv) = await DevolucionPendienteAsync(v, TipoResolucionDevolucion.NotaCredito);

        var plan = Compuerta("aprobar/antes-de-insertar-nota-de-credito", "INSERT INTO [NotasCredito]");
        var aprobar = AprobarDevolucionAsync(devId, rv, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, aprobar));

        var cancel = CancelarVentaAsync(v.VentaId);
        var bloqueada = await QuedaBloqueadaAsync(cancel);
        plan.Release.TrySetResult();
        var resAprobar = await aprobar;
        var resCancel = await cancel;

        var foto = await FotoDevolucionAsync(devId, v.VentaId);
        var stock = await StockAsync(v.ProductoId);
        Informe("R-13", $"cancelación bloqueada por la aprobación en vuelo = {bloqueada}", $"aprobar={resAprobar}", $"cancelación={resCancel}",
            $"devolución={foto.estado} notas={foto.notasCredito} caja: ingresos={foto.ingresos} egresos={foto.egresos}", $"stock={stock}");
        Assert.True(bloqueada, "La cancelación no esperó el lock de la venta tomado por la aprobación de la devolución.");
        Assert.StartsWith("OK(", resAprobar);
        Assert.Contains("devoluciones", resCancel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoVenta.Confirmada, await EstadoVentaAsync(v.VentaId));
        Assert.Equal(EstadoDevolucion.Aprobada, foto.estado);
        Assert.Equal(1, foto.notasCredito);
        Assert.Equal(0m, foto.egresos);
        Assert.Equal(10m, stock);
    }

    /// <summary>
    /// R-14 (FIN-DEV-P0). Registros históricos inconsistentes sobre SQL Server real (camino UPDLOCK/HOLDLOCK sobre
    /// [Ventas]): aprobar sobre venta Cancelada, aprobar sobre venta eliminada y completar sobre venta eliminada se rechazan
    /// sin cambiar estados ni emitir notas de crédito ni mover caja/stock. Los reintentos rechazan igual.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO (CI) [R-14]: AprobarYCompletarSobreVentaCanceladaOEliminada_SeRechazanSinEfectos")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task R14_AprobarYCompletarSobreVentaCanceladaOEliminada_SeRechazanSinEfectos()
    {
        var aperturaId = await NuevaAperturaAsync();

        // a) Aprobar sobre venta Cancelada (histórico).
        var a = await NuevaVentaContadoAsync(aperturaId, stock: 10m);
        var (devA, rvA) = await DevolucionPendienteAsync(a, TipoResolucionDevolucion.NotaCredito);
        await ForzarVentaAsync(a.VentaId, estado: EstadoVenta.Cancelada);
        var antesA = await FotoDevolucionAsync(devA, a.VentaId);
        var resA = await AprobarDevolucionAsync(devA, rvA);
        var resA2 = await AprobarDevolucionAsync(devA, rvA);

        // b) Aprobar sobre venta eliminada (histórico).
        var b = await NuevaVentaContadoAsync(aperturaId, stock: 10m);
        var (devB, rvB) = await DevolucionPendienteAsync(b, TipoResolucionDevolucion.NotaCredito);
        await ForzarVentaAsync(b.VentaId, eliminada: true);
        var antesB = await FotoDevolucionAsync(devB, b.VentaId);
        var resB = await AprobarDevolucionAsync(devB, rvB);

        // c) Completar una devolución aprobada sobre venta eliminada (histórico).
        var c = await NuevaVentaContadoAsync(aperturaId, stock: 10m);
        var (devC, rvC) = await DevolucionAprobadaConReembolsoAsync(c, cantidad: 1);
        await ForzarVentaAsync(c.VentaId, eliminada: true);
        var antesC = await FotoDevolucionAsync(devC, c.VentaId);
        var resC = await CompletarDevolucionAsync(devC, rvC);
        var resC2 = await CompletarDevolucionAsync(devC, rvC);

        Informe("R-14", $"a={resA}", $"a(reintento)={resA2}", $"b={resB}", $"c={resC}", $"c(reintento)={resC2}");
        Assert.Contains("cancelada", resA, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cancelada", resA2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eliminada", resB, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eliminada", resC, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eliminada", resC2, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(antesA, await FotoDevolucionAsync(devA, a.VentaId));
        Assert.Equal(EstadoDevolucion.Pendiente, antesA.estado);
        Assert.Equal(0, antesA.notasCredito);
        Assert.Equal(antesB, await FotoDevolucionAsync(devB, b.VentaId));
        Assert.Equal(EstadoDevolucion.Pendiente, antesB.estado);
        Assert.Equal(0, antesB.notasCredito);
        Assert.Equal(antesC, await FotoDevolucionAsync(devC, c.VentaId));
        Assert.Equal(EstadoDevolucion.Aprobada, antesC.estado);
        Assert.Equal(0m, antesC.egresos);
        Assert.Equal(10m, await StockAsync(a.ProductoId));
        Assert.Equal(10m, await StockAsync(b.ProductoId));
        Assert.Equal(10m, await StockAsync(c.ProductoId));
    }

    /// <summary>
    /// R-08 [esperado: PASS]. Dos completar simultáneos de la MISMA devolución (doble clic): estado + RowVersion
    /// deben dejar un único reembolso y un único reintegro de stock.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-08]: DosCompletarSimultaneosDeLaMismaDevolucion_ReembolsanUnaSolaVez")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "PASS")]
    public async Task R08_DosCompletarSimultaneosDeLaMismaDevolucion_ReembolsanUnaSolaVez()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, stock: 10m);
        var (devId, rv) = await DevolucionAprobadaConReembolsoAsync(v, cantidad: 1);

        var plan = Compuerta("completar/antes-del-reembolso", "INSERT INTO [MovimientosCaja]");
        var primero = CompletarDevolucionAsync(devId, rv, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, primero));
        var segundo = CompletarDevolucionAsync(devId, rv);
        var bloqueado = await QuedaBloqueadaAsync(segundo);
        plan.Release.TrySetResult();
        var resPrimero = await primero;
        var resSegundo = await segundo;

        Informe("R-08", $"segundo bloqueado={bloqueado}", $"primero={resPrimero}", $"segundo={resSegundo}");
        var (_, egr) = await CajaDeVentaAsync(v.VentaId);
        Assert.Equal(50m, egr);
        Assert.Equal(11m, await StockAsync(v.ProductoId));
    }

    // =====================================================================================
    // R-09 / R-10  Facturar vs. cancelar
    // =====================================================================================

    private Task<string> FacturarAsync(int ventaId, GatePlan? plan = null) =>
        Lanzar(plan, async sp => await sp.GetRequiredService<IVentaService>().FacturarVentaAsync(
            ventaId, new FacturaViewModel { VentaId = ventaId, Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow }));


    private sealed record FotoFacturacion(
        EstadoVenta Estado, int FacturasTotales, int FacturasVigentes, decimal Ingresos, decimal Egresos, decimal Stock);

    private async Task<FotoFacturacion> FotoFacturacionAsync(VentaContado v)
    {
        EstadoVenta estado;
        int totales, vigentes;
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            estado = await ctx.Ventas.AsNoTracking().Where(x => x.Id == v.VentaId).Select(x => x.Estado).FirstAsync();
            totales = await ctx.Facturas.IgnoreQueryFilters().CountAsync(f => f.VentaId == v.VentaId);
            vigentes = await ctx.Facturas.CountAsync(f => f.VentaId == v.VentaId && !f.IsDeleted && !f.Anulada);
        }
        var (ing, egr) = await CajaDeVentaAsync(v.VentaId);
        return new FotoFacturacion(estado, totales, vigentes, ing, egr, await StockAsync(v.ProductoId));
    }

    /// <summary>
    /// R-09 (FIN-FACT-P0). GANA FACTURAR: la facturación se detiene justo antes de insertar la factura, ya con el lock de
    /// la venta (Crédito → Venta → Apertura). La cancelación arranca y debe QUEDAR BLOQUEADA en el lock de la venta; al
    /// liberar, factura+estado+ingreso confirman juntos y la cancelación, ya sobre la venta Facturada, anula esa factura,
    /// devuelve el stock y contra-asienta el ingreso. Sin deadlock ni timeout.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [R-09]: CancelarMientrasSeFactura_EsperaYAnulaLaFacturaSinDejarIngresoHuerfano")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task R09_CancelarMientrasSeFactura_EsperaYAnulaLaFacturaSinDejarIngresoHuerfano()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId);
        var antes = await FotoFacturacionAsync(v);
        var plan = Compuerta("facturar/antes-de-insertar-factura", "INSERT INTO [Facturas]");

        Task<string>? facturar = null, cancel = null;
        bool bloqueada;
        try
        {
            facturar = FacturarAsync(v.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, facturar), "Facturar no llegó a la compuerta.");
            cancel = CancelarVentaAsync(v.VentaId);
            bloqueada = await QuedaBloqueadaAsync(cancel);
        }
        finally
        {
            plan.Release.TrySetResult();
        }
        var resFacturar = await ResultadoAsync("facturar", facturar!, plan);
        var resCancel = await ResultadoAsync("cancelación", cancel!, plan);
        var despues = await FotoFacturacionAsync(v);

        Informe("R-09", $"cancelación bloqueada por el lock de la venta = {bloqueada}", $"facturar={resFacturar}", $"cancelación={resCancel}", $"{antes} -> {despues}");
        NoEsFalloDeInfraestructura("facturar", resFacturar);
        NoEsFalloDeInfraestructura("cancelación", resCancel);
        Assert.True(bloqueada, "La cancelación no esperó el lock de la venta tomado por la facturación en curso.");
        Assert.StartsWith("OK(", resFacturar);
        Assert.StartsWith("OK(", resCancel);
        Assert.Equal(EstadoVenta.Cancelada, despues.Estado);
        Assert.Equal(1, despues.FacturasTotales);          // la factura se emitió (ganó la facturación) …
        Assert.Equal(0, despues.FacturasVigentes);         // … y la cancelación la anuló: ninguna vigente sobre venta cancelada
        Assert.Equal(100m, despues.Ingresos);
        Assert.Equal(100m, despues.Egresos);
        Assert.Equal(antes.Stock + 2m, despues.Stock);     // la cancelación devolvió las 2 unidades
    }

    /// <summary>
    /// R-09B (FIN-FACT-P0). GANA CANCELAR: la cancelación toma el lock de la venta y se detiene antes de contra-asentar el
    /// ingreso. La facturación arranca y debe QUEDAR BLOQUEADA; al liberar, la cancelación confirma y la facturación, al
    /// despertar, ve la venta Cancelada y se rechaza SIN crear factura ni ingreso.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [R-09B]: FacturarMientrasSeCancela_EsperaYSeRechazaSinFacturaNiIngreso")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task R09B_FacturarMientrasSeCancela_EsperaYSeRechazaSinFacturaNiIngreso()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId);
        var plan = Compuerta("cancelar/antes-del-contramovimiento", "INSERT INTO [MovimientosCaja]");

        Task<string>? cancel = null, facturar = null;
        bool bloqueada;
        try
        {
            cancel = Lanzar(plan, async sp => await sp.GetRequiredService<IVentaService>().CancelarVentaAsync(v.VentaId, "QA residual"));
            Assert.True(await LlegoALaCompuertaAsync(plan, cancel), "La cancelación no llegó a la compuerta.");
            facturar = FacturarAsync(v.VentaId);
            bloqueada = await QuedaBloqueadaAsync(facturar);
        }
        finally
        {
            plan.Release.TrySetResult();
        }
        var resCancel = await ResultadoAsync("cancelación", cancel!, plan);
        var resFacturar = await ResultadoAsync("facturar", facturar!, plan);
        var despues = await FotoFacturacionAsync(v);

        Informe("R-09B", $"facturar bloqueada por el lock de la venta = {bloqueada}", $"cancelación={resCancel}", $"facturar={resFacturar}", $"{despues}");
        NoEsFalloDeInfraestructura("cancelación", resCancel);
        NoEsFalloDeInfraestructura("facturar", resFacturar);
        Assert.True(bloqueada, "La facturación no esperó el lock de la venta tomado por la cancelación en curso.");
        Assert.StartsWith("OK(", resCancel);
        Assert.Contains("Solo se pueden facturar ventas confirmadas", resFacturar);
        Assert.Contains("Cancelada", resFacturar);
        Assert.Equal(EstadoVenta.Cancelada, despues.Estado);
        Assert.Equal(0, despues.FacturasTotales);          // la facturación rechazada no dejó ni una factura anulada
        Assert.Equal(0, despues.FacturasVigentes);
        Assert.Equal(100m, despues.Ingresos);              // el único ingreso es el original, contra-asentado una vez
        Assert.Equal(100m, despues.Egresos);
    }

    /// <summary>
    /// R-10 (FIN-FACT-P0, H-FACT-1 — ventas LEGADAS Confirmadas sin ingreso asentado). La facturación se detiene justo
    /// antes del insert del ingreso de respaldo, con la factura y el estado todavía SIN confirmar. Antes: ya estaban
    /// commiteados y una cancelación intermedia no encontraba ingreso que contra-asentar, dejando el ingreso huérfano
    /// (ingresos=100, egresos=0). Ahora: la cancelación espera el lock de la venta; al liberar, factura+estado+ingreso
    /// confirman juntos y la cancelación los ve y contra-asienta: venta Cancelada ⇒ Σ egresos = Σ ingresos.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [R-10]: CancelarEntreElCommitDeFacturarYSuIngresoDeRespaldo_NoDejaIngresoHuerfano")]
    [Trait("Categoria", "SqlServerRegresion")]
    [Trait("Hallazgo", "H-FACT-1")]
    public async Task R10_CancelarEntreElCommitDeFacturarYSuIngresoDeRespaldo_NoDejaIngresoHuerfano()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false);
        var antes = await FotoFacturacionAsync(v);
        var plan = Compuerta("facturar/antes-del-ingreso-de-respaldo", "INSERT INTO [MovimientosCaja]");

        Task<string>? facturar = null, cancel = null;
        bool bloqueada;
        try
        {
            facturar = FacturarAsync(v.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, facturar), "Facturar no llegó a la compuerta del ingreso de respaldo.");
            cancel = CancelarVentaAsync(v.VentaId);
            bloqueada = await QuedaBloqueadaAsync(cancel);
        }
        finally
        {
            plan.Release.TrySetResult();
        }
        var resFacturar = await ResultadoAsync("facturar", facturar!, plan);
        var resCancel = await ResultadoAsync("cancelación", cancel!, plan);
        var despues = await FotoFacturacionAsync(v);

        Informe("R-10", $"cancelación bloqueada por el lock de la venta = {bloqueada}", $"facturar={resFacturar}", $"cancelación={resCancel}",
            $"{antes} -> {despues}");
        NoEsFalloDeInfraestructura("facturar", resFacturar);
        NoEsFalloDeInfraestructura("cancelación", resCancel);
        Assert.True(bloqueada, "La cancelación no esperó la facturación en curso (hubiera podido colarse entre la factura y su ingreso).");
        Assert.StartsWith("OK(", resFacturar);
        Assert.StartsWith("OK(", resCancel);
        Assert.Equal(EstadoVenta.Cancelada, despues.Estado);
        Assert.Equal(despues.Ingresos, despues.Egresos);   // el invariante de H-FACT-1
        Assert.Equal(100m, despues.Ingresos);              // exactamente UN ingreso (el de respaldo), no cero ni dos
        Assert.Equal(1, despues.FacturasTotales);
        Assert.Equal(0, despues.FacturasVigentes);
        Assert.Equal(antes.Stock + 2m, despues.Stock);
    }

    /// <summary>
    /// R-10B (FIN-FACT-P0). Dos facturaciones simultáneas de la misma venta legada: la segunda espera el lock, y al
    /// despertar ve la venta ya Facturada y se rechaza. Una sola factura y un solo ingreso, sin deadlock ni timeout.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [R-10B]: DosFacturacionesSimultaneas_UnaFacturaYUnIngreso")]
    [Trait("Categoria", "SqlServerRegresion")]
    [Trait("Hallazgo", "H-FACT-1")]
    public async Task R10B_DosFacturacionesSimultaneas_UnaFacturaYUnIngreso()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false);
        var plan = Compuerta("facturar/antes-del-ingreso-de-respaldo", "INSERT INTO [MovimientosCaja]");

        Task<string>? primera = null, segunda = null;
        bool bloqueada;
        try
        {
            primera = FacturarAsync(v.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, primera), "La primera facturación no llegó a la compuerta.");
            segunda = FacturarAsync(v.VentaId);
            bloqueada = await QuedaBloqueadaAsync(segunda);
        }
        finally
        {
            plan.Release.TrySetResult();
        }
        var res1 = await ResultadoAsync("primera", primera!, plan);
        var res2 = await ResultadoAsync("segunda", segunda!, plan);
        var despues = await FotoFacturacionAsync(v);

        Informe("R-10B", $"segunda bloqueada = {bloqueada}", $"primera={res1}", $"segunda={res2}", $"{despues}");
        NoEsFalloDeInfraestructura("primera", res1);
        NoEsFalloDeInfraestructura("segunda", res2);
        Assert.True(bloqueada, "La segunda facturación no esperó el lock de la venta.");
        Assert.StartsWith("OK(", res1);
        Assert.Contains("Solo se pueden facturar ventas confirmadas", res2);
        Assert.Equal(EstadoVenta.Facturada, despues.Estado);
        Assert.Equal(1, despues.FacturasTotales);
        Assert.Equal(1, despues.FacturasVigentes);
        Assert.Equal(100m, despues.Ingresos);
        Assert.Equal(0m, despues.Egresos);
    }

    /// <summary>
    /// R-10C (FIN-FACT-P0). El cierre de caja tiene el lock de la apertura (detenido antes de insertar CierreCaja); una
    /// facturación de una venta legada de esa apertura arranca: debe quedar bloqueada y, al cerrarse la caja, rechazarse
    /// SIN dejar factura, estado ni ingreso (ni un ingreso dentro de una caja cerrada).
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [R-10C]: FacturarMientrasSeCierraLaCaja_EsperaYSeRechazaSinEfectosParciales")]
    [Trait("Categoria", "SqlServerRegresion")]
    [Trait("Hallazgo", "H-FACT-1")]
    public async Task R10C_FacturarMientrasSeCierraLaCaja_EsperaYSeRechazaSinEfectosParciales()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false);
        var antes = await FotoFacturacionAsync(v);
        var plan = CierreAntesDeInsertar();

        Task<string>? cierre = null, facturar = null;
        bool bloqueada;
        try
        {
            cierre = CerrarAsync(aperturaId, efectivo: 0m, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cierre), "El cierre no llegó a la compuerta.");
            facturar = FacturarAsync(v.VentaId);
            bloqueada = await QuedaBloqueadaAsync(facturar);
        }
        finally
        {
            plan.Release.TrySetResult();
        }
        var resCierre = await ResultadoAsync("cierre", cierre!, plan);
        var resFacturar = await ResultadoAsync("facturar", facturar!, plan);
        var despues = await FotoFacturacionAsync(v);
        var viol = await ViolacionesCierreAsync(aperturaId);

        Informe("R-10C", $"facturar bloqueada por el lock de la apertura = {bloqueada}", $"cierre={resCierre}", $"facturar={resFacturar}",
            $"{antes} -> {despues}", "invariante I-CAJA: " + (viol.Count == 0 ? "OK" : string.Join(" | ", viol)));
        NoEsFalloDeInfraestructura("cierre", resCierre);
        NoEsFalloDeInfraestructura("facturar", resFacturar);
        Assert.True(bloqueada, "La facturación no esperó el lock de la apertura tomado por el cierre.");
        Assert.StartsWith("OK(", resCierre);
        Assert.Contains("cerrada", resFacturar, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(antes, despues);                      // venta Confirmada, sin factura, sin ingreso, mismo stock
        Assert.Empty(viol);
    }

    /// <summary>
    /// R-10D (FIN-FACT-HARDENING-P0). El llamador abre SU transacción; Facturar guarda factura y estado y el registro
    /// del ingreso de respaldo FALLA; el llamador captura la excepción, sigue usando su transacción y la confirma.
    /// Con MultipleActiveResultSets=true (la cadena de estos tests). Facturar revierte hasta su savepoint: no queda
    /// factura ni estado sin asiento, el trabajo previo del llamador sobrevive, y reintentar factura una sola vez.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [R-10D]: FacturarEnTransaccionAmbientalQueFallaYSeConfirma_NoDejaFacturaSinAsiento")]
    [Trait("Categoria", "SqlServerRegresion")]
    [Trait("Hallazgo", "H-FACT-1")]
    public async Task R10D_FacturarEnTransaccionAmbientalQueFallaYSeConfirma_NoDejaFacturaSinAsiento()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false);
        var otra = await NuevaVentaContadoAsync(aperturaId, conIngreso: true);

        string resFalla;
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ventas = scope.ServiceProvider.GetRequiredService<IVentaService>();
            var vm = new FacturaViewModel { VentaId = v.VentaId, Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

            await using var ambiental = await ctx.Database.BeginTransactionAsync();
            // Con MultipleActiveResultSets=true EF desactiva SupportsSavepoints en SQL Server: Facturar usa SAVE TRANSACTION directo.

            // Trabajo previo del llamador dentro de SU transacción.
            var entidadOtra = await ctx.Ventas.FirstAsync(x => x.Id == otra.VentaId);
            entidadOtra.Observaciones = "previo del llamador";
            await ctx.SaveChangesAsync();

            var plan = Compuerta("facturar/falla-en-el-ingreso", "INSERT INTO [MovimientosCaja]");
            plan.FallarAlDisparar = true;
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                await ventas.FacturarVentaAsync(v.VentaId, vm);
                resFalla = "OK(sin falla: la falla inyectada no se disparó)";
            }
            catch (Exception ex)
            {
                resFalla = Clasificar(ex);
            }
            finally
            {
                ConcGateInterceptor.Plan.Value = null;
            }

            Assert.True(plan.Fired, "La compuerta de falla no se alcanzó.");
            Assert.StartsWith("DbUpdateException", resFalla);   // la falla inyectada llega envuelta por EF
            Assert.NotNull(ctx.Database.CurrentTransaction);   // la transacción es del llamador y sigue viva

            // El llamador "se recupera": dentro de su transacción NO hay factura ni estado, y el tracker coincide.
            var enMemoria = ctx.ChangeTracker.Entries<Venta>().FirstOrDefault(e => e.Entity.Id == v.VentaId);
            Assert.True(enMemoria == null || enMemoria.Entity.Estado == EstadoVenta.Confirmada);
            Assert.Equal(0, await ctx.Facturas.CountAsync(f => f.VentaId == v.VentaId));

            // Reintento dentro de la misma transacción ambiental y confirmación del llamador.
            Assert.True(await ventas.FacturarVentaAsync(v.VentaId, vm));
            await ambiental.CommitAsync();
        }

        var foto = await FotoFacturacionAsync(v);
        Informe("R-10D", $"falla={resFalla}", $"{foto}");
        Assert.Equal(EstadoVenta.Facturada, foto.Estado);
        Assert.Equal(1, foto.FacturasTotales);
        Assert.Equal(100m, foto.Ingresos);                 // el ingreso del reintento, exactamente uno
        Assert.Equal(0m, foto.Egresos);

        using var scope2 = _fx.Services.CreateScope();
        Assert.Equal("previo del llamador", await scope2.ServiceProvider.GetRequiredService<AppDbContext>().Ventas.AsNoTracking()
            .Where(x => x.Id == otra.VentaId).Select(x => x.Observaciones).FirstAsync());
    }

    /// <summary>
    /// R-10E (FIN-FACT-HARDENING-P0). Igual que R-10D pero el llamador confirma SIN reintentar: la factura y el estado de
    /// la facturación fallida no pueden quedar confirmados sin su ingreso (venta Confirmada, sin factura, sin ingreso).
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [R-10E]: FacturarEnTransaccionAmbientalQueFallaYSigueSinReintentar_NoPersisteNada")]
    [Trait("Categoria", "SqlServerRegresion")]
    [Trait("Hallazgo", "H-FACT-1")]
    public async Task R10E_FacturarEnTransaccionAmbientalQueFallaYSigueSinReintentar_NoPersisteNada()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false);
        var antes = await FotoFacturacionAsync(v);

        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ventas = scope.ServiceProvider.GetRequiredService<IVentaService>();
            await using var ambiental = await ctx.Database.BeginTransactionAsync();

            var plan = Compuerta("facturar/falla-en-el-ingreso", "INSERT INTO [MovimientosCaja]");
            plan.FallarAlDisparar = true;
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                await Assert.ThrowsAnyAsync<Exception>(() =>
                    ventas.FacturarVentaAsync(v.VentaId, new FacturaViewModel { VentaId = v.VentaId, Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow }));
            }
            finally
            {
                ConcGateInterceptor.Plan.Value = null;
            }

            await ctx.SaveChangesAsync();   // el llamador sigue y guarda: no puede reaparecer nada
            await ambiental.CommitAsync();
        }

        var despues = await FotoFacturacionAsync(v);
        Informe("R-10E", $"{antes} -> {despues}");
        Assert.Equal(antes, despues);
    }
}
