using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L1 — campos editables de Caja (Create y Abrir) por el pipeline HTTP real. Cada caso manda un valor
/// fuera de regla y exige que se rechace sin persistir nada; los válidos de borde exigen que se acepten.
/// </summary>
[Collection("HttpIntegration")]
public class CajaCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 92000;
    private readonly CustomWebApplicationFactory _factory;

    public CajaCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

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

    // ---- Create (AJAX: devuelve { ok } sin importar la vista) ----

    private async Task<bool> CrearCajaAsync(Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        var req = new HttpRequestMessage(HttpMethod.Post, "/Caja/Create") { Content = new FormUrlEncodedContent(campos) };
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean();
    }

    private static Dictionary<string, string> FormCaja(string codigo) => new()
    {
        ["Codigo"] = codigo,
        ["Nombre"] = "Caja de prueba",
        ["Activa"] = "true",
    };

    private async Task<int> CajasConCodigoAsync(string codigo)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.Cajas.AsNoTracking().CountAsync(c => c.Codigo == codigo);
    }

    [Theory]
    [InlineData("A B")]
    [InlineData("CAJA 01")]
    [InlineData("<script>")]
    [InlineData("caja/01")]
    [InlineData("-CAJA")]
    [InlineData("ñandú")]
    [InlineData("CAJA'01")]
    public async Task Create_Codigo_ConFormatoInvalido_SeRechaza(string codigo)
    {
        var ok = await CrearCajaAsync(FormCaja(codigo));

        Assert.False(ok, $"Codigo='{codigo}' fue ACEPTADO.");
        Assert.Equal(0, await CajasConCodigoAsync(codigo));
    }

    [Theory]
    [InlineData("CAJA-01")]
    [InlineData("caja_02")]
    [InlineData("C3")]
    public async Task Create_Codigo_Valido_SeAcepta(string codigo)
    {
        codigo = $"{codigo}-{Interlocked.Increment(ref _counter)}";

        Assert.True(await CrearCajaAsync(FormCaja(codigo)));
        Assert.Equal(1, await CajasConCodigoAsync(codigo));
    }

    [Fact]
    public async Task Create_Codigo_MasDe50Caracteres_SeRechaza()
    {
        var codigo = new string('A', 51);
        Assert.False(await CrearCajaAsync(FormCaja(codigo)));
        Assert.Equal(0, await CajasConCodigoAsync(codigo));
    }

    [Theory]
    [InlineData("Nombre", "   ")]
    [InlineData("Nombre", "")]
    [InlineData("Descripcion", "x")]   // 501 chars, se reemplaza abajo
    [InlineData("Sucursal", "x")]
    [InlineData("Ubicacion", "x")]
    public async Task Create_CamposDeTexto_FueraDeRegla_SeRechazan(string campo, string valor)
    {
        var codigo = $"TXT-{Interlocked.Increment(ref _counter)}";
        var form = FormCaja(codigo);
        form[campo] = valor == "x" ? new string('x', campo == "Descripcion" ? 501 : 101) : valor;

        Assert.False(await CrearCajaAsync(form));
        Assert.Equal(0, await CajasConCodigoAsync(codigo));
    }

    [Fact]
    public async Task Create_CodigoDuplicado_SeRechaza_SinImportarMayusculas()
    {
        var codigo = $"DUP-{Interlocked.Increment(ref _counter)}";
        Assert.True(await CrearCajaAsync(FormCaja(codigo)));

        Assert.False(await CrearCajaAsync(FormCaja(codigo)));
        Assert.False(await CrearCajaAsync(FormCaja(codigo.ToLowerInvariant())));
        Assert.Equal(1, await CajasConCodigoAsync(codigo) + await CajasConCodigoAsync(codigo.ToLowerInvariant()));
    }

    [Fact]
    public async Task Create_EstadoForjado_NoSePersiste()
    {
        var codigo = $"EST-{Interlocked.Increment(ref _counter)}";
        var form = FormCaja(codigo);
        form["Estado"] = "Abierta";

        Assert.True(await CrearCajaAsync(form));
        await using var ctx = await NuevoContextoAsync();
        var caja = await ctx.Cajas.AsNoTracking().SingleAsync(c => c.Codigo == codigo);
        Assert.Equal(TheBuryProject.Models.Enums.EstadoCaja.Cerrada, caja.Estado);
    }

    // ---- Abrir ----

    private async Task<int> SembrarCajaAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        await using var ctx = await NuevoContextoAsync();
        var caja = new Caja { Codigo = $"AB{n}", Nombre = $"Caja Abrir {n}", RowVersion = new byte[8], Activa = true };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();
        return caja.Id;
    }

    private async Task<(HttpStatusCode Status, int Aperturas)> AbrirAsync(int cajaId, Action<Dictionary<string, string>> ajuste)
    {
        var client = _factory.CreateAuthenticatedClient();
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(client),
            ["CajaId"] = cajaId.ToString(),
            ["MontoInicial"] = "1000",
        };
        ajuste(campos);
        var resp = await client.PostAsync("/Caja/Abrir", new FormUrlEncodedContent(campos));
        await using var ctx = await NuevoContextoAsync();
        var aperturas = await ctx.AperturasCaja.AsNoTracking().CountAsync(a => a.CajaId == cajaId);
        return (resp.StatusCode, aperturas);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("20000001")]
    [InlineData("1.500")]       // ambiguo (FIN-S-017)
    [InlineData("abc")]
    [InlineData("1000,555")]
    public async Task Abrir_MontoInicial_FueraDeRegla_SeRechaza(string monto)
    {
        var cajaId = await SembrarCajaAsync();

        var (_, aperturas) = await AbrirAsync(cajaId, f => f["MontoInicial"] = monto);

        Assert.Equal(0, aperturas);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1000,50")]
    [InlineData("20000000")]
    public async Task Abrir_MontoInicial_ValidoDeBorde_SeAcepta(string monto)
    {
        var cajaId = await SembrarCajaAsync();

        var (_, aperturas) = await AbrirAsync(cajaId, f => f["MontoInicial"] = monto);

        Assert.Equal(1, aperturas);
    }

    [Fact]
    public async Task Abrir_Observaciones_MasDe500Caracteres_SeRechaza()
    {
        var cajaId = await SembrarCajaAsync();

        var (_, aperturas) = await AbrirAsync(cajaId, f => f["ObservacionesApertura"] = new string('x', 501));

        Assert.Equal(0, aperturas);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("99999999")]
    public async Task Abrir_CajaInexistente_NoCreaApertura(string cajaId)
    {
        var (_, _) = await AbrirAsync(int.Parse(cajaId), f => { });

        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(0, await ctx.AperturasCaja.AsNoTracking().CountAsync(a => a.CajaId == int.Parse(cajaId)));
    }

    [Fact]
    public async Task Abrir_DosVecesLaMismaCaja_NoDuplicaApertura()
    {
        var cajaId = await SembrarCajaAsync();
        await AbrirAsync(cajaId, f => { });

        var (_, aperturas) = await AbrirAsync(cajaId, f => { });

        Assert.Equal(1, aperturas);
    }
}
