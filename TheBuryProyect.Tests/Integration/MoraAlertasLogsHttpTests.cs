using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// MORA-VISTAS lote 2 — /Mora/Alertas y /Mora/Logs renderizados vía HTTP real sobre la
// SQLite aislada del factory. Sólo GET: ningún test ejecuta MarcarLeida, Resolver ni EjecutarJob.
// ---------------------------------------------------------------------------

[Collection("HttpIntegration")]
public class MoraAlertasLogsHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string SoloVistaUserId = "test-mora-view-only-id";

    private readonly CustomWebApplicationFactory _factory;

    public MoraAlertasLogsHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Alertas_ConPermisoManage_RenderizaLaAlertaYSusAccionesPost()
    {
        var alertaId = await SembrarAlertaAsync(PrioridadAlerta.Critica);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, "/Mora/Alertas");

        Assert.Contains($"data-alerta-id=\"{alertaId}\"", html);
        Assert.Contains("Crítica", html);
        Assert.Contains($"action=\"/Mora/MarcarLeida/{alertaId}\"", html);
        Assert.Contains($"action=\"/Mora/Resolver/{alertaId}\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("name=\"rowVersion\"", html);

        // MoraService envía ClienteNombre como "Apellido, Nombre - DNI: {doc}" y además ClienteDocumento:
        // la vista muestra el documento una sola vez.
        Assert.Contains("Vistas, Mora</span>", html);
        Assert.DoesNotContain("Vistas, Mora - DNI:", html);
    }

    [Fact]
    public async Task Alertas_FiltroDePrioridad_ConservaLaSeleccionYFiltra()
    {
        var critica = await SembrarAlertaAsync(PrioridadAlerta.Critica);
        var baja = await SembrarAlertaAsync(PrioridadAlerta.Baja);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, $"/Mora/Alertas?prioridad={(int)PrioridadAlerta.Critica}");

        Assert.Contains($"data-alerta-id=\"{critica}\"", html);
        Assert.DoesNotContain($"data-alerta-id=\"{baja}\"", html);
        Assert.Matches($"<option value=\"{(int)PrioridadAlerta.Critica}\" selected", html);
        Assert.Contains("data-mora-limpiar-filtros", html);
    }

    [Fact]
    public async Task Alertas_FiltroSinCoincidencias_MuestraEstadoSinResultados()
    {
        await SembrarAlertaAsync(PrioridadAlerta.Media);
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, "/Mora/Alertas?cliente=" + Guid.NewGuid().ToString("N"));

        Assert.Contains("data-mora-estado=\"sin-resultados\"", html);
        Assert.DoesNotContain("data-mora-estado=\"alertas-vacio\"", html);
        Assert.DoesNotContain("data-alerta-id=", html);
    }

    [Fact]
    public async Task Alertas_UsuarioSoloVista_VeElListadoSinAccionesDeEscritura()
    {
        var alertaId = await SembrarAlertaAsync(PrioridadAlerta.Alta);
        await SembrarUsuarioSoloVistaAsync();
        using var client = _factory.CreateClientWithUserId(SoloVistaUserId);

        var html = await GetOkAsync(client, "/Mora/Alertas");

        Assert.Contains($"data-alerta-id=\"{alertaId}\"", html);
        Assert.DoesNotContain("/Mora/MarcarLeida", html);
        Assert.DoesNotContain("/Mora/Resolver", html);
        Assert.DoesNotContain("data-mora-accion=", html);
    }

    [Fact]
    public async Task Logs_RenderizaEjecucionesConDetalleDeError()
    {
        var mensaje = "Ejecución de prueba " + Guid.NewGuid().ToString("N")[..8];
        await SembrarLogAsync(mensaje, exitoso: false, detalleError: "Timeout de prueba");
        using var client = await CrearClienteAdminAsync();

        var html = await GetOkAsync(client, "/Mora/Logs");

        Assert.Contains(mensaje, html);
        Assert.Contains("Con errores", html);
        Assert.Contains("Ver detalle del error", html);
        Assert.Contains("Timeout de prueba", html);
        Assert.DoesNotContain("data-mora-estado=\"logs-vacio\"", html);
        Assert.DoesNotContain("method=\"post\"", html.Split("data-mora-logs-page")[1]);
    }

    [Fact]
    public async Task Logs_UsuarioSoloVista_Responde200()
    {
        await SembrarUsuarioSoloVistaAsync();
        using var client = _factory.CreateClientWithUserId(SoloVistaUserId);

        var html = await GetOkAsync(client, "/Mora/Logs");

        Assert.Contains("Historial de ejecuciones", html);
    }

    [Theory]
    [InlineData("/Mora/Alertas")]
    [InlineData("/Mora/Logs")]
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

    private async Task<int> SembrarAlertaAsync(PrioridadAlerta prioridad)
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = await CrearContextoAsync(scope);

        var cliente = new Cliente
        {
            Nombre = "Mora",
            Apellido = "Vistas",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Email = "mora-vistas@test.com"
        };
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
            FechaSolicitud = DateTime.UtcNow
        };
        context.Set<Credito>().Add(credito);
        await context.SaveChangesAsync();

        var alerta = new AlertaCobranza
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Prioridad = prioridad,
            Tipo = TipoAlertaCobranza.CuotaVencida,
            Mensaje = "Cuota vencida de prueba",
            MontoVencido = 1_000m,
            CuotasVencidas = 1,
            FechaAlerta = DateTime.UtcNow
        };
        context.Set<AlertaCobranza>().Add(alerta);
        await context.SaveChangesAsync();
        return alerta.Id;
    }

    private async Task SembrarLogAsync(string mensaje, bool exitoso, string? detalleError)
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = await CrearContextoAsync(scope);

        context.Set<LogMora>().Add(new LogMora
        {
            // Fecha futura: queda primera aunque otros tests de la clase hayan sembrado logs.
            FechaEjecucion = DateTime.UtcNow.AddYears(1),
            CuotasProcesadas = 12,
            AlertasGeneradas = 3,
            CuotasConMora = 4,
            TotalMora = 1_234.5m,
            Errores = exitoso ? 0 : 1,
            Exitoso = exitoso,
            Mensaje = mensaje,
            DetalleError = detalleError,
            DuracionEjecucion = TimeSpan.FromSeconds(2.5)
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Usuario con sólo cobranzas.viewarrears + mora.view (sin mora.manage). Local a este test:
    /// no modifica el factory compartido ni los seeders de la aplicación.
    /// </summary>
    private async Task SembrarUsuarioSoloVistaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = await CrearContextoAsync(scope);

        if (await context.Users.AnyAsync(u => u.Id == SoloVistaUserId))
            return;

        const string roleId = "test-mora-view-only-role-id";
        const int moduloId = 9961;
        const int accionViewArrears = 9961;
        const int accionMoraView = 9962;

        context.Roles.Add(new IdentityRole { Id = roleId, Name = "TestMoraViewOnlyRole", NormalizedName = "TESTMORAVIEWONLYROLE" });
        context.Users.Add(new ApplicationUser
        {
            Id = SoloVistaUserId,
            UserName = "testuser-mora-view",
            NormalizedUserName = "TESTUSER-MORA-VIEW",
            Email = "testmoraview@test.com",
            NormalizedEmail = "TESTMORAVIEW@TEST.COM",
            Activo = true,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();

        context.UserRoles.Add(new IdentityUserRole<string> { UserId = SoloVistaUserId, RoleId = roleId });
        context.ModulosSistema.Add(new ModuloSistema
        {
            Id = moduloId,
            Nombre = "Test Mora View",
            Clave = "mora-view-test",
            Orden = 0,
            RowVersion = new byte[8]
        });
        context.AccionesModulo.AddRange(
            new AccionModulo { Id = accionViewArrears, ModuloId = moduloId, Nombre = "Test viewarrears", Clave = "viewarrears-test", Orden = 0, RowVersion = new byte[8] },
            new AccionModulo { Id = accionMoraView, ModuloId = moduloId, Nombre = "Test mora view", Clave = "moraview-test", Orden = 1, RowVersion = new byte[8] });
        context.RolPermisos.AddRange(
            new RolPermiso { RoleId = roleId, ModuloId = moduloId, AccionId = accionViewArrears, ClaimValue = "cobranzas.viewarrears", IsDeleted = false, RowVersion = new byte[8] },
            new RolPermiso { RoleId = roleId, ModuloId = moduloId, AccionId = accionMoraView, ClaimValue = "mora.view", IsDeleted = false, RowVersion = new byte[8] });
        await context.SaveChangesAsync();
    }
}
