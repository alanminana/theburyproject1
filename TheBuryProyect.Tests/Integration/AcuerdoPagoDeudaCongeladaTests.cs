using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Un acuerdo de pago congela al activarse las cuotas originales que cubre (capital + punitorio aplicado): los pagos se
/// imputan solo a esa deuda, y mientras está Activo no se cobra por otro circuito ni se aplican/anulan punitorios.
/// Servicios REALES (cobro, punitorios, mora) sobre SQLite; el usuario sin contexto es "Sistema".
/// </summary>
[Collection("HttpIntegration")]
public sealed class AcuerdoPagoDeudaCongeladaTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Usuario = "Sistema";
    private readonly CustomWebApplicationFactory _factory;

    public AcuerdoPagoDeudaCongeladaTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed class UsuarioConPermisos : ICurrentUserService
    {
        public string GetUsername() => "Aplicador";
        public string GetUserId() => "aplicador";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => "aplicador@test.com";
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => modulo == "cobranzas" && (accion == "applyfine" || accion == "revertfine");
        public string? GetIpAddress() => "127.0.0.1";
    }

    // Credito con 3 cuotas de 300, vencidas hace 30/20/10 días.
    private async Task<EscenarioSembrado> SembrarVencidoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
        var apertura = await ConcSeed.SembrarAperturaAsync(ctx, Usuario);
        var s = await ConcSeed.SembrarAsync(ctx, apertura, false, Usuario);
        var hoy = reloj.InicioDiaComercial;
        var cuotas = await ctx.Cuotas.Where(c => s.CuotaIds.Contains(c.Id)).OrderBy(c => c.NumeroCuota).ToListAsync();
        for (var i = 0; i < cuotas.Count; i++)
            cuotas[i].FechaVencimiento = hoy.AddDays(-30 + 10 * i);
        if (!await ctx.ConfiguracionesPunitorio.AnyAsync())
            ctx.ConfiguracionesPunitorio.Add(new ConfiguracionPunitorio
            {
                Porcentaje = 10m, PeriodoDias = 20, DiasGracia = 5, VigenteDesde = new DateOnly(2020, 1, 1), Activa = true, ProrrateoDiario = true
            });
        await ctx.SaveChangesAsync();
        return s;
    }

    private static CrearAcuerdoViewModel Solicitud(EscenarioSembrado s, IRelojComercial reloj, decimal entrega = 100m) => new()
    {
        ClienteId = s.ClienteId, CreditoId = s.CreditoId, MontoEntregaInicial = entrega, CantidadCuotas = 2,
        FechaPrimeraCuota = reloj.InicioDiaComercial.AddDays(30)
    };

    private static PunitorioService Punitorios(AppDbContext ctx, IRelojComercial reloj) =>
        new(ctx, new PunitorioCalculator(), reloj, new UsuarioConPermisos(), NullLogger<PunitorioService>.Instance);

    private static Task<PunitorioAplicado> AplicarPunitorioAsync(AppDbContext ctx, IRelojComercial reloj, int cuotaId) =>
        Punitorios(ctx, reloj).AplicarAsync(cuotaId, new PunitorioAplicarComando { Motivo = "test" });

    private async Task<int> CrearYActivarAsync(EscenarioSembrado s, decimal entrega = 100m)
    {
        using var scope = _factory.Services.CreateScope();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
        var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
        var id = await mora.CrearAcuerdoPagoParaCreditoAsync(Solicitud(s, reloj, entrega), "g");
        await mora.CambiarEstadoAcuerdoAsync(id, s.ClienteId, EstadoAcuerdo.Activo);
        return id;
    }

    private static PagarCuotaViewModel PagoDirecto(EscenarioSembrado s, int cuotaId, Guid? operacion = null) => new()
    {
        CreditoId = s.CreditoId, CuotaId = cuotaId, MontoPagado = 50m, MedioPago = "Efectivo", OperacionExternaId = operacion
    };

    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Activar_CongelaLasCuotasCubiertasConCapitalYPunitorioAplicado()
    {
        var s = await SembrarVencidoAsync();
        decimal punitorio;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aplicado = await AplicarPunitorioAsync(ctx, scope.ServiceProvider.GetRequiredService<IRelojComercial>(), s.CuotaIds[0]);
            punitorio = aplicado.Importe;
        }

        var acuerdoId = await CrearYActivarAsync(s);

        using var verif = _factory.Services.CreateScope();
        var db = verif.ServiceProvider.GetRequiredService<AppDbContext>();
        var cubiertas = await db.AcuerdoPagoCuotasCubiertas.AsNoTracking().Where(c => c.AcuerdoPagoId == acuerdoId).OrderBy(c => c.Orden).ToListAsync();
        Assert.Equal(3, cubiertas.Count);
        Assert.Equal(s.CuotaIds.OrderBy(i => i), cubiertas.Select(c => c.CuotaId).OrderBy(i => i));
        Assert.All(cubiertas, c => Assert.Equal(300m, c.CapitalCubierto));
        Assert.Equal(punitorio, cubiertas[0].PunitorioCubierto);
        Assert.Equal(0m, cubiertas[1].PunitorioCubierto);
        var acuerdo = await db.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == acuerdoId);
        Assert.Equal(900m + punitorio, acuerdo.MontoTotalAcuerdo);
        Assert.Equal(punitorio, acuerdo.MontoMoraOriginal);
    }

    [Fact]
    public async Task Pago_ImputaSoloALaDeudaCongelada_NoIncorporaCuotasQueVencenDespues()
    {
        var s = await SembrarVencidoAsync();
        var acuerdoId = await CrearYActivarAsync(s);

        int cuotaNuevaId;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
            // Una cuota más vieja que las cubiertas que aparece (o vence) después de activar: no pertenece al acuerdo.
            var nueva = new Cuota
            {
                CreditoId = s.CreditoId, NumeroCuota = 4, MontoCapital = 300m, MontoInteres = 0m, MontoTotal = 300m,
                FechaVencimiento = reloj.InicioDiaComercial.AddDays(-90), Estado = EstadoCuota.Pendiente
            };
            ctx.Cuotas.Add(nueva);
            await ctx.SaveChangesAsync();
            cuotaNuevaId = nueva.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
            var r = await mora.RegistrarPagoAcuerdoAsync(acuerdoId, s.ClienteId, "Efectivo", null, pasoEsperado: 0);
            Assert.Equal(100m, r.ImportePagado);
        }

        using var verif = _factory.Services.CreateScope();
        var db = verif.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0m, (await db.Cuotas.AsNoTracking().SingleAsync(c => c.Id == cuotaNuevaId)).MontoPagado);
        Assert.Equal(100m, (await db.Cuotas.AsNoTracking().SingleAsync(c => c.Id == s.CuotaIds[0])).MontoPagado);
    }

    [Fact]
    public async Task CobroDirecto_SobreCuotaCubiertaPorAcuerdoActivo_SeRechazaPorTodosLosCircuitos_YElClienteNoPuedeSaltearlo()
    {
        var s = await SembrarVencidoAsync();
        var acuerdoId = await CrearYActivarAsync(s);

        using var scope = _factory.Services.CreateScope();
        var credito = scope.ServiceProvider.GetRequiredService<ICreditoService>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => credito.PagarCuotaAsync(PagoDirecto(s, s.CuotaIds[0])));
        Assert.Contains("cubierta por el acuerdo", ex.Message);

        // Una identidad inventada por el cliente no coincide con ninguna intención Pendiente del acuerdo.
        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => credito.PagarCuotaAsync(PagoDirecto(s, s.CuotaIds[0], Guid.NewGuid())));

        var cuota = await ctx.Cuotas.AsNoTracking().FirstAsync(c => c.Id == s.CuotaIds[1]);
        var multiple = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => credito.PagarCuotasAsync(new PagoMultipleCuotasRequest
        {
            ClienteId = s.ClienteId,
            CuotaIds = [cuota.Id],
            RowVersionsPorCuota = new Dictionary<int, string> { [cuota.Id] = Convert.ToBase64String(cuota.RowVersion) },
            MedioPago = "Efectivo"
        }));
        Assert.Contains("cubierta por el acuerdo", multiple.Message);

        Assert.Equal(0m, await ctx.Cuotas.AsNoTracking().Where(c => c.CreditoId == s.CreditoId).SumAsync(c => c.MontoPagado));
        Assert.Equal(0, await ctx.PagosCuota.CountAsync(p => p.Cuota.CreditoId == s.CreditoId));
        Assert.NotEqual(0, acuerdoId);
    }

    [Fact]
    public async Task CobroDirecto_ConUnaIdentidadAjenaAUnaIntencionEnVuelo_SeRechaza_YConLaPropiaSePermite()
    {
        var s = await SembrarVencidoAsync();
        var acuerdoId = await CrearYActivarAsync(s);
        var propia = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Intención Pendiente del acuerdo sobre la primera cuota (lo que escribe el pago del acuerdo antes de cobrar).
            ctx.AcuerdoPagoImputaciones.Add(new AcuerdoPagoImputacion
            {
                AcuerdoPagoId = acuerdoId, CuotaCreditoId = s.CuotaIds[0], Monto = 50m, MedioPago = "Efectivo",
                Estado = EstadoImputacionAcuerdo.Pendiente, OperacionId = propia
            });
            await ctx.SaveChangesAsync();
        }

        using var scope2 = _factory.Services.CreateScope();
        var credito = scope2.ServiceProvider.GetRequiredService<ICreditoService>();
        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => credito.PagarCuotaAsync(PagoDirecto(s, s.CuotaIds[0], Guid.NewGuid())));
        // La identidad de la intención sobre OTRA cuota tampoco habilita esta.
        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(() => credito.PagarCuotaAsync(PagoDirecto(s, s.CuotaIds[1], propia)));
        Assert.True(await credito.PagarCuotaAsync(PagoDirecto(s, s.CuotaIds[0], propia)));
    }

    [Fact]
    public async Task CobroDirecto_DespuesDeCancelarElAcuerdo_VuelveAPermitirse()
    {
        var s = await SembrarVencidoAsync();
        var acuerdoId = await CrearYActivarAsync(s);
        using var scope = _factory.Services.CreateScope();
        var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
        var credito = scope.ServiceProvider.GetRequiredService<ICreditoService>();

        await mora.CambiarEstadoAcuerdoAsync(acuerdoId, s.ClienteId, EstadoAcuerdo.Cancelado, "El cliente prefiere pagar por caja");

        Assert.True(await credito.PagarCuotaAsync(PagoDirecto(s, s.CuotaIds[0])));
    }

    [Fact]
    public async Task Punitorios_SobreCuotaCubiertaPorAcuerdoActivo_NoSePuedenAplicarNiAnular_YSeLiberanAlCancelar()
    {
        var s = await SembrarVencidoAsync();
        int punitorioId;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            punitorioId = (await AplicarPunitorioAsync(ctx, scope.ServiceProvider.GetRequiredService<IRelojComercial>(), s.CuotaIds[0])).Id;
        }
        var acuerdoId = await CrearYActivarAsync(s);

        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
            var aplicar = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(() => AplicarPunitorioAsync(ctx, reloj, s.CuotaIds[1]));
            Assert.Contains("acuerdo", aplicar.Message);
            var anular = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
                () => Punitorios(ctx, reloj).AnularAsync(punitorioId, new PunitorioAnularComando { Motivo = "test" }));
            Assert.Contains("cancelalo", anular.Message);
        }

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMoraService>()
                .CambiarEstadoAcuerdoAsync(acuerdoId, s.ClienteId, EstadoAcuerdo.Incumplido, "No pagó");

        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
            // Incumplido: vuelven las operaciones habituales.
            var aplicado = await AplicarPunitorioAsync(ctx, reloj, s.CuotaIds[1]);
            Assert.True(aplicado.Importe > 0m);
            await Punitorios(ctx, reloj).AnularAsync(punitorioId, new PunitorioAnularComando { Motivo = "test" });
        }
    }

    [Fact]
    public async Task Punitorios_SobreCuotaDeUnAcuerdoCumplido_NoSeAplicanRetroactivamente()
    {
        var s = await SembrarVencidoAsync();
        var acuerdoId = await CrearYActivarAsync(s, entrega: 900m); // la entrega es el total: sin cuotas
        using (var scope = _factory.Services.CreateScope())
        {
            var r = await scope.ServiceProvider.GetRequiredService<IMoraService>()
                .RegistrarPagoAcuerdoAsync(acuerdoId, s.ClienteId, "Efectivo", null, pasoEsperado: 0);
            Assert.True(r.AcuerdoCumplido);
        }

        using var verif = _factory.Services.CreateScope();
        var ctx = verif.ServiceProvider.GetRequiredService<AppDbContext>();
        var ex = await Assert.ThrowsAsync<PunitorioAplicadoRechazadoException>(
            () => AplicarPunitorioAsync(ctx, verif.ServiceProvider.GetRequiredService<IRelojComercial>(), s.CuotaIds[0]));
        Assert.Contains("cumplido", ex.Message);
        Assert.Equal(0, await ctx.PunitoriosAplicados.CountAsync(p => s.CuotaIds.Contains(p.CuotaId)));
    }

    [Fact]
    public async Task AcuerdoPorDeudaSoloPunitoria_SeCreaSinAlertaNiRegistrosFicticios_YSeCobraPorElAcuerdo()
    {
        var s = await SembrarVencidoAsync();
        // Capital saldado por el circuito normal; después se aplica un punitorio: queda deuda exclusivamente punitoria.
        decimal punitorio;
        using (var scope = _factory.Services.CreateScope())
        {
            var credito = scope.ServiceProvider.GetRequiredService<ICreditoService>();
            foreach (var cuotaId in s.CuotaIds)
                Assert.True(await credito.PagarCuotaAsync(new PagarCuotaViewModel { CreditoId = s.CreditoId, CuotaId = cuotaId, MontoPagado = 300m, MedioPago = "Efectivo" }));
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            punitorio = (await AplicarPunitorioAsync(ctx, scope.ServiceProvider.GetRequiredService<IRelojComercial>(), s.CuotaIds[0])).Importe;
        }
        Assert.True(punitorio > 0m);

        using var scope2 = _factory.Services.CreateScope();
        var mora = scope2.ServiceProvider.GetRequiredService<IMoraService>();
        var reloj = scope2.ServiceProvider.GetRequiredService<IRelojComercial>();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var alertasAntes = await ctx2.AlertasCobranza.CountAsync();

        // La clasificación de mora de capital no cambia: este crédito no figura como "en mora".
        Assert.DoesNotContain(await mora.GetCreditosEnMoraAsync(s.ClienteId), c => c.CreditoId == s.CreditoId);
        var ficha = await mora.GetFichaClienteAsync(s.ClienteId);
        var paraAcuerdo = Assert.Single(ficha!.CreditosParaAcuerdo, c => c.CreditoId == s.CreditoId);
        Assert.True(paraAcuerdo.SoloPunitorio);
        Assert.Equal(punitorio, paraAcuerdo.Total);

        var id = await mora.CrearAcuerdoPagoParaCreditoAsync(Solicitud(s, reloj, entrega: punitorio), "g");
        await mora.CambiarEstadoAcuerdoAsync(id, s.ClienteId, EstadoAcuerdo.Activo);

        var acuerdo = await ctx2.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Null(acuerdo.AlertaCobranzaId);
        Assert.Equal(punitorio, acuerdo.MontoTotalAcuerdo);
        Assert.Equal(alertasAntes, await ctx2.AlertasCobranza.CountAsync()); // no se inventó ninguna alerta

        var r = await mora.RegistrarPagoAcuerdoAsync(id, s.ClienteId, "Efectivo", null, pasoEsperado: 0);
        Assert.True(r.AcuerdoCumplido);
    }

    [Fact]
    public async Task Alerta_SeDerivaEnElServidorDelMismoCredito_YUnaAlertaAjenaSeRechaza()
    {
        var s = await SembrarVencidoAsync();
        var otro = await SembrarVencidoAsync();
        int alertaPropia, alertaAjena;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var propia = new AlertaCobranza { ClienteId = s.ClienteId, CreditoId = s.CreditoId, Tipo = TipoAlertaCobranza.CuotaVencida, FechaAlerta = DateTime.UtcNow };
            var ajena = new AlertaCobranza { ClienteId = otro.ClienteId, CreditoId = otro.CreditoId, Tipo = TipoAlertaCobranza.CuotaVencida, FechaAlerta = DateTime.UtcNow };
            ctx.AlertasCobranza.AddRange(propia, ajena);
            await ctx.SaveChangesAsync();
            alertaPropia = propia.Id;
            alertaAjena = ajena.Id;
        }

        using var scope2 = _factory.Services.CreateScope();
        var mora = (MoraService)scope2.ServiceProvider.GetRequiredService<IMoraService>();
        var reloj = scope2.ServiceProvider.GetRequiredService<IRelojComercial>();
        var ctx2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

        // Aunque el formulario mande la alerta de otro cliente, el servidor deriva la del crédito.
        var solicitud = Solicitud(s, reloj);
        solicitud.AlertaId = alertaAjena;
        var id = await mora.CrearAcuerdoPagoParaCreditoAsync(solicitud, "g");
        Assert.Equal(alertaPropia, (await ctx2.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == id)).AlertaCobranzaId);

        // La creación de bajo nivel (solo pruebas) valida cliente Y crédito de la alerta.
        var baja = Solicitud(otro, reloj);
        baja.MontoDeudaOriginal = 900m;
        baja.AlertaId = alertaPropia; // alerta de otro cliente y otro crédito
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mora.CrearAcuerdoPagoAsync(baja, "g"));
        Assert.Contains("no pertenece", ex.Message);
        // Alerta del mismo cliente pero de otro crédito.
        int otraAlertaMismoCliente;
        {
            var extra = new AlertaCobranza { ClienteId = otro.ClienteId, CreditoId = s.CreditoId, Tipo = TipoAlertaCobranza.CuotaVencida, FechaAlerta = DateTime.UtcNow };
            ctx2.AlertasCobranza.Add(extra);
            await ctx2.SaveChangesAsync();
            otraAlertaMismoCliente = extra.Id;
        }
        baja.AlertaId = otraAlertaMismoCliente;
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => mora.CrearAcuerdoPagoAsync(baja, "g"));
        Assert.Contains("no pertenece", ex2.Message);
    }

    [Fact]
    public async Task Activar_SiLaFechaDeLaPrimeraCuotaYaPaso_SeRechaza()
    {
        var s = await SembrarVencidoAsync();
        using var scope = _factory.Services.CreateScope();
        var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await mora.CrearAcuerdoPagoParaCreditoAsync(Solicitud(s, reloj), "g");
        var acuerdo = await ctx.AcuerdosPago.SingleAsync(a => a.Id == id);
        acuerdo.FechaPrimeraCuota = reloj.InicioDiaComercial.AddDays(-1); // el borrador "envejeció"
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mora.CambiarEstadoAcuerdoAsync(id, s.ClienteId, EstadoAcuerdo.Activo));

        Assert.Contains("primera cuota", ex.Message);
        ctx.ChangeTracker.Clear();
        Assert.Equal(EstadoAcuerdo.Borrador, (await ctx.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == id)).Estado);
        Assert.Equal(0, await ctx.AcuerdoPagoCuotasCubiertas.CountAsync(c => c.AcuerdoPagoId == id));
    }

    [Fact]
    public async Task Cancelar_ExigeMotivo_LoGuardaYQuedaAuditado()
    {
        var s = await SembrarVencidoAsync();
        var acuerdoId = await CrearYActivarAsync(s);
        using var scope = _factory.Services.CreateScope();
        var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mora.CambiarEstadoAcuerdoAsync(acuerdoId, s.ClienteId, EstadoAcuerdo.Cancelado, "  "));
        Assert.Equal(EstadoAcuerdo.Activo, (await ctx.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == acuerdoId)).Estado);

        await mora.CambiarEstadoAcuerdoAsync(acuerdoId, s.ClienteId, EstadoAcuerdo.Cancelado, "Pedido del cliente");

        var acuerdo = await ctx.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == acuerdoId);
        Assert.Equal(EstadoAcuerdo.Cancelado, acuerdo.Estado);
        Assert.Equal("Pedido del cliente", acuerdo.MotivoCancelacion);
        var evento = await ctx.SeguridadEventosAuditoria.AsNoTracking()
            .Where(e => e.Modulo == "Mora" && e.Accion == "CambiarEstadoAcuerdo" && e.Entidad == $"AcuerdoPago:{acuerdoId}")
            .ToListAsync();
        Assert.Contains(evento, e => e.Detalle!.Contains("Activo -> Cancelado") && e.Detalle.Contains("Pedido del cliente"));
    }

    [Fact]
    public async Task PunitorioCalculadoSinAplicar_SeInformaSeparadoYNoEntraAlAcuerdo()
    {
        var s = await SembrarVencidoAsync();
        using var scope = _factory.Services.CreateScope();
        var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();

        var sinAplicar = await mora.CalcularPunitorioCalculadoSinAplicarAsync(s.CreditoId);
        Assert.True(sinAplicar > 0m); // hay días de atraso pero nada aplicado
        var deuda = await mora.CalcularDesgloseDeudaCobrableCreditoAsync(s.CreditoId);
        Assert.Equal(900m, deuda.Total); // el acuerdo financia solo lo aplicado: capital

        var id = await mora.CrearAcuerdoPagoParaCreditoAsync(Solicitud(s, reloj), "g");
        var resumen = Assert.Single(await mora.GetAcuerdosPagoAsync(s.ClienteId), a => a.AcuerdoId == id);
        Assert.Equal(900m, resumen.MontoTotal);
        Assert.True(resumen.PunitorioCalculadoSinAplicar > 0m); // visible antes de activar

        // Nunca se aplica solo: sigue sin haber punitorios aplicados.
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await ctx.PunitoriosAplicados.CountAsync(p => s.CuotaIds.Contains(p.CuotaId)));
    }

    [Fact]
    public async Task Activar_SiSeAplicoUnPunitorioEnElBorrador_SeRechazaYHayQueRehacerlo()
    {
        var s = await SembrarVencidoAsync();
        using var scope = _factory.Services.CreateScope();
        var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
        var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await mora.CrearAcuerdoPagoParaCreditoAsync(Solicitud(s, reloj), "g");
        await AplicarPunitorioAsync(ctx, reloj, s.CuotaIds[0]); // en borrador todavía se puede

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mora.CambiarEstadoAcuerdoAsync(id, s.ClienteId, EstadoAcuerdo.Activo));

        Assert.Contains("cambió", ex.Message);
        ctx.ChangeTracker.Clear();
        Assert.Equal(0, await ctx.AcuerdoPagoCuotasCubiertas.CountAsync(c => c.AcuerdoPagoId == id));
    }

    [Fact]
    public void LaIdentidadDeUnCobroDelAcuerdoNoSePuedeEnviarDesdeElClienteHttp()
    {
        var propiedad = typeof(PagarCuotaViewModel).GetProperty(nameof(PagarCuotaViewModel.OperacionExternaId))!;
        Assert.NotNull(propiedad.GetCustomAttribute<BindNeverAttribute>());
    }
}
