using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// QA-MORA-UI lote 3 — /Mora/ClientesMora y /Mora/FichaCliente renderizados vía HTTP real sobre la
// SQLite aislada del factory. Sólo GET: ningún test ejecuta acciones de escritura de Mora.
// ---------------------------------------------------------------------------

[Collection("HttpIntegration")]
public class MoraClientesFichaHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string SoloVistaUserId = "test-mora-clientes-view-only-id";

    private readonly CustomWebApplicationFactory _factory;

    public MoraClientesFichaHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Sembrado(int ClienteId, string Documento, int AlertaId);

    [Fact]
    public async Task ClientesMora_RenderizaClienteConMontosPrioridadYEnlaceALaFicha()
    {
        var s = await SembrarClienteEnMoraAsync(PrioridadAlerta.Critica);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, "/Mora/ClientesMora?busqueda=" + s.Documento);

        Assert.Contains($"data-cliente-id=\"{s.ClienteId}\"", html);
        Assert.Contains("Crítica", html);
        Assert.Contains("1 cuota vencida", html);
        Assert.Contains($"/Mora/FichaCliente/{s.ClienteId}", html);
        Assert.Contains("data-kpi=\"capital-vencido\"", html);

        // MoraService arma Nombre como "Apellido, Nombre - DNI: {doc}" y además envía Documento:
        // la vista muestra el documento una sola vez.
        Assert.Contains("Bandeja, Mora</span>", html);
        Assert.DoesNotContain("Bandeja, Mora - DNI:", html);

        // No enlaza a formularios que todavía no tienen vista.
        Assert.DoesNotContain("/Mora/RegistrarContacto", html);
        Assert.DoesNotContain("/Mora/RegistrarPromesa", html);
        Assert.DoesNotContain("/Mora/CrearAcuerdo", html);
    }

    [Fact]
    public async Task ClientesMora_FiltroDePrioridad_ConservaLaSeleccionYFiltra()
    {
        var critico = await SembrarClienteEnMoraAsync(PrioridadAlerta.Critica);
        var bajo = await SembrarClienteEnMoraAsync(PrioridadAlerta.Baja);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, $"/Mora/ClientesMora?prioridad={(int)PrioridadAlerta.Critica}");

        Assert.Contains($"data-cliente-id=\"{critico.ClienteId}\"", html);
        Assert.DoesNotContain($"data-cliente-id=\"{bajo.ClienteId}\"", html);
        Assert.Matches($"<option value=\"{(int)PrioridadAlerta.Critica}\" selected", html);
        Assert.Contains("data-mora-limpiar-filtros", html);
    }

    [Fact]
    public async Task ClientesMora_FiltroSinCoincidencias_MuestraEstadoSinResultados()
    {
        await SembrarClienteEnMoraAsync(PrioridadAlerta.Media);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, "/Mora/ClientesMora?busqueda=" + Guid.NewGuid().ToString("N"));

        Assert.Contains("data-mora-estado=\"sin-resultados\"", html);
        Assert.DoesNotContain("data-mora-estado=\"clientes-vacio\"", html);
        Assert.DoesNotContain("data-cliente-id=", html);
    }

    [Fact]
    public async Task ClientesMora_PaginaFueraDeRango_ExplicaElEstadoSinError()
    {
        await SembrarClienteEnMoraAsync(PrioridadAlerta.Alta);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, "/Mora/ClientesMora?pagina=999");

        Assert.Contains("data-mora-estado=\"pagina-fuera-de-rango\"", html);
        Assert.DoesNotContain("data-mora-estado=\"error\"", html);
    }

    [Theory]
    [InlineData("/Mora/ClientesMora?prioridad=abc")]
    [InlineData("/Mora/ClientesMora?prioridad=99&estadoGestion=99")]
    [InlineData("/Mora/ClientesMora?pagina=0")]
    [InlineData("/Mora/ClientesMora?pagina=-3&ordenamiento=Inexistente")]
    [InlineData("/Mora/ClientesMora?diasMinAtraso=x&montoMinVencido=y")]
    public async Task ClientesMora_ParametrosInvalidos_NoProducen500(string url)
    {
        using var client = await CrearClienteAdminAsync();

        var response = await client.GetAsync(url);

        Assert.True((int)response.StatusCode < 500, $"GET {url} → {(int)response.StatusCode}");
    }

    [Fact]
    public async Task ClientesMora_UsuarioSoloVista_VeLaBandejaSinAccionesDeEscritura()
    {
        var s = await SembrarClienteEnMoraAsync(PrioridadAlerta.Alta);
        await SembrarUsuarioSoloVistaAsync();
        using var client = _factory.CreateClientWithUserId(SoloVistaUserId);

        var html = await GetOkAsync(client, "/Mora/ClientesMora?busqueda=" + s.Documento);

        Assert.Contains($"data-cliente-id=\"{s.ClienteId}\"", html);
        Assert.DoesNotContain("method=\"post\"", html.Split("data-mora-clientes-page")[1]);
    }

    [Fact]
    public async Task FichaCliente_RenderizaResumenCuotasAlertasContactosYPromesa()
    {
        var s = await SembrarClienteEnMoraAsync(PrioridadAlerta.Alta, conContacto: true, conPromesa: true);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, $"/Mora/FichaCliente/{s.ClienteId}");

        Assert.Contains("data-mora-ficha-page", html);
        Assert.Contains("Bandeja, Mora", html);
        Assert.DoesNotContain("Bandeja, Mora - DNI:", html);
        Assert.Contains("data-kpi=\"capital-vencido\"", html);
        Assert.Contains("1 cuota vencida", html);
        Assert.Contains("N.º 1", html);
        Assert.Contains($"data-alerta-id=\"{s.AlertaId}\"", html);
        Assert.Contains("Cuota vencida de prueba", html);
        Assert.Contains("Nota de gestión de prueba", html);
        Assert.Contains("Promesa vigente", html);
        Assert.DoesNotContain("data-mora-estado=\"sin-mora\"", html);
        Assert.DoesNotContain("data-mora-estado=\"promesas-vacio\"", html);
    }

    [Fact]
    public async Task FichaCliente_AdminVeLosFormulariosDeGestion()
    {
        var s = await SembrarClienteEnMoraAsync(PrioridadAlerta.Media);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, $"/Mora/FichaCliente/{s.ClienteId}");

        Assert.Contains("data-mora-gestion=\"contacto\"", html);
        Assert.Contains("data-mora-gestion=\"promesa\"", html);
        Assert.Contains("data-mora-gestion=\"acuerdo\"", html);
        Assert.DoesNotContain("Próximamente", html);
        Assert.Contains("action=\"/Mora/RegistrarContacto", html);
        Assert.Contains("action=\"/Mora/RegistrarPromesa", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
    }

    [Fact]
    public async Task FichaCliente_ClienteSinMora_MuestraEstadoVacioSinError()
    {
        int clienteId;
        using (var scope = _factory.Services.CreateScope())
        {
            await using var context = await CrearContextoAsync(scope);
            var cliente = NuevoCliente();
            context.Set<Cliente>().Add(cliente);
            await context.SaveChangesAsync();
            clienteId = cliente.Id;
        }
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, $"/Mora/FichaCliente/{clienteId}");

        Assert.Contains("data-mora-estado=\"sin-mora\"", html);
        Assert.Contains("data-mora-estado=\"creditos-vacio\"", html);
        Assert.Contains("data-mora-estado=\"contactos-vacio\"", html);
    }

    [Fact]
    public async Task FichaCliente_ClienteInexistente_NoProduce500()
    {
        using var client = await CrearClienteAdminAsync();

        var response = await client.GetAsync("/Mora/FichaCliente/987654");

        Assert.True((int)response.StatusCode < 500, $"→ {(int)response.StatusCode}");
    }

    [Fact]
    public async Task FichaCliente_VerClienteDependeDelPermisoClientesView()
    {
        var s = await SembrarClienteEnMoraAsync(PrioridadAlerta.Alta);
        await SembrarUsuarioSoloVistaAsync();

        using var admin = await CrearClienteAdminAsync();
        var htmlAdmin = await GetOkAsync(admin, $"/Mora/FichaCliente/{s.ClienteId}");
        Assert.Contains($"/Cliente/Details/{s.ClienteId}", htmlAdmin);

        using var soloVista = _factory.CreateClientWithUserId(SoloVistaUserId);
        var htmlSoloVista = await GetOkAsync(soloVista, $"/Mora/FichaCliente/{s.ClienteId}");
        Assert.DoesNotContain("/Cliente/Details", htmlSoloVista);
    }

    [Theory]
    [InlineData("/Mora/ClientesMora")]
    [InlineData("/Mora/FichaCliente/1")]
    public async Task UsuarioSinPermisos_NoAccede(string url)
    {
        await _factory.SeedTestUserAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.GetAsync(url);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<string> GetOkAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} → {(int)response.StatusCode}\n{html}");
        // Razor codifica los no-ASCII ("Crítica" → "Cr&#xED;tica").
        return WebUtility.HtmlDecode(html);
    }

    private async Task<HttpClient> CrearClienteAdminAsync()
    {
        await _factory.SeedTestUserAsync();
        return _factory.CreateAuthenticatedClient();
    }

    private async Task<AppDbContext> CrearContextoAsync(IServiceScope scope) =>
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    private static Cliente NuevoCliente() => new()
    {
        Nombre = "Mora",
        Apellido = "Bandeja",
        TipoDocumento = "DNI",
        NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
        Email = "mora-bandeja@test.com",
        Telefono = "3511234567"
    };

    private async Task<Sembrado> SembrarClienteEnMoraAsync(PrioridadAlerta prioridad, bool conContacto = false, bool conPromesa = false)
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = await CrearContextoAsync(scope);

        var cliente = NuevoCliente();
        context.Set<Cliente>().Add(cliente);
        await context.SaveChangesAsync();

        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = cliente.Id,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = 10_000m,
            MontoAprobado = 10_000m,
            SaldoPendiente = 5_000m,
            TasaInteres = 3m,
            CantidadCuotas = 12,
            FechaSolicitud = DateTime.UtcNow.AddMonths(-3)
        };
        context.Set<Credito>().Add(credito);
        await context.SaveChangesAsync();

        context.Set<Cuota>().Add(new Cuota
        {
            CreditoId = credito.Id,
            NumeroCuota = 1,
            MontoCapital = 800m,
            MontoInteres = 200m,
            MontoTotal = 1_000m,
            FechaVencimiento = DateTime.Today.AddDays(-20),
            Estado = EstadoCuota.Pendiente
        });

        var alerta = new AlertaCobranza
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Prioridad = prioridad,
            Tipo = TipoAlertaCobranza.CuotaVencida,
            Mensaje = "Cuota vencida de prueba",
            MontoVencido = 1_000m,
            CuotasVencidas = 1,
            DiasAtraso = 20,
            FechaAlerta = DateTime.UtcNow,
            FechaPromesaPago = conPromesa ? DateTime.Today.AddDays(5) : null,
            MontoPromesaPago = conPromesa ? 1_000m : null,
            EstadoGestion = conPromesa ? EstadoGestionCobranza.PromesaPago : EstadoGestionCobranza.Pendiente
        };
        context.Set<AlertaCobranza>().Add(alerta);
        await context.SaveChangesAsync();

        if (conContacto)
        {
            context.Set<HistorialContacto>().Add(new HistorialContacto
            {
                AlertaCobranzaId = alerta.Id,
                ClienteId = cliente.Id,
                GestorId = "gestor-test",
                FechaContacto = DateTime.UtcNow.AddDays(-1),
                TipoContacto = TipoContacto.LlamadaTelefonica,
                Resultado = ResultadoContacto.NoContesta,
                Observaciones = "Nota de gestión de prueba"
            });
            await context.SaveChangesAsync();
        }

        return new Sembrado(cliente.Id, cliente.NumeroDocumento, alerta.Id);
    }

    /// <summary>
    /// Usuario con sólo cobranzas.viewarrears + mora.view (sin mora.manage ni clientes.view). Local a esta clase:
    /// no modifica el factory compartido ni los seeders de la aplicación.
    /// </summary>
    private async Task SembrarUsuarioSoloVistaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = await CrearContextoAsync(scope);

        if (await context.Users.AnyAsync(u => u.Id == SoloVistaUserId))
            return;

        const string roleId = "test-mora-clientes-view-only-role-id";
        const int moduloId = 9971;
        const int accionViewArrears = 9971;
        const int accionMoraView = 9972;

        context.Roles.Add(new IdentityRole { Id = roleId, Name = "TestMoraClientesViewOnlyRole", NormalizedName = "TESTMORACLIENTESVIEWONLYROLE" });
        context.Users.Add(new ApplicationUser
        {
            Id = SoloVistaUserId,
            UserName = "testuser-mora-clientes-view",
            NormalizedUserName = "TESTUSER-MORA-CLIENTES-VIEW",
            Email = "testmoraclientesview@test.com",
            NormalizedEmail = "TESTMORACLIENTESVIEW@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = SoloVistaUserId, RoleId = roleId });
        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = moduloId,
            Nombre = "Test Mora Clientes View",
            Clave = "mora-clientes-view-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.AccionesModulo.AddRange(
            new AccionModulo { Id = accionViewArrears, ModuloId = moduloId, Nombre = "Test viewarrears", Clave = "viewarrears-clientes-test", Orden = 0, RowVersion = new byte[8] },
            new AccionModulo { Id = accionMoraView, ModuloId = moduloId, Nombre = "Test mora view", Clave = "moraview-clientes-test", Orden = 1, RowVersion = new byte[8] });
        context.RolPermisos.AddRange(
            new RolPermiso { RoleId = roleId, ModuloId = moduloId, AccionId = accionViewArrears, ClaimValue = "cobranzas.viewarrears", IsDeleted = false, RowVersion = new byte[8] },
            new RolPermiso { RoleId = roleId, ModuloId = moduloId, AccionId = accionMoraView, ClaimValue = "mora.view", IsDeleted = false, RowVersion = new byte[8] });
        await context.SaveChangesAsync();
    }
}
