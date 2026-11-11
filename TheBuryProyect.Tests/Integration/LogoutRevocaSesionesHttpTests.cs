using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Cerrar sesión invalida TODAS las sesiones del mismo usuario, incluidas las cookies copiadas antes del logout.
/// Antes: el logout solo borraba la cookie del navegador y cualquier copia seguía siendo válida hasta expirar.
/// Pipeline REAL de cookie de Identity + antiforgery + ActiveUserCookieValidator; cada HttpClient con su cookie jar
/// equivale a un navegador distinto, y la «cookie copiada» es el valor crudo enviado por otro cliente sin jar.
/// </summary>
[Collection("HttpIntegration")]
public class LogoutRevocaSesionesHttpTests : IClassFixture<CookieAuthWebApplicationFactory>
{
    private const string Password = "Test1234!";
    private const string IdentityCookie = ".AspNetCore.Identity.Application";
    private readonly CookieAuthWebApplicationFactory _factory;
    private readonly ITestOutputHelper _out;

    public LogoutRevocaSesionesHttpTests(CookieAuthWebApplicationFactory factory, ITestOutputHelper output) { _factory = factory; _out = output; }

    // «Autenticado sin permiso» responde 403 en una llamada AJAX; sin sesión válida, 401.
    private const string RutaProtegida = "/Cliente";

    [Fact]
    public async Task CookieCopiadaAntesDelLogout_SeRechazaEnLaSiguienteSolicitud()
    {
        var user = await SeedAsync("copia");
        var navegador = NewBrowser();
        var login = await LoginAsync(navegador, user.UserName!);
        var cookie = ValorDeCookieDeSesion(login);
        var copia = NewCopia(cookie);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(navegador, RutaProtegida)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(copia, RutaProtegida)).StatusCode); // la copia hoy funciona

        await LogoutAsync(navegador);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(navegador, RutaProtegida)).StatusCode);
        // Inmediato: la MISMA solicitud siguiente, sin esperar el intervalo de 30 min del SecurityStampValidator estándar.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(copia, RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task VariosNavegadoresDelMismoUsuario_ElLogoutDeUnoRevocaATodos_PeroNoAOtrosUsuarios()
    {
        var user = await SeedAsync("multi");
        var otro = await SeedAsync("otro");
        var a = NewBrowser();
        var b = NewBrowser();
        var c = NewBrowser();
        var deOtro = NewBrowser();
        await LoginAsync(a, user.UserName!);
        await LoginAsync(b, user.UserName!);
        await LoginAsync(c, user.UserName!);
        await LoginAsync(deOtro, otro.UserName!);
        foreach (var cliente in new[] { a, b, c, deOtro })
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(cliente, RutaProtegida)).StatusCode);

        await LogoutAsync(b);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(a, RutaProtegida)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(b, RutaProtegida)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(c, RutaProtegida)).StatusCode);
        // Las sesiones de otro usuario no se tocan.
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(deOtro, RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task NuevoLoginTrasElLogout_Funciona_YLaCookieViejaSigueRevocada()
    {
        var user = await SeedAsync("relogin");
        var navegador = NewBrowser();
        var cookieVieja = ValorDeCookieDeSesion(await LoginAsync(navegador, user.UserName!));
        var copiaVieja = NewCopia(cookieVieja);
        await LogoutAsync(navegador);

        var login2 = await LoginAsync(navegador, user.UserName!);
        var cookieNueva = ValorDeCookieDeSesion(login2);

        Assert.NotEqual(cookieVieja, cookieNueva);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(navegador, RutaProtegida)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(NewCopia(cookieNueva), RutaProtegida)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(copiaVieja, RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task LogoutConUnaCookieYaRevocada_NoRevocaLaSesionVigente()
    {
        // Diseño: la rotación del stamp ocurre SOLO en el logout explícito de una sesión válida. Si ocurriera al rechazar una cookie
        // vieja (p. ej. en OnSigningOut), una copia vieja arrastraría la sesión legítima y vigente del usuario.
        var user = await SeedAsync("vieja");
        var a = NewBrowser();
        await LoginAsync(a, user.UserName!);
        await LogoutAsync(a);
        var vigente = NewBrowser();
        await LoginAsync(vigente, user.UserName!);
        var stampAntes = await StampAsync(user.Id);

        // Un navegador con la cookie vieja (ya revocada) intenta cerrar sesión y navega.
        var respuesta = await a.PostAsync("/Identity/Account/Logout", FormToken(await ObtenerTokenAsync(a)));
        await GetAjaxAsync(a, RutaProtegida);

        Assert.True(respuesta.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);
        Assert.Equal(stampAntes, await StampAsync(user.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(vigente, RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task Logout_RotaElSecurityStamp_YAuditaElEvento()
    {
        var user = await SeedAsync("stamp");
        var navegador = NewBrowser();
        await LoginAsync(navegador, user.UserName!);
        var antes = await StampAsync(user.Id);

        await LogoutAsync(navegador);

        Assert.NotEqual(antes, await StampAsync(user.Id));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.SeguridadEventosAuditoria.AnyAsync(e => e.Accion == "Logout" && e.Entidad == $"Usuario:{user.Id}"),
            "El logout debe dejar un evento de auditoría con el usuario.");
    }

    [Fact]
    public async Task GetLogout_NoCierraSesion_YRedirigeAlLogin()
    {
        var user = await SeedAsync("get");
        var navegador = NewBrowser();
        await LoginAsync(navegador, user.UserName!);
        var stampAntes = await StampAsync(user.Id);

        var respuesta = await navegador.GetAsync("/Identity/Account/Logout");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Identity/Account/Login", respuesta.Headers.Location!.ToString());
        Assert.Equal(stampAntes, await StampAsync(user.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(navegador, RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task PostLogout_SinTokenAntiforgery_SeRechaza_YNoRevocaNada()
    {
        var user = await SeedAsync("csrf");
        var navegador = NewBrowser();
        await LoginAsync(navegador, user.UserName!);
        var stampAntes = await StampAsync(user.Id);

        var respuesta = await navegador.PostAsync("/Identity/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(stampAntes, await StampAsync(user.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(navegador, RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task LogoutSinSesion_SeCompletaSinError()
    {
        var anonimo = NewBrowser();

        var respuesta = await anonimo.PostAsync("/Identity/Account/Logout", FormToken(await ObtenerTokenAsync(anonimo)));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("https://evil.example/robo", "/Identity/Account/Login")]
    [InlineData("//evil.example/robo", "/Identity/Account/Login")]
    public async Task Logout_ReturnUrl_SoloLocal(string returnUrl, string destinoEsperado)
    {
        var user = await SeedAsync("ret");
        var navegador = NewBrowser();
        await LoginAsync(navegador, user.UserName!);

        var respuesta = await navegador.PostAsync("/Identity/Account/Logout?returnUrl=" + Uri.EscapeDataString(returnUrl),
            FormToken(await ObtenerTokenAsync(navegador)));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.StartsWith(destinoEsperado, respuesta.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task DobleEnvioDelLogout_LaSegundaSolicitudLlegaConLaSesionYaRevocada_NoMuestraUn400()
    {
        // Doble clic: la 2.ª solicitud sale con la cookie de sesión anterior a la rotación del stamp y con un token antiforgery emitido
        // para el usuario autenticado. Debe terminar igual que la primera (redirección), no en un 400 como respuesta final.
        var user = await SeedAsync("doble");
        var navegador = NewBrowser();
        var cookie = ValorDeCookieDeSesion(await LoginAsync(navegador, user.UserName!));

        // Token + cookie antiforgery tal como los habría enviado el navegador, tomados con la sesión viva (antes del primer logout).
        var previo = NewCopia(cookie);
        var pagina = await previo.GetAsync("/Identity/Account/Login");
        var token = ExtraerToken(await pagina.Content.ReadAsStringAsync());
        var antiforgery = string.Join("; ", pagina.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])
            .Where(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)));
        Assert.False(string.IsNullOrEmpty(antiforgery), "No se obtuvo la cookie antiforgery.");

        HttpRequestMessage Envio()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Logout") { Content = FormToken(token) };
            req.Headers.Add("Cookie", $"{IdentityCookie}={cookie}; {antiforgery}");
            return req;
        }
        var sinJar = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var primera = await sinJar.SendAsync(Envio());
        var segunda = await sinJar.SendAsync(Envio());

        Assert.Equal(HttpStatusCode.Redirect, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, segunda.StatusCode);
        Assert.Contains("/Identity/Account/Login", segunda.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAjaxAsync(NewCopia(cookie), RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task PostLogoutConSesionViva_ConTokenDeOtroUsuario_SeRechaza_YNoRevocaNada()
    {
        // El token antiforgery de un usuario no sirve para forzar el logout de otro (CSRF cruzado).
        var victima = await SeedAsync("victima");
        var atacante = await SeedAsync("atacante");
        var navVictima = NewBrowser();
        var navAtacante = NewBrowser();
        await LoginAsync(navVictima, victima.UserName!);
        await LoginAsync(navAtacante, atacante.UserName!);
        var tokenAtacante = await ObtenerTokenAsync(navAtacante);
        var stampAntes = await StampAsync(victima.Id);

        var respuesta = await navVictima.PostAsync("/Identity/Account/Logout", FormToken(tokenAtacante));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(stampAntes, await StampAsync(victima.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAjaxAsync(navVictima, RutaProtegida)).StatusCode);
    }

    [Fact]
    public async Task CostoDelLogout_SonPocasConsultas_YLaSolicitudNormalNoAgregaNinguna()
    {
        var comandos = new CapturaComandosSql();
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureLogging(l =>
        {
            l.AddProvider(comandos);
            l.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        }));
        var user = await SeedAsync("costo", factory);
        var navegador = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(navegador, user.UserName!);
        var token = await ObtenerTokenAsync(navegador);

        comandos.Reiniciar();
        await GetAjaxAsync(navegador, RutaProtegida);
        var porSolicitud = comandos.Total;

        comandos.Reiniciar();
        await navegador.PostAsync("/Identity/Account/Logout", FormToken(token));
        var porLogout = comandos.Total;

        _out.WriteLine($"[costo SQL] solicitud autenticada normal = {porSolicitud} comandos; logout = {porLogout} comandos");
        foreach (var t in comandos.Textos) _out.WriteLine("  logout> " + System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ")[..Math.Min(150, System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Length)]);
        // Medido: el validador de cookie hace 1 SELECT por solicitud (ya existía; no se agrega nada al camino normal).
        // El logout suma la lectura del usuario, el UPDATE del stamp y el INSERT de auditoría.
        Assert.InRange(porSolicitud, 1, 6);
        Assert.InRange(porLogout, 3, 10);
        Assert.True(comandos.Textos.Count(t => t.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)) >= 1);
    }

    // ------------------------------------------------------------------ helpers

    private sealed class CapturaComandosSql : ILoggerProvider
    {
        private readonly List<string> _textos = new();
        public IReadOnlyList<string> Textos { get { lock (_textos) return _textos.ToList(); } }
        public int Total => Textos.Count;
        public void Reiniciar() { lock (_textos) _textos.Clear(); }
        public ILogger CreateLogger(string categoria) => new Logger(categoria, _textos);
        public void Dispose() { }

        private sealed class Logger(string categoria, List<string> textos) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState estado) where TState : notnull => null;
            public bool IsEnabled(LogLevel nivel) => true;
            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex, Func<TState, Exception?, string> formato)
            {
                if (categoria != "Microsoft.EntityFrameworkCore.Database.Command") return;
                var texto = formato(estado, ex);
                if (!texto.Contains("Executed DbCommand", StringComparison.Ordinal)) return;
                lock (textos) textos.Add(texto);
            }
        }
    }

    private HttpClient NewBrowser() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>Otro «dispositivo» con una copia cruda de la cookie de sesión (sin cookie jar propio).</summary>
    private HttpClient NewCopia(string valorCookie)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"{IdentityCookie}={valorCookie}");
        return client;
    }

    private static string NuevoNombre(string prefijo) => $"{prefijo}_{Guid.NewGuid():N}"[..28];

    private static string ExtraerToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<string> ObtenerTokenAsync(HttpClient client) =>
        ExtraerToken(await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync());

    private static FormUrlEncodedContent FormToken(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

    private static async Task LogoutAsync(HttpClient client)
    {
        var respuesta = await client.PostAsync("/Identity/Account/Logout", FormToken(await ObtenerTokenAsync(client)));
        Assert.True(respuesta.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK, $"Logout respondió {(int)respuesta.StatusCode}");
    }

    private static string ValorDeCookieDeSesion(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "El login no emitió cookies.");
        var linea = values!.FirstOrDefault(v => v.StartsWith(IdentityCookie + "=", StringComparison.Ordinal)
                                                && !v.StartsWith(IdentityCookie + "=;", StringComparison.Ordinal));
        Assert.NotNull(linea);
        return linea!.Split(';')[0][(IdentityCookie.Length + 1)..];
    }

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

    private async Task<string?> StampAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstOrDefaultAsync();
    }

    private async Task<ApplicationUser> SeedAsync(string prefijo, WebApplicationFactory<Program>? factory = null)
    {
        using var scope = (factory ?? _factory).Services.CreateScope();
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
        return user;
    }
}
