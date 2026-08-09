using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// PUN-ML7: tests unitarios del resolver canónico de estado de cuota (frontera de vencimiento,
/// protección de estados terminales y lectura derivada de mora). Puro — sin DB, sin reloj real.
/// Complementa <c>CreditoServiceEstadoCuotaTests</c> (contrato Pagada/Parcial/Pendiente de 3
/// argumentos, sin tocar).
/// </summary>
public class EstadoCuotaResolverTests
{
    private static readonly DateTime Vencimiento = new(2026, 6, 15);

    // ---------------------------------------------------------------------------
    // Frontera de vencimiento comercial
    // ---------------------------------------------------------------------------

    [Fact]
    public void DiaAnteriorAlVencimiento_DevuelvePendiente()
    {
        var fechaComercial = new DateOnly(2026, 6, 14);

        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Pendiente, Vencimiento, fechaComercial, montoPagado: 0m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Pendiente, estado);
    }

    [Fact]
    public void DiaDelVencimiento_NoEstaVencida()
    {
        var fechaComercial = new DateOnly(2026, 6, 15);

        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Pendiente, Vencimiento, fechaComercial, montoPagado: 0m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Pendiente, estado);
    }

    [Fact]
    public void DiaPosteriorAlVencimiento_DevuelveVencida()
    {
        var fechaComercial = new DateOnly(2026, 6, 16);

        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Pendiente, Vencimiento, fechaComercial, montoPagado: 0m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Vencida, estado);
    }

    [Fact]
    public void CuotaVencidaConPagoParcial_QuedaParcial_NoVencida()
    {
        // Contrato: una cuota con pago parcial nunca pasa a Vencida, sea cual sea la fecha —
        // se mantiene Parcial. Único lugar que expresa esta precedencia.
        var fechaComercial = new DateOnly(2026, 7, 1); // muy posterior al vencimiento

        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Parcial, Vencimiento, fechaComercial, montoPagado: 100m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Parcial, estado);
    }

    // ---------------------------------------------------------------------------
    // Gate de punitorio pendiente
    // ---------------------------------------------------------------------------

    [Fact]
    public void CapitalCero_ConPunitorioPendiente_NoQuedaPagada()
    {
        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Parcial, Vencimiento, new DateOnly(2026, 6, 15),
            montoPagado: 1000m, montoTotal: 1000m, punitorioAplicadoPendiente: 50m);

        Assert.Equal(EstadoCuota.Parcial, estado);
    }

    [Fact]
    public void CapitalPendiente_ConPunitorioPagado_NoQuedaPagada()
    {
        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Parcial, Vencimiento, new DateOnly(2026, 6, 15),
            montoPagado: 400m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Parcial, estado);
    }

    [Fact]
    public void CapitalCero_PunitorioCero_DevuelvePagada()
    {
        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Parcial, Vencimiento, new DateOnly(2026, 6, 15),
            montoPagado: 1000m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Pagada, estado);
    }

    // ---------------------------------------------------------------------------
    // Estados terminales: nunca se reabren
    // ---------------------------------------------------------------------------

    [Fact]
    public void Cancelada_NoSeReabrePorVencimiento()
    {
        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Cancelada, Vencimiento, new DateOnly(2027, 1, 1),
            montoPagado: 0m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Cancelada, estado);
    }

    [Fact]
    public void Cancelada_NoSeReabreAunqueLlegueUnPagoTeorico()
    {
        // No debería ocurrir en la práctica (los caminos de cobro rechazan cuotas Cancelada antes
        // de llegar acá), pero el resolver protege el invariante igual si algo lo invocara.
        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Cancelada, Vencimiento, new DateOnly(2026, 6, 15),
            montoPagado: 1000m, montoTotal: 1000m, punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Cancelada, estado);
    }

    // ---------------------------------------------------------------------------
    // EsVencidaPorFecha (frontera compartida)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(2026, 6, 14, false)]
    [InlineData(2026, 6, 15, false)]
    [InlineData(2026, 6, 16, true)]
    public void EsVencidaPorFecha_Frontera(int anio, int mes, int dia, bool esperado)
    {
        var fechaComercial = new DateOnly(anio, mes, dia);

        Assert.Equal(esperado, EstadoCuotaResolver.EsVencidaPorFecha(Vencimiento, fechaComercial));
    }

    // ---------------------------------------------------------------------------
    // EstaVencidaDerivado / DiasAtrasoDerivado — lectura "en mora hoy" sin persistir
    // ---------------------------------------------------------------------------

    [Fact]
    public void EstaVencidaDerivado_ParcialVencida_EsTrue()
    {
        // A diferencia de Resolver (que nunca marca Vencida a una Parcial), la lectura derivada de
        // "en mora" SÍ considera vencida a una Parcial atrasada — mismo criterio que
        // ClienteScoringCalculator/MoraService/ClienteAptitudService.
        var fechaComercial = new DateOnly(2026, 6, 20);

        Assert.True(EstadoCuotaResolver.EstaVencidaDerivado(EstadoCuota.Parcial, Vencimiento, fechaComercial));
    }

    [Fact]
    public void EstaVencidaDerivado_Pagada_EsFalse_AunqueLaFechaHayaPasado()
    {
        var fechaComercial = new DateOnly(2026, 6, 20);

        Assert.False(EstadoCuotaResolver.EstaVencidaDerivado(EstadoCuota.Pagada, Vencimiento, fechaComercial));
    }

    [Fact]
    public void EstaVencidaDerivado_Cancelada_EsFalse_AunqueLaFechaHayaPasado()
    {
        var fechaComercial = new DateOnly(2026, 6, 20);

        Assert.False(EstadoCuotaResolver.EstaVencidaDerivado(EstadoCuota.Cancelada, Vencimiento, fechaComercial));
    }

    [Fact]
    public void DiasAtrasoDerivado_CuentaDiasCorridosDesdeElVencimiento()
    {
        var fechaComercial = new DateOnly(2026, 6, 20); // 5 días después

        var dias = EstadoCuotaResolver.DiasAtrasoDerivado(EstadoCuota.Vencida, Vencimiento, fechaComercial);

        Assert.Equal(5, dias);
    }

    [Fact]
    public void DiasAtrasoDerivado_NoVencida_EsCero()
    {
        var dias = EstadoCuotaResolver.DiasAtrasoDerivado(EstadoCuota.Pendiente, Vencimiento, new DateOnly(2026, 6, 15));

        Assert.Equal(0, dias);
    }

    // ---------------------------------------------------------------------------
    // EstaEnMoraCapitalDerivado — predicado canónico de mora de CAPITAL (PUN-ML7, auditoría lote 2)
    // ---------------------------------------------------------------------------

    private static readonly DateOnly FechaVencida = new(2026, 6, 20); // 5 días después de Vencimiento

    [Fact]
    public void EstaEnMoraCapitalDerivado_PendienteVencidaConSaldo_EsTrue()
    {
        Assert.True(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Pendiente, montoPagado: 0m, montoTotal: 1000m, Vencimiento, FechaVencida));
    }

    [Fact]
    public void EstaEnMoraCapitalDerivado_VencidaConSaldo_EsTrue()
    {
        Assert.True(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Vencida, montoPagado: 0m, montoTotal: 1000m, Vencimiento, FechaVencida));
    }

    [Fact]
    public void EstaEnMoraCapitalDerivado_ParcialVencidaConSaldoDeCapital_EsTrue()
    {
        Assert.True(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Parcial, montoPagado: 400m, montoTotal: 1000m, Vencimiento, FechaVencida));
    }

    [Fact]
    public void EstaEnMoraCapitalDerivado_ParcialNoVencida_EsFalse()
    {
        Assert.False(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Parcial, montoPagado: 400m, montoTotal: 1000m, Vencimiento, new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void EstaEnMoraCapitalDerivado_Pagada_EsFalse()
    {
        Assert.False(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Pagada, montoPagado: 1000m, montoTotal: 1000m, Vencimiento, FechaVencida));
    }

    [Fact]
    public void EstaEnMoraCapitalDerivado_Cancelada_EsFalse()
    {
        Assert.False(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Cancelada, montoPagado: 0m, montoTotal: 1000m, Vencimiento, FechaVencida));
    }

    [Fact]
    public void EstaEnMoraCapitalDerivado_CapitalSaldadoConPunitorioPendiente_EsFalse()
    {
        // Estado=Parcial porque EstadoCuotaResolver.Resolver retiene ahí una cuota con capital
        // saldado mientras haya punitorio aplicado pendiente (ver CapitalCero_ConPunitorioPendiente_
        // NoQuedaPagada más arriba) — pero sin saldo de CAPITAL no es mora de capital: ese
        // seguimiento es responsabilidad de PunitorioService, no de la cola de cobranza de capital.
        Assert.False(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Parcial, montoPagado: 1000m, montoTotal: 1000m, Vencimiento, FechaVencida));
    }

    [Theory]
    [InlineData(2026, 6, 15, false)] // día del vencimiento: no vencida
    [InlineData(2026, 6, 16, true)]  // día siguiente: vencida
    public void EstaEnMoraCapitalDerivado_FronteraDeVencimiento(int anio, int mes, int dia, bool esperado)
    {
        var fechaComercial = new DateOnly(anio, mes, dia);

        Assert.Equal(esperado, EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Pendiente, montoPagado: 0m, montoTotal: 1000m, Vencimiento, fechaComercial));
    }

    // ---------------------------------------------------------------------------
    // CSR-ML4 — regresión de mora/punitorio: una cuota de un plan "sin recargo" persiste con
    // MontoInteres=0 (MontoTotal == MontoCapital). Ni Resolver ni EstaEnMoraCapitalDerivado
    // reciben MontoInteres como parámetro — dependen solo de MontoTotal/MontoPagado/fechas — así
    // que esa cuota vence y entra en mora exactamente igual que cualquier otra. No se toca
    // producción: este test solo caracteriza el comportamiento ya vigente.
    // ---------------------------------------------------------------------------

    [Fact]
    public void CuotaConMontoInteresCeroCSR_VenceImpaga_EntraAVencidaIgualQueCualquierCuota()
    {
        var fechaComercial = new DateOnly(2026, 6, 16); // un día después del vencimiento

        var estado = EstadoCuotaResolver.Resolver(
            EstadoCuota.Pendiente, Vencimiento, fechaComercial,
            montoPagado: 0m, montoTotal: 1000m, // MontoTotal == MontoCapital (MontoInteres = 0)
            punitorioAplicadoPendiente: 0m);

        Assert.Equal(EstadoCuota.Vencida, estado);
    }

    [Fact]
    public void CuotaConMontoInteresCeroCSR_EstaEnMoraCapitalDerivado_IgualQueCualquierCuota()
    {
        Assert.True(EstadoCuotaResolver.EstaEnMoraCapitalDerivado(
            EstadoCuota.Pendiente, montoPagado: 0m, montoTotal: 1000m, Vencimiento, FechaVencida));
    }
}
