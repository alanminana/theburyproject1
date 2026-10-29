using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// H2-B2 P5: matriz de autorización REAL (pipeline HTTP completo) sobre TODAS las acciones MVC.
/// Para cada rol (sistema + "sin permisos" + "solo view") se calcula por reflexión qué acciones debería denegarle
/// <c>[PermisoRequerido]</c>/<c>[Authorize(Roles)]</c> (los filtros de clase y de método se acumulan) según sus
/// permisos sembrados, y se verifica por HTTP que TODAS esas respuestas son denegaciones. Solo se envían las
/// peticiones que deben ser denegadas (si el filtro funciona, ningún handler corre).
/// </summary>
[Collection("HttpIntegration")]
public class AutorizacionMatrizPorRolHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string SinPermisos = "H2B2_SinPermisos";
    private const string SoloView = "H2B2_SoloView";
    private string NombreRol(string rol) => rol is SinPermisos or SoloView ? $"{rol}_{_sfx}" : rol;

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _out;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<string, string> _userIdPorRol = new();
    private readonly Dictionary<string, HashSet<string>> _grants = new();

    public AutorizacionMatrizPorRolHttpTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolesPermisosSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());

        var catalogo = await context.AccionesModulo.Include(a => a.Modulo).ToListAsync();

        await CrearRolAsync(context, NombreRol(SinPermisos), Array.Empty<AccionModulo>());
        await CrearRolAsync(context, NombreRol(SoloView), catalogo.Where(a => a.Clave.StartsWith("view", StringComparison.Ordinal)).ToArray());

        var roles = new[] { Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Cajero, Roles.Repositor, Roles.Tecnico, Roles.Contador, SinPermisos, SoloView };
        foreach (var rol in roles)
        {
            var roleId = await context.Roles.Where(r => r.Name == NombreRol(rol)).Select(r => r.Id).SingleAsync();
            _grants[rol] = (await context.RolPermisos.Where(p => p.RoleId == roleId && !p.IsDeleted)
                .Select(p => p.ClaimValue).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _userIdPorRol[rol] = await CrearUsuarioAsync(scope, $"h2b2_{rol}_{_sfx}".ToLowerInvariant(), NombreRol(rol));
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object[]> RolesAProbar() => new[]
    {
        Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Cajero, Roles.Repositor, Roles.Tecnico, Roles.Contador,
        SinPermisos, SoloView,
    }.Select(r => new object[] { r });

    private sealed record Accion(string Nombre, string Ruta, string Metodo, List<(string Modulo, string Accion)> Permisos, List<string[]> RolesRequeridos);

    /// <summary>Controllers cuyo control de acceso es por diseño propio (autenticado / dueño del recurso / firma externa).</summary>
    private static readonly HashSet<string> ControllersExcluidos = new() { "NotaRapidaApi", "Home", "MercadoLibreWebhook" };

    private List<Accion> DescubrirAcciones()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var lista = new List<Accion>();
        foreach (var d in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (d.ControllerTypeInfo.Namespace?.StartsWith("TheBuryProject.Controllers", StringComparison.Ordinal) != true) continue;
            if (ControllersExcluidos.Contains(d.ControllerName)) continue;
            var nombre = $"{d.ControllerName}.{d.ActionName}";

            var metodo = d.ActionConstraints?.OfType<HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? "GET";
            var ruta = d.AttributeRouteInfo?.Template ?? $"{d.ControllerName}/{d.ActionName}";
            ruta = "/" + Regex.Replace(ruta, @"\{[^}]+\}", "1").TrimStart('/');

            var permisos = d.ControllerTypeInfo.GetCustomAttributes<PermisoRequeridoAttribute>(true)
                .Concat(d.MethodInfo.GetCustomAttributes<PermisoRequeridoAttribute>(true))
                .Select(p => (p.Modulo, p.Accion)).Distinct().ToList();
            var roles = d.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(d.MethodInfo.GetCustomAttributes<AuthorizeAttribute>(true))
                .Where(a => a is not PermisoRequeridoAttribute && !string.IsNullOrWhiteSpace(a.Roles))
                .Select(a => a.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToList();
            lista.Add(new Accion(nombre, ruta, metodo, permisos, roles));
        }
        return lista;
    }

    private static bool DebeDenegar(Accion a, HashSet<string> claims, string rol) =>
        a.RolesRequeridos.Any(req => !req.Contains(rol, StringComparer.OrdinalIgnoreCase))
        || a.Permisos.Any(p => !claims.Any(c => PermissionAliasHelper.MatchesPermissionClaim(c, p.Modulo, p.Accion)));

    [Fact]
    public void ElDescubrimiento_EncuentraLasAccionesEsperadas()
    {
        var acciones = DescubrirAcciones();
        Assert.True(acciones.Count > 300, $"Se descubrieron {acciones.Count} acciones.");
        Assert.Contains(acciones, a => a.Nombre == "Caja.Abrir" && a.Permisos.Count == 2);
    }

    [Fact]
    public void ElDescubrimiento_IncluyeLasAccionesConActionNameDistintoDelMetodo()
    {
        // H9-M6: la matriz documentada se genera por NOMBRE DE MÉTODO (DeleteConfirmed, AutorizarConfirmed...), pero la ruta
        // real usa [ActionName]. El barrido parte de los descriptores del runtime, así que esas acciones SÍ se prueban.
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var alias = provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(d => d.MethodInfo.Name != d.ActionName)
            .Select(d => $"{d.ControllerName}.{d.ActionName}").ToHashSet();
        Assert.Contains("Venta.Delete", alias);
        Assert.Contains("Venta.Autorizar", alias);
        Assert.Contains("Venta.Cancelar", alias);
        Assert.Contains("Venta.Rechazar", alias);
        Assert.Contains("OrdenCompra.Delete", alias);
        Assert.Contains("Catalogo.AplicarCambioPrecioDirecto", alias);

        var acciones = DescubrirAcciones();
        Assert.All(alias, nombre => Assert.Contains(acciones, a => a.Nombre == nombre));
    }

    [Theory]
    [MemberData(nameof(RolesAProbar))]
    public async Task ElRol_NoPuedeEjecutarNingunaAccionQueSusPermisosNoCubren(string rol)
    {
        var claims = _grants[rol];
        var objetivo = DescubrirAcciones().Where(a => DebeDenegar(a, claims, rol)).ToList();
        Assert.NotEmpty(objetivo);

        var client = _factory.CreateClientWithUserId(_userIdPorRol[rol]);
        var token = await ObtenerTokenAsync(client);
        var fallas = new List<string>();

        foreach (var a in objetivo)
        {
            HttpResponseMessage? resp = null;
            // Un [Consumes] distinto responde 415 antes de autorizar: se reintenta con cada tipo de contenido.
            for (var intento = 0; intento < 3; intento++)
            {
                resp?.Dispose();
                using var req = new HttpRequestMessage(new HttpMethod(a.Metodo), a.Ruta);
                if (a.Metodo != "GET")
                {
                    req.Headers.Add("RequestVerificationToken", token);
                    req.Content = intento switch
                    {
                        0 => new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("__RequestVerificationToken", token) }),
                        1 => new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } },
                        _ => new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                    };
                }
                resp = await client.SendAsync(req);
                if (resp.StatusCode != HttpStatusCode.UnsupportedMediaType || a.Metodo == "GET") break;
            }
            if (!Denegado(resp!))
                fallas.Add($"{a.Metodo} {a.Ruta} [{a.Nombre}] -> {(int)resp!.StatusCode}");
            resp!.Dispose();
        }

        _out.WriteLine($"{rol}: {objetivo.Count} acciones que debia denegar, {fallas.Count} fallas.");
        Assert.True(fallas.Count == 0, $"{rol} accedio a acciones que sus permisos no cubren:\n" + string.Join("\n", fallas));
    }

    [Fact]
    public async Task AlertaStock_SoloViewPuedeVerPeroNoVeNiEjecutaLasAccionesDeEscritura()
    {
        var soloView = _factory.CreateClientWithUserId(_userIdPorRol[SoloView]);
        var index = await soloView.GetAsync("/AlertaStock");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.DoesNotContain("Generar alertas", await index.Content.ReadAsStringAsync());

        var token = await ObtenerTokenAsync(soloView);
        using var post = new HttpRequestMessage(HttpMethod.Post, "/AlertaStock/GenerarAlertas");
        post.Headers.Add("RequestVerificationToken", token);
        post.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("__RequestVerificationToken", token) });
        Assert.True(Denegado(await soloView.SendAsync(post)));

        var repositor = _factory.CreateClientWithUserId(_userIdPorRol[Roles.Repositor]);
        var conPermiso = await repositor.GetAsync("/AlertaStock");
        Assert.Contains("Generar alertas", await conPermiso.Content.ReadAsStringAsync());
    }

    private static bool Denegado(HttpResponseMessage r) =>
        r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
        || ((int)r.StatusCode is >= 300 and < 400
            && (r.Headers.Location?.ToString().Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false));

    private static async Task<string> ObtenerTokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Sin token antiforgery (HTTP {(int)login.StatusCode}).");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task CrearRolAsync(AppDbContext context, string nombre, AccionModulo[] acciones)
    {
        var roleId = Guid.NewGuid().ToString();
        context.Roles.Add(new IdentityRole { Id = roleId, Name = nombre, NormalizedName = nombre.ToUpperInvariant() });
        await context.SaveChangesAsync();
        foreach (var a in acciones)
        {
            context.RolPermisos.Add(new RolPermiso
            {
                RoleId = roleId, ModuloId = a.ModuloId, AccionId = a.Id,
                ClaimValue = $"{a.Modulo.Clave}.{a.Clave}", IsDeleted = false, RowVersion = new byte[8]
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
