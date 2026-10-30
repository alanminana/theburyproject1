using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// H10 / B13 - cancelar una venta pagada con cheque cuando la caja del ingreso ya CERRÓ, contra SQL Server real y con las
// OPERACIONES REALES (VentaService.CancelarVentaAsync, CajaService acreditar/rechazar, DevolucionService). La contrapartida se
// asienta en la caja vigente; la caja cerrada (movimientos, saldo total, saldo real, efectivo esperado, CierreCaja) no cambia.
// Se valida el resultado de AMBAS operaciones y la conciliación antes/después. Un deadlock o un timeout nunca es un rechazo válido.
// ORDEN DE LOCKS: Crédito → Venta → Apertura del cheque → Apertura vigente.
// ---------------------------------------------------------------------------------------------
public partial class FinResidualSqlServerTests
{
    private static GatePlan AntesDeActualizarMovimiento() => Compuerta("antes-de-actualizar-movimiento", "UPDATE [MovimientosCaja]");

    private sealed record Libro(int Movimientos, decimal Total, decimal Real, decimal Fisica, int Reversiones, int Rechazos, int Acreditaciones);

    private async Task<Libro> LibroAsync(int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movs = await ctx.MovimientosCaja.AsNoTracking().Where(m => m.AperturaCajaId == aperturaId && !m.IsDeleted).ToListAsync();
        var det = await caja.ObtenerDetallesAperturaAsync(aperturaId);
        return new Libro(movs.Count, await caja.CalcularSaldoActualAsync(aperturaId), await caja.CalcularSaldoRealAsync(aperturaId),
            det.CajaFisicaEsperada,
            movs.Count(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta),
            movs.Count(m => m.Concepto == ConceptoMovimientoCaja.RechazoCheque),
            movs.Count(m => m.Concepto == ConceptoMovimientoCaja.AcreditacionCheque));
    }

    private async Task<EstadoAcreditacionMovimientoCaja?> EstadoChequeAsync(int chequeId)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosCaja.AsNoTracking()
            .Where(m => m.Id == chequeId).Select(m => m.EstadoAcreditacion).FirstAsync();
    }

    /// <summary>Venta Confirmada con cheque $100 en la apertura indicada (Pendiente o Acreditado), SIN cerrar la caja.</summary>
    private async Task<VentaConCheque> NuevaVentaConChequeEnEstadoAsync(int aperturaId, EstadoAcreditacionMovimientoCaja estado)
    {
        var v = await NuevaVentaConChequeAcreditadoAsync(aperturaId);
        if (estado != EstadoAcreditacionMovimientoCaja.Acreditado)
        {
            using var scope = _fx.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.MovimientosCaja.Where(m => m.Id == v.ChequeId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.EstadoAcreditacion, estado));
        }
        return v;
    }

    private async Task CerrarCajaOkAsync(int aperturaId) =>
        Assert.StartsWith("OK", await CerrarAsync(aperturaId, efectivo: 0m));

    private sealed record Escenario13(VentaConCheque C, int Origen, int Vigente);

    /// <summary>Cheque en una caja que luego CIERRA (con CierreCaja real) y una caja vigente nueva del mismo usuario.</summary>
    private async Task<Escenario13> SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja estado)
    {
        var origen = await NuevaAperturaAsync();
        var c = await NuevaVentaConChequeEnEstadoAsync(origen, estado);
        await CerrarCajaOkAsync(origen);
        var vigente = await NuevaAperturaAsync();
        return new Escenario13(c, origen, vigente);
    }

    private Task<string> AcreditarComoAsync(int chequeId, string usuario, GatePlan? plan = null) =>
        Lanzar(plan, async sp => (await sp.GetRequiredService<IChequeRecibidoService>().AcreditarChequeAsync(chequeId, usuario)).MovimientoAsentadoId);

    private Task<string> RechazarComoAsync(int chequeId, string usuario, GatePlan? plan = null) =>
        Lanzar(plan, async sp => (await sp.GetRequiredService<IChequeRecibidoService>().RechazarChequeAsync(chequeId, "rebotado", usuario)).MovimientoAsentadoId);

    private async Task<int> NuevaAperturaDeAsync(string usuario)
    {
        using var scope = _fx.Services.CreateScope();
        return await ConcSeed.SembrarAperturaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), usuario);
    }

    private static string UsuarioNuevo() => "qb" + Guid.NewGuid().ToString("N")[..8];

    // =====================================================================================
    // B13-A  Cancelación real, sin concurrencia
    // =====================================================================================

    [SqlServerConcFact(DisplayName = "H10-B13 [B13a] cheque PENDIENTE de caja cerrada: la cancelacion real marca Anulado, sin egreso, sin tocar ninguna caja")]
    public async Task B13a_ChequePendiente_CajaCerrada_AnulaSinEgreso()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Pendiente);
        var antesO = await LibroAsync(e.Origen);
        var antesV = await LibroAsync(e.Vigente);

        var r = await CancelarVentaAsync(e.C.Venta.VentaId);

        var despuesO = await LibroAsync(e.Origen);
        var despuesV = await LibroAsync(e.Vigente);
        Informe("B13a", $"cancelación={r}", $"cerrada antes={antesO} después={despuesO}", $"vigente antes={antesV} después={despuesV}");
        Assert.StartsWith("OK", r);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Anulado, await EstadoChequeAsync(e.C.ChequeId));
        Assert.Equal(antesO, despuesO);
        Assert.Equal(antesV, despuesV);
        Assert.Empty(await ViolacionesCierreAsync(e.Origen));
        Assert.Equal(EstadoVenta.Cancelada, await EstadoVentaAsync(e.C.Venta.VentaId));
        Assert.Equal(12m, await StockAsync(e.C.Venta.ProductoId)); // stock devuelto una vez
    }

    [SqlServerConcFact(DisplayName = "H10-B13 [B13b] cheque ACREDITADO de caja cerrada: la cancelacion real asienta UN egreso bancario en la caja vigente, la cerrada intacta")]
    public async Task B13b_ChequeAcreditado_CajaCerrada_EgresoEnCajaVigente()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var antesO = await LibroAsync(e.Origen);
        var antesV = await LibroAsync(e.Vigente);

        var r = await CancelarVentaAsync(e.C.Venta.VentaId);

        var despuesO = await LibroAsync(e.Origen);
        var despuesV = await LibroAsync(e.Vigente);
        Informe("B13b", $"cancelación={r}", $"cerrada antes={antesO} después={despuesO}", $"vigente antes={antesV} después={despuesV}");
        Assert.StartsWith("OK", r);
        Assert.Equal(antesO, despuesO);
        Assert.Empty(await ViolacionesCierreAsync(e.Origen));
        Assert.Equal(1, despuesV.Reversiones);
        Assert.Equal(antesV.Total - 100m, despuesV.Total);
        Assert.Equal(antesV.Real - 100m, despuesV.Real);
        Assert.Equal(antesV.Fisica, despuesV.Fisica); // bancario: no toca el efectivo
        using var scope = _fx.Services.CreateScope();
        var egreso = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosCaja.AsNoTracking()
            .SingleAsync(m => m.AperturaCajaId == e.Vigente && m.Concepto == ConceptoMovimientoCaja.ReversionVenta);
        Assert.Equal(e.C.ChequeId, egreso.MovimientoOrigenId);
        Assert.Equal(e.C.Venta.VentaId, egreso.VentaId);
        Assert.Equal(TipoPago.Cheque, egreso.TipoPago);
    }

    [SqlServerConcFact(DisplayName = "H10-B13 [B13c] dos cancelaciones simultaneas de la misma venta (cheque acreditado, caja cerrada): una sola contrapartida")]
    public async Task B13c_DosCancelaciones_UnaSolaContrapartida()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Acreditado);

        var t1 = CancelarVentaAsync(e.C.Venta.VentaId);
        var t2 = CancelarVentaAsync(e.C.Venta.VentaId);
        var r1 = await ResultadoAsync("cancelación 1", t1);
        var r2 = await ResultadoAsync("cancelación 2", t2);

        var v = await LibroAsync(e.Vigente);
        Informe("B13c", $"c1={r1}", $"c2={r2}", $"vigente={v}");
        SinFalloDeInfra(r1, r2);
        Assert.Equal(1, new[] { r1, r2 }.Count(x => x.StartsWith("OK")));
        Assert.Equal(1, v.Reversiones);
        Assert.Equal(EstadoVenta.Cancelada, await EstadoVentaAsync(e.C.Venta.VentaId));
    }

    // =====================================================================================
    // B13-B  Acreditación × cancelación
    // =====================================================================================

    /// <summary>La cancelación real tiene el lock de la venta y se detiene antes de pedir la apertura del cheque; la acreditación (otro usuario, otra caja vigente) corre en la ventana.</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13d] cancelacion con el lock de venta + acreditacion en la ventana (cheque pendiente, caja cerrada): sin deadlock, la cancelacion devuelve lo acreditado")]
    public async Task B13d_CancelacionPrimero_AcreditacionEnLaVentana()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Pendiente);
        var usuarioB = UsuarioNuevo();
        var cajaB = await NuevaAperturaDeAsync(usuarioB);
        var antesO = await LibroAsync(e.Origen);
        var plan = AntesDeLockearApertura();

        Task<string>? cancelar = null, acreditar = null;
        bool bloqueada;
        try
        {
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cancelar), "La cancelación no llegó a pedir el lock de la apertura.");
            acreditar = AcreditarComoAsync(e.C.ChequeId, usuarioB);
            bloqueada = await QuedaBloqueadaAsync(acreditar);
        }
        finally { plan.Release.TrySetResult(); }
        var rCancelar = await ResultadoAsync("cancelación", cancelar!, plan);
        var rAcreditar = await ResultadoAsync("acreditación", acreditar!);

        var despuesO = await LibroAsync(e.Origen);
        var libroB = await LibroAsync(cajaB);
        var libroV = await LibroAsync(e.Vigente);
        Informe("B13d", $"acreditación bloqueada por la venta = {bloqueada}", $"cancelación={rCancelar}", $"acreditación={rAcreditar}",
            $"cerrada={despuesO}", $"cajaB={libroB}", $"vigente={libroV}");
        SinFalloDeInfra(rCancelar, rAcreditar);
        Assert.False(bloqueada, "La acreditación no debía esperar la fila de la venta.");
        Assert.StartsWith("OK", rAcreditar);
        Assert.StartsWith("OK", rCancelar);
        Assert.Equal(antesO, despuesO);
        Assert.Empty(await ViolacionesCierreAsync(e.Origen));
        Assert.Equal(1, libroB.Acreditaciones);
        Assert.Equal(1, libroV.Reversiones);
        // Lo acreditado en la caja B se devuelve desde la caja vigente de la cancelación: neto 0 entre ambas.
        Assert.Equal(100m, libroB.Real);
        Assert.Equal(-100m, libroV.Real);
    }

    /// <summary>La acreditación tiene la apertura y se detiene antes de asentar; la cancelación real espera la apertura del cheque y luego ve el cheque Acreditado.</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13e] acreditacion con el lock de apertura + cancelacion real: la cancelacion espera y devuelve lo acreditado")]
    public async Task B13e_AcreditacionPrimero_LaCancelacionEspera()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Pendiente);
        var usuarioB = UsuarioNuevo();
        var cajaB = await NuevaAperturaDeAsync(usuarioB);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? acreditar = null, cancelar = null;
        bool bloqueada;
        try
        {
            acreditar = AcreditarComoAsync(e.C.ChequeId, usuarioB, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, acreditar), "La acreditación no llegó a asentar.");
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId);
            bloqueada = await QuedaBloqueadaAsync(cancelar);
        }
        finally { plan.Release.TrySetResult(); }
        var rAcreditar = await ResultadoAsync("acreditación", acreditar!, plan);
        var rCancelar = await ResultadoAsync("cancelación", cancelar!);

        var libroB = await LibroAsync(cajaB);
        var libroV = await LibroAsync(e.Vigente);
        Informe("B13e", $"cancelación bloqueada = {bloqueada}", $"acreditación={rAcreditar}", $"cancelación={rCancelar}", $"cajaB={libroB}", $"vigente={libroV}");
        SinFalloDeInfra(rAcreditar, rCancelar);
        Assert.True(bloqueada);
        Assert.StartsWith("OK", rAcreditar);
        Assert.StartsWith("OK", rCancelar);
        Assert.Equal(1, libroB.Acreditaciones);
        Assert.Equal(1, libroV.Reversiones);
        Assert.Empty(await ViolacionesCierreAsync(e.Origen));
    }

    // =====================================================================================
    // B13-C  Rechazo × cancelación
    // =====================================================================================

    /// <summary>La cancelación espera con el lock de la venta; el rechazo (cheque Pendiente) corre en la ventana y termina; la cancelación ve Rechazado y no agrega nada.</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13f] cancelacion con el lock de venta + rechazo en la ventana (pendiente, caja cerrada): una sola salida, nada duplicado")]
    public async Task B13f_CancelacionPrimero_RechazoPendienteEnLaVentana()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Pendiente);
        var antesO = await LibroAsync(e.Origen);
        var antesV = await LibroAsync(e.Vigente);
        var plan = AntesDeLockearApertura();

        Task<string>? cancelar = null, rechazar = null;
        bool bloqueado;
        try
        {
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cancelar));
            rechazar = RechazarComoAsync(e.C.ChequeId, ConcCurrentUser.Usuario);
            bloqueado = await QuedaBloqueadaAsync(rechazar);
        }
        finally { plan.Release.TrySetResult(); }
        var rCancelar = await ResultadoAsync("cancelación", cancelar!, plan);
        var rRechazar = await ResultadoAsync("rechazo", rechazar!);

        var despuesO = await LibroAsync(e.Origen);
        var despuesV = await LibroAsync(e.Vigente);
        Informe("B13f", $"rechazo bloqueado por la venta = {bloqueado}", $"cancelación={rCancelar}", $"rechazo={rRechazar}",
            $"cerrada antes={antesO} después={despuesO}", $"vigente antes={antesV} después={despuesV}");
        SinFalloDeInfra(rCancelar, rRechazar);
        Assert.False(bloqueado, "El rechazo no debía esperar la fila de la venta.");
        Assert.StartsWith("OK", rRechazar);
        Assert.StartsWith("OK", rCancelar);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Rechazado, await EstadoChequeAsync(e.C.ChequeId));
        // Efecto ya documentado del rechazo de un Pendiente (D2-A): el ingreso Rechazado deja de sumar al saldo total; el real, el
        // efectivo y los movimientos no cambian. La cancelación no agrega nada encima.
        Assert.Equal(antesO with { Total = antesO.Total - 100m }, despuesO);
        Assert.Equal(antesV, despuesV); // ni reversión ni egreso de rechazo: el cheque Pendiente rechazado nunca fue dinero
    }

    /// <summary>La cancelación (cheque Pendiente) tiene el lock del cheque y se detiene antes de actualizarlo; el rechazo espera y luego se rechaza (estado Anulado).</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13g] cancelacion con el lock del cheque (pendiente, caja cerrada) + rechazo: el rechazo espera y se rechaza")]
    public async Task B13g_CancelacionPendienteConLockDelCheque_ElRechazoEspera()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Pendiente);
        var plan = AntesDeActualizarMovimiento();

        Task<string>? cancelar = null, rechazar = null;
        bool bloqueado;
        try
        {
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cancelar), "La cancelación no llegó a anular el cheque.");
            rechazar = RechazarComoAsync(e.C.ChequeId, ConcCurrentUser.Usuario);
            bloqueado = await QuedaBloqueadaAsync(rechazar);
        }
        finally { plan.Release.TrySetResult(); }
        var rCancelar = await ResultadoAsync("cancelación", cancelar!, plan);
        var rRechazar = await ResultadoAsync("rechazo", rechazar!);

        Informe("B13g", $"rechazo bloqueado = {bloqueado}", $"cancelación={rCancelar}", $"rechazo={rRechazar}");
        SinFalloDeInfra(rCancelar, rRechazar);
        Assert.True(bloqueado);
        Assert.StartsWith("OK", rCancelar);
        Assert.StartsWith("InvalidOperationException", rRechazar);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Anulado, await EstadoChequeAsync(e.C.ChequeId));
        Assert.Equal(0, (await LibroAsync(e.Vigente)).Rechazos);
    }

    /// <summary>El rechazo de un cheque Acreditado (caja cerrada) asienta su egreso en la caja de otro usuario; la cancelación, que esperaba, ve el rechazo y no agrega una segunda salida.</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13h] rechazo de un acreditado con el lock del cheque + cancelacion real: la cancelacion espera y no duplica la salida")]
    public async Task B13h_RechazoAcreditadoPrimero_LaCancelacionNoDuplica()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var usuarioB = UsuarioNuevo();
        var cajaB = await NuevaAperturaDeAsync(usuarioB);
        var antesO = await LibroAsync(e.Origen);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? rechazar = null, cancelar = null;
        bool bloqueada;
        try
        {
            rechazar = RechazarComoAsync(e.C.ChequeId, usuarioB, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, rechazar), "El rechazo no llegó a asentar su egreso.");
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId);
            bloqueada = await QuedaBloqueadaAsync(cancelar);
        }
        finally { plan.Release.TrySetResult(); }
        var rRechazar = await ResultadoAsync("rechazo", rechazar!, plan);
        var rCancelar = await ResultadoAsync("cancelación", cancelar!);

        var libroB = await LibroAsync(cajaB);
        var libroV = await LibroAsync(e.Vigente);
        Informe("B13h", $"cancelación bloqueada = {bloqueada}", $"rechazo={rRechazar}", $"cancelación={rCancelar}", $"cajaB={libroB}", $"vigente={libroV}");
        SinFalloDeInfra(rRechazar, rCancelar);
        Assert.True(bloqueada);
        Assert.StartsWith("OK", rRechazar);
        Assert.StartsWith("OK", rCancelar);
        Assert.Equal(antesO, await LibroAsync(e.Origen));
        Assert.Equal(1, libroB.Rechazos);
        Assert.Equal(0, libroV.Reversiones); // una sola salida (la del rechazo)
        Assert.Empty(await ViolacionesCierreAsync(e.Origen));
    }

    /// <summary>La cancelación (cheque Acreditado) asentó su egreso y está por confirmar; el rechazo (que leyó Acreditado antes) espera y se rechaza: venta cancelada.</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13i] cancelacion de un acreditado con la reversion asentada + rechazo: el rechazo se rechaza bajo el lock, una sola salida")]
    public async Task B13i_CancelacionAcreditadoAsentada_ElRechazoSeRechaza()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var usuarioB = UsuarioNuevo();
        var cajaB = await NuevaAperturaDeAsync(usuarioB);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? cancelar = null, rechazar = null;
        bool bloqueado;
        try
        {
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cancelar), "La cancelación no llegó a insertar la reversión.");
            rechazar = RechazarComoAsync(e.C.ChequeId, usuarioB);
            bloqueado = await QuedaBloqueadaAsync(rechazar);
        }
        finally { plan.Release.TrySetResult(); }
        var rCancelar = await ResultadoAsync("cancelación", cancelar!, plan);
        var rRechazar = await ResultadoAsync("rechazo", rechazar!);

        var libroB = await LibroAsync(cajaB);
        var libroV = await LibroAsync(e.Vigente);
        Informe("B13i", $"rechazo bloqueado = {bloqueado}", $"cancelación={rCancelar}", $"rechazo={rRechazar}", $"cajaB={libroB}", $"vigente={libroV}");
        SinFalloDeInfra(rCancelar, rRechazar);
        Assert.True(bloqueado);
        Assert.StartsWith("OK", rCancelar);
        Assert.StartsWith("InvalidOperationException", rRechazar);
        Assert.Contains("cancelada", rRechazar);
        Assert.Equal(0, libroB.Rechazos);
        Assert.Equal(1, libroV.Reversiones);
    }

    // =====================================================================================
    // B13-D  Devolución × cancelación
    // =====================================================================================

    /// <summary>La devolución completada tiene los locks y se detiene antes de asentar el reembolso; la cancelación real espera la venta y luego rechaza (hay devolución registrada).</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13j] devolucion real en vuelo (cheque acreditado, caja cerrada) + cancelacion: la cancelacion se rechaza, solo hay reembolso")]
    public async Task B13j_DevolucionPrimero_LaCancelacionSeRechaza()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var (devId, rv) = await DevolucionAprobadaConReembolsoAsync(e.C.Venta, cantidad: 1);
        var antesO = await LibroAsync(e.Origen);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? devolver = null, cancelar = null;
        bool bloqueada;
        try
        {
            devolver = CompletarDevolucionAsync(devId, rv, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, devolver), "La devolución no llegó a asentar el reembolso.");
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId);
            bloqueada = await QuedaBloqueadaAsync(cancelar);
        }
        finally { plan.Release.TrySetResult(); }
        var rDevolver = await ResultadoAsync("devolución", devolver!, plan);
        var rCancelar = await ResultadoAsync("cancelación", cancelar!);

        var libroV = await LibroAsync(e.Vigente);
        Informe("B13j", $"cancelación bloqueada = {bloqueada}", $"devolución={rDevolver}", $"cancelación={rCancelar}", $"vigente={libroV}");
        SinFalloDeInfra(rDevolver, rCancelar);
        Assert.True(bloqueada);
        Assert.StartsWith("OK", rDevolver);
        Assert.StartsWith("InvalidOperationException", rCancelar);
        Assert.Contains("devoluciones", rCancelar);
        Assert.Equal(0, libroV.Reversiones);
        Assert.Equal(antesO, await LibroAsync(e.Origen));
        Assert.NotEqual(EstadoVenta.Cancelada, await EstadoVentaAsync(e.C.Venta.VentaId));
    }

    /// <summary>La cancelación real ya asentó su contrapartida y está por confirmar; la creación de una devolución espera la venta y falla: no hay reembolso ni segunda salida.</summary>
    [SqlServerConcFact(DisplayName = "H10-B13 [B13k] cancelacion real con la contrapartida asentada (cheque acreditado, caja cerrada) + alta de devolucion: la devolucion no se crea")]
    public async Task B13k_CancelacionPrimero_LaDevolucionNoSeCrea()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? cancelar = null, devolver = null;
        bool bloqueada;
        try
        {
            cancelar = CancelarVentaAsync(e.C.Venta.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cancelar), "La cancelación no llegó a insertar la contrapartida.");
            devolver = CrearDevolucionAsync(e.C.Venta, cantidad: 1);
            bloqueada = await QuedaBloqueadaAsync(devolver);
        }
        finally { plan.Release.TrySetResult(); }
        var rCancelar = await ResultadoAsync("cancelación", cancelar!, plan);
        var rDevolver = await ResultadoAsync("devolución", devolver!);

        var libroV = await LibroAsync(e.Vigente);
        var devoluciones = await DevolucionesAsync(e.C.Venta.VentaId);
        Informe("B13k", $"devolución bloqueada = {bloqueada}", $"cancelación={rCancelar}", $"devolución={rDevolver}", $"devoluciones={devoluciones}", $"vigente={libroV}");
        SinFalloDeInfra(rCancelar, rDevolver);
        Assert.True(bloqueada);
        Assert.StartsWith("OK", rCancelar);
        Assert.False(rDevolver.StartsWith("OK"), "No debía crearse una devolución sobre una venta cancelada.");
        Assert.Equal(0, devoluciones);
        Assert.Equal(1, libroV.Reversiones);
        Assert.Equal(12m, await StockAsync(e.C.Venta.ProductoId)); // stock devuelto una sola vez (por la cancelación)
    }

    // =====================================================================================
    // B13-E  Sin caja vigente
    // =====================================================================================

    [SqlServerConcFact(DisplayName = "H10-B13 [B13l] cancelacion REAL de un cheque acreditado de caja cerrada con el usuario SIN caja vigente: rollback completo")]
    public async Task B13l_SinCajaVigente_CancelacionRealHaceRollbackCompleto()
    {
        var e = await SembrarCerradaAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        // El usuario del harness (qaconc) queda sin ninguna caja abierta (las de otros tests y la vigente de este escenario).
        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().AperturasCaja
                .Where(a => a.UsuarioApertura == ConcCurrentUser.Usuario && !a.Cerrada)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.Cerrada, true));
        var antesO = await LibroAsync(e.Origen);
        var antesV = await LibroAsync(e.Vigente);
        var stockAntes = await StockAsync(e.C.Venta.ProductoId);
        int eventosAntes;
        using (var scope = _fx.Services.CreateScope())
            eventosAntes = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SeguridadEventosAuditoria
                .CountAsync(x => x.Accion == "CancelarVenta" || x.Accion == "ReversionChequeCajaCerrada");

        var r = await CancelarVentaAsync(e.C.Venta.VentaId);

        int eventosDespues;
        using (var scope = _fx.Services.CreateScope())
            eventosDespues = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SeguridadEventosAuditoria
                .CountAsync(x => x.Accion == "CancelarVenta" || x.Accion == "ReversionChequeCajaCerrada");
        Informe("B13l", $"cancelación={r}", $"estado venta={await EstadoVentaAsync(e.C.Venta.VentaId)}", $"stock {stockAntes}->{await StockAsync(e.C.Venta.ProductoId)}");
        NoEsFalloDeInfraestructura("cancelación", r);
        Assert.StartsWith("InvalidOperationException", r);
        Assert.Contains("caja abierta", r);
        Assert.Equal(EstadoVenta.Confirmada, await EstadoVentaAsync(e.C.Venta.VentaId));
        Assert.Equal(stockAntes, await StockAsync(e.C.Venta.ProductoId)); // el stock no se devolvió
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, await EstadoChequeAsync(e.C.ChequeId));
        Assert.Equal(antesO, await LibroAsync(e.Origen));
        Assert.Equal(antesV, await LibroAsync(e.Vigente));
        Assert.Equal(eventosAntes, eventosDespues);
    }
}
