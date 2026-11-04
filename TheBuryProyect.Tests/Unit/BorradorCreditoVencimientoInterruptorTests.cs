using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// H11 Etapa D: el job de vencimiento de borradores cancela ventas y créditos reales, así que solo corre si el
/// interruptor de arranque está habilitado de forma explícita (apagado por defecto).
/// </summary>
public class BorradorCreditoVencimientoInterruptorTests
{
    private sealed class ServicioFalso : IBorradorCreditoVencimientoService
    {
        public int Llamadas;

        public Task<BorradorCreditoVencimientoResultado> VencerBorradoresAsync(DateTime ahoraUtc, CancellationToken cancellationToken = default)
        {
            Llamadas++;
            return Task.FromResult(new BorradorCreditoVencimientoResultado(true, 7, 0, Array.Empty<int>(), 0, 0));
        }
    }

    private static (BorradorCreditoVencimientoBackgroundService Job, ServicioFalso Servicio) Crear(string? valor)
    {
        var servicio = new ServicioFalso();
        var provider = new ServiceCollection()
            .AddSingleton<IBorradorCreditoVencimientoService>(servicio)
            .AddSingleton<IRelojComercial>(RelojComercial.Sistema)
            .BuildServiceProvider();
        var datos = new Dictionary<string, string?>();
        if (valor != null) datos[BorradorCreditoVencimientoBackgroundService.ClaveHabilitado] = valor;
        var config = new ConfigurationBuilder().AddInMemoryCollection(datos).Build();
        return (new BorradorCreditoVencimientoBackgroundService(
            provider, NullLogger<BorradorCreditoVencimientoBackgroundService>.Instance, config), servicio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("flase")]
    [InlineData("")]
    [InlineData("1")]
    public async Task SinHabilitarExplicitamente_NoVenceNada(string? valor)
    {
        var (job, servicio) = Crear(valor);
        Assert.False(await job.EjecutarSiCorrespondeAsync(CancellationToken.None));
        Assert.Equal(0, servicio.Llamadas);

        await job.StartAsync(CancellationToken.None);
        await job.StopAsync(CancellationToken.None);
        Assert.Equal(0, servicio.Llamadas);
    }

    [Fact]
    public async Task HabilitadoExplicitamente_Corre()
    {
        var (job, servicio) = Crear("true");
        Assert.True(await job.EjecutarSiCorrespondeAsync(CancellationToken.None));
        Assert.Equal(1, servicio.Llamadas);
    }

    [Fact]
    public void AppsettingsBaseTraeElInterruptorApagado()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = new ConfigurationBuilder().AddJsonFile(ruta, optional: false).Build();
        Assert.False(config.GetValue(BorradorCreditoVencimientoBackgroundService.ClaveHabilitado, true));
    }
}
