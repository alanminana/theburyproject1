using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// FIN-CAJA-4-P0 (H-CAJA-4). Cobro de cuota vs. cierre de caja: el deadlock 1205 que dejaba al CIERRE como víctima.
//
// Causa raíz (grafo de deadlock real, ver docs/qa-fin-residual-registro-defectos.md): el cobro corre en SERIALIZABLE y,
// ya dentro de la transacción, CajaService.RegistrarMovimientoCuotaAsync volvía a consultar "la apertura activa del
// usuario" (SELECT sin hints sobre AperturasCaja) => RangeS-S retenido. Después pedía el UPDLOCK de la apertura
// (conversión a U); el cierre ya tenía U y necesitaba X para marcarla Cerrada: ciclo U/X vs. RangeS-S/U.
// Corrección: el llamador pasa el id de la apertura que ya resolvió ANTES de abrir la transacción; el servicio toma el
// UPDLOCK directo (sin lectura previa) y revalida estado/vigencia/propietario desde la base bajo el lock.
//
// Mapeo de los escenarios CAJA-4-xx de la tarea:
//   CAJA-4-01  reproducción del deadlock sobre el código previo: R-01 en rojo (DEADLOCK_VICTIMA del cierre), medido.
//   CAJA-4-02  R-01  (cierre primero, el cobro espera y se rechaza)             -> FinResidualSqlServerTests.R01_
//   CAJA-4-03  R-02  (cobro primero, el cierre espera e incorpora el movimiento) -> FinResidualSqlServerTests.R02_
//   CAJA-4-04..06, 08..10  C4_04..C4_10 de este archivo
//   CAJA-4-07  cobro vs. cancelación de crédito/venta                             -> CancelarVsPagoServiciosRealesSqlServerTests
// ---------------------------------------------------------------------------------------------
public partial class FinResidualSqlServerTests
{
    private sealed record FotoCredito(
        int Pagos, int CobrosEnCaja, decimal MontoPagadoTotal, int CuotasPagadas, decimal SaldoCredito);

    private async Task<FotoCredito> FotoCreditoAsync(EscenarioSembrado s, int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cuotas = await ctx.Cuotas.AsNoTracking().Where(c => c.CreditoId == s.CreditoId).ToListAsync();
        var ids = cuotas.Select(c => c.Id).ToList();
        var pagos = await ctx.PagosCuota.AsNoTracking().CountAsync(p => ids.Contains(p.CuotaId) && !p.IsDeleted);
        var cobros = await ctx.MovimientosCaja.AsNoTracking().CountAsync(m =>
            m.AperturaCajaId == aperturaId && m.Concepto == ConceptoMovimientoCaja.CobroCuota && !m.IsDeleted);
        var saldo = await ctx.Creditos.AsNoTracking().Where(c => c.Id == s.CreditoId).Select(c => c.SaldoPendiente).FirstAsync();
        return new FotoCredito(pagos, cobros, cuotas.Sum(c => c.MontoPagado),
            cuotas.Count(c => c.Estado == EstadoCuota.Pagada), saldo);
    }

    private async Task<CierreCaja> CierreDeAsync(int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().CierresCaja.AsNoTracking()
            .FirstAsync(c => c.AperturaCajaId == aperturaId);
    }

    private Task<string> PagarMultipleAsync(EscenarioSembrado s, int cantidad, GatePlan? plan = null) =>
        Lanzar(plan, async sp =>
        {
            var ctx = sp.GetRequiredService<AppDbContext>();
            var ids = s.CuotaIds.Take(cantidad).ToList();
            var cuotas = await ctx.Cuotas.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
            var r = await sp.GetRequiredService<ICreditoService>().PagarCuotasAsync(new PagoMultipleCuotasRequest
            {
                ClienteId = s.ClienteId,
                CuotaIds = ids,
                RowVersionsPorCuota = cuotas.ToDictionary(c => c.Id, c => Convert.ToBase64String(c.RowVersion)),
                MedioPago = "Efectivo"
            });
            return r.GetType().Name;
        });

    private Task<string> AdelantarUltimaCuotaAsync(EscenarioSembrado s, GatePlan? plan = null) =>
        Lanzar(plan, async sp =>
            await sp.GetRequiredService<ICreditoService>().AdelantarCuotaAsync(new PagarCuotaViewModel
            {
                CreditoId = s.CreditoId, CuotaId = s.CuotaIds[^1], MontoPagado = 300m, MedioPago = "Efectivo"
            }));

    /// <summary>
    /// El cierre gana: ya tomó el lock de la apertura y está detenido antes de insertar CierreCaja. El cobro (cualquier
    /// variante) debe quedar ESPERANDO, y al liberar el cierre rechazarse completo: cero pagos, cero movimientos, cuotas
    /// y saldo intactos, cierre con totales en cero y sin diferencia. Un deadlock o un timeout nunca cuentan.
    /// </summary>
    private async Task CierrePrimero_CobroEsperaYSeRechazaAsync(
        string escenario, Func<EscenarioSembrado, Task<string>> lanzarCobro)
    {
        var aperturaId = await NuevaAperturaAsync();
        var s = await NuevoCreditoAsync(aperturaId);
        var antes = await FotoCreditoAsync(s, aperturaId);
        var plan = CierreAntesDeInsertar();

        Task<string>? cierre = null, cobro = null;
        bool bloqueado;
        try
        {
            cierre = CerrarAsync(aperturaId, efectivo: 0m, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cierre), "El cierre no llegó a la compuerta.");
            cobro = lanzarCobro(s);
            bloqueado = await QuedaBloqueadaAsync(cobro);
        }
        finally
        {
            plan.Release.TrySetResult();
        }

        var resCierre = await ResultadoAsync("cierre", cierre!, plan);
        var resCobro = await ResultadoAsync("cobro", cobro!);
        var despues = await FotoCreditoAsync(s, aperturaId);
        var foto = await FotoAperturaAsync(aperturaId);
        var viol = await ViolacionesCierreAsync(aperturaId);
        var cierreFila = await CierreDeAsync(aperturaId);

        Informe(escenario, $"cobro bloqueado por el lock del cierre = {bloqueado}", $"cierre={resCierre}", $"cobro={resCobro}",
            $"antes={antes}", $"despues={despues}",
            $"apertura cerrada={foto.cerrada} cierres={foto.cierres}; cierre: ingresos={cierreFila.TotalIngresosSistema} diferencia={cierreFila.Diferencia}",
            "invariante I-CAJA: " + (viol.Count == 0 ? "OK" : string.Join(" | ", viol)));

        Assert.True(bloqueado, "El cobro no esperó el lock de la apertura tomado por el cierre.");
        NoEsFalloDeInfraestructura("cobro", resCobro);
        NoEsFalloDeInfraestructura("cierre", resCierre);
        Assert.StartsWith("OK(", resCierre);
        Assert.False(resCobro.StartsWith("OK(True", StringComparison.Ordinal), $"El cobro confirmó sobre una caja cerrada: {resCobro}");
        Assert.True(foto.cerrada && foto.cierres == 1 && foto.estadoCaja == EstadoCaja.Cerrada);
        Assert.Empty(viol);
        Assert.Equal(antes, despues);                                  // rollback completo: nada parcial
        Assert.Equal(0, despues.Pagos);
        Assert.Equal(0, despues.CobrosEnCaja);
        Assert.Equal(0, despues.CuotasPagadas);
        Assert.Equal(0m, cierreFila.TotalIngresosSistema);
        Assert.Equal(0m, cierreFila.Diferencia);
    }

    /// <summary>
    /// El cobro gana: está en vuelo (cuota, movimiento de caja ya escritos bajo el lock de la apertura; detenido antes de
    /// insertar PagoCuota). El cierre debe esperar, y al confirmarse el cobro incorporarlo: totales definitivos exactos y
    /// diferencia cero con el efectivo contado igual al cobrado.
    /// </summary>
    private async Task CobroPrimero_CierreEsperaEIncorporaAsync(
        string escenario, Func<EscenarioSembrado, GatePlan, Task<string>> lanzarCobro, int cuotasEsperadas)
    {
        var aperturaId = await NuevaAperturaAsync();
        var s = await NuevoCreditoAsync(aperturaId);
        var plan = CobroEnVuelo();
        var cobrado = 300m * cuotasEsperadas;

        Task<string>? cobro = null, cierre = null;
        bool bloqueado;
        try
        {
            cobro = lanzarCobro(s, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cobro), "El cobro no llegó a la compuerta.");
            cierre = CerrarAsync(aperturaId, efectivo: cobrado);
            bloqueado = await QuedaBloqueadaAsync(cierre);
        }
        finally
        {
            plan.Release.TrySetResult();
        }

        var resCobro = await ResultadoAsync("cobro", cobro!, plan);
        var resCierre = await ResultadoAsync("cierre", cierre!, plan);
        var despues = await FotoCreditoAsync(s, aperturaId);
        var foto = await FotoAperturaAsync(aperturaId);
        var viol = await ViolacionesCierreAsync(aperturaId);
        var cierreFila = await CierreDeAsync(aperturaId);

        Informe(escenario, $"cierre bloqueado por el cobro en vuelo = {bloqueado}", $"cobro={resCobro}", $"cierre={resCierre}",
            $"despues={despues}",
            $"cierre: ingresos={cierreFila.TotalIngresosSistema} esperado={cierreFila.MontoEsperadoSistema} diferencia={cierreFila.Diferencia}",
            "invariante I-CAJA: " + (viol.Count == 0 ? "OK" : string.Join(" | ", viol)));

        Assert.True(bloqueado, "El cierre no esperó el lock de la apertura tomado por el cobro en vuelo.");
        NoEsFalloDeInfraestructura("cobro", resCobro);
        NoEsFalloDeInfraestructura("cierre", resCierre);
        Assert.StartsWith("OK(", resCobro);
        Assert.StartsWith("OK(", resCierre);
        Assert.Empty(viol);
        Assert.True(foto.cerrada && foto.cierres == 1 && foto.estadoCaja == EstadoCaja.Cerrada);
        Assert.Equal(cobrado, cierreFila.TotalIngresosSistema);        // el arqueo incorporó todo lo cobrado
        Assert.Equal(cobrado, cierreFila.MontoEsperadoSistema);
        Assert.Equal(0m, cierreFila.Diferencia);
        Assert.False(cierreFila.TieneDiferencia);
        Assert.Equal(cuotasEsperadas, despues.Pagos);
        Assert.Equal(cuotasEsperadas, despues.CobrosEnCaja);
        Assert.Equal(cuotasEsperadas, despues.CuotasPagadas);
        Assert.Equal(cobrado, despues.MontoPagadoTotal);
        Assert.Equal(900m - cobrado, despues.SaldoCredito);
    }

    // ------------------------------------------------------------------ CAJA-4-04

    /// <summary>
    /// CAJA-4-04. Dos cobros de la MISMA cuota: el primero queda en vuelo con su lock de crédito; el segundo debe esperar
    /// (lock del crédito, anterior a la apertura), y al confirmarse el primero ser rechazado. Un solo pago, un solo
    /// movimiento de caja, sin deadlock.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-04]: DosCobrosConcurrentesDeLaMismaCuota_UnSoloPago")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task C4_04_DosCobrosConcurrentesDeLaMismaCuota_NoDuplicanElPago()
    {
        var aperturaId = await NuevaAperturaAsync();
        var s = await NuevoCreditoAsync(aperturaId);
        var plan = CobroEnVuelo();

        Task<string>? a = null, b = null;
        bool bloqueado;
        try
        {
            a = CobrarCuotaAsync(s, 0, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, a), "El primer cobro no llegó a la compuerta.");
            b = CobrarCuotaAsync(s, 0);
            bloqueado = await QuedaBloqueadaAsync(b);
        }
        finally
        {
            plan.Release.TrySetResult();
        }

        var resA = await ResultadoAsync("cobro A", a!, plan);
        var resB = await ResultadoAsync("cobro B", b!);
        var despues = await FotoCreditoAsync(s, aperturaId);

        Informe("CAJA-4-04", $"cobro B bloqueado = {bloqueado}", $"A={resA}", $"B={resB}", $"despues={despues}");

        Assert.True(bloqueado, "El segundo cobro no esperó al primero.");
        NoEsFalloDeInfraestructura("A", resA);
        NoEsFalloDeInfraestructura("B", resB);
        Assert.Equal("OK(True)", resA);
        Assert.False(resB.StartsWith("OK(True", StringComparison.Ordinal), $"El segundo cobro de la misma cuota confirmó: {resB}");
        Assert.Equal(1, despues.Pagos);
        Assert.Equal(1, despues.CobrosEnCaja);
        Assert.Equal(300m, despues.MontoPagadoTotal);
        Assert.Equal(600m, despues.SaldoCredito);
    }

    // ------------------------------------------------------------------ CAJA-4-05

    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-05a]: PagoDeVariasCuotas_CierrePrimero_SeRechazaSinParciales")]
    [Trait("Categoria", "SqlServerRegresion")]
    public Task C4_05a_PagoDeVariasCuotasVsCierre_CierrePrimero() =>
        CierrePrimero_CobroEsperaYSeRechazaAsync("CAJA-4-05a", s => PagarMultipleAsync(s, 2));

    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-05b]: PagoDeVariasCuotas_CobroPrimero_ElCierreIncorpora")]
    [Trait("Categoria", "SqlServerRegresion")]
    public Task C4_05b_PagoDeVariasCuotasVsCierre_CobroPrimero() =>
        CobroPrimero_CierreEsperaEIncorporaAsync("CAJA-4-05b", (s, plan) => PagarMultipleAsync(s, 2, plan), cuotasEsperadas: 2);

    // ------------------------------------------------------------------ CAJA-4-06

    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-06a]: AdelantoDeCuota_CierrePrimero_SeRechazaSinParciales")]
    [Trait("Categoria", "SqlServerRegresion")]
    public Task C4_06a_AdelantoVsCierre_CierrePrimero() =>
        CierrePrimero_CobroEsperaYSeRechazaAsync("CAJA-4-06a", s => AdelantarUltimaCuotaAsync(s));

    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-06b]: AdelantoDeCuota_CobroPrimero_ElCierreIncorpora")]
    [Trait("Categoria", "SqlServerRegresion")]
    public Task C4_06b_AdelantoVsCierre_CobroPrimero() =>
        CobroPrimero_CierreEsperaEIncorporaAsync("CAJA-4-06b", (s, plan) => AdelantarUltimaCuotaAsync(s, plan), cuotasEsperadas: 1);

    // ------------------------------------------------------------------ CAJA-4-08

    /// <summary>
    /// CAJA-4-08. La apertura se cierra DESPUÉS de que el cobro la seleccionó (candidata previa a la transacción) y ANTES
    /// de que adquiera el lock. El cobro queda detenido justo antes de su primer lock (el del crédito); el cierre corre
    /// completo; al liberar, el cobro revalida bajo lock desde la base y se rechaza sin efectos. No usa otra caja.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-08]: AperturaCerradaTrasLaSeleccionPrevia_SeRechazaElCobro")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task C4_08_AperturaQueSeCierraDespuesDeLaSeleccionPrevia_RechazaElCobro()
    {
        var aperturaId = await NuevaAperturaAsync();
        var s = await NuevoCreditoAsync(aperturaId);
        var antes = await FotoCreditoAsync(s, aperturaId);
        var plan = Compuerta("cobro/antes-del-lock-de-credito", "FROM [Creditos] WITH (UPDLOCK");

        Task<string>? cobro = null;
        string resCierre;
        try
        {
            cobro = CobrarCuotaAsync(s, 0, plan);
            Assert.True(await LlegoALaCompuertaAsync(plan, cobro), "El cobro no llegó a la compuerta previa a sus locks.");
            resCierre = await ResultadoAsync("cierre", CerrarAsync(aperturaId, efectivo: 0m));   // cierra con el cobro "detenido"
        }
        finally
        {
            plan.Release.TrySetResult();
        }

        var resCobro = await ResultadoAsync("cobro", cobro!, plan);
        var despues = await FotoCreditoAsync(s, aperturaId);
        var foto = await FotoAperturaAsync(aperturaId);
        var viol = await ViolacionesCierreAsync(aperturaId);
        int cobrosEnOtrasCajas;
        using (var scope = _fx.Services.CreateScope())
            cobrosEnOtrasCajas = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosCaja
                .AsNoTracking().CountAsync(m => m.Concepto == ConceptoMovimientoCaja.CobroCuota
                                                && m.ReferenciaId != null && s.CuotaIds.Contains(m.ReferenciaId.Value));

        Informe("CAJA-4-08", $"cierre={resCierre}", $"cobro={resCobro}", $"antes={antes}", $"despues={despues}",
            $"apertura cerrada={foto.cerrada} cierres={foto.cierres}", $"cobros de sus cuotas en cualquier caja={cobrosEnOtrasCajas}");

        NoEsFalloDeInfraestructura("cobro", resCobro);
        NoEsFalloDeInfraestructura("cierre", resCierre);
        Assert.StartsWith("OK(", resCierre);
        Assert.False(resCobro.StartsWith("OK(True", StringComparison.Ordinal), $"El cobro confirmó sobre una caja cerrada: {resCobro}");
        Assert.True(foto.cerrada && foto.cierres == 1);
        Assert.Empty(viol);
        Assert.Equal(antes, despues);
        Assert.Equal(0, cobrosEnOtrasCajas);
    }

    // ------------------------------------------------------------------ CAJA-4-09

    /// <summary>
    /// CAJA-4-09. El id de apertura es solo un candidato: una apertura abierta de OTRO usuario no recibe el movimiento
    /// (el servicio exige propietario bajo el lock), y tampoco se elige otra caja en su lugar.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-09]: AperturaDeOtroUsuario_NoRecibeElMovimiento")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task C4_09_AperturaDeOtroUsuario_NoRecibeElCobro()
    {
        int aperturaPropia, aperturaAjena;
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            aperturaPropia = await ConcSeed.SembrarAperturaAsync(ctx);
            aperturaAjena = await ConcSeed.SembrarAperturaAsync(ctx, "otro.usuario");
        }

        string res;
        using (var scope = _fx.Services.CreateScope())
        {
            try
            {
                var m = await scope.ServiceProvider.GetRequiredService<ICajaService>().RegistrarMovimientoCuotaAsync(
                    cuotaId: 1, creditoNumero: "CR-C4-09", numeroCuota: 1, montoBase: 100m, recargoMedioPago: 0m,
                    tipoPago: null, medioPago: "Efectivo", usuario: ConcCurrentUser.Usuario, aperturaIdCandidata: aperturaAjena);
                res = $"OK({m?.Id})";
            }
            catch (Exception ex)
            {
                res = Clasificar(ex);
            }
        }

        int enAjena, enPropia;
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            enAjena = await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.AperturaCajaId == aperturaAjena);
            enPropia = await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.AperturaCajaId == aperturaPropia);
        }

        Informe("CAJA-4-09", $"resultado={res}", $"movimientos en caja ajena={enAjena}", $"movimientos en caja propia={enPropia}");

        NoEsFalloDeInfraestructura("registro", res);
        Assert.Contains("no pertenece", res, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, enAjena);
        Assert.Equal(0, enPropia);
    }

    // ------------------------------------------------------------------ CAJA-4-10

    /// <summary>
    /// CAJA-4-10. Regresión de diferencias y totales del cierre en ambos órdenes con un cobro individual:
    /// cierre primero => ingresos 0 y diferencia 0 (el cobro rechazado no suma); cobro primero => ingresos 300 y
    /// diferencia 0 con el efectivo contado igual al cobrado; y con un faltante el cierre exige y conserva la diferencia.
    /// </summary>
    [SqlServerConcFact(DisplayName = "SQL SERVER EFIMERO [CAJA-4-10]: TotalesYDiferenciaDelCierre_EnAmbosOrdenes")]
    [Trait("Categoria", "SqlServerRegresion")]
    public async Task C4_10_TotalesYDiferenciaDelCierre_NoCambian()
    {
        await CierrePrimero_CobroEsperaYSeRechazaAsync("CAJA-4-10/cierre-primero", s => CobrarCuotaAsync(s, 0));
        await CobroPrimero_CierreEsperaEIncorporaAsync("CAJA-4-10/cobro-primero", (s, plan) => CobrarCuotaAsync(s, 0, plan), cuotasEsperadas: 1);

        // Con faltante: cobro de 300 y efectivo contado 250 => diferencia -50, justificada y conservada.
        var aperturaId = await NuevaAperturaAsync();
        var s2 = await NuevoCreditoAsync(aperturaId);
        var resCobro = await ResultadoAsync("cobro", CobrarCuotaAsync(s2, 0));
        var resCierre = await ResultadoAsync("cierre", CerrarAsync(aperturaId, efectivo: 250m));
        var fila = await CierreDeAsync(aperturaId);
        Informe("CAJA-4-10/faltante", $"cobro={resCobro}", $"cierre={resCierre}",
            $"ingresos={fila.TotalIngresosSistema} esperado={fila.MontoEsperadoSistema} contado={fila.EfectivoContado} diferencia={fila.Diferencia}");
        Assert.Equal("OK(True)", resCobro);
        Assert.StartsWith("OK(", resCierre);
        Assert.Equal(300m, fila.TotalIngresosSistema);
        Assert.Equal(-50m, fila.Diferencia);
        Assert.True(fila.TieneDiferencia);
        Assert.Empty(await ViolacionesCierreAsync(aperturaId));
    }
}
