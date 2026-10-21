using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PROMPT 57-B: enforcement REAL (pipeline HTTP completo) de la autorización por rol en Caja, Venta y MercadoLibre.
/// Un usuario con solo el permiso <c>view</c> del módulo no puede ejecutar NINGUNA acción mutante, y el SuperAdmin
/// (control positivo) sí atraviesa el filtro de autorización. Las acciones se descubren por reflexión, así que una
/// acción nueva queda cubierta sin tocar este archivo. Los filtros de autorización corren antes del model binding:
/// un cuerpo vacío alcanza para distinguir "denegado" de "autorizado".
/// </summary>
[Collection("HttpIntegration")]
public class AutorizacionPorRolCajaVentaMercadoLibreHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _out;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<string, string> _soloView = new();
    private string _superId = null!;

    // Acciones mutantes que por diseño solo exigen view (ver AccionesMutantesPermisoContractTests).
    private static readonly HashSet<string> SoloViewPorDiseno = new() { "MercadoLibre.ProbarConexion" };

    public AutorizacionPorRolCajaVentaMercadoLibreHttpTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
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

        foreach (var modulo in new[] { "caja", "ventas", "mercadolibre" })
        {
            var rol = $"SoloView_{modulo}_{_sfx}";
            await CrearRolConPermisoAsync(context, rol, $"{modulo}.view");
            _soloView[modulo] = await CrearUsuarioAsync(scope, $"v_{modulo}_{_sfx}", rol);
        }
        _superId = await CrearUsuarioAsync(scope, $"sup_{_sfx}", Roles.SuperAdmin);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object[]> Endpoints()
    {
        foreach (var (tipo, modulo, nombre) in new[]
                 {
                     (typeof(CajaController), "caja", "Caja"),
                     (typeof(VentaController), "ventas", "Venta"),
                     (typeof(MercadoLibreController), "mercadolibre", "MercadoLibre"),
                 })
        {
            foreach (var m in tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var http = m.GetCustomAttributes<HttpMethodAttribute>(true)
                    .FirstOrDefault(a => a.HttpMethods.Any(h => h is "POST" or "PUT" or "PATCH" or "DELETE"));
                if (http == null || SoloViewPorDiseno.Contains($"{nombre}.{m.Name}")) continue;
                var ruta = string.IsNullOrEmpty(http.Template) ? $"/{nombre}/{m.GetCustomAttribute<ActionNameAttribute>()?.Name ?? m.Name}" : $"/{nombre}/{http.Template}";
                // Los templates con parámetros ({id}) se resuelven con un valor cualquiera.
                ruta = Regex.Replace(ruta, @"\{[^}]+\}", "1");
                yield return new object[] { modulo, nombre, m.Name, ruta };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task UsuarioConSoloView_NoPuedeEjecutarAccionesMutantes(string modulo, string controlador, string accion, string ruta)
    {
        var client = _factory.CreateClientWithUserId(_soloView[modulo]);
        var response = await PostAsync(client, ruta);

        _out.WriteLine($"{controlador}.{accion} -> {(int)response.StatusCode}");
        Assert.True(Denegado(response), $"{controlador}.{accion} ({ruta}) respondió {(int)response.StatusCode} a un usuario con solo {modulo}.view.");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task SuperAdmin_AtraviesaElFiltroDeAutorizacion(string modulo, string controlador, string accion, string ruta)
    {
        var client = _factory.CreateClientWithUserId(_superId);
        var response = await PostAsync(client, ruta);

        _out.WriteLine($"{controlador}.{accion} -> {(int)response.StatusCode}");
        Assert.False(Denegado(response), $"{controlador}.{accion} ({ruta}) denegó al SuperAdmin ({(int)response.StatusCode}).");
    }

    private static bool Denegado(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Forbidden
        || r.StatusCode == HttpStatusCode.Unauthorized
        || ((int)r.StatusCode is >= 300 and < 400
            && (r.Headers.Location?.ToString().Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false));

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string ruta)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var html = await login.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Sin token antiforgery (HTTP {(int)login.StatusCode}).");
        var token = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync(ruta, new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
        }));
    }

    private static async Task CrearRolConPermisoAsync(AppDbContext context, string nombre, string claim)
    {
        var roleId = Guid.NewGuid().ToString();
        context.Roles.Add(new IdentityRole { Id = roleId, Name = nombre, NormalizedName = nombre.ToUpperInvariant() });
        await context.SaveChangesAsync();
        var partes = claim.Split('.', 2);
        var accion = await context.AccionesModulo.Include(a => a.Modulo)
            .SingleAsync(a => a.Modulo.Clave == partes[0] && a.Clave == partes[1]);
        context.RolPermisos.Add(new RolPermiso
        {
            RoleId = roleId, ModuloId = accion.ModuloId, AccionId = accion.Id,
            ClaimValue = claim, IsDeleted = false, RowVersion = new byte[8]
        });
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
