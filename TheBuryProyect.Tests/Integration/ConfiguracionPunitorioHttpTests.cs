using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// PUN-ML8 — UI administrativa de ConfiguracionPunitorio, extremo a extremo vía HTTP real
// (permisos, antiforgery, persistencia). Complementa:
//   - ConfiguracionPunitorioServiceTests (contrato del servicio, sin HTTP)
//   - ConfiguracionPagoControllerTests (unit, sin HTTP)
//   - ConfiguracionPunitorioUiContractTests (lectura de archivo, sin HTTP)
// ---------------------------------------------------------------------------

[Collection("HttpIntegration")]
public class ConfiguracionPunitorioHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string CreditoPersonalUrl = "/ConfiguracionPago/CreditoPersonal";
    private const string CrearVersionUrl = "/ConfiguracionPago/CrearVersionPunitorio";
    private const string PreviewUrl = "/ConfiguracionPago/PreviewPunitorio";

    private readonly CustomWebApplicationFactory _factory;

    public ConfiguracionPunitorioHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Permisos ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewPunitorio_SinPermiso_DevuelveForbidden()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.GetAsync($"{PreviewUrl}?porcentaje=10&periodoDias=20&diasGracia=5");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PreviewPunitorio_ConPermisoViewpunitorio_DevuelveOk()
    {
        await _factory.SeedUserWithPunitorioManagePermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PunitorioManagePermsUserId);

        var response = await client.GetAsync($"{PreviewUrl}?porcentaje=10&periodoDias=20&diasGracia=5");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PreviewResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(body);
        Assert.Equal(100_000m, body!.MontoReferencia);
        Assert.Equal(4, body.Puntos.Count);
    }

    [Fact]
    public async Task CrearVersionPunitorio_SinNingunPermiso_DevuelveForbidden()
    {
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.NoPermsUserId);

        var response = await client.PostAsync(CrearVersionUrl, new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CrearVersionPunitorio_SoloConfiguracionView_SinManagepunitorio_DevuelveForbidden()
    {
        await _factory.SeedUserWithConfiguracionViewOnlyPermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PunitorioNoAccessUserId);

        // Token real: este usuario SÍ puede ver la página (configuracion.view), así que un 403
        // en el POST se debe pura y exclusivamente a la falta de "managepunitorio" —
        // no a un antiforgery faltante.
        var token = await GetAntiForgeryTokenAsync(client, CreditoPersonalUrl);
        var vigenteDesde = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)).ToString("yyyy-MM-dd");

        var response = await client.PostAsync(CrearVersionUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Punitorios.CrearForm.VigenteDesde"] = vigenteDesde,
            ["Punitorios.CrearForm.Activa"] = "true",
            ["Punitorios.CrearForm.Porcentaje"] = "10",
            ["Punitorios.CrearForm.PeriodoDias"] = "20",
            ["Punitorios.CrearForm.DiasGracia"] = "5"
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Flujo completo ───────────────────────────────────────────────────

    [Fact]
    public async Task CrearVersionPunitorio_VersionFutura_Persiste_RedirigeConFragmentoS7()
    {
        await _factory.SeedUserWithPunitorioManagePermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PunitorioManagePermsUserId);

        var token = await GetAntiForgeryTokenAsync(client, CreditoPersonalUrl);
        var vigenteDesde = await SiguienteVigenciaAsync();

        var response = await client.PostAsync(CrearVersionUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Punitorios.CrearForm.VigenteDesde"] = vigenteDesde.ToString("yyyy-MM-dd"),
            ["Punitorios.CrearForm.Activa"] = "true",
            ["Punitorios.CrearForm.Porcentaje"] = "12.5",
            ["Punitorios.CrearForm.PeriodoDias"] = "15",
            ["Punitorios.CrearForm.DiasGracia"] = "3",
            ["Punitorios.CrearForm.MotivoCambio"] = "Ajuste PUN-ML8 HTTP test — version futura"
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.EndsWith("#s7", response.Headers.Location!.ToString());

        var persistida = await ObtenerUnicaAsync(vigenteDesde);
        Assert.Equal(12.5m, persistida.Porcentaje);
        Assert.Equal(15, persistida.PeriodoDias);
        Assert.Equal(3, persistida.DiasGracia);
        Assert.True(persistida.Activa);
        Assert.True(persistida.ProrrateoDiario);
        Assert.False(persistida.AplicacionRetroactiva);
    }

    [Fact]
    public async Task CrearVersionPunitorio_VersionInactiva_Persiste()
    {
        await _factory.SeedUserWithPunitorioManagePermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PunitorioManagePermsUserId);

        var token = await GetAntiForgeryTokenAsync(client, CreditoPersonalUrl);
        var vigenteDesde = await SiguienteVigenciaAsync();

        var response = await client.PostAsync(CrearVersionUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Punitorios.CrearForm.VigenteDesde"] = vigenteDesde.ToString("yyyy-MM-dd"),
            // El tag helper asp-for de un checkbox emite un hidden companion "false" para que un
            // checkbox desmarcado postee igual; acá se simula eso explícitamente, ya que un POST
            // crudo sin el campo dejaría Activa en su default de ViewModel (true), no en false.
            ["Punitorios.CrearForm.Activa"] = "false",
            ["Punitorios.CrearForm.Porcentaje"] = "8",
            ["Punitorios.CrearForm.PeriodoDias"] = "10",
            ["Punitorios.CrearForm.DiasGracia"] = "2",
            ["Punitorios.CrearForm.MotivoCambio"] = "Desactivacion PUN-ML8 HTTP test"
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var persistida = await ObtenerUnicaAsync(vigenteDesde);
        Assert.False(persistida.Activa);
    }

    [Fact]
    public async Task CrearVersionPunitorio_PorcentajeCero_SeCreaComoActivaConCero()
    {
        await _factory.SeedUserWithPunitorioManagePermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PunitorioManagePermsUserId);

        var token = await GetAntiForgeryTokenAsync(client, CreditoPersonalUrl);
        var vigenteDesde = await SiguienteVigenciaAsync();

        var response = await client.PostAsync(CrearVersionUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Punitorios.CrearForm.VigenteDesde"] = vigenteDesde.ToString("yyyy-MM-dd"),
            ["Punitorios.CrearForm.Activa"] = "true",
            ["Punitorios.CrearForm.Porcentaje"] = "0",
            ["Punitorios.CrearForm.PeriodoDias"] = "30",
            ["Punitorios.CrearForm.DiasGracia"] = "0",
            ["Punitorios.CrearForm.MotivoCambio"] = "Tasa 0% PUN-ML8 HTTP test"
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var persistida = await ObtenerUnicaAsync(vigenteDesde);
        Assert.Equal(0m, persistida.Porcentaje);
        Assert.True(persistida.Activa);
    }

    [Fact]
    public async Task CrearVersionPunitorio_VigenciaDuplicada_NoDuplicaYMuestraError()
    {
        await _factory.SeedUserWithPunitorioManagePermissionAsync();
        using var client = _factory.CreateClientWithUserId(CustomWebApplicationFactory.PunitorioManagePermsUserId);

        var vigenteDesde = await SiguienteVigenciaAsync();

        var token1 = await GetAntiForgeryTokenAsync(client, CreditoPersonalUrl);
        var primera = await client.PostAsync(CrearVersionUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token1,
            ["Punitorios.CrearForm.VigenteDesde"] = vigenteDesde.ToString("yyyy-MM-dd"),
            ["Punitorios.CrearForm.Activa"] = "true",
            ["Punitorios.CrearForm.Porcentaje"] = "5",
            ["Punitorios.CrearForm.PeriodoDias"] = "20",
            ["Punitorios.CrearForm.DiasGracia"] = "5",
            ["Punitorios.CrearForm.MotivoCambio"] = "Primera version PUN-ML8 HTTP test"
        }));
        Assert.Equal(HttpStatusCode.Found, primera.StatusCode);

        var token2 = await GetAntiForgeryTokenAsync(client, CreditoPersonalUrl);
        var segunda = await client.PostAsync(CrearVersionUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token2,
            ["Punitorios.CrearForm.VigenteDesde"] = vigenteDesde.ToString("yyyy-MM-dd"), // misma vigencia: conflicto
            ["Punitorios.CrearForm.Activa"] = "true",
            ["Punitorios.CrearForm.Porcentaje"] = "9",
            ["Punitorios.CrearForm.PeriodoDias"] = "20",
            ["Punitorios.CrearForm.DiasGracia"] = "5",
            ["Punitorios.CrearForm.MotivoCambio"] = "Segunda version con vigencia duplicada"
        }));

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode); // re-render, no redirect
        var html = await segunda.Content.ReadAsStringAsync();
        // Substring ASCII: el HtmlEncoder por defecto entity-encodea acentos ("versión" -> "versi&#243;n").
        Assert.Contains("append-only", html);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        var cantidad = await context.ConfiguracionesPunitorio
            .CountAsync(c => c.VigenteDesde == vigenteDesde);
        Assert.Equal(1, cantidad); // no se duplicó
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve una fecha garantizada posterior a TODAS las versiones ya persistidas por esta clase
    /// (invariante monotónico append-only, PUN-ML3). <see cref="CustomWebApplicationFactory"/> se
    /// comparte entre todos los tests de esta clase (misma BD SQLite in-memory vía IClassFixture) y
    /// xUnit NO garantiza orden de ejecución entre [Fact] — sin esto, dos tests con fechas
    /// hardcodeadas cercanas podían chocar según qué orden corriera primero.
    /// </summary>
    private async Task<DateOnly> SiguienteVigenciaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var maxima = await context.ConfiguracionesPunitorio
            .Select(c => (DateOnly?)c.VigenteDesde)
            .MaxAsync();

        var baseFutura = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));
        return maxima.HasValue && maxima.Value >= baseFutura ? maxima.Value.AddDays(30) : baseFutura;
    }

    private async Task<ConfiguracionPunitorio> ObtenerUnicaAsync(DateOnly vigenteDesde)
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.ConfiguracionesPunitorio.AsNoTracking().SingleAsync(c => c.VigenteDesde == vigenteDesde);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, $"No se encontró el token antiforgery en {url}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed class PreviewResponse
    {
        public decimal MontoReferencia { get; set; }
        public List<object> Puntos { get; set; } = new();
    }
}
