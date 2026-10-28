using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;
using TheBuryProject.ViewModels.Mora;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// La verificación diaria del incumplimiento corre sola, una vez por fecha comercial y sin depender de
/// ProcesoAutomaticoActivo (la configuración por defecto lo deja apagado).
/// </summary>
[Collection("HttpIntegration")]
public sealed class AcuerdoIncumplimientoBackgroundServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AcuerdoIncumplimientoBackgroundServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CicloDiario_MarcaIncumplidoUnAcuerdoVencido_UnaVezPorFechaComercial_ConElProcesoAutomaticoApagado()
    {
        EscenarioSembrado s;
        int acuerdoId;
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloj = scope.ServiceProvider.GetRequiredService<IRelojComercial>();
            var apertura = await ConcSeed.SembrarAperturaAsync(ctx, "Sistema");
            s = await ConcSeed.SembrarAsync(ctx, apertura, false, "Sistema");
            var cuotas = await ctx.Cuotas.Where(c => s.CuotaIds.Contains(c.Id)).ToListAsync();
            foreach (var c in cuotas)
                c.FechaVencimiento = reloj.InicioDiaComercial.AddDays(-30);
            await ctx.SaveChangesAsync();

            var mora = scope.ServiceProvider.GetRequiredService<IMoraService>();
            var config = await mora.GetConfiguracionAsync();
            var enBase = await ctx.ConfiguracionesMora.SingleAsync(c => c.Id == config.Id);
            enBase.ProcesoAutomaticoActivo = false; // el proceso de mora apagado no impide verificar acuerdos
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            acuerdoId = await mora.CrearAcuerdoPagoParaCreditoAsync(new CrearAcuerdoViewModel
            {
                ClienteId = s.ClienteId, CreditoId = s.CreditoId, MontoEntregaInicial = 100m, CantidadCuotas = 2,
                FechaPrimeraCuota = reloj.InicioDiaComercial.AddDays(30)
            }, "g");
            await mora.CambiarEstadoAcuerdoAsync(acuerdoId, s.ClienteId, EstadoAcuerdo.Activo);

            // La entrega venció hace 10 días (gracia de 3): el ciclo diario debe marcarlo.
            var acuerdo = await ctx.AcuerdosPago.SingleAsync(a => a.Id == acuerdoId);
            acuerdo.FechaActivacionComercial = reloj.HoyComercial.AddDays(-10);
            await ctx.SaveChangesAsync();
        }

        var servicio = new AcuerdoIncumplimientoBackgroundService(_factory.Services, NullLogger<AcuerdoIncumplimientoBackgroundService>.Instance);

        Assert.True(await servicio.EjecutarSiCorrespondeAsync(CancellationToken.None));
        Assert.False(await servicio.EjecutarSiCorrespondeAsync(CancellationToken.None)); // misma fecha comercial: no repite

        using var verif = _factory.Services.CreateScope();
        var db = verif.ServiceProvider.GetRequiredService<AppDbContext>();
        var guardado = await db.AcuerdosPago.AsNoTracking().SingleAsync(a => a.Id == acuerdoId);
        Assert.Equal(EstadoAcuerdo.Incumplido, guardado.Estado);
        Assert.Contains("entrega inicial", guardado.MotivoIncumplimiento);
        Assert.Contains(await db.SeguridadEventosAuditoria.AsNoTracking().ToListAsync(),
            e => e.Accion == "IncumplimientoAutomatico" && e.Entidad == $"AcuerdoPago:{acuerdoId}");
    }
}
