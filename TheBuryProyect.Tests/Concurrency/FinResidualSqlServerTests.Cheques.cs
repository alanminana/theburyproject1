using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// H10 - cheque recibido: integridad contable y de locks contra SQL Server real, con las OPERACIONES REALES
// (VentaService.CancelarVentaAsync, DevolucionService.CompletarDevolucionAsync, CierreCaja, CreditoService.PagarCuotaAsync,
// VentaService.CreateAsync) y la compuerta determinista del harness: una operación se detiene EN un comando SQL concreto
// (con sus locks tomados) y la otra se lanza dentro de esa ventana. Se valida el resultado de AMBAS operaciones: un deadlock,
// un timeout o un error inesperado nunca cuentan como rechazo de negocio.
//
// ORDEN GLOBAL DE LOCKS: Crédito → Venta → Apertura del cheque → Apertura vigente; la clave del cheque (sp_getapplock) es una hoja.
// ---------------------------------------------------------------------------------------------
public partial class FinResidualSqlServerTests
{
    private static GatePlan AntesDeLockearApertura() => Compuerta("antes-del-lock-de-apertura", "[AperturasCaja] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)");
    private static GatePlan AntesDeInsertarMovimiento() => Compuerta("antes-de-insertar-movimiento", "INSERT INTO [MovimientosCaja]");

    private sealed record VentaConCheque(VentaContado Venta, int ChequeId);

    /// <summary>Venta Confirmada pagada con cheque ya ACREDITADO (ingreso VentaCheque $100) en una caja abierta del usuario.</summary>
    private async Task<VentaConCheque> NuevaVentaConChequeAcreditadoAsync(int aperturaId)
    {
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false, stock: 10m, tipoPago: TipoPago.Cheque);
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.DatosCheque.Add(new DatosCheque
        {
            VentaId = v.VentaId, NumeroCheque = Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(), Banco = "GALICIA", Titular = "QA",
            FechaEmision = DateTime.UtcNow.Date, FechaVencimiento = DateTime.UtcNow.Date.AddDays(30), Monto = 100m
        });
        var cheque = new MovimientoCaja
        {
            AperturaCajaId = aperturaId, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaCheque,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Acreditado, TipoPago = TipoPago.Cheque, Monto = 100m,
            Descripcion = "Venta " + v.Numero, Referencia = v.Numero, ReferenciaId = v.VentaId, VentaId = v.VentaId,
            Usuario = ConcCurrentUser.Usuario, FechaMovimiento = DateTime.UtcNow
        };
        ctx.MovimientosCaja.Add(cheque);
        await ctx.SaveChangesAsync();
        return new VentaConCheque(v, cheque.Id);
    }

    private Task<string> RechazarChequeAsync(int chequeId, GatePlan? plan = null) =>
        Lanzar(plan, async sp => (await sp.GetRequiredService<IChequeRecibidoService>()
            .RechazarChequeAsync(chequeId, "rebotado", ConcCurrentUser.Usuario)).MovimientoAsentadoId);

    private async Task<(decimal Total, decimal Real, int Egresos, int Reversiones, int Rechazos)> LibroDeChequeAsync(int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movs = await ctx.MovimientosCaja.AsNoTracking().Where(m => m.AperturaCajaId == aperturaId && !m.IsDeleted).ToListAsync();
        return (await caja.CalcularSaldoActualAsync(aperturaId), await caja.CalcularSaldoRealAsync(aperturaId),
            movs.Count(m => m.Tipo == TipoMovimientoCaja.Egreso),
            movs.Count(m => m.Concepto == ConceptoMovimientoCaja.ReversionVenta),
            movs.Count(m => m.Concepto == ConceptoMovimientoCaja.RechazoCheque));
    }

    private static void SinFalloDeInfra(string r1, string r2)
    {
        NoEsFalloDeInfraestructura("operación 1", r1);
        NoEsFalloDeInfraestructura("operación 2", r2);
    }

    // =====================================================================================
    // CHQ-H1  Rechazo vs. cancelación REAL de la venta (orden de locks Venta → Apertura)
    // =====================================================================================

    /// <summary>
    /// La cancelación real ya tiene el lock de la VENTA y se detiene justo antes de pedir la APERTURA. El rechazo corre en esa ventana:
    /// no debe leer la fila de la venta reteniendo la apertura (deadlock con READ COMMITTED sin snapshot) ni quedar bloqueado.
    /// Al liberar, la cancelación ve el cheque rechazado y NO agrega una segunda salida.
    /// </summary>
    [SqlServerConcFact(DisplayName = "H10-CHQ [H1a] cancelacion real con el lock de la venta + rechazo en la ventana: sin deadlock, una sola salida, saldos neutros")]
    public async Task H1a_CancelacionConLockDeVenta_RechazoNoSeBloqueaNiDuplicaLaSalida()
    {
        var aperturaId = await NuevaAperturaAsync();
        var c = await NuevaVentaConChequeAcreditadoAsync(aperturaId);
        var plan = AntesDeLockearApertura();

        Task<string>? cancelar = null, rechazar = null;
        bool bloqueado;
        try
        {
            cancelar = CancelarVentaAsync(c.Venta.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cancelar), "La cancelación no llegó a pedir el lock de la apertura.");
            rechazar = RechazarChequeAsync(c.ChequeId);
            bloqueado = await QuedaBloqueadaAsync(rechazar);
        }
        finally { plan.Release.TrySetResult(); }
        var rCancelar = await ResultadoAsync("cancelación", cancelar!, plan);
        var rRechazar = await ResultadoAsync("rechazo", rechazar!);

        var libro = await LibroDeChequeAsync(aperturaId);
        Informe("H1a", $"rechazo bloqueado por la venta = {bloqueado}", $"cancelación={rCancelar}", $"rechazo={rRechazar}",
            $"total={libro.Total} real={libro.Real} egresos={libro.Egresos} reversiones={libro.Reversiones} rechazos={libro.Rechazos}");
        SinFalloDeInfra(rCancelar, rRechazar);
        Assert.False(bloqueado, "El rechazo quedó esperando la fila de la venta (orden de locks invertido).");
        Assert.StartsWith("OK", rCancelar);
        Assert.StartsWith("OK", rRechazar);
        Assert.Equal(1, libro.Egresos);
        Assert.Equal(0, libro.Reversiones);
        Assert.Equal(0m, libro.Total);
        Assert.Equal(0m, libro.Real);
        Assert.Equal(EstadoVenta.Cancelada, await EstadoVentaAsync(c.Venta.VentaId));
    }

    /// <summary>La cancelación real ya asentó su reversión y está por confirmar; el rechazo (que leyó el cheque Acreditado antes) debe rechazarse bajo el lock.</summary>
    [SqlServerConcFact(DisplayName = "H10-CHQ [H1b] cancelacion real con la reversion asentada + rechazo concurrente: el rechazo se rechaza bajo el lock, una sola salida")]
    public async Task H1b_CancelacionAsentadaPrimero_ElRechazoSeRechazaBajoElLock()
    {
        var aperturaId = await NuevaAperturaAsync();
        var c = await NuevaVentaConChequeAcreditadoAsync(aperturaId);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? cancelar = null, rechazar = null;
        bool bloqueado;
        try
        {
            cancelar = CancelarVentaAsync(c.Venta.VentaId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cancelar), "La cancelación no llegó a insertar la reversión.");
            rechazar = RechazarChequeAsync(c.ChequeId);
            bloqueado = await QuedaBloqueadaAsync(rechazar);
        }
        finally { plan.Release.TrySetResult(); }
        var rCancelar = await ResultadoAsync("cancelación", cancelar!, plan);
        var rRechazar = await ResultadoAsync("rechazo", rechazar!);

        var libro = await LibroDeChequeAsync(aperturaId);
        Informe("H1b", $"rechazo bloqueado por el lock de apertura = {bloqueado}", $"cancelación={rCancelar}", $"rechazo={rRechazar}",
            $"total={libro.Total} real={libro.Real} egresos={libro.Egresos} reversiones={libro.Reversiones} rechazos={libro.Rechazos}");
        SinFalloDeInfra(rCancelar, rRechazar);
        Assert.True(bloqueado, "El rechazo debía esperar el lock de la apertura retenido por la cancelación.");
        Assert.StartsWith("OK", rCancelar);
        Assert.StartsWith("InvalidOperationException", rRechazar);
        Assert.Contains("cancelada", rRechazar);
        Assert.Equal(1, libro.Egresos);
        Assert.Equal(1, libro.Reversiones);
        Assert.Equal(0, libro.Rechazos);
        Assert.Equal(0m, libro.Total);
        Assert.Equal(0m, libro.Real);
    }

    /// <summary>El rechazo tiene la apertura y se detiene antes de insertar su egreso; la cancelación real espera y, al entrar, ve el cheque rechazado.</summary>
    [SqlServerConcFact(DisplayName = "H10-CHQ [H1c] rechazo con el lock de apertura + cancelacion real: la cancelacion espera y no duplica la salida")]
    public async Task H1c_RechazoPrimero_LaCancelacionEsperaYNoDuplicaLaSalida()
    {
        var aperturaId = await NuevaAperturaAsync();
        var c = await NuevaVentaConChequeAcreditadoAsync(aperturaId);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? rechazar = null, cancelar = null;
        bool bloqueada;
        try
        {
            rechazar = RechazarChequeAsync(c.ChequeId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, rechazar), "El rechazo no llegó a insertar su egreso.");
            cancelar = CancelarVentaAsync(c.Venta.VentaId);
            bloqueada = await QuedaBloqueadaAsync(cancelar);
        }
        finally { plan.Release.TrySetResult(); }
        var rRechazar = await ResultadoAsync("rechazo", rechazar!, plan);
        var rCancelar = await ResultadoAsync("cancelación", cancelar!);

        var libro = await LibroDeChequeAsync(aperturaId);
        Informe("H1c", $"cancelación bloqueada = {bloqueada}", $"rechazo={rRechazar}", $"cancelación={rCancelar}",
            $"total={libro.Total} real={libro.Real} egresos={libro.Egresos} reversiones={libro.Reversiones} rechazos={libro.Rechazos}");
        SinFalloDeInfra(rRechazar, rCancelar);
        Assert.True(bloqueada);
        Assert.StartsWith("OK", rRechazar);
        Assert.StartsWith("OK", rCancelar);
        Assert.Equal(1, libro.Egresos);
        Assert.Equal(0, libro.Reversiones);
        Assert.Equal(0m, libro.Total);
        Assert.Equal(0m, libro.Real);
    }

    // =====================================================================================
    // CHQ-H2  Rechazo vs. devolución REAL completada (D4 bajo el mismo lock)
    // =====================================================================================

    [SqlServerConcFact(DisplayName = "H10-CHQ [H2a] rechazo confirmado antes + devolucion real en efectivo: la devolucion se rechaza bajo el lock y no paga")]
    public async Task H2a_RechazoConLockDeApertura_LaDevolucionNoPagaEfectivo()
    {
        var aperturaId = await NuevaAperturaAsync();
        var c = await NuevaVentaConChequeAcreditadoAsync(aperturaId);
        var (devId, rv) = await DevolucionAprobadaConReembolsoAsync(c.Venta, cantidad: 1);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? rechazar = null, devolver = null;
        bool bloqueada;
        try
        {
            rechazar = RechazarChequeAsync(c.ChequeId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, rechazar), "El rechazo no llegó a insertar su egreso.");
            devolver = CompletarDevolucionAsync(devId, rv);
            bloqueada = await QuedaBloqueadaAsync(devolver);
        }
        finally { plan.Release.TrySetResult(); }
        var rRechazar = await ResultadoAsync("rechazo", rechazar!, plan);
        var rDevolver = await ResultadoAsync("devolución", devolver!);

        int reembolsos;
        using (var scope = _fx.Services.CreateScope())
            reembolsos = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosCaja
                .CountAsync(m => m.Concepto == ConceptoMovimientoCaja.DevolucionCliente);
        Informe("H2a", $"devolución bloqueada = {bloqueada}", $"rechazo={rRechazar}", $"devolución={rDevolver}", $"reembolsos en efectivo={reembolsos}");
        SinFalloDeInfra(rRechazar, rDevolver);
        Assert.True(bloqueada, "La devolución debía esperar el lock de la apertura del cheque.");
        Assert.StartsWith("OK", rRechazar);
        Assert.StartsWith("InvalidOperationException", rDevolver);
        Assert.Contains("rechazado", rDevolver);
        Assert.Equal(0, reembolsos);
        Assert.Equal(10m, await StockAsync(c.Venta.ProductoId)); // la devolución hizo rollback: ningún reintegro de stock
    }

    [SqlServerConcFact(DisplayName = "H10-CHQ [H2b] devolucion real con el lock de apertura + rechazo: la devolucion (anterior) paga, el rechazo espera y despues contrapesa")]
    public async Task H2b_DevolucionPrimero_ElRechazoEsperaYAmbosSeSerializan()
    {
        var aperturaId = await NuevaAperturaAsync();
        var c = await NuevaVentaConChequeAcreditadoAsync(aperturaId);
        var (devId, rv) = await DevolucionAprobadaConReembolsoAsync(c.Venta, cantidad: 1);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? devolver = null, rechazar = null;
        bool bloqueado;
        try
        {
            devolver = CompletarDevolucionAsync(devId, rv, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, devolver), "La devolución no llegó a insertar el reembolso.");
            rechazar = RechazarChequeAsync(c.ChequeId);
            bloqueado = await QuedaBloqueadaAsync(rechazar);
        }
        finally { plan.Release.TrySetResult(); }
        var rDevolver = await ResultadoAsync("devolución", devolver!, plan);
        var rRechazar = await ResultadoAsync("rechazo", rechazar!);

        Informe("H2b", $"rechazo bloqueado = {bloqueado}", $"devolución={rDevolver}", $"rechazo={rRechazar}");
        SinFalloDeInfra(rDevolver, rRechazar);
        Assert.True(bloqueado);
        Assert.StartsWith("OK", rDevolver);
        Assert.StartsWith("OK", rRechazar);
    }

    // =====================================================================================
    // CHQ-H3  Rechazo vs. cierre de caja
    // =====================================================================================

    [SqlServerConcFact(DisplayName = "H10-CHQ [H3] rechazo con el lock de apertura + cierre de caja real: el cierre espera, sin deadlock, libro en efectivo = cierre")]
    public async Task H3_RechazoVsCierre_SinDeadlock_LibroEnEfectivoIgualAlCierre()
    {
        var aperturaId = await NuevaAperturaAsync();
        var c = await NuevaVentaConChequeAcreditadoAsync(aperturaId);
        var plan = AntesDeInsertarMovimiento();

        Task<string>? rechazar = null, cerrar = null;
        bool bloqueado;
        try
        {
            rechazar = RechazarChequeAsync(c.ChequeId, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, rechazar), "El rechazo no llegó a insertar su egreso.");
            cerrar = CerrarAsync(aperturaId, efectivo: 0m);
            bloqueado = await QuedaBloqueadaAsync(cerrar);
        }
        finally { plan.Release.TrySetResult(); }
        var rRechazar = await ResultadoAsync("rechazo", rechazar!, plan);
        var rCerrar = await ResultadoAsync("cierre", cerrar!);

        var viol = await ViolacionesCierreAsync(aperturaId);
        Informe("H3", $"cierre bloqueado = {bloqueado}", $"rechazo={rRechazar}", $"cierre={rCerrar}", $"violaciones={viol.Count}");
        SinFalloDeInfra(rRechazar, rCerrar);
        Assert.True(bloqueado);
        Assert.StartsWith("OK", rRechazar);
        Assert.StartsWith("OK", rCerrar);
        Assert.Empty(viol);
    }

    // =====================================================================================
    // CHQ-H4  Un mismo cheque no puede pagar dos cuotas de créditos distintos a la vez (lock de aplicación por clave)
    // =====================================================================================

    private Task<string> PagarCuotaConChequeAsync(EscenarioSembrado s, string banco, string numero, GatePlan? plan = null) =>
        Lanzar(plan, async sp =>
        {
            var ctx = sp.GetRequiredService<AppDbContext>();
            var cuota = await ctx.Cuotas.AsNoTracking().FirstAsync(c => c.Id == s.CuotaIds[0]);
            return await sp.GetRequiredService<ICreditoService>().PagarCuotaAsync(new PagarCuotaViewModel
            {
                CreditoId = s.CreditoId, CuotaId = cuota.Id, MontoPagado = 100m, MedioPago = "Cheque",
                Cheque = new TheBuryProject.Services.Models.DatosChequeCuota(banco, numero, DateTime.UtcNow.Date.AddDays(15))
            });
        });

    [SqlServerConcFact(DisplayName = "H10-CHQ [H4a] dos cobros de cuota (creditos distintos) con el mismo cheque a la vez: gana uno, el otro espera la clave y se rechaza")]
    public async Task H4a_MismoChequeEnDosCuotas_UnoSoloGana()
    {
        var aperturaId = await NuevaAperturaAsync();
        var s1 = await NuevoCreditoAsync(aperturaId);
        var s2 = await NuevoCreditoAsync(aperturaId);
        var numero = Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();
        var plan = Compuerta("cobro/antes-de-insertar-pago", "INSERT INTO [PagosCuota]");

        Task<string>? primero = null, segundo = null;
        bool bloqueado;
        try
        {
            primero = PagarCuotaConChequeAsync(s1, "Galicia", numero, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, primero), "El primer cobro no llegó a insertar el pago.");
            segundo = PagarCuotaConChequeAsync(s2, "  gALICIA ", numero); // misma clave normalizada
            bloqueado = await QuedaBloqueadaAsync(segundo);
        }
        finally { plan.Release.TrySetResult(); }
        var r1 = await ResultadoAsync("primer cobro", primero!, plan);
        var r2 = await ResultadoAsync("segundo cobro", segundo!);

        int cobros;
        using (var scope = _fx.Services.CreateScope())
            cobros = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosCaja
                .CountAsync(m => m.Concepto == ConceptoMovimientoCaja.CobroCuota && m.TipoPago == TipoPago.Cheque
                              && m.MedioPagoDetalle != null && m.MedioPagoDetalle.Contains(numero));
        Informe("H4a", $"segundo bloqueado por la clave = {bloqueado}", $"primero={r1}", $"segundo={r2}", $"ingresos con ese cheque={cobros}");
        SinFalloDeInfra(r1, r2);
        Assert.True(bloqueado, "El segundo cobro debía esperar el lock de aplicación del cheque.");
        Assert.StartsWith("OK", r1);
        Assert.Contains("ya se usó para cobrar una cuota", r2);
        Assert.Equal(1, cobros);
    }

    private Task<string> CrearVentaConChequeAsync(VentaContado ref0, string banco, string numero, GatePlan? plan = null) =>
        Lanzar(plan, async sp =>
        {
            var vm = new VentaViewModel
            {
                IdempotencyKey = Guid.NewGuid(), ClienteId = ref0.ClienteId, FechaVenta = DateTime.UtcNow, TipoPago = TipoPago.Cheque,
                Detalles = new List<VentaDetalleViewModel>
                {
                    new() { ProductoId = ref0.ProductoId, Cantidad = 1, PrecioUnitario = 50m, Subtotal = 50m }
                },
                DatosCheque = new DatosChequeViewModel
                {
                    NumeroCheque = numero, Banco = banco, Titular = "QA", FechaEmision = DateTime.UtcNow.Date,
                    FechaVencimiento = DateTime.UtcNow.Date.AddDays(30), Monto = 50m
                }
            };
            try { return (await sp.GetRequiredService<IVentaService>().CreateAsync(vm)).Numero; }
            catch (DbUpdateException ex) { throw new InvalidOperationException("DbUpdate: " + ex.InnerException?.Message, ex); }
        });

    [SqlServerConcFact(DisplayName = "H10-CHQ [H4b] dos altas de venta con el mismo cheque a la vez (normalizado): gana una, la otra espera la clave y se rechaza")]
    public async Task H4b_MismoChequeEnDosVentas_UnaSolaGana()
    {
        var aperturaId = await NuevaAperturaAsync();
        var v = await NuevaVentaContadoAsync(aperturaId, conIngreso: false);
        using (var scope = _fx.Services.CreateScope())
        {
            // La alta resuelve el vendedor = usuario actual (qaconc-id): debe existir (FK Ventas.VendedorUserId).
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await ctx.Users.AnyAsync(u => u.Id == "qaconc-id"))
            {
                ctx.Users.Add(new ApplicationUser
                {
                    Id = "qaconc-id", UserName = "qaconc", NormalizedUserName = "QACONC", Email = "qaconc@example.invalid",
                    NormalizedEmail = "QACONC@EXAMPLE.INVALID", Activo = true, RowVersion = new byte[8]
                });
                await ctx.SaveChangesAsync();
            }
        }
        var numero = Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();
        var plan = Compuerta("alta/antes-de-insertar-cheque", "INSERT INTO [DatosCheque]");

        Task<string>? primera = null, segunda = null;
        bool bloqueada;
        try
        {
            primera = CrearVentaConChequeAsync(v, "Banco Galicia", numero, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, primera), $"La primera alta no llegó a insertar el cheque: {await primera.WaitAsync(TimeSpan.FromSeconds(1)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : "(sin resultado)")}");
            segunda = CrearVentaConChequeAsync(v, " banco   galicia ", numero);
            bloqueada = await QuedaBloqueadaAsync(segunda);
        }
        finally { plan.Release.TrySetResult(); }
        var r1 = await ResultadoAsync("primera alta", primera!, plan);
        var r2 = await ResultadoAsync("segunda alta", segunda!);

        int cheques;
        using (var scope = _fx.Services.CreateScope())
            cheques = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DatosCheque.CountAsync(d => d.NumeroCheque == numero && !d.IsDeleted);
        Informe("H4b", $"segunda bloqueada por la clave = {bloqueada}", $"primera={r1}", $"segunda={r2}", $"cheques guardados={cheques}");
        SinFalloDeInfra(r1, r2);
        Assert.True(bloqueada, "La segunda alta debía esperar el lock de aplicación del cheque.");
        Assert.StartsWith("OK", r1);
        Assert.Contains("ya está registrado en la venta", r2);
        Assert.Equal(1, cheques);
    }
}
