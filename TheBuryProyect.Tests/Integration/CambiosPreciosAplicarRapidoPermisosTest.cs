using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// El cambio rápido de precios simula, APRUEBA y aplica en un solo paso. Con solo precios.apply se saltaba la aprobación que el
/// circuito normal reserva a precios.approve (separación de funciones). Ahora exige también simular y aprobar.
/// </summary>
[Collection("HttpIntegration")]
public class CambiosPreciosAplicarRapidoPermisosTest : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CambiosPreciosAplicarRapidoPermisosTest(CustomWebApplicationFactory factory) => _factory = factory;

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> AplicarRapidoAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/CambiosPrecios/AplicarRapido")
        {
            Content = new StringContent(
                "{\"modo\":\"seleccionados\",\"porcentaje\":10,\"productoIds\":[999999]}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Add("RequestVerificationToken", await TokenAsync(client));
        return await client.SendAsync(request);
    }

    private async Task<int> CantidadBatchesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        return await ctx.PriceChangeBatches.CountAsync();
    }

    [Fact]
    public async Task SoloConApply_NoPuedeAprobarPorElAtajo_403YNoCreaNingunBatch()
    {
        await _factory.SeedUserWithPreciosSoloAplicarAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosSoloAplicarUserId);
        var antes = await CantidadBatchesAsync();

        var respuesta = await AplicarRapidoAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains("simular y aprobar", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(antes, await CantidadBatchesAsync());
    }

    [Fact]
    public async Task ConSimularAprobarYAplicar_LlegaALaLogicaDelCambioRapido()
    {
        await _factory.SeedUserWithPreciosCambioRapidoCompletoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosCambioRapidoUserId);

        var respuesta = await AplicarRapidoAsync(client);

        // Producto inexistente: la lógica responde 400 "No se encontraron productos válidos" (no 403 por permisos).
        Assert.NotEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.DoesNotContain("simular y aprobar", await respuesta.Content.ReadAsStringAsync());
    }

    private async Task<(int Id, string RowVersion)> SembrarBatchSimuladoAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        var batch = new TheBuryProject.Models.Entities.PriceChangeBatch
        {
            Nombre = "Simulado sin aprobar " + Guid.NewGuid().ToString("N")[..6],
            TipoCambio = TheBuryProject.Models.Enums.TipoCambio.PorcentajeSobrePrecioActual,
            TipoAplicacion = TheBuryProject.Models.Enums.TipoAplicacion.Aumento,
            ValorCambio = 10m,
            AlcanceJson = "{}",
            ListasAfectadasJson = "[]",
            Estado = TheBuryProject.Models.Enums.EstadoBatch.Simulado,
            RowVersion = new byte[8]
        };
        ctx.PriceChangeBatches.Add(batch);
        await ctx.SaveChangesAsync();
        await ctx.Entry(batch).ReloadAsync();
        return (batch.Id, Convert.ToBase64String(batch.RowVersion));
    }

    [Fact]
    public async Task Catalogo_AplicarCambioPrecios_SoloConApply_NoAutoapruebaUnLoteSimulado()
    {
        // Segundo camino de autoaprobacion (Catalogo): aplicar un lote ya aprobado se permite con precios.apply, pero aprobar
        // uno simulado de paso requiere precios.approve.
        await _factory.SeedUserWithPreciosSoloAplicarAsync();
        var (id, rowVersion) = await SembrarBatchSimuladoAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PreciosSoloAplicarUserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Catalogo/AplicarCambioPrecios")
        {
            Content = new StringContent(
                $"{{\"batchId\":{id},\"rowVersion\":\"{rowVersion}\"}}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", await TokenAsync(client));

        var respuesta = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("permiso de aprobar", await respuesta.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        var batch = await ctx.PriceChangeBatches.AsNoTracking().FirstAsync(b => b.Id == id);
        Assert.Equal(TheBuryProject.Models.Enums.EstadoBatch.Simulado, batch.Estado);
    }
}
