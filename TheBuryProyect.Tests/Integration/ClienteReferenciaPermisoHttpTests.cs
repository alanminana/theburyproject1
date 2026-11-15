using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-02 AC-02-3 y AC-02-5 por el pipeline HTTP real (antiforgery + autorización + binding + servicio):
/// con solo clientes.view las referencias se ven pero no se crean, editan, verifican ni dan de baja;
/// con clientes.edit sí; y el id de una referencia siempre se resuelve dentro del cliente de la ruta.
/// </summary>
[Collection("HttpIntegration.4")]
public class ClienteReferenciaPermisoHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _dni = 36_000_000;
    private readonly CustomWebApplicationFactory _factory;

    public ClienteReferenciaPermisoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        await _factory.SeedUserWithClienteSoloLecturaAsync();
        await _factory.SeedUserWithClienteEditorAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<AppDbContext> NuevoContextoAsync()
    {
        var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await contextFactory.CreateDbContextAsync();
    }

    private async Task<int> SembrarClienteAsync(string nombre = "Titular")
    {
        await using var ctx = await NuevoContextoAsync();
        var cliente = new Cliente
        {
            Nombre = nombre, Apellido = "Referencias", TipoDocumento = "DNI",
            NumeroDocumento = Interlocked.Increment(ref _dni).ToString(), Domicilio = "Calle 1",
            Telefono = "3415551000", NivelRiesgo = NivelRiesgoCredito.AprobadoCondicional, Activo = true
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return cliente.Id;
    }

    private async Task<int> SembrarReferenciaAsync(int clienteId, string nombre = "Maria Ref", string telefono = "3414440001")
    {
        await using var ctx = await NuevoContextoAsync();
        var r = new ClienteReferencia
        {
            ClienteId = clienteId, Tipo = TipoReferenciaCliente.Personal, Nombre = nombre,
            Telefono = telefono, TelefonoNormalizado = telefono, Relacion = "Amiga"
        };
        ctx.ClienteReferencias.Add(r);
        await ctx.SaveChangesAsync();
        return r.Id;
    }

    private async Task<ClienteReferencia> LeerAsync(int id)
    {
        await using var ctx = await NuevoContextoAsync();
        return await ctx.ClienteReferencias.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object? cuerpo)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = cuerpo == null ? new StringContent(string.Empty) : JsonContent.Create(cuerpo)
        };
        req.Headers.Add("RequestVerificationToken", await TokenAsync(client));
        return await client.SendAsync(req);
    }

    private static object NuevaReferencia(string nombre = "Pedro Nuevo", string telefono = "341 444-0002", string? rowVersion = null) =>
        new { tipo = 1, nombre, telefono, relacion = "Vecino", rowVersion };

    private async Task<string> RvAsync(int id) => Convert.ToBase64String((await LeerAsync(id)).RowVersion);

    private static void AssertDenegado(HttpResponseMessage resp) =>
        Assert.True(resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Redirect,
            $"Se esperaba 403 o redirección a acceso denegado, llegó {(int)resp.StatusCode}.");

    // ── AC-02-3 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SoloLectura_VeLasReferenciasPeroNoRecibeNingunaAccionDeEdicion()
    {
        var clienteId = await SembrarClienteAsync();
        await SembrarReferenciaAsync(clienteId, "Maria Visible");
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteSoloLecturaUserId);

        var resp = await client.GetAsync($"/Cliente/Details/{clienteId}");
        var html = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("id=\"referencias-card\"", html);
        Assert.Contains("Maria Visible", html);
        Assert.DoesNotContain("data-ref-agregar", html);
        Assert.DoesNotContain("data-ref-editar", html);
        Assert.DoesNotContain("data-ref-baja", html);
        Assert.DoesNotContain("data-ref-verificar", html);
        Assert.DoesNotContain("id=\"referenciaModal\"", html);
    }

    [Fact]
    public async Task SoloLectura_PuedePedirElListado()
    {
        var clienteId = await SembrarClienteAsync();
        await SembrarReferenciaAsync(clienteId, "Maria Listada");
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteSoloLecturaUserId);

        var resp = await client.GetAsync($"/ClienteReferencia/Lista?clienteId={clienteId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Maria Listada", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SoloLectura_NoPuedeCrearEditarVerificarNiDarDeBaja_YNoCambiaNada()
    {
        var clienteId = await SembrarClienteAsync();
        var refId = await SembrarReferenciaAsync(clienteId, "Intacta");
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteSoloLecturaUserId);

        AssertDenegado(await PostJsonAsync(client, $"/ClienteReferencia/Crear?clienteId={clienteId}", NuevaReferencia()));
        AssertDenegado(await PostJsonAsync(client, $"/ClienteReferencia/Editar?clienteId={clienteId}&id={refId}", NuevaReferencia("Cambiada", "341 444-0777")));
        AssertDenegado(await PostJsonAsync(client, $"/ClienteReferencia/Verificar?clienteId={clienteId}&id={refId}", new { estado = 2 }));
        AssertDenegado(await PostJsonAsync(client, $"/ClienteReferencia/Baja?clienteId={clienteId}&id={refId}", null));

        var bd = await LeerAsync(refId);
        Assert.Equal("Intacta", bd.Nombre);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
        Assert.False(bd.IsDeleted);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(1, await ctx.ClienteReferencias.CountAsync(r => r.ClienteId == clienteId));
    }

    [Fact]
    public async Task SinPermisoDeClientes_NoPuedeVerElListado()
    {
        var clienteId = await SembrarClienteAsync();
        using var client = _factory.CreateClientWithUserId("usuario-sin-permisos-referencias");

        AssertDenegado(await client.GetAsync($"/ClienteReferencia/Lista?clienteId={clienteId}"));
    }

    [Fact]
    public async Task Editor_RecibeLasAccionesYPuedeCrearEditarVerificarYDarDeBaja()
    {
        var clienteId = await SembrarClienteAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteEditorUserId);

        var crear = await PostJsonAsync(client, $"/ClienteReferencia/Crear?clienteId={clienteId}", NuevaReferencia());
        Assert.Equal(HttpStatusCode.OK, crear.StatusCode);
        var id = (await crear.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal("Pedro Nuevo", (await LeerAsync(id)).Nombre);

        var html = await (await client.GetAsync($"/Cliente/Details/{clienteId}")).Content.ReadAsStringAsync();
        Assert.Contains("data-ref-agregar", html);
        Assert.Contains("data-ref-editar", html);
        Assert.Contains("id=\"referenciaModal\"", html);
        Assert.Contains("role=\"dialog\"", html);

        var editar = await PostJsonAsync(client, $"/ClienteReferencia/Editar?clienteId={clienteId}&id={id}", NuevaReferencia("Pedro Editado", "341 444-0002", await RvAsync(id)));
        Assert.Equal(HttpStatusCode.OK, editar.StatusCode);
        Assert.Equal("Pedro Editado", (await LeerAsync(id)).Nombre);

        var verificar = await PostJsonAsync(client, $"/ClienteReferencia/Verificar?clienteId={clienteId}&id={id}", new { estado = 2, observaciones = "Confirmó", rowVersion = await RvAsync(id) });
        Assert.Equal(HttpStatusCode.OK, verificar.StatusCode);
        var verificada = await LeerAsync(id);
        Assert.Equal(EstadoVerificacionReferencia.Verificada, verificada.EstadoVerificacion);
        Assert.NotNull(verificada.VerificadaPor);

        var baja = await PostJsonAsync(client, $"/ClienteReferencia/Baja?clienteId={clienteId}&id={id}", new { rowVersion = await RvAsync(id) });
        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);
        Assert.True((await LeerAsync(id)).IsDeleted);
    }

    [Fact]
    public async Task Editor_ConDatosInvalidos_RecibeErrorYNoSeGuarda()
    {
        var clienteId = await SembrarClienteAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteEditorUserId);

        var sinNombre = await PostJsonAsync(client, $"/ClienteReferencia/Crear?clienteId={clienteId}", NuevaReferencia("", "341 444-0003"));
        var telefonoPropio = await PostJsonAsync(client, $"/ClienteReferencia/Crear?clienteId={clienteId}", NuevaReferencia("Mismo Tel", "341 555-1000"));

        Assert.Equal(HttpStatusCode.BadRequest, sinNombre.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, telefonoPropio.StatusCode);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(0, await ctx.ClienteReferencias.CountAsync(r => r.ClienteId == clienteId));
    }

    [Fact]
    public async Task Editor_SinRowVersion_NoPuedeEditarVerificarNiDarDeBaja()
    {
        var clienteId = await SembrarClienteAsync();
        var refId = await SembrarReferenciaAsync(clienteId, "Intacta");
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteEditorUserId);

        var editar = await PostJsonAsync(client, $"/ClienteReferencia/Editar?clienteId={clienteId}&id={refId}", NuevaReferencia("Cambiada", "341 444-0777"));
        var verificar = await PostJsonAsync(client, $"/ClienteReferencia/Verificar?clienteId={clienteId}&id={refId}", new { estado = 2 });
        var baja = await PostJsonAsync(client, $"/ClienteReferencia/Baja?clienteId={clienteId}&id={refId}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, editar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, verificar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, baja.StatusCode);
        var bd = await LeerAsync(refId);
        Assert.Equal("Intacta", bd.Nombre);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
        Assert.False(bd.IsDeleted);
    }

    [Fact]
    public async Task Editor_SinTokenAntiforgery_SeRechaza()
    {
        var clienteId = await SembrarClienteAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteEditorUserId);

        var resp = await client.PostAsJsonAsync($"/ClienteReferencia/Crear?clienteId={clienteId}", NuevaReferencia());

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await using var ctx = await NuevoContextoAsync();
        Assert.Equal(0, await ctx.ClienteReferencias.CountAsync(r => r.ClienteId == clienteId));
    }

    // ── AC-02-5 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Aislamiento_UnEditorNoPuedeOperarUnaReferenciaDeOtroClientePasandoSuId()
    {
        var clienteA = await SembrarClienteAsync("A");
        var clienteB = await SembrarClienteAsync("B");
        var refDeA = await SembrarReferenciaAsync(clienteA, "Solo de A");
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.ClienteEditorUserId);

        var editar = await PostJsonAsync(client, $"/ClienteReferencia/Editar?clienteId={clienteB}&id={refDeA}", NuevaReferencia("Robada", "341 444-0888", await RvAsync(refDeA)));
        var verificar = await PostJsonAsync(client, $"/ClienteReferencia/Verificar?clienteId={clienteB}&id={refDeA}", new { estado = 2, rowVersion = await RvAsync(refDeA) });
        var baja = await PostJsonAsync(client, $"/ClienteReferencia/Baja?clienteId={clienteB}&id={refDeA}", new { rowVersion = await RvAsync(refDeA) });

        Assert.Equal(HttpStatusCode.BadRequest, editar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, verificar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, baja.StatusCode);
        var bd = await LeerAsync(refDeA);
        Assert.Equal("Solo de A", bd.Nombre);
        Assert.Equal(EstadoVerificacionReferencia.Pendiente, bd.EstadoVerificacion);
        Assert.False(bd.IsDeleted);

        var listadoDeB = await (await client.GetAsync($"/ClienteReferencia/Lista?clienteId={clienteB}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Solo de A", listadoDeB);
        var detalleDeB = await (await client.GetAsync($"/Cliente/Details/{clienteB}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Solo de A", detalleDeB);
    }
}
