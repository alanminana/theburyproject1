using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H11: un valor de crédito personalizado guardado antes de los topes de validación (monto máximo 100.000.000 contra el tope
/// de 20.000.000) no debe impedir editar datos ajenos al crédito, pero cambiarlo a un valor fuera de tope se rechaza igual.
/// Por el pipeline HTTP real (antiforgery + autorización + binding + servicio).
/// </summary>
[Collection("HttpIntegration.4")]
public class ClienteEditValoresHistoricosCreditoHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const decimal MaximoHistorico = 100_000_000m;
    private static int _counter = 94000;
    private readonly CustomWebApplicationFactory _factory;

    public ClienteEditValoresHistoricosCreditoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedTestUserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<(int Id, string Dni)> SembrarClienteConMaximoHistoricoAsync()
    {
        var dni = (40_000_000 + Interlocked.Increment(ref _counter)).ToString();
        await using var ctx = await NuevoContextoAsync();
        var cliente = new Cliente
        {
            Nombre = "Historico", Apellido = "Credito", NumeroDocumento = dni, TipoDocumento = "DNI",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, Domicilio = "Calle 1",
            MontoMinimoPersonalizado = 5_000m, MontoMaximoPersonalizado = MaximoHistorico
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return (cliente.Id, dni);
    }

    private async Task<(bool Exito, string Cuerpo)> EditarAsync(int id, string dni, string telefono, string maximo)
    {
        var client = _factory.CreateAuthenticatedClient();
        await using var ctx = await NuevoContextoAsync();
        var rowVersion = await ctx.Clientes.AsNoTracking().Where(x => x.Id == id).Select(x => x.RowVersion).SingleAsync();
        var campos = new Dictionary<string, string>
        {
            ["RowVersion"] = Convert.ToBase64String(rowVersion),
            ["__RequestVerificationToken"] = await TokenAsync(client),
            ["Id"] = id.ToString(),
            ["TipoDocumento"] = "DNI",
            ["NumeroDocumento"] = dni,
            ["Apellido"] = "Credito",
            ["Nombre"] = "Historico",
            ["Domicilio"] = "Calle 1",
            ["Telefono"] = telefono,
            ["NivelRiesgo"] = ((int)NivelRiesgoCredito.AprobadoTotal).ToString(),
            ["MontoMinimoPersonalizado"] = "5000",
            ["MontoMaximoPersonalizado"] = maximo,
        };
        var resp = await client.PostAsync("/Cliente/EditAjax", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var cuerpo = await resp.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(cuerpo).RootElement.GetProperty("success").GetBoolean(), cuerpo);
    }

    private async Task<(string? Telefono, decimal? Maximo)> LeerAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        var c = await ctx.Clientes.AsNoTracking().SingleAsync(x => x.Id == id);
        return (c.Telefono, c.MontoMaximoPersonalizado);
    }

    [Fact]
    public async Task Editar_Otro_Dato_Con_El_Maximo_Historico_Sin_Cambios_Guarda()
    {
        var (id, dni) = await SembrarClienteConMaximoHistoricoAsync();

        var (exito, cuerpo) = await EditarAsync(id, dni, "1144556677", MaximoHistorico.ToString("0"));

        Assert.True(exito, cuerpo);
        var (telefono, maximo) = await LeerAsync(id);
        Assert.Equal("1144556677", telefono);
        Assert.Equal(MaximoHistorico, maximo);
    }

    [Theory]
    [InlineData("30000000")]   // un valor nuevo fuera de tope se rechaza aunque el histórico fuera mayor
    [InlineData("20000001")]
    [InlineData("-1")]
    public async Task Cambiar_El_Maximo_A_Un_Valor_Fuera_De_Tope_Se_Rechaza_Y_No_Persiste(string nuevo)
    {
        var (id, dni) = await SembrarClienteConMaximoHistoricoAsync();

        var (exito, _) = await EditarAsync(id, dni, "1144556677", nuevo);

        Assert.False(exito);
        var (telefono, maximo) = await LeerAsync(id);
        Assert.NotEqual("1144556677", telefono);
        Assert.Equal(MaximoHistorico, maximo);
    }

    [Fact]
    public async Task Cambiar_El_Maximo_A_Un_Valor_Dentro_De_Tope_Guarda()
    {
        var (id, dni) = await SembrarClienteConMaximoHistoricoAsync();

        var (exito, cuerpo) = await EditarAsync(id, dni, "1144556677", "15000000");

        Assert.True(exito, cuerpo);
        Assert.Equal(15_000_000m, (await LeerAsync(id)).Maximo);
    }
}
