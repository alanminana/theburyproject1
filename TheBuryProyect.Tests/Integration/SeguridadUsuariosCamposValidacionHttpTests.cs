using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// VALID-L6 — alta de usuarios, roles y bloqueo por el pipeline HTTP real (antiforgery + permisos + binding + servicio).
/// Los valores fuera de regla no deben persistirse.
/// </summary>
[Collection("HttpIntegration")]
public class SeguridadUsuariosCamposValidacionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static int _counter = 97000;
    private readonly CustomWebApplicationFactory _factory;

    public SeguridadUsuariosCamposValidacionHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private const string RolBase = "QA Rol Base";

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(RolBase))
            await roles.CreateAsync(new IdentityRole(RolBase));
    }
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Sin token antiforgery.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private async Task<string> PostAsync(string ruta, Dictionary<string, string> campos)
    {
        var client = _factory.CreateAuthenticatedClient();
        campos["__RequestVerificationToken"] = await TokenAsync(client);
        var resp = await client.PostAsync(ruta, new FormUrlEncodedContent(campos));
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<ApplicationUser?> UsuarioAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(userName);
    }

    private async Task<IdentityRole?> RolAsync(string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().FindByNameAsync(nombre);
    }

    private static Dictionary<string, string> FormUsuario(string userName) => new()
    {
        ["UserName"] = userName,
        ["Email"] = $"{userName.ToLowerInvariant().Replace(' ', '_')}@qa.test",
        ["Password"] = "Abcdef1",
        ["ConfirmPassword"] = "Abcdef1",
        ["RolesSeleccionados"] = RolBase,
        ["Activo"] = "true",
        ["EmailConfirmed"] = "true",
    };

    private static string NuevoUsuario() => $"qa_user_{Interlocked.Increment(ref _counter)}";

    [Fact]
    public async Task Usuario_Valido_SeCrea()
    {
        var u = NuevoUsuario();
        var respuesta = await PostAsync("/Usuarios/Create", FormUsuario(u));
        Assert.True(await UsuarioAsync(u) != null, respuesta);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("con espacio")]
    [InlineData("tilde_ñandú")]
    [InlineData("<b>x</b>")]
    [InlineData("a;b")]
    public async Task Usuario_NombreFueraDeRegla_NoSeCrea(string userName)
    {
        await PostAsync("/Usuarios/Create", FormUsuario(userName));
        Assert.Null(await UsuarioAsync(userName));
    }

    [Fact]
    public async Task Usuario_EmailMasLargoQue256_NoSeCrea()
    {
        var u = NuevoUsuario();
        var form = FormUsuario(u);
        form["Email"] = new string('a', 250) + "@qa.test";
        await PostAsync("/Usuarios/Create", form);
        Assert.Null(await UsuarioAsync(u));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("sinmayuscula1")]
    [InlineData("SINMINUSCULA1")]
    [InlineData("SinNumeros")]
    public async Task Usuario_PasswordDebil_NoSeCrea(string pass)
    {
        var u = NuevoUsuario();
        var form = FormUsuario(u);
        form["Password"] = pass;
        form["ConfirmPassword"] = pass;
        await PostAsync("/Usuarios/Create", form);
        Assert.Null(await UsuarioAsync(u));
    }

    [Fact]
    public async Task Usuario_PasswordMasLargaQue100_NoSeCrea()
    {
        var u = NuevoUsuario();
        var form = FormUsuario(u);
        var larga = "Aa1" + new string('x', 150);
        form["Password"] = larga;
        form["ConfirmPassword"] = larga;
        await PostAsync("/Usuarios/Create", form);
        Assert.Null(await UsuarioAsync(u));
    }

    [Fact]
    public async Task Usuario_RolInexistente_NoSeCrea()
    {
        var u = NuevoUsuario();
        var form = FormUsuario(u);
        form["RolesSeleccionados"] = "RolQueNoExiste";
        await PostAsync("/Usuarios/Create", form);
        Assert.Null(await UsuarioAsync(u));
    }

    // ================= Roles =================

    private static string NuevoRol() => $"Rol QA {Interlocked.Increment(ref _counter)}";

    [Fact]
    public async Task Rol_Valido_SeCrea()
    {
        var r = NuevoRol();
        await PostAsync("/Seguridad/CreateRol", new() { ["Nombre"] = r, ["Descripcion"] = "x", ["Activo"] = "true" });
        Assert.NotNull(await RolAsync(r));
    }

    [Theory]
    [InlineData("<b>x</b>")]
    [InlineData("a;b")]
    [InlineData("Rol  doble")]
    [InlineData("Rol--x")]
    [InlineData("-Rol")]
    public async Task Rol_NombreFueraDeRegla_NoSeCrea(string nombre)
    {
        await PostAsync("/Seguridad/CreateRol", new() { ["Nombre"] = nombre, ["Activo"] = "true" });
        Assert.Null(await RolAsync(nombre));
    }

    [Fact]
    public async Task Rol_DescripcionMasLargaQue500_NoSeCrea()
    {
        var r = NuevoRol();
        await PostAsync("/Seguridad/CreateRol", new() { ["Nombre"] = r, ["Descripcion"] = new string('x', 501), ["Activo"] = "true" });
        Assert.Null(await RolAsync(r));
    }

    // ================= Bloqueo =================

    private async Task<ApplicationUser> CrearUsuarioParaBloqueoAsync()
    {
        var u = NuevoUsuario();
        await PostAsync("/Usuarios/Create", FormUsuario(u));
        var user = await UsuarioAsync(u);
        Assert.NotNull(user);
        return user!;
    }

    [Theory]
    [InlineData(-1, 0)]        // pasado
    [InlineData(0, 11)]        // más de 10 años
    public async Task Bloquear_FechaInvalida_NoBloquea(int dias, int anios)
    {
        var user = await CrearUsuarioParaBloqueoAsync();
        var hasta = DateTime.Now.AddDays(dias).AddYears(anios);
        await PostAsync("/Usuarios/Bloquear", new()
        {
            ["UserId"] = user.Id,
            ["UserName"] = user.UserName!,
            ["MotivoBloqueo"] = "prueba",
            ["BloqueadoHasta"] = hasta.ToString("yyyy-MM-ddTHH:mm"),
        });
        var luego = await UsuarioAsync(user.UserName!);
        Assert.True(luego!.LockoutEnd == null || luego.LockoutEnd <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Bloquear_DentroDeLasProximasHoras_SeAplica()
    {
        // Regresión: el valor local se comparaba contra UTC y se rechazaba un bloqueo válido de las próximas horas.
        var user = await CrearUsuarioParaBloqueoAsync();
        await PostAsync("/Usuarios/Bloquear", new()
        {
            ["UserId"] = user.Id,
            ["UserName"] = user.UserName!,
            ["MotivoBloqueo"] = "prueba",
            ["BloqueadoHasta"] = DateTime.Now.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm"),
        });
        var luego = await UsuarioAsync(user.UserName!);
        Assert.True(luego!.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Bloquear_MotivoMasLargoQue250_NoBloquea()
    {
        var user = await CrearUsuarioParaBloqueoAsync();
        await PostAsync("/Usuarios/Bloquear", new()
        {
            ["UserId"] = user.Id,
            ["UserName"] = user.UserName!,
            ["MotivoBloqueo"] = new string('x', 251),
        });
        var luego = await UsuarioAsync(user.UserName!);
        Assert.True(luego!.LockoutEnd == null || luego.LockoutEnd <= DateTimeOffset.UtcNow);
    }
}
