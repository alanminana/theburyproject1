using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// Hito 10 - concurrencia REAL en SQL Server del circuito del cheque recibido (acreditar / rechazar).
// Servicios reales (CajaService como IChequeRecibidoService) sobre un catalogo descartable "qa_fin_conc*_chq".
// Mismo gate que los demas harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catalogo "qa_fin_conc*".
// El workflow corre la clase con READ_COMMITTED_SNAPSHOT apagado y encendido (QA_FIN_CONC_RCSI = 0 / 1).
// Dos operaciones lanzadas a la vez sobre el MISMO cheque: gana una sola, la otra recibe un error de negocio.
// ---------------------------------------------------------------------------------------------

public sealed class ChequeRecibidoSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_chq";
}

public class ChequeRecibidoSqlServerTests : IClassFixture<ChequeRecibidoSqlHostFixture>
{
    private readonly ChequeRecibidoSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public ChequeRecibidoSqlServerTests(ChequeRecibidoSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    /// <summary>Cheque en la apertura del dueno (puede estar cerrada) y dos usuarios con caja vigente propia y distinta.</summary>
    private sealed record Escenario(string UsuarioA, string UsuarioB, string Dueno, int AperturaDueno, int AperturaB, int ChequeId);

    private async Task<Escenario> SembrarAsync(bool cerrarCajaDelCheque, EstadoAcreditacionMovimientoCaja estado = EstadoAcreditacionMovimientoCaja.Pendiente)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N")[..10];
        var dueno = "chqo" + s;
        var usuarioA = "chqa" + s;
        var usuarioB = "chqb" + s;
        var aperturaDueno = await ConcSeed.SembrarAperturaAsync(ctx, dueno);
        await ConcSeed.SembrarAperturaAsync(ctx, usuarioA);
        var aperturaB = await ConcSeed.SembrarAperturaAsync(ctx, usuarioB);

        var cheque = new MovimientoCaja
        {
            AperturaCajaId = aperturaDueno, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaCheque,
            TipoPago = TipoPago.Cheque, EstadoAcreditacion = estado, Monto = 120m, Descripcion = "Cheque H10",
            Referencia = "V-" + s, Usuario = dueno, FechaMovimiento = DateTime.UtcNow
        };
        ctx.MovimientosCaja.Add(cheque);
        await ctx.SaveChangesAsync();

        if (cerrarCajaDelCheque)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE [AperturasCaja] SET [Cerrada] = 1 WHERE [Id] = {aperturaDueno}");
        }
        return new Escenario(usuarioA, usuarioB, dueno, aperturaDueno, aperturaB, cheque.Id);
    }

    private async Task<string> EjecutarAsync(Task arranque, Func<IChequeRecibidoService, Task> operacion)
    {
        await arranque;
        try
        {
            using var scope = _fx.Services.CreateScope();
            await operacion(scope.ServiceProvider.GetRequiredService<IChequeRecibidoService>());
            return "OK";
        }
        catch (Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
                if (e is SqlException sql && sql.Errors.Cast<SqlError>().Any(x => x.Number == 1205))
                    return $"DEADLOCK_VICTIMA({ex.GetType().Name})";
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task<(string R1, string R2)> DosALaVezAsync(
        Func<IChequeRecibidoService, Task> op1, Func<IChequeRecibidoService, Task> op2)
    {
        var inicio = new TaskCompletionSource();
        var t1 = Task.Run(() => EjecutarAsync(inicio.Task, op1));
        var t2 = Task.Run(() => EjecutarAsync(inicio.Task, op2));
        inicio.SetResult();
        var r = await Task.WhenAll(t1, t2);
        _out.WriteLine($"op1={r[0]} | op2={r[1]}");
        return (r[0], r[1]);
    }

    private async Task<(EstadoAcreditacionMovimientoCaja? Estado, int Acreditaciones, int Rechazos)> LeerAsync(int chequeId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mov = await ctx.MovimientosCaja.AsNoTracking().FirstAsync(m => m.Id == chequeId);
        var acred = await ctx.MovimientosCaja.CountAsync(m => m.MovimientoOrigenId == chequeId && m.Concepto == ConceptoMovimientoCaja.AcreditacionCheque);
        var rech = await ctx.MovimientosCaja.CountAsync(m => m.MovimientoOrigenId == chequeId && m.Concepto == ConceptoMovimientoCaja.RechazoCheque);
        return (mov.EstadoAcreditacion, acred, rech);
    }

    private static void ExactamenteUnoGana(string a, string b)
    {
        var oks = new[] { a, b }.Count(r => r == "OK");
        Assert.True(oks == 1, $"Debía ganar exactamente una operación: [{a}] [{b}]");
        var perdedor = a == "OK" ? b : a;
        Assert.StartsWith("InvalidOperationException", perdedor);
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-01] dos acreditaciones concurrentes (caja del cheque CERRADA, usuarios con cajas vigentes distintas): gana una")]
    public async Task CHQ01_DosAcreditaciones_CajaCerrada_UnaSolaGana()
    {
        var e = await SembrarAsync(cerrarCajaDelCheque: true);

        var (r1, r2) = await DosALaVezAsync(
            s => s.AcreditarChequeAsync(e.ChequeId, e.UsuarioA),
            s => s.AcreditarChequeAsync(e.ChequeId, e.UsuarioB));

        ExactamenteUnoGana(r1, r2);
        var f = await LeerAsync(e.ChequeId);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Estado);
        Assert.Equal(1, f.Acreditaciones);
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-02] dos acreditaciones concurrentes (caja del cheque ABIERTA): gana una")]
    public async Task CHQ02_DosAcreditaciones_CajaAbierta_UnaSolaGana()
    {
        var e = await SembrarAsync(cerrarCajaDelCheque: false);

        var (r1, r2) = await DosALaVezAsync(
            s => s.AcreditarChequeAsync(e.ChequeId, e.Dueno),
            s => s.AcreditarChequeAsync(e.ChequeId, e.Dueno));

        ExactamenteUnoGana(r1, r2);
        var f = await LeerAsync(e.ChequeId);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Estado);
        Assert.Equal(0, f.Acreditaciones); // en su propia caja no hay asiento nuevo
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-03] dos rechazos concurrentes de un cheque Pendiente: gana uno")]
    public async Task CHQ03_DosRechazos_Pendiente_UnoSoloGana()
    {
        var e = await SembrarAsync(cerrarCajaDelCheque: true);

        var (r1, r2) = await DosALaVezAsync(
            s => s.RechazarChequeAsync(e.ChequeId, "rebotado", e.UsuarioA),
            s => s.RechazarChequeAsync(e.ChequeId, "sin fondos", e.UsuarioB));

        ExactamenteUnoGana(r1, r2);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Rechazado, (await LeerAsync(e.ChequeId)).Estado);
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-04] dos rechazos concurrentes de un cheque Acreditado: un solo egreso de contrapartida")]
    public async Task CHQ04_DosRechazos_Acreditado_UnSoloEgreso()
    {
        var e = await SembrarAsync(cerrarCajaDelCheque: true, EstadoAcreditacionMovimientoCaja.Acreditado);

        var (r1, r2) = await DosALaVezAsync(
            s => s.RechazarChequeAsync(e.ChequeId, "rebotado", e.UsuarioA),
            s => s.RechazarChequeAsync(e.ChequeId, "sin fondos", e.UsuarioB));

        ExactamenteUnoGana(r1, r2);
        var f = await LeerAsync(e.ChequeId);
        Assert.Equal(1, f.Rechazos);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Estado);
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-05] acreditar y rechazar a la vez sobre el mismo Pendiente: queda un estado coherente, sin doble efecto")]
    public async Task CHQ05_AcreditarVsRechazar_EstadoCoherente()
    {
        var e = await SembrarAsync(cerrarCajaDelCheque: true);

        var (r1, r2) = await DosALaVezAsync(
            s => s.AcreditarChequeAsync(e.ChequeId, e.UsuarioA),
            s => s.RechazarChequeAsync(e.ChequeId, "rebotado", e.UsuarioB));

        ExactamenteUnoGana(r1, r2);
        var f = await LeerAsync(e.ChequeId);
        if (r1 == "OK")
        {
            Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Estado);
            Assert.Equal(1, f.Acreditaciones);
            Assert.Equal(0, f.Rechazos);
        }
        else
        {
            Assert.Equal(EstadoAcreditacionMovimientoCaja.Rechazado, f.Estado);
            Assert.Equal(0, f.Acreditaciones);
            Assert.Equal(0, f.Rechazos);
        }
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-06] el indice unico (origen, concepto) impide por si solo un segundo asiento de acreditacion")]
    public async Task CHQ06_IndiceUnico_ImpideDuplicadoAunSinLaLogicaDeAplicacion()
    {
        var e = await SembrarAsync(cerrarCajaDelCheque: true);
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        MovimientoCaja Asiento() => new()
        {
            AperturaCajaId = e.AperturaB, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.AcreditacionCheque,
            TipoPago = TipoPago.Cheque, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado, Monto = 120m,
            Descripcion = "dup", Usuario = e.UsuarioB, MovimientoOrigenId = e.ChequeId, FechaMovimiento = DateTime.UtcNow
        };
        ctx.MovimientosCaja.Add(Asiento());
        await ctx.SaveChangesAsync();
        ctx.MovimientosCaja.Add(Asiento());

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ------------------------------------------------------------------ integridad contable: rechazo vs cancelacion / devolucion

    private sealed record EscenarioVenta(string Dueno, int AperturaId, int VentaId, string VentaNumero, int ChequeId);

    /// <summary>Venta con su cheque Acreditado en una caja ABIERTA del dueno (el caso donde cancelar y devolver son posibles).</summary>
    private async Task<EscenarioVenta> SembrarVentaConChequeAcreditadoAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N")[..10];
        var dueno = "chqv" + s;
        var apertura = await ConcSeed.SembrarAperturaAsync(ctx, dueno);
        var cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = s, Nombre = "Chq", Apellido = "QA" + s, Telefono = "0000000000", Domicilio = "QA 1",
            PuntajeCliente = 5, NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        var venta = new Venta
        {
            ClienteId = cliente.Id, AperturaCajaId = apertura, Numero = "VCH" + s, Estado = EstadoVenta.Confirmada,
            TipoPago = TipoPago.Cheque, Total = 100m, Subtotal = 100m, FechaVenta = DateTime.UtcNow
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        var cheque = new MovimientoCaja
        {
            AperturaCajaId = apertura, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaCheque,
            TipoPago = TipoPago.Cheque, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado, Monto = 100m,
            Descripcion = "Venta " + venta.Numero, Referencia = venta.Numero, ReferenciaId = venta.Id, VentaId = venta.Id,
            Usuario = dueno, FechaMovimiento = DateTime.UtcNow
        };
        ctx.MovimientosCaja.Add(cheque);
        await ctx.SaveChangesAsync();
        return new EscenarioVenta(dueno, apertura, venta.Id, venta.Numero, cheque.Id);
    }

    private async Task<(decimal Total, decimal Real, int Egresos)> SaldosAsync(EscenarioVenta e)
    {
        using var scope = _fx.Services.CreateScope();
        var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var egresos = await ctx.MovimientosCaja.CountAsync(m => m.AperturaCajaId == e.AperturaId && m.Tipo == TipoMovimientoCaja.Egreso);
        return (await caja.CalcularSaldoActualAsync(e.AperturaId), await caja.CalcularSaldoRealAsync(e.AperturaId), egresos);
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-07] rechazar y cancelar la venta a la vez (caja abierta): una sola salida, saldos total y real neutros")]
    public async Task CHQ07_RechazoVsCancelacion_UnaSolaSalida_SaldosNeutros()
    {
        for (var i = 0; i < 6; i++)
        {
            var e = await SembrarVentaConChequeAcreditadoAsync();

            var (r1, r2) = await DosALaVezAsync(
                s => s.RechazarChequeAsync(e.ChequeId, "rebotado", e.Dueno),
                s => Task.Run(async () =>
                {
                    using var scope = _fx.Services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ICajaService>()
                        .RegistrarContramovimientoVentaAsync(e.VentaId, e.VentaNumero, "cancelada", e.Dueno);
                }));

            var saldos = await SaldosAsync(e);
            _out.WriteLine($"iter {i}: rechazo={r1} | cancelacion={r2} | total={saldos.Total} real={saldos.Real} egresos={saldos.Egresos}");
            Assert.Equal(1, saldos.Egresos);        // nunca las dos salidas
            Assert.Equal(0m, saldos.Total);         // ingreso 100 - una salida 100
            Assert.Equal(0m, saldos.Real);
        }
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [CHQ-08] rechazar y devolver en efectivo a la vez: la devolucion nunca se paga despues de un rechazo ya confirmado")]
    public async Task CHQ08_RechazoVsDevolucion_SeSerializanSobreElMismoLock()
    {
        for (var i = 0; i < 8; i++)
        {
            var e = await SembrarVentaConChequeAcreditadoAsync();

            var (r1, r2) = await DosALaVezAsync(
                s => s.RechazarChequeAsync(e.ChequeId, "rebotado", e.Dueno),
                s => Task.Run(async () =>
                {
                    using var scope = _fx.Services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ICajaService>()
                        .RegistrarMovimientoDevolucionAsync(900 + i, e.VentaId, e.VentaNumero, "DEV-H10-" + i, 100m, e.Dueno);
                }));

            using var scope2 = _fx.Services.CreateScope();
            var ctx = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var rechazoId = await ctx.MovimientosCaja.Where(m => m.MovimientoOrigenId == e.ChequeId && m.Concepto == ConceptoMovimientoCaja.RechazoCheque)
                .Select(m => (int?)m.Id).FirstOrDefaultAsync();
            var devolucionId = await ctx.MovimientosCaja.Where(m => m.VentaId == null && m.AperturaCajaId == e.AperturaId
                    && m.Concepto == ConceptoMovimientoCaja.DevolucionCliente).Select(m => (int?)m.Id).FirstOrDefaultAsync();
            _out.WriteLine($"iter {i}: rechazo={r1} | devolucion={r2} | idRechazo={rechazoId} idDevolucion={devolucionId}");

            Assert.Equal("OK", r1);
            if (rechazoId.HasValue && devolucionId.HasValue)
                Assert.True(devolucionId < rechazoId, "La devolucion se pago DESPUES de confirmado el rechazo.");
        }
    }
}
