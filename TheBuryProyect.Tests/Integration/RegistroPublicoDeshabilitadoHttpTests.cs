using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// SEC-S-003: nadie puede autorregistrarse. Toda cuenta nueva nace por la administración autorizada
/// (/Usuarios/Create). Pipeline REAL de cookie de Identity + antiforgery; cada HttpClient con su cookie jar
/// equivale a un navegador distinto. Después de cada intento se verifica directamente el contenido de la base.
/// </summary>
[Collection("HttpIntegration")]
public class RegistroPublicoDeshabilitadoHttpTests : IClassFixture<CookieAuthWebApplicationFactory>
{
    private const string Password = "Test1234!";
    private const string IdentityCookie = ".AspNetCore.Identity.Application";
    private readonly CookieAuthWebApplicationFactory _factory;

    public RegistroPublicoDeshabilitadoHttpTests(CookieAuthWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/Identity/Account/Register?returnUrl=%2F")]
    [InlineData("/Identity/Account/ExternalLogin")]
    public async Task Get_PaginaDeAltaPublica_Anonimo_Devuelve404(string url)
    {
        var response = await NewBrowser().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Register_Anonimo_ConTokenValido_Devuelve404_NoCreaUsuario_NiSesion()
    {
        var client = NewBrowser();
        var userName = NuevoNombre("anon");

        var response = await PostRegisterAsync(client, userName);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(TieneCookieDeSesion(response));
        Assert.False(await ExisteUsuarioAsync(userName));
        // Sin sesión nueva: un endpoint que sólo exige autenticación sigue rechazando.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(client, "/api/notas-rapidas")).StatusCode);
    }

    [Fact]
    public async Task Post_Register_Anonimo_SinToken_Devuelve404_NoCreaUsuario()
    {
        var userName = NuevoNombre("sintoken");

        var response = await NewBrowser().PostAsync("/Identity/Account/Register", FormRegistro(userName, token: null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await ExisteUsuarioAsync(userName));
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task OtrosVerbos_Register_NoCreanUsuario(string verbo)
    {
        var userName = NuevoNombre("verbo");
        var request = new HttpRequestMessage(new HttpMethod(verbo), "/Identity/Account/Register")
        {
            Content = FormRegistro(userName, token: null),
        };

        var response = await NewBrowser().SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Se esperaba 404/405 y se obtuvo {(int)response.StatusCode}.");
        Assert.False(await ExisteUsuarioAsync(userName));
    }

    [Fact]
    public async Task Post_Register_ConHandlerArbitrario_NoCreaUsuario()
    {
        var client = NewBrowser();
        var userName = NuevoNombre("handler");
        var token = await ObtenerTokenAsync(client);

        var response = await client.PostAsync("/Identity/Account/Register?handler=Confirmation",
            FormRegistro(userName, token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(await ExisteUsuarioAsync(userName));
    }

    [Fact]
    public async Task Register_UsuarioAutenticadoSinPermisos_GetYPost_Devuelven404_NoCreanUsuario()
    {
        var existente = await SeedAsync("autenticado");
        var client = NewBrowser();
        Assert.True(TieneCookieDeSesion(await LoginAsync(client, existente.UserName!)));
        var userName = NuevoNombre("auth");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Identity/Account/Register")).StatusCode);
        var post = await PostRegisterAsync(client, userName);

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.False(await ExisteUsuarioAsync(userName));
    }

    [Fact]
    public async Task Register_Administrador_TambienDevuelve404_LaAltaSoloEsPorUsuariosCreate()
    {
        var admin = await SeedAsync("admin-reg", rol: "SuperAdmin");
        var client = NewBrowser();
        await LoginAsync(client, admin.UserName!);
        var userName = NuevoNombre("adm-reg");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Identity/Account/Register")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostRegisterAsync(client, userName)).StatusCode);
        Assert.False(await ExisteUsuarioAsync(userName));
    }

    [Fact]
    public async Task ExternalLogin_Post_Devuelve404_YNoHayProveedoresExternosConfigurados()
    {
        var client = NewBrowser();
        var token = await ObtenerTokenAsync(client);

        var post = await client.PostAsync("/Identity/Account/ExternalLogin?handler=Confirmation",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = $"{NuevoNombre("ext")}@test.com",
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var schemes = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        var externos = (await schemes.GetRequestHandlerSchemesAsync()).Select(s => s.Name).ToList();
        Assert.Empty(externos);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>()
            .GetExternalAuthenticationSchemesAsync());
    }

    [Fact]
    public async Task PantallaDeLogin_NoOfreceEnlaceDeRegistro()
    {
        var html = await (await NewBrowser().GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Account/Register", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Existente_SigueFuncionando_YLogoutCierraLaSesion()
    {
        var user = await SeedAsync("login-ok");
        var client = NewBrowser();

        var login = await LoginAsync(client, user.UserName!);
        Assert.True(TieneCookieDeSesion(login));
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await GetAjaxAsync(client, "/Cliente")).StatusCode);

        var logout = await client.PostAsync("/Identity/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await ObtenerTokenAsync(client),
        }));

        Assert.True(logout.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(client, "/Cliente")).StatusCode);
    }

    [Fact]
    public async Task AccessDenied_Y_Lockout_SiguenAccesiblesSinSesion()
    {
        Assert.Equal(HttpStatusCode.OK, (await NewBrowser().GetAsync("/Identity/Account/AccessDenied")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NewBrowser().GetAsync("/Identity/Account/Lockout")).StatusCode);
    }

    [Fact]
    public async Task RecuperacionDeContrasena_NoSeDeshabilita()
    {
        var user = await SeedAsync("forgot");
        var client = NewBrowser();

        var get = await client.GetAsync("/Identity/Account/ForgotPassword");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var post = await client.PostAsync("/Identity/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["__RequestVerificationToken"] = ExtraerToken(await get.Content.ReadAsStringAsync()),
        }));

        Assert.True((int)post.StatusCode is >= 200 and < 400, $"ForgotPassword POST devolvió {(int)post.StatusCode}.");
        Assert.NotEqual(HttpStatusCode.NotFound,
            (await NewBrowser().GetAsync("/Identity/Account/ResetPassword?code=abc")).StatusCode);
    }

    [Fact]
    public async Task CreacionAdministrativa_SigueFuncionando()
    {
        var admin = await SeedAsync("admin-alta", rol: "SuperAdmin");
        var client = NewBrowser();
        await LoginAsync(client, admin.UserName!);
        var nuevo = NuevoNombre("alta");
        await AsegurarRolAsync("Vendedor");

        var form = await client.GetAsync("/Usuarios/Create");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var create = await client.PostAsync("/Usuarios/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = nuevo,
            ["Email"] = $"{nuevo}@test.com",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
            ["Activo"] = "true",
            ["RolesSeleccionados"] = "Vendedor",
            ["__RequestVerificationToken"] = ExtraerToken(await form.Content.ReadAsStringAsync()),
        }));

        var body = await create.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Contains("\"success\":true", body.Replace(" ", string.Empty));
        Assert.True(await ExisteUsuarioAsync(nuevo));
        Assert.True(await TieneRolAsync(nuevo, "Vendedor"));
    }

    [Fact]
    public async Task CreacionAdministrativa_UsuarioSinPermiso_NoPuedeCrear()
    {
        var comun = await SeedAsync("sin-perm");
        var client = NewBrowser();
        await LoginAsync(client, comun.UserName!);
        var nuevo = NuevoNombre("denegado");

        var create = await client.PostAsync("/Usuarios/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = nuevo,
            ["Email"] = $"{nuevo}@test.com",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
            ["__RequestVerificationToken"] = await ObtenerTokenAsync(client),
        }));

        Assert.NotEqual(HttpStatusCode.OK, create.StatusCode);
        Assert.False(await ExisteUsuarioAsync(nuevo));
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private HttpClient NewBrowser() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static string NuevoNombre(string prefijo) => $"{prefijo}_{Guid.NewGuid():N}"[..28];

    private static string ExtraerToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Token antiforgery válido (y su cookie en el jar del cliente), tomado de la pantalla de login.</summary>
    private static async Task<string> ObtenerTokenAsync(HttpClient client) =>
        ExtraerToken(await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync());

    private static FormUrlEncodedContent FormRegistro(string userName, string? token)
    {
        var campos = new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Email"] = $"{userName}@test.com",
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        };
        if (token is not null) campos["__RequestVerificationToken"] = token;
        return new FormUrlEncodedContent(campos);
    }

    private static async Task<HttpResponseMessage> PostRegisterAsync(HttpClient client, string userName) =>
        await client.PostAsync("/Identity/Account/Register", FormRegistro(userName, await ObtenerTokenAsync(client)));

    private static bool TieneCookieDeSesion(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) &&
        values.Any(v => v.StartsWith(IdentityCookie + "=", StringComparison.Ordinal) &&
                        !v.StartsWith(IdentityCookie + "=;", StringComparison.Ordinal));

    private static Task<HttpResponseMessage> GetAjaxAsync(HttpClient client, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName)
    {
        var token = await ObtenerTokenAsync(client);
        return await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = token,
        }));
    }

    private async Task<bool> ExisteUsuarioAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == userName || u.Email == $"{userName}@test.com");
    }

    private async Task AsegurarRolAsync(string rol)
    {
        using var scope = _factory.Services.CreateScope();
        var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await rm.RoleExistsAsync(rol)) await rm.CreateAsync(new IdentityRole(rol));
    }

    private async Task<bool> TieneRolAsync(string userName, string rol)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await um.FindByNameAsync(userName);
        return u is not null && await um.IsInRoleAsync(u, rol);
    }

    private async Task<ApplicationUser> SeedAsync(string prefijo, string? rol = null)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userName = NuevoNombre(prefijo);
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@test.com",
            EmailConfirmed = true,
            Activo = true,
            LockoutEnabled = true,
        };
        var result = await um.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        if (rol is not null)
        {
            var rm = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await rm.RoleExistsAsync(rol)) await rm.CreateAsync(new IdentityRole(rol));
            Assert.True((await um.AddToRoleAsync(user, rol)).Succeeded);
        }
        return user;
    }
}
