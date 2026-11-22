using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Agente 2 (seguridad por roles): los cupos por puntaje solo los administra <c>clientes.managecreditlimits</c>.
/// La pantalla Configuración de pagos → Crédito personal (<c>configuracion.update</c>) también guarda esa grilla;
/// sin el permiso de cupos el POST no puede cambiarla (los valores enviados se descartan, sin error).
/// </summary>
[Collection("HttpIntegration.4")]
public class ConfiguracionCreditoLimitesPermisoHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];
    private string _soloConfigId = null!;
    private string _configYCuposId = null!;

    public ConfiguracionCreditoLimitesPermisoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RolesPermisosSeeder.SeedAsync(context, scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());

        await CrearRolAsync(context, $"CfgSolo_{_sfx}", "configuracion.view", "configuracion.update");
        await CrearRolAsync(context, $"CfgCupos_{_sfx}", "configuracion.view", "configuracion.update", "clientes.managecreditlimits");
        _soloConfigId = await CrearUsuarioAsync(scope, $"cfgsolo_{_sfx}", $"CfgSolo_{_sfx}");
        _configYCuposId = await CrearUsuarioAsync(scope, $"cfgcupos_{_sfx}", $"CfgCupos_{_sfx}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConfiguracionUpdateSinManageCreditLimits_NoPuedeCambiarLosCuposPorPuntaje_YLaPantallaSigueGuardando()
    {
        await SembrarCuposAsync(0m);
        var client = _factory.CreateClientWithUserId(_soloConfigId);

        var response = await PostCreditoPersonalAsync(client, limite: 750_000m);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.All(await LeerCuposAsync(), c => Assert.Equal(0m, c));
    }

    [Fact]
    public async Task ConfiguracionUpdateConManageCreditLimits_SiPuedeCambiarLosCuposPorPuntaje()
    {
        await SembrarCuposAsync(0m);
        var client = _factory.CreateClientWithUserId(_configYCuposId);

        var response = await PostCreditoPersonalAsync(client, limite: 750_000m);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.All(await LeerCuposAsync(), c => Assert.Equal(750_000m, c));
    }

    [Fact]
    public async Task SuperAdmin_SiPuedeCambiarLosCuposPorPuntaje()
    {
        await SembrarCuposAsync(0m);
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await PostCreditoPersonalAsync(client, limite: 500_000m);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.All(await LeerCuposAsync(), c => Assert.Equal(500_000m, c));
    }

    private async Task SembrarCuposAsync(decimal limite)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existentes = await context.PuntajesCreditoLimite.ToListAsync();
        for (var p = 0; p <= 5; p++)
        {
            var fila = existentes.FirstOrDefault(x => x.Puntaje == p);
            if (fila == null)
                context.PuntajesCreditoLimite.Add(new PuntajeCreditoLimite { Puntaje = p, LimiteMonto = limite, Activo = true, FechaActualizacion = DateTime.UtcNow, UsuarioActualizacion = "seed" });
            else
            {
                fila.LimiteMonto = limite;
                fila.Activo = true;
            }
        }
        await context.SaveChangesAsync();
    }

    private async Task<List<decimal>> LeerCuposAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var filas = await context.PuntajesCreditoLimite.AsNoTracking().Where(p => p.Puntaje <= 5).ToListAsync();
        Assert.Equal(6, filas.Count);
        return filas.Select(f => f.LimiteMonto).ToList();
    }

    private static async Task<HttpResponseMessage> PostCreditoPersonalAsync(HttpClient client, decimal limite)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var match = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Sin token antiforgery (HTTP {(int)login.StatusCode}).");

        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value)),
            new("DefaultsGlobales.TasaMensual", "0"),
            new("DefaultsGlobales.GastosAdministrativos", "0"),
            new("DefaultsGlobales.MinCuotas", "1"),
            new("DefaultsGlobales.MaxCuotas", "12"),
        };
        for (var p = 0; p <= 5; p++)
        {
            campos.Add(new($"LimitesPorPuntaje[{p}].Puntaje", p.ToString()));
            campos.Add(new($"LimitesPorPuntaje[{p}].LimiteMonto", limite.ToString("0")));
            campos.Add(new($"LimitesPorPuntaje[{p}].Activo", "true"));
        }

        return await client.PostAsync("/ConfiguracionPago/CreditoPersonal", new FormUrlEncodedContent(campos));
    }

    private static async Task CrearRolAsync(AppDbContext context, string nombre, params string[] claims)
    {
        var roleId = Guid.NewGuid().ToString();
        context.Roles.Add(new IdentityRole { Id = roleId, Name = nombre, NormalizedName = nombre.ToUpperInvariant() });
        await context.SaveChangesAsync();

        foreach (var claim in claims)
        {
            var partes = claim.Split('.', 2);
            var accion = await context.AccionesModulo.Include(a => a.Modulo)
                .SingleAsync(a => a.Modulo.Clave == partes[0] && a.Clave == partes[1]);
            context.RolPermisos.Add(new RolPermiso
            {
                RoleId = roleId, ModuloId = accion.ModuloId, AccionId = accion.Id,
                ClaimValue = claim, IsDeleted = false, RowVersion = new byte[8]
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
