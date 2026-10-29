using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// SEG-BAJA (auditoría dinámica 2026-10-28): los GET no deben tener efectos secundarios. Reimprimir pasó a POST
/// con antiforgery (ver DocumentosDescuentoPermisoHttpTests); CuotasVencidas es lectura pura y se fija aquí.
/// </summary>
[Collection("HttpIntegration")]
public sealed class SegBajaGetSinEfectosHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SegBajaGetSinEfectosHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CuotasVencidas_Get_NoEscribeEnBaseDeDatos()
    {
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);

        var antes = await HuellaAsync();
        var response = await client.GetAsync("/Credito/CuotasVencidas");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var despues = await HuellaAsync();

        Assert.Equal(antes, despues);
    }

    [Fact]
    public async Task CuotasVencidas_PostNoEstaPermitido()
    {
        await _factory.SeedUserWithCreditoViewPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.CreditoViewPermsUserId);
        using var vacio = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/Credito/CuotasVencidas", vacio)).StatusCode);
    }

    private async Task<(int Alertas, int Cuotas, int Eventos, DateTime? UltimaCuota)> HuellaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (
            await ctx.AlertasCobranza.IgnoreQueryFilters().CountAsync(),
            await ctx.Cuotas.IgnoreQueryFilters().CountAsync(),
            await ctx.SeguridadEventosAuditoria.CountAsync(),
            await ctx.Cuotas.IgnoreQueryFilters().MaxAsync(c => (DateTime?)c.UpdatedAt));
    }
}
