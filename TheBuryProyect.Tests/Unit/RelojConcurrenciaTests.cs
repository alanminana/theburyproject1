using TheBuryProject.Services;
using TheBuryProject.Tests.Concurrency;
using Xunit;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// El reloj del harness SQL Server de concurrencia no cruza la medianoche comercial por accidente (causa del fallo
/// intermitente de H10-B13 [B13e]); solo lo hace cuando un test lo pide. Corre sin SQL Server.
/// </summary>
public sealed class RelojConcurrenciaTests
{
    private sealed class RelojManual(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    // 2026-11-11 02:59:54 UTC = 2026-11-10 23:59:54 en Argentina: el instante en que arrancó [B13e] en la corrida que falló.
    private static readonly DateTimeOffset InicioB13e = new(2026, 11, 11, 2, 59, 54, TimeSpan.Zero);

    [Fact]
    public void ElSistemaCruzaLaMedianocheComercial_ElRelojDelHarnessSigueEnElDiaDeArranque()
    {
        var sistema = new RelojManual(InicioB13e);
        var reloj = new RelojConcurrencia(sistema);
        var comercial = new RelojComercial(reloj);
        var diaDeArranque = comercial.HoyComercial;

        sistema.Ahora = InicioB13e.AddSeconds(7); // 03:00:01 UTC: ya es el día comercial siguiente en el reloj de pared

        Assert.Equal(new DateOnly(2026, 11, 10), diaDeArranque);
        Assert.Equal(diaDeArranque, comercial.HoyComercial);
        Assert.Equal(reloj.LimiteUtc, reloj.GetUtcNow());
        // Contraprueba: con el reloj del sistema (lo que usaba el harness) el día comercial sí cambió.
        Assert.Equal(diaDeArranque.AddDays(1), new RelojComercial(sistema).HoyComercial);
    }

    [Fact]
    public void AntesDelLimite_ElTiempoAvanzaConNormalidad()
    {
        var sistema = new RelojManual(InicioB13e.AddHours(-5));
        var reloj = new RelojConcurrencia(sistema);

        sistema.Ahora = sistema.Ahora.AddMinutes(10);

        Assert.Equal(sistema.Ahora, reloj.GetUtcNow());
    }

    [Fact]
    public void EnElDiaComercialSiguiente_AdelantaUnDiaYSeRestauraAlLiberar()
    {
        var sistema = new RelojManual(InicioB13e.AddHours(-5));
        var reloj = new RelojConcurrencia(sistema);
        var comercial = new RelojComercial(reloj);
        var hoy = comercial.HoyComercial;

        using (reloj.EnElDiaComercialSiguiente())
            Assert.Equal(hoy.AddDays(1), comercial.HoyComercial);

        Assert.Equal(hoy, comercial.HoyComercial);
    }
}
