using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// P-03: ensayo sintético (base SQLite descartable, pipeline REAL de cookie) de la transición de cuentas
/// de prueba a cuentas reales. Cada escenario mueve cuentas con las acciones administrativas reales por HTTP
/// y verifica en el siguiente request de las sesiones ya abiertas: rotación de clave, baja, degradación de
/// rol, intento de dejar al sistema sin administración y recuperación.
/// </summary>
[Collection("HttpIntegration.1")]
public class TransicionCuentasCookieHttpTests : IClassFixture<CookieAuthWebApplicationFactory>
{
    private const string Clave = "Test1234!";
    private const string ClaveNueva = "Nueva1234!";
    private readonly CookieAuthWebApplicationFactory _factory;

    public TransicionCuentasCookieHttpTests(CookieAuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Transicion_PropietarioYRecuperacion_LuegoSeRotaYSeDaDeBajaLaCuentaDeDemostracion()
    {
        await AsegurarRolesAsync();
        var demo = await CrearAsync("demo", Roles.SuperAdmin);
        var propietario = await CrearAsync("prop", Roles.SuperAdmin);
        var recuperacion = await CrearAsync("recu", Roles.SuperAdmin);
        await FijarSuperAdminsAsync(demo, propietario, recuperacion);

        var sDemo = Navegador(); await LoginAsync(sDemo, demo.UserName!);
        var sProp = Navegador(); await LoginAsync(sProp, propietario.UserName!);
        var sRecu = Navegador(); await LoginAsync(sRecu, recuperacion.UserName!);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(sProp)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(sRecu)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(sDemo)).StatusCode);

        // El propietario rota la clave de la cuenta demo: su sesión previa cae y la clave vieja no entra.
        var rot = await PostAsync(sProp, "/Usuarios/CambiarPassword", ("UserId", demo.Id), ("NewPassword", ClaveNueva), ("ConfirmPassword", ClaveNueva));
        Assert.Contains("\"success\":true", await rot.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminAsync(sDemo)).StatusCode);
        Assert.False(TieneSesion(await LoginAsync(Navegador(), demo.UserName!, Clave)));
        Assert.True(TieneSesion(await LoginAsync(Navegador(), demo.UserName!, ClaveNueva)));

        // Baja de la cuenta demo: la nueva sesión se revoca en el siguiente request y no hay login.
        var sDemo2 = Navegador(); await LoginAsync(sDemo2, demo.UserName!, ClaveNueva);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(sDemo2)).StatusCode);
        await PostAsync(sProp, "/Usuarios/Desactivar", ("id", demo.Id));
        Assert.False(await ActivoAsync(demo.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminAsync(sDemo2)).StatusCode);
        Assert.False(TieneSesion(await LoginAsync(Navegador(), demo.UserName!, ClaveNueva)));
        Assert.Equal(2, await SuperAdminUtilizablesAsync());
    }

    [Fact]
    public async Task PerdidaDeAccesoAdministrativo_NoSePuedeDejarSinAdministracion_YLaRecuperacionFunciona()
    {
        await AsegurarRolesAsync();
        var a = await CrearAsync("adm-a", Roles.SuperAdmin);
        var b = await CrearAsync("adm-b", Roles.SuperAdmin);
        await FijarSuperAdminsAsync(a, b);
        var sA = Navegador(); await LoginAsync(sA, a.UserName!);
        var sB = Navegador(); await LoginAsync(sB, b.UserName!);

        // A da de baja a B (permitido: queda A) y B pierde la sesión.
        await PostAsync(sA, "/Usuarios/Desactivar", ("id", b.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminAsync(sB)).StatusCode);

        // A, el último, no puede desactivarse ni eliminarse; conserva sesión y rol.
        await PostAsync(sA, "/Usuarios/Desactivar", ("id", a.Id));
        await PostAsync(sA, "/Usuarios/Delete", ("id", a.Id), ("motivo", "baja"));
        Assert.True(await ActivoAsync(a.Id));
        Assert.Equal(1, await SuperAdminUtilizablesAsync());
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(sA)).StatusCode);

        // Recuperación: A reactiva a B; B vuelve con login nuevo (la cookie vieja no revive).
        await PostAsync(sA, "/Usuarios/Reactivar", ("id", b.Id));
        Assert.True(await ActivoAsync(b.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdminAsync(sB)).StatusCode);
        var sB2 = Navegador(); await LoginAsync(sB2, b.UserName!);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(sB2)).StatusCode);
        Assert.Equal(2, await SuperAdminUtilizablesAsync());
    }

    [Fact]
    public async Task DegradacionDeRol_QuitarSuperAdmin_RevocaLaAutoridadEnElSiguienteRequest_ConLaMismaCookie()
    {
        await AsegurarRolesAsync();
        var a = await CrearAsync("deg-a", Roles.SuperAdmin);
        var b = await CrearAsync("deg-b", Roles.SuperAdmin);
        await FijarSuperAdminsAsync(a, b);
        var sB = Navegador(); await LoginAsync(sB, b.UserName!);
        Assert.Equal(HttpStatusCode.OK, (await AdminAsync(sB)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var ub = (await um.FindByIdAsync(b.Id))!;
            Assert.True((await um.RemoveFromRoleAsync(ub, Roles.SuperAdmin)).Succeeded);
            Assert.True((await um.AddToRoleAsync(ub, Roles.Vendedor)).Succeeded);
        }

        // La misma cookie ya no administra usuarios (los roles se releen de la base en cada request).
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminAsync(sB)).StatusCode);
    }

    // ── helpers ──
    private HttpClient Navegador() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static bool TieneSesion(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var v) &&
        v.Any(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal) &&
                   !c.StartsWith(".AspNetCore.Identity.Application=;", StringComparison.Ordinal));

    private static string Token(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient c, string user, string clave = Clave)
    {
        var page = await c.GetAsync("/Identity/Account/Login");
        return await c.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.UserName"] = user,
            ["Input.Password"] = clave,
            ["__RequestVerificationToken"] = Token(await page.Content.ReadAsStringAsync()),
        }));
    }

    /// <summary>Operación reservada a quien administra usuarios: 200 SuperAdmin, 403 sin permiso, 401 sin sesión.</summary>
    private static Task<HttpResponseMessage> AdminAsync(HttpClient c)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/Seguridad");
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return c.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient c, string url, params (string K, string V)[] campos)
    {
        var html = await (await c.GetAsync("/Seguridad")).Content.ReadAsStringAsync();
        var datos = campos.Select(x => new KeyValuePair<string, string>(x.K, x.V)).ToList();
        datos.Add(new("__RequestVerificationToken", Token(html)));
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(datos) };
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await c.SendAsync(req);
    }

    private async Task AsegurarRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await RolesPermisosSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
    }

    private async Task<ApplicationUser> CrearAsync(string prefijo, string rol)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var nombre = $"{prefijo}_{Guid.NewGuid():N}"[..24];
        var u = new ApplicationUser { UserName = nombre, Email = $"{nombre}@test.com", EmailConfirmed = true, Activo = true, LockoutEnabled = true };
        Assert.True((await um.CreateAsync(u, Clave)).Succeeded);
        Assert.True((await um.AddToRoleAsync(u, rol)).Succeeded);
        return u;
    }

    /// <summary>Estado de partida determinista: solo estos SuperAdmin (los de otros tests de la fábrica quedan fuera).</summary>
    private async Task FijarSuperAdminsAsync(params ApplicationUser[] cuentas)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rolId = await db.Roles.Where(r => r.Name == Roles.SuperAdmin).Select(r => r.Id).SingleAsync();
        var conservar = cuentas.Select(c => c.Id).ToHashSet();
        db.UserRoles.RemoveRange(await db.UserRoles.Where(ur => ur.RoleId == rolId && !conservar.Contains(ur.UserId)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task<bool> ActivoAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.Activo).SingleAsync();
    }

    private async Task<int> SuperAdminUtilizablesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var c = await (from u in db.Users.AsNoTracking()
                       join ur in db.UserRoles on u.Id equals ur.UserId
                       join r in db.Roles on ur.RoleId equals r.Id
                       where r.Name == Roles.SuperAdmin && u.Activo
                       select new { u.LockoutEnabled, u.LockoutEnd }).ToListAsync();
        return c.Count(x => !x.LockoutEnabled || x.LockoutEnd == null || x.LockoutEnd <= ahora);
    }
}
