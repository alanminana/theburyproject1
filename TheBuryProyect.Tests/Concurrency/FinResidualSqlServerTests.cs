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
    /// R-07 [esperado: FAIL]. Devolución completa (reembolso en caja) y cancelación de la misma venta con la
    /// cancelación en vuelo (venta y stock ya bloqueados, contramovimiento sin insertar). Se observa si Completar
    /// espera (fila de Producto/Venta) y, al terminar ambas, se exige que lo que sale de caja no supere lo ingresado
    /// y que el stock no supere lo existente más lo vendido. Variante determinista de H-DEV-2/H-DEV-3.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-07]: CompletarDevolucionYCancelarLaMismaVenta_NoSacaMasDineroNiMasStockQueLoVendido")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "FAIL")]
    public async Task R07_CompletarDevolucionYCancelarLaMismaVenta_NoSacaMasDineroNiMasStockQueLoVendido()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, stock: 10m);
        var (devId, rv) = await DevolucionAprobadaConReembolsoAsync(v, cantidad: 2);

        var plan = Compuerta("cancelacion/antes-del-contramovimiento", "INSERT INTO [MovimientosCaja]");
        var cancel = CancelarVentaAsync(v.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, cancel));

        var completar = CompletarDevolucionAsync(devId, rv);
        var bloqueada = await QuedaBloqueadaAsync(completar);
        Informe("R-07", $"completar bloqueada por la cancelación en vuelo = {bloqueada}");
        plan.Release.TrySetResult();
        var resCancel = await cancel;
        var resCompletar = await completar;

        var (ing, egr) = await CajaDeVentaAsync(v.VentaId);
        var stock = await StockAsync(v.ProductoId);
        Informe("R-07", $"cancelación={resCancel}", $"completar={resCompletar}", $"caja: ingresos={ing} egresos={egr}", $"stock={stock} (inicial 10 + vendido 2 = tope 12)");
        Assert.True(egr <= ing, $"Egresos {egr} > ingresos {ing} para la venta.");
        Assert.True(stock <= 12m, $"Stock {stock} > 12.");
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

    /// <summary>
    /// R-09 [esperado: PASS por RowVersion de Venta]. Facturar (sin transacción) cargó la venta Confirmada y se
    /// detiene antes de insertar la factura; la cancelación completa; al liberar, el UPDATE de Venta debe fallar por
    /// RowVersion. Invariante: venta Cancelada ⇒ ninguna factura vigente.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-09]: CancelarMientrasSeFactura_NoDejaVentaCanceladaConFacturaVigente")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "PASS")]
    public async Task R09_CancelarMientrasSeFactura_NoDejaVentaCanceladaConFacturaVigente()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId);
        var plan = Compuerta("facturar/antes-de-insertar-factura", "INSERT INTO [Facturas]");

        var facturar = FacturarAsync(v.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, facturar));
        var cancel = await CancelarVentaAsync(v.VentaId);
        plan.Release.TrySetResult();
        var resFacturar = await facturar;

        Informe("R-09", $"cancelación={cancel}", $"facturar={resFacturar}");
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estado = await ctx.Ventas.AsNoTracking().Where(x => x.Id == v.VentaId).Select(x => x.Estado).FirstAsync();
        var vigentes = await ctx.Facturas.CountAsync(f => f.VentaId == v.VentaId && !f.IsDeleted && !f.Anulada);
        Assert.False(estado == EstadoVenta.Cancelada && vigentes > 0, $"Venta {estado} con {vigentes} factura(s) vigente(s).");
    }

    /// <summary>
    /// R-10 [esperado: FAIL, solo ventas legadas sin ingreso asentado]. Facturar commitea la venta como Facturada
    /// y, sin transacción, recién después asienta el ingreso "de respaldo". Detenido justo antes de ese insert, la
    /// cancelación completa (no encuentra ingreso que revertir) y al liberar Facturar asienta un ingreso para una
    /// venta Cancelada. Invariante: venta Cancelada ⇒ Σ egresos = Σ ingresos.
    /// </summary>
    [SqlServerConcFact(DisplayName = "NO EJECUTADO / BLOQUEADO [R-10]: CancelarEntreElCommitDeFacturarYSuIngresoDeRespaldo_NoDejaIngresoHuerfano")]
    [Trait("Categoria", "SqlServerNoEjecutado")]
    [Trait("ResultadoEsperadoInferido", "FAIL-LEGADO")]
    public async Task R10_CancelarEntreElCommitDeFacturarYSuIngresoDeRespaldo_NoDejaIngresoHuerfano()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false);
        var plan = Compuerta("facturar/antes-del-ingreso-de-respaldo", "INSERT INTO [MovimientosCaja]");

        var facturar = FacturarAsync(v.VentaId, plan);
        Assert.True(await LlegoALaCompuertaAsync(plan, facturar));
        var cancel = await CancelarVentaAsync(v.VentaId);
        plan.Release.TrySetResult();
        var resFacturar = await facturar;

        Informe("R-10", $"cancelación={cancel}", $"facturar={resFacturar}");
        var (ing, egr) = await CajaDeVentaAsync(v.VentaId);
        Informe("R-10", $"caja: ingresos={ing} egresos={egr}");
        Assert.Equal(ing, egr);
    }
}
