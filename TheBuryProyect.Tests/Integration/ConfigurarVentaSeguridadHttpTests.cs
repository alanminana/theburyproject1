using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PROMPT 90: enforcement REAL (pipeline HTTP completo) de Credito/ConfigurarVenta.
/// Dos clientes, dos créditos y dos ventas (cada venta apunta a su crédito):
/// - creditos.view NO alcanza para ver ni modificar la configuración (exige creditos.configure);
/// - con permiso, ids cruzados (GET/POST) y ClienteId manipulado se rechazan sin modificar datos.
/// </summary>
[Collection("HttpIntegration")]
public class ConfigurarVentaSeguridadHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];
    private string _soloViewId = null!;
    private string _configuradorId = null!;
    private string _vendedorId = null!;
    private string _cajeroId = null!;
    private string _contadorId = null!;
    private (int ClienteId, int CreditoId, int VentaId) _a;
    private (int ClienteId, int CreditoId, int VentaId) _b;

    public ConfigurarVentaSeguridadHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolesPermisosSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());

        var rolView = $"CredView_{_sfx}";
        await CrearRolConPermisosAsync(context, rolView, "creditos.view");
        var rolConfigurar = $"CredCfg_{_sfx}";
        await CrearRolConPermisosAsync(context, rolConfigurar, "creditos.view", "creditos.configure");

        _soloViewId = await CrearUsuarioAsync(scope, $"cv_{_sfx}", rolView);
        _configuradorId = await CrearUsuarioAsync(scope, $"cc_{_sfx}", rolConfigurar);
        _vendedorId = await CrearUsuarioAsync(scope, $"vend_{_sfx}", Roles.Vendedor);
        _cajeroId = await CrearUsuarioAsync(scope, $"caj_{_sfx}", Roles.Cajero);
        _contadorId = await CrearUsuarioAsync(scope, $"cont_{_sfx}", Roles.Contador);

        _a = await SembrarCircuitoAsync(context, "A");
        _b = await SembrarCircuitoAsync(context, "B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UsuarioConSoloCreditosView_NoPuedeVerNiConfigurar_Get()
    {
        var client = _factory.CreateClientWithUserId(_soloViewId);

        var response = await client.GetAsync($"/Credito/ConfigurarVenta?id={_a.CreditoId}&ventaId={_a.VentaId}");

        Assert.True(Denegado(response), $"GET respondió {(int)response.StatusCode} a un usuario con solo creditos.view.");
    }

    [Fact]
    public async Task UsuarioConSoloCreditosView_NoPuedeConfigurar_Post_YNoSeModificaNada()
    {
        var client = _factory.CreateClientWithUserId(_soloViewId);

        var response = await PostConfigurarAsync(client, _a.CreditoId, _a.VentaId, _a.ClienteId);

        Assert.True(Denegado(response), $"POST respondió {(int)response.StatusCode} a un usuario con solo creditos.view.");
        await AssertCircuitoIntactoAsync(_a);
        await AssertCircuitoIntactoAsync(_b);
    }

    [Fact]
    public async Task RolesSinConfigure_Cajero_Contador_SonDenegados_Y_Vendedor_AtraviesaElFiltro()
    {
        var url = $"/Credito/ConfigurarVenta?id={_a.CreditoId}&ventaId={_a.VentaId}&embedded=true";

        Assert.True(Denegado(await _factory.CreateClientWithUserId(_cajeroId).GetAsync(url)), "Cajero no debe configurar créditos.");
        Assert.True(Denegado(await _factory.CreateClientWithUserId(_contadorId).GetAsync(url)), "Contador no debe configurar créditos.");
        Assert.False(Denegado(await _factory.CreateClientWithUserId(_vendedorId).GetAsync(url)), "Vendedor arma el crédito en el wizard de Venta.");
    }

    [Theory]
    [InlineData("A", "B")] // crédito del cliente A con la venta del cliente B
    [InlineData("B", "A")]
    public async Task Get_IdsCruzados_EmbebidoRechaza409_SinDevolverElFormulario(string credito, string venta)
    {
        var client = _factory.CreateClientWithUserId(_configuradorId);
        var c = Circuito(credito);
        var v = Circuito(venta);

        var response = await client.GetAsync($"/Credito/ConfigurarVenta?id={c.CreditoId}&ventaId={v.VentaId}&embedded=true");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body.Replace(" ", string.Empty));
        await AssertCircuitoIntactoAsync(_a);
        await AssertCircuitoIntactoAsync(_b);
    }

    [Fact]
    public async Task Get_CreditoConVentaSinVentaId_EmbebidoRechaza409()
    {
        var client = _factory.CreateClientWithUserId(_configuradorId);

        var response = await client.GetAsync($"/Credito/ConfigurarVenta?id={_a.CreditoId}&embedded=true");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("A", "B")]
    [InlineData("B", "A")]
    public async Task Post_IdsCruzadosYClienteManipulado_Rechaza409_YNoModificaDatos(string credito, string venta)
    {
        var client = _factory.CreateClientWithUserId(_configuradorId);
        var c = Circuito(credito);
        var v = Circuito(venta);

        // ClienteId del formulario = el de la venta ajena (intento de condicionar tasas/rangos).
        var response = await PostConfigurarAsync(client, c.CreditoId, v.VentaId, v.ClienteId, embedded: true);

        Assert.True(response.StatusCode == HttpStatusCode.Conflict,
            $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await AssertCircuitoIntactoAsync(_a);
        await AssertCircuitoIntactoAsync(_b);
    }

    [Fact]
    public async Task Post_VentaInexistente_Rechaza404_YNoModificaDatos()
    {
        var client = _factory.CreateClientWithUserId(_configuradorId);

        var response = await PostConfigurarAsync(client, _a.CreditoId, ventaId: 987_654, _a.ClienteId, embedded: true);

        Assert.True(response.StatusCode == HttpStatusCode.NotFound,
            $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        await AssertCircuitoIntactoAsync(_a);
        await AssertCircuitoIntactoAsync(_b);
    }

    [Fact]
    public async Task SimularPlanVenta_ConVentaId_ExigeConfigure_SinVentaIdBastaView()
    {
        var viewOnly = _factory.CreateClientWithUserId(_soloViewId);
        var configurador = _factory.CreateClientWithUserId(_configuradorId);
        var conVenta = $"/Credito/SimularPlanVenta?totalVenta=1&cuotas=6&ventaId={_b.VentaId}";
        var sinVenta = "/Credito/SimularPlanVenta?totalVenta=1000&cuotas=6";

        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.GetAsync(conVenta)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await configurador.GetAsync(conVenta)).StatusCode);
        // Simulación standalone (Cotizador): sigue siendo de lectura.
        Assert.False(Denegado(await viewOnly.GetAsync(sinVenta)));
    }

    [Fact]
    public async Task Get_y_Post_VentaCancelada_EmbebidoRechaza409_YNoModificaCreditoNiVenta()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var venta = await context.Ventas.SingleAsync(v => v.Id == _a.VentaId);
            venta.Estado = EstadoVenta.Cancelada;
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithUserId(_configuradorId);
        var get = await client.GetAsync($"/Credito/ConfigurarVenta?id={_a.CreditoId}&ventaId={_a.VentaId}&embedded=true");
        var post = await PostConfigurarAsync(client, _a.CreditoId, _a.VentaId, _a.ClienteId, embedded: true);

        Assert.Equal(HttpStatusCode.Conflict, get.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
        using var verif = _factory.Services.CreateScope();
        var ctx = verif.ServiceProvider.GetRequiredService<AppDbContext>();
        var credito = await ctx.Creditos.AsNoTracking().SingleAsync(c => c.Id == _a.CreditoId);
        Assert.Equal(EstadoCredito.PendienteConfiguracion, credito.Estado);
        Assert.Equal(0, credito.CantidadCuotas);
        Assert.Equal(EstadoVenta.Cancelada, (await ctx.Ventas.AsNoTracking().SingleAsync(v => v.Id == _a.VentaId)).Estado);
    }

    [Fact]
    public async Task Asignar_y_Revocar_CreditosConfigure_SurteEfectoEnElSiguienteRequest()
    {
        var url = $"/Credito/ConfigurarVenta?id={_a.CreditoId}&ventaId={_a.VentaId}&embedded=true";
        var client = _factory.CreateClientWithUserId(_soloViewId);
        Assert.True(Denegado(await client.GetAsync(url)));

        string roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var rolService = scope.ServiceProvider.GetRequiredService<TheBuryProject.Services.Interfaces.IRolService>();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            roleId = await context.Roles.Where(r => r.Name == $"CredView_{_sfx}").Select(r => r.Id).SingleAsync();
            var accion = await context.AccionesModulo.Include(a => a.Modulo).SingleAsync(a => a.Modulo.Clave == "creditos" && a.Clave == "configure");
            await rolService.AssignPermissionToRoleAsync(roleId, accion.ModuloId, accion.Id);
        }
        Assert.False(Denegado(await _factory.CreateClientWithUserId(_soloViewId).GetAsync(url)), "Asignado: debería pasar el filtro.");

        using (var scope = _factory.Services.CreateScope())
        {
            var rolService = scope.ServiceProvider.GetRequiredService<TheBuryProject.Services.Interfaces.IRolService>();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accion = await context.AccionesModulo.Include(a => a.Modulo).SingleAsync(a => a.Modulo.Clave == "creditos" && a.Clave == "configure");
            Assert.True(await rolService.RemovePermissionFromRoleAsync(roleId, accion.ModuloId, accion.Id));
        }
        Assert.True(Denegado(await _factory.CreateClientWithUserId(_soloViewId).GetAsync(url)), "Revocado: debe volver a denegar.");
    }

    private (int ClienteId, int CreditoId, int VentaId) Circuito(string clave) => clave == "A" ? _a : _b;

    private async Task AssertCircuitoIntactoAsync((int ClienteId, int CreditoId, int VentaId) circuito)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var credito = await context.Creditos.AsNoTracking().SingleAsync(x => x.Id == circuito.CreditoId);
        var venta = await context.Ventas.AsNoTracking().SingleAsync(x => x.Id == circuito.VentaId);

        Assert.Equal(EstadoCredito.PendienteConfiguracion, credito.Estado);
        Assert.Equal(10_000m, credito.MontoAprobado);
        Assert.Equal(0, credito.CantidadCuotas);
        Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);
        Assert.Null(venta.FechaConfiguracionCredito);
        Assert.Equal(circuito.CreditoId, venta.CreditoId);
        Assert.Equal(circuito.ClienteId, credito.ClienteId);
    }

    private static bool Denegado(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Forbidden
        || r.StatusCode == HttpStatusCode.Unauthorized
        || ((int)r.StatusCode is >= 300 and < 400
            && (r.Headers.Location?.ToString().Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false));

    private static async Task<HttpResponseMessage> PostConfigurarAsync(
        HttpClient client, int creditoId, int? ventaId, int clienteId, bool embedded = false)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Sin token antiforgery (HTTP {(int)login.StatusCode}).");
        var token = WebUtility.HtmlDecode(match.Groups[1].Value);

        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("CreditoId", creditoId.ToString()),
            new("ClienteId", clienteId.ToString()),
            new("ClienteNombre", "Cliente Formulario"),
            new("Monto", "1"),
            new("Anticipo", "0"),
            new("CantidadCuotas", "6"),
            new("FechaPrimeraCuota", DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")),
            new("MetodoCalculo", ((int)MetodoCalculoCredito.Global).ToString()),
            new("FuenteConfiguracion", ((int)FuenteConfiguracionCredito.Global).ToString()),
        };
        if (ventaId.HasValue)
            campos.Add(new("VentaId", ventaId.Value.ToString()));

        return await client.PostAsync(
            embedded ? "/Credito/ConfigurarVenta?embedded=true" : "/Credito/ConfigurarVenta",
            new FormUrlEncodedContent(campos));
    }

    private async Task<(int ClienteId, int CreditoId, int VentaId)> SembrarCircuitoAsync(AppDbContext context, string clave)
    {
        var cliente = new Cliente
        {
            Nombre = $"Cliente{clave}",
            Apellido = "ConfigurarVenta",
            NumeroDocumento = $"9{Math.Abs(HashCode.Combine(_sfx, clave)) % 10_000_000:D7}",
            IsDeleted = false
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRE-{_sfx}-{clave}",
            Estado = EstadoCredito.PendienteConfiguracion,
            MontoSolicitado = 10_000m,
            MontoAprobado = 10_000m,
            SaldoPendiente = 10_000m,
            IsDeleted = false
        };
        context.Creditos.Add(credito);
        await context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Numero = $"VTA-{_sfx}-{clave}",
            Estado = EstadoVenta.PendienteFinanciacion,
            Total = 10_000m,
            IsDeleted = false
        };
        context.Ventas.Add(venta);
        await context.SaveChangesAsync();

        return (cliente.Id, credito.Id, venta.Id);
    }

    private static async Task CrearRolConPermisosAsync(AppDbContext context, string nombre, params string[] claims)
    {
        var roleId = Guid.NewGuid().ToString();
        context.Roles.Add(new IdentityRole { Id = roleId, Name = nombre, NormalizedName = nombre.ToUpperInvariant() });
        await context.SaveChangesAsync();

        foreach (var claim in claims)
        {
            var partes = claim.Split('.', 2);
            var accion = await context.AccionesModulo.Include(a => a.Modulo)
                .SingleAsync(a => a.Modulo.Clave == partes[0] && a.Clave == partes[1]);
            context.RolPermisos.Add(new RolPermiso
            {
                RoleId = roleId, ModuloId = accion.ModuloId, AccionId = accion.Id,
                ClaimValue = claim, IsDeleted = false, RowVersion = new byte[8]
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task<string> CrearUsuarioAsync(IServiceScope scope, string userName, string rol)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.com", EmailConfirmed = true, Activo = true };
        Assert.True((await userManager.CreateAsync(user, "Test123!")).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, rol)).Succeeded);
        return user.Id;
    }
}
