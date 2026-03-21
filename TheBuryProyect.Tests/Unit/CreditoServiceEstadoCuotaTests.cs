using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para CreditoService.ResolverEstadoCuota.
///
/// Documenta el contrato de transición de estado extraído de
/// PagarCuotaAsync y AdelantarCuotaAsync.
///
/// No requiere DB ni infraestructura — función pura.
/// </summary>
public class CreditoServiceEstadoCuotaTests
{
    // ---------------------------------------------------------------------------
    // monto >= total → Pagada
    // ---------------------------------------------------------------------------

    [Fact]
    public void MontoIgualATotal_DevuelvePagada()
    {
        var estado = CreditoService.ResolverEstadoCuota(montoPagado: 100m, totalACobrar: 100m);

        Assert.Equal(EstadoCuota.Pagada, estado);
    }

    [Fact]
    public void MontoMayorQueTotal_DevuelvePagada()
    {
        var estado = CreditoService.ResolverEstadoCuota(montoPagado: 110m, totalACobrar: 100m);

        Assert.Equal(EstadoCuota.Pagada, estado);
    }

    // ---------------------------------------------------------------------------
    // monto > 0 y < total → Parcial
    // ---------------------------------------------------------------------------

    [Fact]
    public void MontoParcial_DevuelveParcial()
    {
        var estado = CreditoService.ResolverEstadoCuota(montoPagado: 50m, totalACobrar: 100m);

        Assert.Equal(EstadoCuota.Parcial, estado);
    }

    // ---------------------------------------------------------------------------
    // monto = 0 → Pendiente
    // ---------------------------------------------------------------------------

    [Fact]
    public void MontoCero_DevuelvePendiente()
    {
        var estado = CreditoService.ResolverEstadoCuota(montoPagado: 0m, totalACobrar: 100m);

        Assert.Equal(EstadoCuota.Pendiente, estado);
    }

    // ---------------------------------------------------------------------------
    // monto = total exacto (con punitorio) → Pagada
    // ---------------------------------------------------------------------------

    [Fact]
    public void MontoExactoConPunitorio_DevuelvePagada()
    {
        // Simula el caso de PagarCuotaAsync donde totalACobrar = MontoTotal + MontoPunitorio
        const decimal montoTotal = 500m;
        const decimal punitorio = 10m;
        var totalACobrar = montoTotal + punitorio;

        var estado = CreditoService.ResolverEstadoCuota(montoPagado: totalACobrar, totalACobrar: totalACobrar);

        Assert.Equal(EstadoCuota.Pagada, estado);
    }
}
