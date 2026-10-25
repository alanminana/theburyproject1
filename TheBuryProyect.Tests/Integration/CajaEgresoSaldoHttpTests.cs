using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// FIN-S-007: la carga manual de egresos no validaba el saldo y dejaba la caja en negativo. Ahora un egreso no puede
/// superar el saldo real en efectivo de la apertura, salvo que lo cargue un supervisor de caja (Administrador/SuperAdmin).
/// </summary>
[Collection("HttpIntegration")]
public class CajaEgresoSaldoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string User = CustomWebApplicationFactory.CajaCierrePermsUserId;
    private readonly CustomWebApplicationFactory _factory;

    public CajaEgresoSaldoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<int> SembrarAperturaAsync(decimal fondo, bool asignarAUsuario)
    {
        await _factory.SeedUserWithCajaCierrePermissionAsync(User);
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aperturaId = await ConcSeed.SembrarAperturaAsync(ctx, User);
        var apertura = await ctx.AperturasCaja.FirstAsync(a => a.Id == aperturaId);
        apertura.MontoInicial = fondo;
        if (asignarAUsuario)
        {
            ctx.CajaVendedores.Add(new CajaVendedor { CajaId = apertura.CajaId, VendedorUserId = User });
        }
        await ctx.SaveChangesAsync();
        return aperturaId;
    }

    private static async Task<string> TokenAsync(HttpClient client, int aperturaId)
    {
        var response = await client.GetAsync($"/Caja/RegistrarMovimiento?aperturaId={aperturaId}");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> PostEgresoAsync(HttpClient client, string token, int aperturaId, string monto, string marca, string concepto = "10", string? observaciones = null) =>
        client.PostAsync("/Caja/RegistrarMovimiento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["AperturaCajaId"] = aperturaId.ToString(),
            ["IdempotencyKey"] = Guid.NewGuid().ToString(),
            ["Tipo"] = "1",
            ["Concepto"] = concepto,
            ["Descripcion"] = marca,
            ["Monto"] = monto,
            ["Observaciones"] = observaciones ?? ""
        }));

    private async Task<int> CantidadAsync(int aperturaId, string marca)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.AperturaCajaId == aperturaId && m.Descripcion == marca);
    }

    [Fact]
    public async Task NoSupervisor_EgresoMayorAlSaldo_SeRechazaYNoRegistraNada()
    {
        var aperturaId = await SembrarAperturaAsync(fondo: 100m, asignarAUsuario: true);
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca = "FIN-S-007 saldo " + Guid.NewGuid().ToString("N")[..8];

        var response = await PostEgresoAsync(client, token, aperturaId, "100,01", marca);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // vuelve al formulario con el error
        Assert.Contains("supera el efectivo disponible", html);
        Assert.Equal(0, await CantidadAsync(aperturaId, marca));
    }

    [Fact]
    public async Task NoSupervisor_TransferenciaAcreditada_NoHabilitaRetirarEfectivo()
    {
        // El dinero acreditado por transferencia no está físicamente en la caja: el cierre no lo cuenta como efectivo.
        var aperturaId = await SembrarAperturaAsync(fondo: 0m, asignarAUsuario: true);
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ctx.MovimientosCaja.Add(new MovimientoCaja
            {
                AperturaCajaId = aperturaId,
                FechaMovimiento = DateTime.UtcNow,
                Tipo = TheBuryProject.Models.Enums.TipoMovimientoCaja.Ingreso,
                Concepto = TheBuryProject.Models.Enums.ConceptoMovimientoCaja.VentaTransferencia,
                EstadoAcreditacion = TheBuryProject.Models.Enums.EstadoAcreditacionMovimientoCaja.Acreditado,
                TipoPago = TheBuryProject.Models.Enums.TipoPago.Transferencia,
                Monto = 1000m,
                Descripcion = "Transferencia acreditada",
                Usuario = "test",
                CreatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca = "FIN-S-007 transf " + Guid.NewGuid().ToString("N")[..8];

        var response = await PostEgresoAsync(client, token, aperturaId, "1000", marca);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await CantidadAsync(aperturaId, marca));
    }

    [Fact]
    public async Task NoSupervisor_ObservacionesNoOcultanEgresosDelEfectivo()
    {
        // Un egreso "Otro" cuyo texto libre dice "transferencia" no debe dejar de descontarse del efectivo disponible.
        var aperturaId = await SembrarAperturaAsync(fondo: 100m, asignarAUsuario: true);
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca1 = "FIN-S-007 obs a " + Guid.NewGuid().ToString("N")[..8];
        var marca2 = "FIN-S-007 obs b " + Guid.NewGuid().ToString("N")[..8];

        await PostEgresoAsync(client, token, aperturaId, "100", marca1, concepto: "99", observaciones: "transferencia");
        var segundo = await PostEgresoAsync(client, token, aperturaId, "100", marca2, concepto: "99", observaciones: "transferencia");

        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.Equal(1, await CantidadAsync(aperturaId, marca1));
        Assert.Equal(0, await CantidadAsync(aperturaId, marca2));
    }

    [Fact]
    public async Task NoSupervisor_EgresoIgualAlSaldo_SeRegistra()
    {
        var aperturaId = await SembrarAperturaAsync(fondo: 100m, asignarAUsuario: true);
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca = "FIN-S-007 igual " + Guid.NewGuid().ToString("N")[..8];

        var response = await PostEgresoAsync(client, token, aperturaId, "100", marca);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"Esperaba redirect y fue {(int)response.StatusCode}.");
        Assert.Equal(1, await CantidadAsync(aperturaId, marca));
    }

    [Fact]
    public async Task NoSupervisor_SegundoEgresoQueExcedeLoRestante_SeRechaza()
    {
        var aperturaId = await SembrarAperturaAsync(fondo: 100m, asignarAUsuario: true);
        var client = _factory.CreateClientWithUserId(User);
        var token = await TokenAsync(client, aperturaId);
        var marca1 = "FIN-S-007 a " + Guid.NewGuid().ToString("N")[..8];
        var marca2 = "FIN-S-007 b " + Guid.NewGuid().ToString("N")[..8];

        await PostEgresoAsync(client, token, aperturaId, "60", marca1);
        var segundo = await PostEgresoAsync(client, token, aperturaId, "60", marca2);

        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.Equal(1, await CantidadAsync(aperturaId, marca1));
        Assert.Equal(0, await CantidadAsync(aperturaId, marca2));
    }

    [Fact]
    public async Task Supervisor_PuedeRegistrarEgresoMayorAlSaldo()
    {
        // El cliente por defecto de las pruebas es Administrador (supervisor de caja).
        await _factory.SeedTestUserAsync();
        var aperturaId = await SembrarAperturaAsync(fondo: 100m, asignarAUsuario: false);
        var client = _factory.CreateClient();
        var token = await TokenAsync(client, aperturaId);
        var marca = "FIN-S-007 sup " + Guid.NewGuid().ToString("N")[..8];

        await PostEgresoAsync(client, token, aperturaId, "500", marca);

        // El cliente por defecto sigue la redirección: lo que importa es que el egreso quedó registrado.
        Assert.Equal(1, await CantidadAsync(aperturaId, marca));
    }
}
