using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Mitigacion determinista del cruce de la medianoche comercial en el suite Playwright de CI: sin reloj acotado,
/// las aperturas sembradas antes de las 00:00 ART pasan a "turno vencido"; con el reloj acotado (solo CI) no. La regla
/// <c>CajaService.EsAperturaVencida</c> no cambia: lo unico que cambia es el reloj que se le inyecta.
/// </summary>
[Collection("HttpIntegration")]
public sealed class TimeProviderDiaComercialAcotadoTests
{
    // 2026-10-28 23:55 ART == 2026-10-29 02:55 UTC
    private static readonly DateTimeOffset Seed2355Art = new(2026, 10, 29, 2, 55, 0, TimeSpan.Zero);

    private sealed class RelojAvanzable : TimeProvider
    {
        private DateTimeOffset _utc;
        public RelojAvanzable(DateTimeOffset utc) => _utc = utc;
        public void Avanzar(TimeSpan t) => _utc += t;
        public override DateTimeOffset GetUtcNow() => _utc;
    }

    private static AperturaCaja AperturaA(DateTimeOffset utc) =>
        new() { FechaApertura = utc.UtcDateTime, Cerrada = false };

    [Fact]
    public void SinAcotar_CruzarMedianocheComercial_VuelveVencidaLaAperturaSembrada()
    {
        // Documenta el fallo que se mitiga (seed 23:55 ART, specs desde 00:05 ART).
        var sistema = new RelojAvanzable(Seed2355Art);
        var reloj = new RelojComercial(sistema);
        var apertura = AperturaA(sistema.GetUtcNow());

        Assert.False(CajaService.EsAperturaVencida(apertura, reloj.HoyComercial, reloj.ZonaComercial));

        sistema.Avanzar(TimeSpan.FromMinutes(10)); // 00:05 ART del dia siguiente

        Assert.True(CajaService.EsAperturaVencida(apertura, reloj.HoyComercial, reloj.ZonaComercial));
    }

    [Fact]
    public void Acotado_CruzarMedianocheComercial_NoVuelveVencidaLaAperturaSembrada()
    {
        var sistema = new RelojAvanzable(Seed2355Art);
        var acotado = new TimeProviderDiaComercialAcotado(sistema);
        var reloj = new RelojComercial(acotado);
        var apertura = AperturaA(sistema.GetUtcNow());
        var hoyAntes = reloj.HoyComercial;

        sistema.Avanzar(TimeSpan.FromMinutes(10)); // 00:05 ART
        Assert.False(CajaService.EsAperturaVencida(apertura, reloj.HoyComercial, reloj.ZonaComercial));

        sistema.Avanzar(TimeSpan.FromHours(30)); // mucho despues: el reloj acotado no pasa del dia de arranque
        Assert.Equal(hoyAntes, reloj.HoyComercial);
        Assert.False(CajaService.EsAperturaVencida(apertura, reloj.HoyComercial, reloj.ZonaComercial));
    }

    [Fact]
    public void Acotado_AperturaSembradaDespuesDelCruce_NoEstaVencida()
    {
        // La app arranco antes de las 00:00 ART y el seed corrio despues: la apertura es del dia siguiente al reloj acotado.
        var sistema = new RelojAvanzable(Seed2355Art);
        var reloj = new RelojComercial(new TimeProviderDiaComercialAcotado(sistema));

        sistema.Avanzar(TimeSpan.FromMinutes(7)); // 00:02 ART
        var apertura = AperturaA(sistema.GetUtcNow());

        Assert.False(CajaService.EsAperturaVencida(apertura, reloj.HoyComercial, reloj.ZonaComercial));
    }

    [Fact]
    public void Acotado_AntesDelLimite_ElTiempoAvanzaConNormalidad_YElLimiteEsElFinDelDiaComercial()
    {
        var sistema = new RelojAvanzable(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero));
        var acotado = new TimeProviderDiaComercialAcotado(sistema);

        Assert.Equal(sistema.GetUtcNow(), acotado.GetUtcNow());
        sistema.Avanzar(TimeSpan.FromHours(1));
        Assert.Equal(sistema.GetUtcNow(), acotado.GetUtcNow());

        // Dia comercial 28/10 (ART) termina 2026-10-29 03:00 UTC (exclusive).
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 3, 0, 0, TimeSpan.Zero).AddTicks(-1), acotado.LimiteUtc);
        sistema.Avanzar(TimeSpan.FromDays(3));
        Assert.Equal(acotado.LimiteUtc, acotado.GetUtcNow());
    }

    [Fact]
    public void Resolver_SinClave_DevuelveElProveedorDelSistema()
    {
        var sistema = TimeProvider.System;
        var config = new ConfigurationBuilder().Build();

        Assert.Same(sistema, TimeProviderDiaComercialAcotado.Resolver(config, sistema));

        var apagado = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TimeProviderDiaComercialAcotado.ClaveConfiguracion] = "false" })
            .Build();
        Assert.Same(sistema, TimeProviderDiaComercialAcotado.Resolver(apagado, sistema));
    }

    [Fact]
    public void Resolver_ConClaveTrue_DevuelveElProveedorAcotado()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [TimeProviderDiaComercialAcotado.ClaveConfiguracion] = "true" })
            .Build();

        Assert.IsType<TimeProviderDiaComercialAcotado>(TimeProviderDiaComercialAcotado.Resolver(config, TimeProvider.System));
    }

    [Fact]
    public void App_SinClave_RegistraTimeProviderSystem_ConClave_RegistraElAcotado()
    {
        // Program.cs: por defecto (produccion y suite normal) el reloj es TimeProvider.System.
        using (var normal = new CustomWebApplicationFactory())
        {
            Assert.Same(TimeProvider.System, normal.Services.GetRequiredService<TimeProvider>());
        }

        using var e2e = new CustomWebApplicationFactory()
            .WithWebHostBuilder(b => b.UseSetting(TimeProviderDiaComercialAcotado.ClaveConfiguracion, "true"));
        Assert.IsType<TimeProviderDiaComercialAcotado>(e2e.Services.GetRequiredService<TimeProvider>());
    }
}
