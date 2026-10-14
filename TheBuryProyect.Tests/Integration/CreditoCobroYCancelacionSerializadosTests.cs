using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-FIN-CONC-P0 — contrato SECUENCIAL (SQLite, sin concurrencia real) de los servicios reales:
/// el cobro re-comprueba el estado del crédito y la cancelación no deja pagos huérfanos.
/// La concurrencia real vive en Concurrency/CancelarVsPagoServiciosRealesSqlServerTests (SQL Server).
/// Sin HttpContext el usuario actual es "Sistema" (CurrentUserService), de ahí la apertura de caja.
/// </summary>
[Collection("HttpIntegration")]
public sealed class CreditoCobroYCancelacionSerializadosTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string UsuarioSinContexto = "Sistema";
    private readonly CustomWebApplicationFactory _factory;

    public CreditoCobroYCancelacionSerializadosTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<EscenarioSembrado> SembrarAsync(bool conIngreso = false)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var apertura = await ConcSeed.SembrarAperturaAsync(ctx, UsuarioSinContexto);
        return await ConcSeed.SembrarAsync(ctx, apertura, conIngreso, UsuarioSinContexto);
    }

    private static PagarCuotaViewModel Pago(EscenarioSembrado s, int i, decimal monto = 300m) => new()
    {
        CreditoId = s.CreditoId,
        CuotaId = s.CuotaIds[i],
        MontoPagado = monto,
        MedioPago = "Efectivo"
    };

    private async Task<(int pagos, int cobros, decimal montoPagado)> ContarAsync(EscenarioSembrado s)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pagos = await ctx.PagosCuota.CountAsync(p => p.Cuota.CreditoId == s.CreditoId);
        var cobros = await ctx.MovimientosCaja.CountAsync(m => m.Concepto == ConceptoMovimientoCaja.CobroCuota
            && m.ReferenciaId != null && s.CuotaIds.Contains(m.ReferenciaId.Value));
        var monto = await ctx.Cuotas.Where(c => c.CreditoId == s.CreditoId).SumAsync(c => c.MontoPagado);
        return (pagos, cobros, monto);
    }

    private async Task MarcarCreditoAsync(EscenarioSembrado s, EstadoCredito estado)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var credito = await ctx.Creditos.FirstAsync(c => c.Id == s.CreditoId);
        credito.Estado = estado;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Pago_ConCreditoActivo_SigueRegistrandose()
    {
        var s = await SembrarAsync();
        using var scope = _factory.Services.CreateScope();

        Assert.True(await scope.ServiceProvider.GetRequiredService<ICreditoService>().PagarCuotaAsync(Pago(s, 0)));

        Assert.Equal((1, 1, 300m), await ContarAsync(s));
    }

    [Theory]
    [InlineData(EstadoCredito.Cancelado)]
    [InlineData(EstadoCredito.Rechazado)]
    public async Task Pago_ConCreditoEnEstadoTerminal_SeRechazaSinEscribirAunqueLaCuotaSigaPendiente(EstadoCredito estado)
    {
        // Estado inconsistente a propósito (crédito terminal con cuota Pendiente): es lo que vería un
        // cobro que espera el lock mientras otra operación cancela el crédito. La cuota sola no basta.
        var s = await SembrarAsync();
        await MarcarCreditoAsync(s, estado);
        using var scope = _factory.Services.CreateScope();

        var ex = await Assert.ThrowsAsync<PagoCuotaRechazadoException>(
            () => scope.ServiceProvider.GetRequiredService<ICreditoService>().PagarCuotaAsync(Pago(s, 0)));

        Assert.Contains("no admite cobros", ex.Message);
        Assert.Equal((0, 0, 0m), await ContarAsync(s));
    }

    [Fact]
    public async Task PagoMultiple_ConCreditoCancelado_SeRechazaSinEscribir()
    {
        var s = await SembrarAsync();
        await MarcarCreditoAsync(s, EstadoCredito.Cancelado);

        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cuota = await ctx.Cuotas.AsNoTracking().FirstAsync(c => c.Id == s.CuotaIds[0]);

        await Assert.ThrowsAsync<PagoCuotaRechazadoException>(
            () => scope.ServiceProvider.GetRequiredService<ICreditoService>().PagarCuotasAsync(new PagoMultipleCuotasRequest
            {
                ClienteId = s.ClienteId,
                CuotaIds = [cuota.Id],
                RowVersionsPorCuota = new Dictionary<int, string> { [cuota.Id] = Convert.ToBase64String(cuota.RowVersion) },
                MedioPago = "Efectivo"
            }));

        Assert.Equal((0, 0, 0m), await ContarAsync(s));
    }

    [Fact]
    public async Task CancelarVenta_ConPagoYaAplicado_SeRechazaYNoTocaNada()
    {
        var s = await SembrarAsync();
        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ICreditoService>().PagarCuotaAsync(Pago(s, 1)));

        using (var scope = _factory.Services.CreateScope())
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(s.VentaId, "con pago"));
            Assert.Contains("pagos aplicados", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        using var check = _factory.Services.CreateScope();
        var ctx = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(EstadoVenta.Confirmada, (await ctx.Ventas.AsNoTracking().FirstAsync(v => v.Id == s.VentaId)).Estado);
        Assert.NotEqual(EstadoCredito.Cancelado, (await ctx.Creditos.AsNoTracking().FirstAsync(c => c.Id == s.CreditoId)).Estado);
        Assert.Equal((1, 1, 300m), await ContarAsync(s));
    }

    [Fact]
    public async Task CancelarVenta_SinPagos_CancelaVentaYCreditoYUnSoloContramovimiento_YElPagoPosteriorSeRechaza()
    {
        var s = await SembrarAsync(conIngreso: true);
        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(s.VentaId, "ok"));

        // Segunda cancelación: rechazada, sin segundo contramovimiento.
        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(s.VentaId, "otra vez"));

        // Pago posterior: rechazado, nada escrito.
        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAsync<PagoCuotaRechazadoException>(
                () => scope.ServiceProvider.GetRequiredService<ICreditoService>().PagarCuotaAsync(Pago(s, 0)));

        using var check = _factory.Services.CreateScope();
        var ctx = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(EstadoVenta.Cancelada, (await ctx.Ventas.AsNoTracking().FirstAsync(v => v.Id == s.VentaId)).Estado);
        Assert.Equal(EstadoCredito.Cancelado, (await ctx.Creditos.AsNoTracking().FirstAsync(c => c.Id == s.CreditoId)).Estado);
        Assert.Equal(1, await ctx.MovimientosCaja.CountAsync(m => m.VentaId == s.VentaId && m.Tipo == TipoMovimientoCaja.Egreso && !m.IsDeleted));
        Assert.Equal((0, 0, 0m), await ContarAsync(s));
    }
}
