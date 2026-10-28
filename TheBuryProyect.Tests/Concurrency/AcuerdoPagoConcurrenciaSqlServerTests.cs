using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

/// <summary>Catálogo propio de esta clase para poder correr en paralelo con las demás de concurrencia.</summary>
public sealed class AcuerdoSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_acu";
}

/// <summary>
/// Acuerdos de pago con deuda congelada sobre SQL Server REAL (migraciones aplicadas): activación, aplicación de punitorios,
/// cobros directos y pagos del acuerdo corriendo a la vez. Invariante: la deuda acordada y la deuda del libro nunca se
/// desincronizan, y nada se cobra dos veces. Se saltea sin QA_RELEASE_TURNO / QA_FIN_CONC_SQL (ver ConcEnv).
/// </summary>
public class AcuerdoPagoConcurrenciaSqlServerTests : IClassFixture<AcuerdoSqlHostFixture>
{
    private const int Intentos = 4;
    private readonly AcuerdoSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public AcuerdoPagoConcurrenciaSqlServerTests(AcuerdoSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private async Task<EscenarioSembrado> SembrarVencidoAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
        var s = await ConcSeed.SembrarAsync(ctx, _fx.AperturaCajaId, false);
        var cuotas = await ctx.Cuotas.Where(c => s.CuotaIds.Contains(c.Id)).OrderBy(c => c.NumeroCuota).ToListAsync();
        for (var i = 0; i < cuotas.Count; i++)
            cuotas[i].FechaVencimiento = reloj.InicioDiaComercial.AddDays(-30 + 10 * i);
        if (!await ctx.ConfiguracionesPunitorio.AnyAsync())
            ctx.ConfiguracionesPunitorio.Add(new ConfiguracionPunitorio
            {
                Porcentaje = 10m, PeriodoDias = 20, DiasGracia = 5, VigenteDesde = new DateOnly(2020, 1, 1), Activa = true, ProrrateoDiario = true
            });
        await ctx.SaveChangesAsync();
        return s;
    }

    private static CrearAcuerdoViewModel Solicitud(EscenarioSembrado s, DateTime hoy, decimal entrega = 100m) => new()
    {
        ClienteId = s.ClienteId, CreditoId = s.CreditoId, MontoEntregaInicial = entrega, CantidadCuotas = 2, FechaPrimeraCuota = hoy.AddDays(30)
    };

    private async Task<int> CrearBorradorAsync(EscenarioSembrado s, decimal entrega = 100m)
    {
        using var scope = _fx.Services.CreateScope();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
        return await scope.ServiceProvider.GetRequiredService<IMoraService>()
            .CrearAcuerdoPagoParaCreditoAsync(Solicitud(s, reloj.InicioDiaComercial, entrega), "g");
    }

    // Cada operación en su propio scope (propio DbContext y conexión), lanzada en paralelo de verdad.
    private static async Task<(bool Ok, string Detalle)> EjecutarAsync(IServiceProvider raiz, Func<IServiceProvider, Task> operacion)
    {
        await Task.Yield();
        try
        {
            using var scope = raiz.CreateScope();
            await operacion(scope.ServiceProvider);
            return (true, "OK");
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private Task<(bool Ok, string Detalle)> Activar(int acuerdoId, int clienteId) =>
        EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<IMoraService>().CambiarEstadoAcuerdoAsync(acuerdoId, clienteId, EstadoAcuerdo.Activo));

    private Task<(bool Ok, string Detalle)> AplicarPunitorio(int cuotaId) =>
        EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<IPunitorioService>()
            .AplicarAsync(cuotaId, new PunitorioAplicarComando { Motivo = "concurrencia" }));

    private Task<(bool Ok, string Detalle)> CobroDirecto(EscenarioSembrado s, int cuotaId, decimal monto = 50m) =>
        EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<ICreditoService>().PagarCuotaAsync(
            new PagarCuotaViewModel { CreditoId = s.CreditoId, CuotaId = cuotaId, MontoPagado = monto, MedioPago = "Efectivo" }));

    private async Task<AcuerdoPago> LeerAcuerdoAsync(int acuerdoId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == acuerdoId);
    }

    [SqlServerConcFact]
    public async Task Migracion_CreaLaTablaDeCuotasCubiertas_YLaAlertaEsOpcional()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tabla = await ctx.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'AcuerdoPagoCuotasCubiertas'").SingleAsync();
        var nulable = await ctx.Database.SqlQueryRaw<int>(
            "SELECT CAST(is_nullable AS int) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('AcuerdosPago') AND name = 'AlertaCobranzaId'").SingleAsync();
        Assert.Equal(1, tabla);
        Assert.Equal(1, nulable);
    }

    // Activar a la vez que se aplica un punitorio: o gana el acuerdo (y el punitorio se rechaza) o gana el punitorio (y la
    // activación detecta que la deuda cambió). Nunca quedan las dos cosas: eso sería un acuerdo activo con deuda desfasada.
    [SqlServerConcFact]
    public async Task ActivarVsAplicarPunitorio_NuncaQuedaUnAcuerdoActivoConLaDeudaDesfasada()
    {
        for (var i = 0; i < Intentos; i++)
        {
            var s = await SembrarVencidoAsync();
            var acuerdoId = await CrearBorradorAsync(s);

            var activar = Activar(acuerdoId, s.ClienteId);
            var aplicar = AplicarPunitorio(s.CuotaIds[0]);
            await Task.WhenAll(activar, aplicar);
            _out.WriteLine($"[{i}] activar={activar.Result.Detalle} | aplicar={aplicar.Result.Detalle}");

            Assert.False(activar.Result.Ok && aplicar.Result.Ok, "Activación y aplicación de punitorio no pueden ganar las dos.");
            using var scope = _fx.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var acuerdo = await ctx.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == acuerdoId);
            var cubiertas = await ctx.AcuerdoPagoCuotasCubiertas.AsNoTracking().Where(c => c.AcuerdoPagoId == acuerdoId).ToListAsync();
            if (acuerdo.Estado == EstadoAcuerdo.Activo)
            {
                // Lo congelado coincide con la deuda real del libro.
                var deuda = await scope.ServiceProvider.GetRequiredService<IMoraService>().CalcularDesgloseDeudaCobrableCreditoAsync(s.CreditoId);
                Assert.Equal(acuerdo.MontoTotalAcuerdo, cubiertas.Sum(c => c.CapitalCubierto + c.PunitorioCubierto));
                Assert.Equal(deuda.Total, acuerdo.MontoTotalAcuerdo);
            }
            else
            {
                Assert.Empty(cubiertas);
            }
        }
    }

    // Un cobro directo a la vez que se activa: si el cobro entra antes, la activación ve otra deuda y se rechaza; si la
    // activación entra antes, el cobro se rechaza. Nunca cobra y activa a la vez.
    [SqlServerConcFact]
    public async Task ActivarVsCobroDirecto_NuncaSeCobraPorFueraDeUnAcuerdoRecienActivado()
    {
        for (var i = 0; i < Intentos; i++)
        {
            var s = await SembrarVencidoAsync();
            var acuerdoId = await CrearBorradorAsync(s);

            var activar = Activar(acuerdoId, s.ClienteId);
            var cobro = CobroDirecto(s, s.CuotaIds[0]);
            await Task.WhenAll(activar, cobro);
            _out.WriteLine($"[{i}] activar={activar.Result.Detalle} | cobro={cobro.Result.Detalle}");

            Assert.False(activar.Result.Ok && cobro.Result.Ok, "Activación y cobro directo no pueden ganar los dos.");
            var acuerdo = await LeerAcuerdoAsync(acuerdoId);
            Assert.Equal(activar.Result.Ok ? EstadoAcuerdo.Activo : EstadoAcuerdo.Borrador, acuerdo.Estado);
        }
    }

    // Aplicar y anular punitorios y cobrar por afuera, todo contra un acuerdo activo, en paralelo: ninguno puede pasar.
    [SqlServerConcFact]
    public async Task ConUnAcuerdoActivo_NingunaOperacionParalelaModificaLaDeudaCongelada()
    {
        var s = await SembrarVencidoAsync();
        int punitorioId;
        using (var scope = _fx.Services.CreateScope())
            punitorioId = (await scope.ServiceProvider.GetRequiredService<IPunitorioService>()
                .AplicarAsync(s.CuotaIds[0], new PunitorioAplicarComando { Motivo = "previo" })).Id;
        var acuerdoId = await CrearBorradorAsync(s);
        Assert.True((await Activar(acuerdoId, s.ClienteId)).Ok);

        var operaciones = new List<Task<(bool Ok, string Detalle)>>();
        for (var i = 0; i < 3; i++)
        {
            operaciones.Add(AplicarPunitorio(s.CuotaIds[1]));
            operaciones.Add(CobroDirecto(s, s.CuotaIds[i]));
            operaciones.Add(EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<IPunitorioService>()
                .AnularAsync(punitorioId, new PunitorioAnularComando { Motivo = "paralelo" })));
        }
        var resultados = await Task.WhenAll(operaciones);
        foreach (var r in resultados) _out.WriteLine(r.Detalle);

        Assert.All(resultados, r => Assert.False(r.Ok, "Una operación paralela modificó la deuda de un acuerdo activo: " + r.Detalle));
        using var verif = _fx.Services.CreateScope();
        var ctx = verif.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0m, await ctx.Cuotas.AsNoTracking().Where(c => c.CreditoId == s.CreditoId).SumAsync(c => c.MontoPagado));
        Assert.Equal(EstadoPunitorioAplicado.Aplicado, (await ctx.PunitoriosAplicados.AsNoTracking().SingleAsync(p => p.Id == punitorioId)).Estado);
    }

    // Dos envíos simultáneos del MISMO formulario de pago: cobra una sola vez.
    [SqlServerConcFact]
    public async Task DosPagosSimultaneosDelMismoPasoDelAcuerdo_CobranUnaSolaVez()
    {
        for (var i = 0; i < Intentos; i++)
        {
            var s = await SembrarVencidoAsync();
            var acuerdoId = await CrearBorradorAsync(s);
            Assert.True((await Activar(acuerdoId, s.ClienteId)).Ok);

            Task<(bool Ok, string Detalle)> Pagar() => EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<IMoraService>()
                .RegistrarPagoAcuerdoAsync(acuerdoId, s.ClienteId, "Efectivo", null, pasoEsperado: 0));
            var a = Pagar();
            var b = Pagar();
            var c = Pagar();
            await Task.WhenAll(a, b, c);
            _out.WriteLine($"[{i}] {a.Result.Detalle} | {b.Result.Detalle} | {c.Result.Detalle}");

            Assert.Equal(1, new[] { a, b, c }.Count(t => t.Result.Ok));
            using var scope = _fx.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(100m, await ctx.Cuotas.AsNoTracking().Where(x => x.CreditoId == s.CreditoId).SumAsync(x => x.MontoPagado));
            Assert.Equal(1, await ctx.AcuerdoPagoImputaciones.CountAsync(x => x.AcuerdoPagoId == acuerdoId && x.Estado == EstadoImputacionAcuerdo.Aplicada));
            Assert.Equal(0, await ctx.AcuerdoPagoImputaciones.CountAsync(x => x.AcuerdoPagoId == acuerdoId && x.Estado == EstadoImputacionAcuerdo.Pendiente));
        }
    }

    private async Task RetrocederActivacionAsync(int acuerdoId, int dias)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
        var acuerdo = await ctx.AcuerdosPago.SingleAsync(a => a.Id == acuerdoId);
        acuerdo.FechaActivacionComercial = reloj.HoyComercial.AddDays(-dias);
        await ctx.SaveChangesAsync();
    }

    private Task<(bool Ok, string Detalle)> Verificar() =>
        EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<IMoraService>().VerificarIncumplimientoAcuerdosAsync());

    // La entrega venció fuera de gracia: la verificación diaria y los pagos corren a la vez. Gane quien gane, el acuerdo queda
    // Incumplido UNA vez y no se cobra nada por el acuerdo (un pago tardío no lo reactiva).
    [SqlServerConcFact]
    public async Task IncumplimientoVsPagos_ElAcuerdoQuedaIncumplidoYNoSeCobraNada()
    {
        for (var i = 0; i < Intentos; i++)
        {
            var s = await SembrarVencidoAsync();
            var acuerdoId = await CrearBorradorAsync(s);
            Assert.True((await Activar(acuerdoId, s.ClienteId)).Ok);
            await RetrocederActivacionAsync(acuerdoId, 10);

            Task<(bool Ok, string Detalle)> Pagar() => EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<IMoraService>()
                .RegistrarPagoAcuerdoAsync(acuerdoId, s.ClienteId, "Efectivo", null, pasoEsperado: 0));
            var tareas = new[] { Verificar(), Pagar(), Pagar(), Verificar() };
            await Task.WhenAll(tareas);
            _out.WriteLine($"[{i}] " + string.Join(" | ", tareas.Select(t => t.Result.Detalle)));

            var acuerdo = await LeerAcuerdoAsync(acuerdoId);
            Assert.Equal(EstadoAcuerdo.Incumplido, acuerdo.Estado);
            Assert.False(tareas[1].Result.Ok || tareas[2].Result.Ok, "Un pago tardío no puede registrarse ni reactivar el acuerdo.");
            using var scope = _fx.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0m, await ctx.Cuotas.AsNoTracking().Where(x => x.CreditoId == s.CreditoId).SumAsync(x => x.MontoPagado));
            Assert.Equal(1, await ctx.SeguridadEventosAuditoria.CountAsync(e => e.Accion == "IncumplimientoAutomatico" && e.Entidad == $"AcuerdoPago:{acuerdoId}"));
        }
    }

    // Dentro de la gracia la verificación no toca nada y el pago concurrente se cobra una sola vez.
    [SqlServerConcFact]
    public async Task VerificacionVsPagoDentroDeGracia_CobraUnaVezYNoIncumple()
    {
        for (var i = 0; i < Intentos; i++)
        {
            var s = await SembrarVencidoAsync();
            var acuerdoId = await CrearBorradorAsync(s);
            Assert.True((await Activar(acuerdoId, s.ClienteId)).Ok);
            await RetrocederActivacionAsync(acuerdoId, 3); // D+3: todavía en gracia

            Task<(bool Ok, string Detalle)> Pagar() => EjecutarAsync(_fx.Services, sp => sp.GetRequiredService<IMoraService>()
                .RegistrarPagoAcuerdoAsync(acuerdoId, s.ClienteId, "Efectivo", null, pasoEsperado: 0));
            var tareas = new[] { Verificar(), Pagar(), Pagar(), Verificar() };
            await Task.WhenAll(tareas);
            _out.WriteLine($"[{i}] " + string.Join(" | ", tareas.Select(t => t.Result.Detalle)));

            Assert.Equal(EstadoAcuerdo.Activo, (await LeerAcuerdoAsync(acuerdoId)).Estado);
            Assert.Equal(1, new[] { tareas[1], tareas[2] }.Count(t => t.Result.Ok));
            using var scope = _fx.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(100m, await ctx.Cuotas.AsNoTracking().Where(x => x.CreditoId == s.CreditoId).SumAsync(x => x.MontoPagado));
        }
    }

    [SqlServerConcFact]
    public async Task DosActivacionesSimultaneas_CongelanLaDeudaUnaSolaVez()
    {
        var s = await SembrarVencidoAsync();
        var acuerdoId = await CrearBorradorAsync(s);

        var resultados = await Task.WhenAll(Activar(acuerdoId, s.ClienteId), Activar(acuerdoId, s.ClienteId), Activar(acuerdoId, s.ClienteId));
        foreach (var r in resultados) _out.WriteLine(r.Detalle);

        Assert.Equal(1, resultados.Count(r => r.Ok));
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await ctx.AcuerdoPagoCuotasCubiertas.CountAsync(c => c.AcuerdoPagoId == acuerdoId));
        Assert.Equal(2, await ctx.CuotasAcuerdo.CountAsync(c => c.AcuerdoPagoId == acuerdoId));
    }

    [SqlServerConcFact]
    public async Task DosCreacionesSimultaneasParaElMismoCredito_CreanUnSoloAcuerdoVigente()
    {
        var s = await SembrarVencidoAsync();
        Task<(bool Ok, string Detalle)> Crear() => EjecutarAsync(_fx.Services, sp =>
            sp.GetRequiredService<IMoraService>().CrearAcuerdoPagoParaCreditoAsync(
                Solicitud(s, sp.GetRequiredService<IRelojComercial>().InicioDiaComercial), "g"));

        var resultados = await Task.WhenAll(Crear(), Crear(), Crear());
        foreach (var r in resultados) _out.WriteLine(r.Detalle);

        Assert.Equal(1, resultados.Count(r => r.Ok));
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await ctx.AcuerdosPago.CountAsync(a => a.CreditoId == s.CreditoId));
    }
}
