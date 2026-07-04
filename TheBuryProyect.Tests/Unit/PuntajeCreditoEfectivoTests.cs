using TheBuryProject.Services;
using Xunit;

namespace TheBuryProject.Tests.Unit;

[Trait("Category", "Scoring")]
public sealed class PuntajeCreditoEfectivoTests
{
    [Fact]
    public void Resolver_SinManual_UsaAutomatico()
    {
        Assert.Equal(1, PuntajeCreditoEfectivo.Resolver(automatico: 1, manual: null));
    }

    [Fact]
    public void Resolver_ConManual_UsaManual()
    {
        // Criterio 1: automático 1 + manual 4 ⇒ efectivo 4
        Assert.Equal(4, PuntajeCreditoEfectivo.Resolver(automatico: 1, manual: 4));
    }

    [Fact]
    public void Resolver_ManualMenorQueAutomatico_ElManualManda()
    {
        Assert.Equal(2, PuntajeCreditoEfectivo.Resolver(automatico: 5, manual: 2));
    }

    [Fact]
    public void Fuente_SinManual_EsAutomatico()
    {
        Assert.Equal("Automatico", PuntajeCreditoEfectivo.Fuente(null));
    }

    [Fact]
    public void Fuente_ConManual_EsManual()
    {
        Assert.Equal("Manual", PuntajeCreditoEfectivo.Fuente(4));
    }
}
