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

    // ---------------------------------------------------------------------------------------------
    // SEC-S-005 (vigente en main, PR #128): un bloqueo de cuenta vigente (LockoutEnd futuro) impide
    // nuevos inicios de sesión Y expulsa la sesión ya abierta en el siguiente request
    // (ActiveUserCookieValidator). Activo=false, SecurityStamp rotado y usuario inexistente también revocan.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SesionAbierta_BloqueoAutomatico_PorIntentosFallidos_EsRevocada()
    {
        var user = await SeedAsync("lock-auto");
        var sesion = NewBrowser();
        await LoginAsync(sesion, user.UserName!);
        AssertAutenticada(await ProbeAsync(sesion));

        await BloquearPorIntentosFallidosAsync(user.UserName!);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);
    }

    [Fact]
    public async Task SesionAbierta_BloqueoAdministrativo_EsRevocada()
    {
        var user = await SeedAsync("lock-admin");
        var sesion = NewBrowser();
        await LoginAsync(sesion, user.UserName!);

        // Mismas llamadas que UsuariosController.BulkAction("bloquear"): habilitar + fin indefinido.
        await BloquearAsync(user.Id, DateTimeOffset.MaxValue);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);
    }

    [Fact]
    public async Task SesionAbierta_BloqueoTemporalAdministrativo_EsRevocada()
    {
        var user = await SeedAsync("lock-temp");
        var sesion = NewBrowser();
        await LoginAsync(sesion, user.UserName!);

        await BloquearAsync(user.Id, DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);
    }

    [Fact]
    public async Task NuevoLogin_DuranteBloqueoAutomatico_EsRechazado()
    {
        var user = await SeedAsync("login-auto");
        await BloquearPorIntentosFallidosAsync(user.UserName!);

        var otro = NewBrowser();
        var login = await LoginAsync(otro, user.UserName!); // credenciales correctas

        Assert.False(TieneCookieDeSesion(login));
        Assert.Contains("Lockout", login.Headers.Location?.ToString() ?? string.Empty);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(otro)).StatusCode);
    }

    [Fact]
    public async Task NuevoLogin_DuranteBloqueoAdministrativo_EsRechazado()
    {
        var user = await SeedAsync("login-admin");
        await BloquearAsync(user.Id, DateTimeOffset.MaxValue);

        var otro = NewBrowser();
        var login = await LoginAsync(otro, user.UserName!);

        Assert.False(TieneCookieDeSesion(login));
        Assert.Contains("Lockout", login.Headers.Location?.ToString() ?? string.Empty);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(otro)).StatusCode);
    }

    [Fact]
    public async Task NuevoLogin_BloqueoVencido_EsPermitido()
    {
        var user = await SeedAsync("login-venc");
        await BloquearDirectoAsync(user.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        var otro = NewBrowser();
        Assert.True(TieneCookieDeSesion(await LoginAsync(otro, user.UserName!)));
        AssertAutenticada(await ProbeAsync(otro));
    }

    [Fact]
    public async Task NuevoLogin_TrasDesbloqueo_EsPermitido()
    {
        var user = await SeedAsync("login-unlock");
        await BloquearAsync(user.Id, DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(TieneCookieDeSesion(await LoginAsync(NewBrowser(), user.UserName!)));

        await BloquearAsync(user.Id, null);

        var otro = NewBrowser();
        Assert.True(TieneCookieDeSesion(await LoginAsync(otro, user.UserName!)));
        AssertAutenticada(await ProbeAsync(otro));
    }

    [Fact]
    public async Task SuperAdminBloqueado_ConservaRolYActivo_PeroPierdeLaSesionPrevia()
    {
        var user = await SeedAsync("lock-sa");
        using (var scope = _factory.Services.CreateScope())
        {
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await rm.RoleExistsAsync("SuperAdmin"))
                Assert.True((await rm.CreateAsync(new IdentityRole("SuperAdmin"))).Succeeded);
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await um.AddToRoleAsync((await um.FindByIdAsync(user.Id))!, "SuperAdmin")).Succeeded);
        }
        var sesion = NewBrowser();
        await LoginAsync(sesion, user.UserName!);

        await BloquearAsync(user.Id, DateTimeOffset.MaxValue);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);
        using var scope2 = _factory.Services.CreateScope();
        var um2 = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var recargado = await um2.FindByIdAsync(user.Id);
        Assert.True(recargado!.Activo);
        Assert.True(await um2.IsInRoleAsync(recargado, "SuperAdmin"));
        Assert.False(TieneCookieDeSesion(await LoginAsync(NewBrowser(), user.UserName!)));
    }

    [Fact]
    public async Task UsuarioBloqueadoYLuegoDesactivado_SesionSigueRechazada()
    {
        // Bloqueo y desactivación se acumulan: la sesión sigue revocada.
        var user = await SeedAsync("lock-inact");
        var sesion = NewBrowser();
        await LoginAsync(sesion, user.UserName!);
        await BloquearAsync(user.Id, DateTimeOffset.MaxValue);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);

        await SetActivoDirectoAsync(user.Id, false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(sesion)).StatusCode);
    }

    [Fact]
    public async Task SecurityStampRotado_RevocaSesion()
    {
        var user = await SeedAsync("stamp");
        var client = NewBrowser();
        await LoginAsync(client, user.UserName!);
        AssertAutenticada(await ProbeAsync(client));

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await um.UpdateSecurityStampAsync((await um.FindByIdAsync(user.Id))!);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);
    }

    [Fact]
    public async Task UsuarioInexistente_CookieSinFilaEnBase_Rechazada()
    {
        var user = await SeedAsync("borrado");
        var client = NewBrowser();
        await LoginAsync(client, user.UserName!);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Remove((await db.Users.FindAsync(user.Id))!);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(client)).StatusCode);
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

    /// <summary>Sesión aceptada: llegó al recurso (200 o 403 por permisos), no 401/302/500.</summary>
    private static void AssertAutenticada(HttpResponseMessage r) =>
        Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden, $"Se esperaba sesión aceptada, llegó {(int)r.StatusCode}.");

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

    /// <summary>Escribe LockoutEnd/LockoutEnabled directo en la base (permite fechas pasadas y LockoutEnabled=false).</summary>
    private async Task BloquearDirectoAsync(string userId, DateTimeOffset? hasta, bool lockoutEnabled = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var u = await db.Users.FindAsync(userId);
        u!.LockoutEnabled = lockoutEnabled;
        u.LockoutEnd = hasta;
        await db.SaveChangesAsync();
    }

    /// <summary>Bloqueo automático real de Identity: MaxFailedAccessAttempts (5) passwords malas desde otro navegador.</summary>
    private async Task BloquearPorIntentosFallidosAsync(string userName)
    {
        for (var i = 0; i < 5; i++)
            await LoginAsync(NewBrowser(), userName, "Mala1234!");
    }

    private async Task BloquearAsync(string userId, DateTimeOffset? hasta)
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
