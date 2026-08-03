using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para <see cref="CreditoService.DistribuirPago"/> (PUN-ML6): distribuidor
/// canónico de un pago entre punitorio aplicado pendiente y saldo de cuota, prioridad estricta
/// punitorio → cuota. Función pura — no requiere DB ni infraestructura.
/// </summary>
public class CreditoServiceDistribuirPagoTests
{
    [Fact]
    public void Pago80_Punitorio100_Cuota1000_TodoAPunitorio()
    {
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 80m, punitorioPendiente: 100m, saldoCuota: 1_000m);

        Assert.Equal(80m, aplicadoPunitorio);
        Assert.Equal(0m, aplicadoCuota);
        Assert.Equal(0m, excedente);
    }

    [Fact]
    public void Pago100_Punitorio100_Cuota1000_SaldaExactoElPunitorio()
    {
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 100m, punitorioPendiente: 100m, saldoCuota: 1_000m);

        Assert.Equal(100m, aplicadoPunitorio);
        Assert.Equal(0m, aplicadoCuota);
        Assert.Equal(0m, excedente);
    }

    [Fact]
    public void Pago150_Punitorio100_Cuota1000_RemanenteAcuota()
    {
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 150m, punitorioPendiente: 100m, saldoCuota: 1_000m);

        Assert.Equal(100m, aplicadoPunitorio);
        Assert.Equal(50m, aplicadoCuota);
        Assert.Equal(0m, excedente);
    }

    [Fact]
    public void Pago1100_Punitorio100_Cuota1000_CancelaAmbosSinExcedente()
    {
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 1_100m, punitorioPendiente: 100m, saldoCuota: 1_000m);

        Assert.Equal(100m, aplicadoPunitorio);
        Assert.Equal(1_000m, aplicadoCuota);
        Assert.Equal(0m, excedente);
    }

    [Fact]
    public void SinPunitorio_Pago150_Cuota1000_TodoACuota()
    {
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 150m, punitorioPendiente: 0m, saldoCuota: 1_000m);

        Assert.Equal(0m, aplicadoPunitorio);
        Assert.Equal(150m, aplicadoCuota);
        Assert.Equal(0m, excedente);
    }

    [Fact]
    public void PagoSuperaTotal_ExcedenteQuedaExplicito()
    {
        // El distribuidor no rechaza sobrepagos: eso es política de ResolverCobroCuotaAsync, más
        // arriba en la pila. El distribuidor en sí sólo reparte y expone el remanente.
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 1_300m, punitorioPendiente: 100m, saldoCuota: 1_000m);

        Assert.Equal(100m, aplicadoPunitorio);
        Assert.Equal(1_000m, aplicadoCuota);
        Assert.Equal(200m, excedente);
    }

    [Fact]
    public void PunitorioPendienteNegativo_SeTrataComoCero()
    {
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 50m, punitorioPendiente: -10m, saldoCuota: 1_000m);

        Assert.Equal(0m, aplicadoPunitorio);
        Assert.Equal(50m, aplicadoCuota);
        Assert.Equal(0m, excedente);
    }

    [Fact]
    public void SaldoCuotaNegativo_SeTrataComoCero()
    {
        var (aplicadoPunitorio, aplicadoCuota, excedente) =
            CreditoService.DistribuirPago(importeDisponible: 50m, punitorioPendiente: 20m, saldoCuota: -5m);

        Assert.Equal(20m, aplicadoPunitorio);
        Assert.Equal(0m, aplicadoCuota);
        Assert.Equal(30m, excedente);
    }
}
