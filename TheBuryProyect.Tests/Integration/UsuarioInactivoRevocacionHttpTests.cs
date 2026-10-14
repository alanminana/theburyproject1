using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-SEG-P0: un usuario con <c>Activo = false</c> no puede iniciar sesión y pierde las sesiones ya
/// abiertas en el siguiente request protegido. Usa el pipeline REAL de cookie de Identity (login por
/// formulario con antiforgery, cookie, validación de principal por request); cada HttpClient con su
/// propio cookie jar equivale a una sesión/navegador distinto.
/// </summary>
[Collection("HttpIntegration")]
public class UsuarioInactivoRevocacionHttpTests : IClassFixture<CookieAuthWebApplicationFactory>
{
    private const string Password = "Test1234!";
    private const string IdentityCookie = ".AspNetCore.Identity.Application";
    private readonly CookieAuthWebApplicationFactory _factory;

    public UsuarioInactivoRevocacionHttpTests(CookieAuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_UsuarioActivo_EmiteCookieYAccede()
    {
        var user = await SeedAsync("activo");
        var client = NewBrowser();

        var login = await LoginAsync(client, user.UserName!);

        Assert.True(TieneCookieDeSesion(login));
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);
    }

    [Fact]
    public async Task Login_UsuarioInactivo_NoEmiteCookie_NoAccede_YMensajeGenerico()
    {
        var user = await SeedAsync("inactivo", activo: false);
        var client = NewBrowser();

        var login = await LoginAsync(client, user.UserName!);
        var html = await login.Content.ReadAsStringAsync();

        Assert.False(TieneCookieDeSesion(login));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); // re-renderiza el formulario
        Assert.Contains("Usuario o contraseña incorrectos.", WebUtility.HtmlDecode(html));
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);
    }

    [Fact]
    public async Task Login_UsuarioInactivo_MismoMensajeQueUnActivoConPasswordIncorrecta_SinEnumeracion()
    {
        var inactivo = await SeedAsync("inact-enum", activo: false);
        var activo = await SeedAsync("act-enum");

        var r1 = await (await LoginAsync(NewBrowser(), inactivo.UserName!, "Mala1234!")).Content.ReadAsStringAsync();
        var r2 = await (await LoginAsync(NewBrowser(), activo.UserName!, "Mala1234!")).Content.ReadAsStringAsync();

        Assert.Contains("Usuario o contraseña incorrectos.", WebUtility.HtmlDecode(r1));
        Assert.Contains("Usuario o contraseña incorrectos.", WebUtility.HtmlDecode(r2));
    }

    [Fact]
    public async Task Desactivar_SesionAbierta_PierdeAcceso_EnElSiguienteRequest()
    {
        var user = await SeedAsync("sesion");
        var client = NewBrowser();
        await LoginAsync(client, user.UserName!);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);

        // Desactivación directa en la base: NO toca el SecurityStamp, así que lo único que puede
        // revocar la sesión es la validación por request (el stamp validator estándar revalida cada 30 min).
        await SetActivoDirectoAsync(user.Id, false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);
    }

    [Fact]
    public async Task Desactivar_ConDosSesionesAbiertas_AmbasPierdenAcceso()
    {
        var user = await SeedAsync("dos-sesiones");
        var chrome = NewBrowser();
        var firefox = NewBrowser();
        await LoginAsync(chrome, user.UserName!);
        await LoginAsync(firefox, user.UserName!);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await ProbeAsync(chrome)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await ProbeAsync(firefox)).StatusCode);

        await SetActivoDirectoAsync(user.Id, false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(chrome)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(firefox)).StatusCode);
    }

    [Fact]
    public async Task Desactivar_PorUsuarioService_RevocaSesion_YReactivarNoResucitaLaCookieVieja()
    {
        var user = await SeedAsync("service");
        var client = NewBrowser();
        var login = await LoginAsync(client, user.UserName!);
        var cookieVieja = login.Headers.GetValues("Set-Cookie")
            .First(v => v.StartsWith(IdentityCookie + "=", StringComparison.Ordinal)).Split(';')[0];

        await ActualizarActivoPorServiceAsync(user.Id, false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);

        // Reactivación: un login nuevo funciona, pero la cookie anterior (stamp rotado) no revive.
        await ActualizarActivoPorServiceAsync(user.Id, true);
        var otro = NewBrowser();
        await LoginAsync(otro, user.UserName!);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await ProbeAsync(otro)).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, "/Cliente");
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        req.Headers.Add("Cookie", cookieVieja);
        Assert.Equal(HttpStatusCode.Unauthorized, (await NewBrowser().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Usuario_Bloqueado_SigueBloqueado_YDesactivarNoModificaElBloqueo()
    {
        var user = await SeedAsync("bloqueado");
        await BloquearAsync(user.Id, DateTimeOffset.UtcNow.AddHours(1));

        var login = await LoginAsync(NewBrowser(), user.UserName!);
        Assert.False(TieneCookieDeSesion(login));
        Assert.Contains("Lockout", login.Headers.Location?.ToString() ?? string.Empty);

        await ActualizarActivoPorServiceAsync(user.Id, false);

        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var recargado = await um.FindByIdAsync(user.Id);
        Assert.True(recargado!.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.False(recargado.Activo);
    }

    [Fact]
    public async Task Usuario_ActivoSinPermisos_NoSeConfundeConDesactivado()
    {
        // Sin roles ni permisos: puede autenticarse (no es "desactivado"); sólo se le niega el recurso.
        var user = await SeedAsync("sin-permisos");
        var client = NewBrowser();

        Assert.True(TieneCookieDeSesion(await LoginAsync(client, user.UserName!)));
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);
    }

    [Fact]
    public async Task CheckPasswordSignIn_UsuarioInactivo_NoPermitido_ViaOtraEntradaDelSignInManager()
    {
        var user = await SeedAsync("alt-entrada", activo: false);
        using var scope = _factory.Services.CreateScope();
        var sm = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();

        var result = await sm.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: false);

        Assert.True(result.IsNotAllowed);
        Assert.False(await sm.CanSignInAsync(user));
    }

    [Fact]
    public async Task RutasAnonimasLegitimas_Funcionan_AunConCookieYaInvalida()
    {
        Assert.Equal(HttpStatusCode.OK, (await NewBrowser().GetAsync("/Identity/Account/Login")).StatusCode);

        var user = await SeedAsync("cookie-vieja");
        var client = NewBrowser();
        await LoginAsync(client, user.UserName!);
        await SetActivoDirectoAsync(user.Id, false);

        // Con una cookie ya inválida la pantalla de login sigue respondiendo (sin 500 ni loop).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Identity/Account/Login")).StatusCode);
    }

    [Fact]
    public async Task Login_SinTokenAntiforgery_SeRechaza_AunConCredencialesValidas()
    {
        var user = await SeedAsync("csrf");

        var response = await NewBrowser().PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.UserName"] = user.UserName!,
            ["Input.Password"] = Password,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(TieneCookieDeSesion(response));
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private HttpClient NewBrowser() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static bool TieneCookieDeSesion(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) &&
        values.Any(v => v.StartsWith(IdentityCookie + "=", StringComparison.Ordinal) &&
                        !v.StartsWith(IdentityCookie + "=;", StringComparison.Ordinal));

    /// <summary>Request AJAX a un endpoint protegido: sin sesión responde 401 (no redirige).</summary>
    private static Task<HttpResponseMessage> ProbeAsync(HttpClient client)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/Cliente");
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password = Password)
    {
        var page = await client.GetAsync("/Identity/Account/Login");
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "No se encontró el token antiforgery del login.");

        return await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
        }));
    }

    private async Task<ApplicationUser> SeedAsync(string prefijo, bool activo = true)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userName = $"{prefijo}_{Guid.NewGuid():N}"[..28];
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@test.com",
            EmailConfirmed = true,
            Activo = activo,
            LockoutEnabled = true,
        };
        var result = await um.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task SetActivoDirectoAsync(string userId, bool activo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var u = await db.Users.FindAsync(userId);
        u!.Activo = activo;
        await db.SaveChangesAsync();
    }

    private async Task BloquearAsync(string userId, DateTimeOffset hasta)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await um.FindByIdAsync(userId);
        await um.SetLockoutEndDateAsync(u!, hasta);
    }

    private async Task ActualizarActivoPorServiceAsync(string userId, bool activo)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var svc = scope.ServiceProvider.GetRequiredService<IUsuarioService>();
        var u = await um.FindByIdAsync(userId);
        var result = await svc.UpdateUsuarioAsync(new UsuarioUpdateRequest
        {
            UserId = u!.Id,
            UserName = u.UserName!,
            Email = u.Email!,
            Nombre = u.Nombre,
            Apellido = u.Apellido,
            Telefono = u.Telefono,
            SucursalId = u.SucursalId,
            SucursalNombre = u.Sucursal,
            Activo = activo,
            RolesDeseados = [],
            RowVersion = u.RowVersion,
            EditadoPor = "admin-test",
        });
        Assert.True(result.Ok, string.Join(", ", result.Errors));
    }
}
