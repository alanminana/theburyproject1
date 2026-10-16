using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// FIN-ANT-P0 — concurrencia REAL en SQL Server (efímero, solo CI) de la confirmación de ventas a crédito con
// anticipo frente a la cancelación de la misma venta y frente al cierre de caja.
//
// Servicios reales (VentaService, CajaService) con compuertas deterministas (ConcGateInterceptor): una operación se
// detiene en un comando SQL concreto y el test observa/lanza la otra. Sin Task.Delay como única sincronización: la
// espera fija solo se usa para comprobar que una operación sigue BLOQUEADA por un lock.
//
// Mismo gate de seguridad que los demás harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*".
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Contrato de venta siempre "generado": el contrato real exige plantilla y un archivo en disco, ajenos a esta PR.
/// Se reemplaza solo en este catálogo de pruebas; el resto del flujo de confirmación es el real.
/// </summary>
public class ContratoSiempreGeneradoProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IContratoVentaCreditoService.ExisteContratoGeneradoAsync))
            return Task.FromResult(true);
        throw new NotSupportedException($"{targetMethod?.Name} no está soportado por el stub de contrato de FIN-ANT-P0.");
    }
}

public sealed class FinAntSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_ant";

    protected override void ConfigurarServicios(IServiceCollection services)
    {
        services.RemoveAll<IContratoVentaCreditoService>();
        services.AddScoped<IContratoVentaCreditoService>(_ => DispatchProxy.Create<IContratoVentaCreditoService, ContratoSiempreGeneradoProxy>());
    }
}

public class FinAntSqlServerTests : IClassFixture<FinAntSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(7);

    private const decimal Total = 10_000m;
    private const decimal Financiado = 8_000m;
    private const decimal Anticipo = Total - Financiado;
    private const int Cuotas = 12;

    private readonly FinAntSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public FinAntSqlServerTests(FinAntSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    // ------------------------------------------------------------------ compuertas

    private static GatePlan Compuerta(string nombre, string fragmentoSql) =>
        new(nombre, sql => sql.Contains(fragmentoSql, StringComparison.Ordinal));

    /// <summary>Confirmación: justo ANTES de asentar el anticipo (venta, stock, cuotas y crédito ya escritos y SIN commit).</summary>
    private static GatePlan ConfirmacionAntesDelAnticipo() =>
        Compuerta("confirmacion/antes-del-anticipo", "INSERT INTO [MovimientosCaja]");

    /// <summary>Cancelación: justo ANTES del claim de la venta, con el lock del crédito ya tomado en esta transacción.</summary>
    private static GatePlan CancelacionConCreditoBloqueado() =>
        Compuerta("cancelacion/con-credito-bloqueado", "UPDATE [v]");

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

    private Task<string> Confirmar(int ventaId, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
            await sp.GetRequiredService<IVentaService>().ConfirmarVentaCreditoAsync(ventaId)));

    private Task<string> Cancelar(int ventaId, GatePlan? plan = null) =>
        Task.Run(() => EjecutarAsync(plan, async sp =>
            await sp.GetRequiredService<IVentaService>().CancelarVentaAsync(ventaId, "FIN-ANT-P0 conc")));

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

    // ------------------------------------------------------------------ siembra

    private sealed record VentaCredito(int VentaId, int CreditoId, int ProductoId, int AperturaId);

    /// <summary>Apertura PROPIA y vigente (la más reciente del usuario es la activa) + venta a crédito lista para confirmar.</summary>
    private async Task<VentaCredito> SembrarVentaConfirmableAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = await ConcSeed.SembrarAperturaAsync(ctx);
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var categoria = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        var cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = sufijo, Nombre = "Ant", Apellido = "QA" + sufijo,
            Telefono = "0000000000", Domicilio = "QA 123", PuntajeCliente = 5, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.AddRange(categoria, marca, cliente);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = Total, PorcentajeIVA = 21m, StockActual = 10m, Activo = true
        };
        ctx.Productos.Add(producto);
        // La confirmación real exige un plan global activo para la cantidad de cuotas (un catálogo recién migrado no trae ninguno).
        if (!await ctx.ConfiguracionCreditoPersonalCuotas.AnyAsync(c => c.CantidadCuotas == Cuotas && c.Activo))
            ctx.ConfiguracionCreditoPersonalCuotas.Add(new ConfiguracionCreditoPersonalCuota
            {
                CantidadCuotas = Cuotas, TasaMensual = 3m, Activo = true, Orden = Cuotas
            });
        var credito = new Credito
        {
            ClienteId = cliente.Id, Numero = "CRANT" + sufijo, Estado = EstadoCredito.Configurado,
            TasaInteres = 3m, MontoSolicitado = Financiado, MontoAprobado = Financiado, SaldoPendiente = Financiado,
            CantidadCuotas = Cuotas, MontoCuota = 0m, TotalAPagar = 0m, FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1)
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id, CreditoId = credito.Id, Numero = "VANT" + sufijo, Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.CreditoPersonal, Total = Total, Subtotal = Total, FechaVenta = DateTime.UtcNow,
            FechaConfiguracionCredito = DateTime.UtcNow,
            Detalles = new List<VentaDetalle>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = Total, Subtotal = Total }
            }
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return new VentaCredito(venta.Id, credito.Id, producto.Id, apertura);
    }

    private sealed record Estado(
        EstadoVenta Venta, EstadoCredito Credito, int Cuotas, decimal Stock, List<MovimientoCaja> Anticipos, int Contramovimientos);

    private async Task<Estado> LeerEstadoAsync(VentaCredito v)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new Estado(
            (await ctx.Ventas.AsNoTracking().FirstAsync(x => x.Id == v.VentaId)).Estado,
            (await ctx.Creditos.AsNoTracking().FirstAsync(x => x.Id == v.CreditoId)).Estado,
            await ctx.Cuotas.AsNoTracking().CountAsync(c => c.CreditoId == v.CreditoId && !c.IsDeleted),
            await ctx.Productos.AsNoTracking().Where(p => p.Id == v.ProductoId).Select(p => p.StockActual).FirstAsync(),
            await ctx.MovimientosCaja.AsNoTracking()
                .Where(m => m.Concepto == ConceptoMovimientoCaja.AnticipoCredito && m.ReferenciaId == v.CreditoId && !m.IsDeleted)
                .ToListAsync(),
            await ctx.MovimientosCaja.AsNoTracking()
                .CountAsync(m => m.VentaId == v.VentaId && m.Tipo == TipoMovimientoCaja.Egreso && !m.IsDeleted));
    }

    // ================================================================== escenarios

    [SqlServerConcFact(DisplayName = "FIN-ANT-P0 [ANT-01] confirmacion secuencial con anticipo: un solo asiento vinculado a la venta")]
    public async Task ANT01_ConfirmacionSecuencial_AsientaUnAnticipoVinculado()
    {
        var v = await SembrarVentaConfirmableAsync();

        var r = await Confirmar(v.VentaId);

        Assert.StartsWith("OK(True", r);
        var e = await LeerEstadoAsync(v);
        Assert.Equal(EstadoVenta.Confirmada, e.Venta);
        Assert.Equal(EstadoCredito.Generado, e.Credito);
        Assert.Equal(Cuotas, e.Cuotas);
        var anticipo = Assert.Single(e.Anticipos);
        Assert.Equal(v.VentaId, anticipo.VentaId);
        Assert.Equal(Anticipo, anticipo.Monto);
        Assert.Equal(v.AperturaId, anticipo.AperturaCajaId);
    }

    [SqlServerConcFact(DisplayName = "FIN-ANT-P0 [ANT-02] confirmacion con anticipo en vuelo vs cancelacion de la misma venta")]
    public async Task ANT02_CancelacionDuranteConfirmacionConAnticipo_EsperaYSeRechaza()
    {
        var v = await SembrarVentaConfirmableAsync();
        var plan = ConfirmacionAntesDelAnticipo();
        var confirmar = Confirmar(v.VentaId, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, confirmar),
            "La confirmación no llegó a la compuerta: " + (confirmar.IsCompleted ? confirmar.Result : "?"));

        // Confirmación detenida con el crédito (y la venta) bloqueados y el anticipo AÚN sin asentar.
        var cancelar = Cancelar(v.VentaId);
        var bloqueada = await QuedaBloqueadaAsync(cancelar);
        _out.WriteLine($"cancelación bloqueada por el lock del crédito mientras la confirmación está en vuelo: {bloqueada}");
        Assert.True(bloqueada, "La cancelación NO debió avanzar mientras la confirmación retiene el crédito: " + (cancelar.IsCompleted ? cancelar.Result : "?"));

        plan.Release.TrySetResult();
        var resConfirmar = await confirmar;
        var resCancelar = await cancelar;
        Volcar("confirmación", plan);
        _out.WriteLine($"confirmación: {resConfirmar}\ncancelación:  {resCancelar}");

        Assert.StartsWith("OK(True", resConfirmar);
        Assert.Contains("InvalidOperationException", resCancelar);
        Assert.Contains("anticipo", resCancelar, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEADLOCK", resConfirmar + resCancelar);

        var e = await LeerEstadoAsync(v);
        Assert.Equal(EstadoVenta.Confirmada, e.Venta);   // la cancelación leyó la venta YA confirmada y con su anticipo
        Assert.Equal(EstadoCredito.Generado, e.Credito);
        Assert.Single(e.Anticipos);
        Assert.Equal(0, e.Contramovimientos);
        Assert.Equal(9m, e.Stock);                        // el stock descontado por la confirmación no se devolvió
    }

    [SqlServerConcFact(DisplayName = "FIN-ANT-P0 [ANT-03] cancelacion en vuelo vs confirmacion con anticipo de la misma venta")]
    public async Task ANT03_ConfirmacionDuranteCancelacion_EsperaYSeRechazaSinAnticipo()
    {
        var v = await SembrarVentaConfirmableAsync();
        var plan = CancelacionConCreditoBloqueado();
        var cancelar = Cancelar(v.VentaId, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, cancelar),
            "La cancelación no llegó a la compuerta: " + (cancelar.IsCompleted ? cancelar.Result : "?"));

        var confirmar = Confirmar(v.VentaId);
        var bloqueada = await QuedaBloqueadaAsync(confirmar);
        _out.WriteLine($"confirmación bloqueada por el lock del crédito mientras la cancelación está en vuelo: {bloqueada}");
        Assert.True(bloqueada, "La confirmación NO debió avanzar mientras la cancelación retiene el crédito: " + (confirmar.IsCompleted ? confirmar.Result : "?"));

        plan.Release.TrySetResult();
        var resCancelar = await cancelar;
        var resConfirmar = await confirmar;
        Volcar("cancelación", plan);
        _out.WriteLine($"cancelación:  {resCancelar}\nconfirmación: {resConfirmar}");

        Assert.StartsWith("OK(True", resCancelar);
        Assert.Contains("InvalidOperationException", resConfirmar);
        Assert.DoesNotContain("DEADLOCK", resConfirmar + resCancelar);

        var e = await LeerEstadoAsync(v);
        Assert.Equal(EstadoVenta.Cancelada, e.Venta);
        Assert.NotEqual(EstadoCredito.Generado, e.Credito);
        Assert.Equal(0, e.Cuotas);                        // la confirmación rechazada no generó cuotas
        Assert.Empty(e.Anticipos);                        // ni asentó un anticipo en una venta cancelada
        Assert.Equal(10m, e.Stock);                       // ni descontó stock
    }

    [SqlServerConcFact(DisplayName = "FIN-ANT-P0 [ANT-04] dos confirmaciones simultaneas de la misma venta: un solo anticipo")]
    public async Task ANT04_DosConfirmacionesSimultaneas_UnSoloAnticipoUnSoloDescuento()
    {
        var v = await SembrarVentaConfirmableAsync();
        var plan = ConfirmacionAntesDelAnticipo();
        var primera = Confirmar(v.VentaId, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, primera),
            "La primera confirmación no llegó a la compuerta: " + (primera.IsCompleted ? primera.Result : "?"));

        var segunda = Confirmar(v.VentaId);
        Assert.True(await QuedaBloqueadaAsync(segunda), "La segunda confirmación debía esperar el lock del crédito.");

        plan.Release.TrySetResult();
        var res1 = await primera;
        var res2 = await segunda;
        _out.WriteLine($"primera: {res1}\nsegunda: {res2}");

        Assert.StartsWith("OK(True", res1);
        Assert.Contains("InvalidOperationException", res2); // la segunda ve la venta ya confirmada
        Assert.DoesNotContain("DEADLOCK", res1 + res2);

        var e = await LeerEstadoAsync(v);
        Assert.Equal(EstadoVenta.Confirmada, e.Venta);
        Assert.Single(e.Anticipos);
        Assert.Equal(Cuotas, e.Cuotas);
        Assert.Equal(9m, e.Stock);                          // descontado una sola vez
    }

    // ------------------------------------------------------------------ dependencia H-CAJA-1 (CORREGIDA en FIN-CAJA-P0)

    // H-CAJA-1: la confirmación asentaba el anticipo sin lock compartido con CerrarCajaAsync. Si la caja se cerraba entre
    // la lectura de la apertura y el asiento, el anticipo caía en una apertura ya CERRADA y quedaba fuera del snapshot del
    // cierre. Ahora el asiento y el cierre toman el mismo lock de apertura (FIN-CAJA-P0): ANT-05 exige el comportamiento
    // correcto (el cierre espera, incluye el anticipo y el snapshot coincide con el libro). Las aserciones se INVIRTIERON;
    // el escenario (compuerta en el INSERT del anticipo, cierre lanzado mientras la confirmación está en vuelo) no cambia.
    [SqlServerConcFact(DisplayName = "FIN-ANT-P0 [ANT-05] REGRESION [H-CAJA-1]: cierre en vuelo con la confirmacion espera e incluye el anticipo")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-CAJA-1")]
    [Trait("Estado", "CORREGIDO")]
    public async Task ANT05_Regresion_HCaja1_ConfirmacionVsCierreDeCaja_CierreEsperaEIncluyeElAnticipo()
    {
        var v = await SembrarVentaConfirmableAsync();
        var plan = ConfirmacionAntesDelAnticipo();
        var confirmar = Confirmar(v.VentaId, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, confirmar),
            "La confirmación no llegó a la compuerta: " + (confirmar.IsCompleted ? confirmar.Result : "?"));

        // Confirmación detenida con el lock de la apertura tomado y el anticipo AÚN sin insertar: el cierre debe esperar.
        var cierre = Task.Run(() => EjecutarAsync(null, async sp =>
        {
            var c = await sp.GetRequiredService<ICajaService>().CerrarCajaAsync(
                new CerrarCajaViewModel { AperturaCajaId = v.AperturaId, EfectivoContado = 0m, JustificacionDiferencia = "FIN-CAJA-P0" },
                ConcCurrentUser.Usuario);
            return c.TotalIngresosSistema;
        }));
        var bloqueado = await QuedaBloqueadaAsync(cierre);
        _out.WriteLine($"cierre de caja bloqueado por el lock de la apertura con la confirmación en vuelo: {bloqueado}");
        Assert.True(bloqueado, "El cierre NO debió avanzar mientras la confirmación retiene la apertura: " + (cierre.IsCompleted ? cierre.Result : "?"));

        plan.Release.TrySetResult();
        var resConfirmar = await confirmar;
        var resCierre = await cierre;
        _out.WriteLine($"confirmación: {resConfirmar}\ncierre: {resCierre}");

        Assert.StartsWith("OK(True", resConfirmar);
        Assert.StartsWith("OK(", resCierre);
        Assert.DoesNotContain("DEADLOCK", resConfirmar + resCierre);

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = await ctx.AperturasCaja.AsNoTracking().Include(a => a.Movimientos).FirstAsync(a => a.Id == v.AperturaId);
        var anticipo = await ctx.MovimientosCaja.AsNoTracking().SingleAsync(m =>
            m.Concepto == ConceptoMovimientoCaja.AnticipoCredito && m.VentaId == v.VentaId);
        var snapshotCierre = await ctx.CierresCaja.AsNoTracking().SingleAsync(c => c.AperturaCajaId == v.AperturaId);
        Assert.True(apertura.Cerrada);
        Assert.Equal(v.AperturaId, anticipo.AperturaCajaId);
        Assert.Equal(Anticipo, anticipo.Monto);
        // El cierre congelado coincide con el libro: lo que el cierre calculó es exactamente lo que quedó en la apertura.
        var fisico = TheBuryProject.Services.CajaService.CalcularResumenFisico(apertura);
        Assert.Equal(fisico.IngresosFisicos, snapshotCierre.TotalIngresosSistema);
        Assert.Equal(fisico.EgresosFisicos, snapshotCierre.TotalEgresosSistema);
        var e = await LeerEstadoAsync(v);
        Assert.Equal(EstadoVenta.Confirmada, e.Venta);
        Assert.Equal(EstadoCredito.Generado, e.Credito);
    }

    [SqlServerConcFact(DisplayName = "FIN-ANT-P0 [ANT-05B] REGRESION [H-CAJA-1]: cierre completado antes del anticipo rechaza la confirmacion entera sin efectos")]
    [Trait("Categoria", "Regresion")]
    [Trait("Hallazgo", "H-CAJA-1")]
    [Trait("Estado", "CORREGIDO")]
    public async Task ANT05B_Regresion_HCaja1_CierreAntesDelAnticipo_RechazaLaConfirmacionYRevierteTodo()
    {
        var v = await SembrarVentaConfirmableAsync();
        // Detenida justo antes de tomar el lock de la apertura: venta, stock, cuotas y crédito ya escritos y SIN commit.
        var plan = Compuerta("confirmacion/antes-del-lock-de-apertura", "[AperturasCaja] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
        var confirmar = Confirmar(v.VentaId, plan);

        Assert.True(await LlegoALaCompuertaAsync(plan, confirmar),
            "La confirmación no llegó a la compuerta: " + (confirmar.IsCompleted ? confirmar.Result : "?"));

        var cierre = await Task.Run(() => EjecutarAsync(null, async sp =>
        {
            var c = await sp.GetRequiredService<ICajaService>().CerrarCajaAsync(
                new CerrarCajaViewModel { AperturaCajaId = v.AperturaId, EfectivoContado = 0m, JustificacionDiferencia = "FIN-CAJA-P0" },
                ConcCurrentUser.Usuario);
            return c.TotalIngresosSistema;
        }));
        _out.WriteLine($"cierre de caja con la confirmación detenida antes del lock: {cierre}");
        Assert.StartsWith("OK(", cierre);

        plan.Release.TrySetResult();
        var resConfirmar = await confirmar;
        _out.WriteLine($"confirmación: {resConfirmar}");

        Assert.Contains("InvalidOperationException", resConfirmar);
        Assert.Contains("cerrada", resConfirmar, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEADLOCK", resConfirmar);

        // Rollback completo de la confirmación: nada de lo que había escrito antes del anticipo sobrevive.
        var e = await LeerEstadoAsync(v);
        Assert.Equal(EstadoVenta.Presupuesto, e.Venta);
        Assert.NotEqual(EstadoCredito.Generado, e.Credito);
        Assert.Equal(0, e.Cuotas);
        Assert.Equal(10m, e.Stock);
        Assert.Empty(e.Anticipos);
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cierreCaja = await ctx.CierresCaja.AsNoTracking().SingleAsync(c => c.AperturaCajaId == v.AperturaId);
        Assert.Equal(0m, cierreCaja.TotalIngresosSistema);
        Assert.Equal(0, await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.AperturaCajaId == v.AperturaId && !m.IsDeleted));
    }
}
