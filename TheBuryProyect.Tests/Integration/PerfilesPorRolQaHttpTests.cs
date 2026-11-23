using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
using TheBuryProject.Tests.Infrastructure;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Agente 4 (QA por perfil): complementa la matriz de DENEGACIÓN existente (<c>AutorizacionMatrizPorRolHttpTests</c>)
/// con (1) el lado POSITIVO: cada rol de sistema llega a todas las pantallas GET sin parámetros que sus permisos
/// cubren; (2) la autoridad sobre los permisos sensibles (usuarios, roles, autorizar ventas, cupos de crédito) y la
/// separación Administrador / SuperAdmin; y (3) que las acciones sensibles denegadas no dejan ninguna modificación.
/// Todo corre sobre la base SQLite descartable de la fábrica de pruebas (nunca producción).
/// </summary>
[Collection("HttpIntegration.4")]
public class PerfilesPorRolQaHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private static readonly string[] RolesSistema =
    {
        Roles.Administrador, Roles.Gerente, Roles.Vendedor, Roles.Cajero, Roles.Repositor, Roles.Tecnico, Roles.Contador,
    };

    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _out;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<string, string> _userIdPorRol = new();
    private readonly Dictionary<string, HashSet<string>> _grants = new();
    private string _adminRoleId = null!;
    private string _superAdminUserId = null!;
    private string _usuarioObjetivoId = null!;

    public PerfilesPorRolQaHttpTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _out = output;
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await RolesPermisosSeeder.SeedAsync(context, roleManager);

        foreach (var rol in RolesSistema)
        {
            var roleId = await context.Roles.Where(r => r.Name == rol).Select(r => r.Id).SingleAsync();
            _grants[rol] = (await context.RolPermisos.Where(p => p.RoleId == roleId && !p.IsDeleted)
                .Select(p => p.ClaimValue).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _userIdPorRol[rol] = await CrearUsuarioAsync(scope, $"qa4_{rol}_{_sfx}".ToLowerInvariant(), rol);
        }

        _adminRoleId = await context.Roles.Where(r => r.Name == Roles.Administrador).Select(r => r.Id).SingleAsync();
        _superAdminUserId = TestAuthHandler.UserId;
        _usuarioObjetivoId = await CrearUsuarioAsync(scope, $"qa4_objetivo_{_sfx}", Roles.Vendedor);

        for (var p = 0; p <= 5; p++)
        {
            if (!await context.PuntajesCreditoLimite.AnyAsync(x => x.Puntaje == p))
                context.PuntajesCreditoLimite.Add(new PuntajeCreditoLimite
                {
                    Puntaje = p, LimiteMonto = 100_000m + p, Activo = true,
                    FechaActualizacion = DateTime.UtcNow, UsuarioActualizacion = "seed",
                });
        }
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static IEnumerable<object[]> Roles7() => RolesSistema.Select(r => new object[] { r });

    // ───────────── 1. Lado positivo: cada rol llega a lo que sus permisos cubren ─────────────

    [Theory]
    [MemberData(nameof(Roles7))]
    public async Task ElRol_LlegaATodasLasPantallasGetSinParametrosQueSusPermisosCubren(string rol)
    {
        var claims = _grants[rol];
        var objetivo = Descubrir()
            .Where(a => a.Metodo == "GET" && !a.Ruta.Contains('{') && !DebeDenegar(a, claims, rol)
                        && !PantallasConControlInterno.ContainsKey(a.Ruta))
            .ToList();
        Assert.NotEmpty(objetivo);

        var client = _factory.CreateClientWithUserId(_userIdPorRol[rol]);
        var denegadas = new List<string>();
        var errores5xx = new List<string>();
        var ok = 0;
        foreach (var a in objetivo)
        {
            using var resp = await client.GetAsync(a.Ruta);
            if (Denegado(resp)) denegadas.Add($"GET {a.Ruta} [{a.Nombre}] -> {(int)resp.StatusCode}");
            else if ((int)resp.StatusCode >= 500) errores5xx.Add($"GET {a.Ruta} [{a.Nombre}] -> {(int)resp.StatusCode}");
            else ok++;
        }

        _out.WriteLine($"{rol}: {objetivo.Count} pantallas GET permitidas por sus permisos -> {ok} accesibles, {denegadas.Count} denegadas, {errores5xx.Count} con 5xx.");
        foreach (var e in errores5xx) _out.WriteLine("  5xx: " + e);
        Assert.True(denegadas.Count == 0, $"{rol} no llega a pantallas que sus permisos cubren:\n" + string.Join("\n", denegadas));
        Assert.True(errores5xx.Count == 0, $"{rol} recibe errores de servidor en pantallas permitidas:\n" + string.Join("\n", errores5xx));
    }

    /// <summary>Pantallas sin atributo cuyo acceso decide el propio action (Forbid): se verifican con su regla real.</summary>
    private static readonly Dictionary<string, string[]> PantallasConControlInterno = new()
    {
        ["/Seguridad/Index"] = new[] { "usuarios.view", "roles.view" },
        ["/DocumentosConfig/Index"] = new[] { "documentos.managetemplates", "documentos.managerules", "documentos.managetypes" },
    };

    [Theory]
    [MemberData(nameof(Roles7))]
    public async Task PantallasConControlInterno_SoloAbrenAQuienTieneAlgunoDeSusPermisos(string rol)
    {
        var client = _factory.CreateClientWithUserId(_userIdPorRol[rol]);
        foreach (var (ruta, permisos) in PantallasConControlInterno)
        {
            var debePoder = permisos.Any(p => _grants[rol].Contains(p));
            using var resp = await client.GetAsync(ruta);
            if (debePoder)
                Assert.False(Denegado(resp) || (int)resp.StatusCode >= 500, $"{rol} debía abrir {ruta} y fue HTTP {(int)resp.StatusCode}.");
            else
                Assert.True(Denegado(resp), $"{rol} NO debía abrir {ruta} y fue HTTP {(int)resp.StatusCode}.");
        }
    }

    // ───────────── 2. Autoridad sobre permisos sensibles y separación Administrador / SuperAdmin ─────────────

    public static IEnumerable<object[]> PermisosSensibles() => new[]
    {
        // permiso, roles de sistema que lo reciben sembrado (matriz aprobada, docs/permisos-matriz-roles.md)
        new object[] { "usuarios.create", new[] { Roles.Administrador } },
        new object[] { "usuarios.update", new[] { Roles.Administrador } },
        new object[] { "usuarios.assignroles", new[] { Roles.Administrador } },
        new object[] { "usuarios.delete", Array.Empty<string>() },
        new object[] { "usuarios.resetpassword", new[] { Roles.Administrador } },
        new object[] { "roles.create", new[] { Roles.Administrador } },
        new object[] { "roles.update", new[] { Roles.Administrador } },
        new object[] { "roles.assignpermissions", new[] { Roles.Administrador } },
        new object[] { "roles.delete", Array.Empty<string>() },
        new object[] { "configuracion.update", Array.Empty<string>() },
        new object[] { "configuracion.managepunitorio", Array.Empty<string>() },
        new object[] { "configuracion.recalcularscoringglobal", Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(PermisosSensibles))]
    public void PermisoSensible_SoLoLoTienenLosRolesDeLaMatrizAprobada(string permiso, string[] esperados)
    {
        var tienen = RolesSistema.Where(r => _grants[r].Contains(permiso)).OrderBy(r => r).ToArray();
        _out.WriteLine($"{permiso}: {(tienen.Length == 0 ? "(ningún rol de sistema salvo SuperAdmin)" : string.Join(", ", tienen))}");
        Assert.Equal(esperados.OrderBy(r => r).ToArray(), tienen);
    }

    [Theory]
    [InlineData("ventas.authorize")]
    [InlineData("clientes.managecreditlimits")]
    [InlineData("creditos.configure")]
    public void PermisoDeNegocioSensible_RegistraQuienLoTiene_YNingunRolOperativoPuroLoRecibe(string permiso)
    {
        var tienen = RolesSistema.Where(r => _grants[r].Contains(permiso)).OrderBy(r => r).ToArray();
        _out.WriteLine($"{permiso}: {string.Join(", ", tienen)}");
        // Cajero, Repositor, Tecnico y Contador no deciden autorizaciones ni cupos.
        if (permiso is "ventas.authorize" or "clientes.managecreditlimits")
            Assert.Empty(tienen.Intersect(new[] { Roles.Cajero, Roles.Repositor, Roles.Tecnico, Roles.Contador, Roles.Vendedor }));
    }

    [Fact]
    public void Administrador_RecibeTodoElCatalogoSalvoLaListaAprobada_YNoEsSuperAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var catalogo = context.AccionesModulo.Include(a => a.Modulo).AsNoTracking().ToList()
            .Select(a => $"{a.Modulo.Clave}.{a.Clave}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var faltan = catalogo.Except(_grants[Roles.Administrador], StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        _out.WriteLine("Permisos del catálogo que Administrador NO recibe: " + string.Join(", ", faltan));

        // Únicas exclusiones aprobadas (seeder + decisiones de producto documentadas); cualquier otra es una regresión.
        var aprobadas = new[]
        {
            "usuarios.delete", "roles.delete", "configuracion.update", "configuracion.managepunitorio",
            "configuracion.retroactivepunitorio", "configuracion.recalcularscoringglobal",
        };
        var noAprobadas = faltan.Except(aprobadas, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(noAprobadas.Count == 0, "Administrador carece de permisos fuera de la lista aprobada: " + string.Join(", ", noAprobadas));

        var roles = context.UserRoles.Where(ur => ur.UserId == _userIdPorRol[Roles.Administrador]).Select(ur => ur.RoleId).ToList();
        var nombres = context.Roles.Where(r => roles.Contains(r.Id)).Select(r => r.Name).ToList();
        Assert.DoesNotContain(Roles.SuperAdmin, nombres);
    }

    // ───────────── 3. Acciones sensibles denegadas no modifican datos ─────────────

    private sealed record Sensible(string Nombre, string Permiso, Func<PerfilesPorRolQaHttpTests, string> Ruta,
        Func<PerfilesPorRolQaHttpTests, List<KeyValuePair<string, string>>> Campos);

    private static List<KeyValuePair<string, string>> F(params (string, string)[] c) =>
        c.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)).ToList();

    private static readonly Sensible[] Sensibles =
    {
        new("Usuarios/Create", "usuarios.create", _ => "/Usuarios/Create",
            _ => F(("UserName", "intruso"), ("Email", "intruso@test.com"), ("Password", "Test123!"), ("ConfirmPassword", "Test123!"), ("Roles", Roles.SuperAdmin))),
        new("Seguridad/EditUsuario", "usuarios.update", _ => "/Seguridad/EditUsuario",
            t => F(("Id", t._usuarioObjetivoId), ("UserName", "renombrado"), ("Email", "x@test.com"), ("Activo", "false"), ("Roles", Roles.SuperAdmin))),
        new("Usuarios/BulkAction desactivar", "usuarios.update", _ => "/Usuarios/BulkAction",
            t => F(("ids", t._superAdminUserId), ("accion", "desactivar"))),
        new("Usuarios/BulkAction asignar rol", "usuarios.update", _ => "/Usuarios/BulkAction",
            t => F(("ids", t._usuarioObjetivoId), ("accion", "asignarrol"), ("rol", Roles.SuperAdmin))),
        new("Usuarios/CambiarPassword", "usuarios.resetpassword", _ => "/Usuarios/CambiarPassword",
            t => F(("Id", t._superAdminUserId), ("NuevaPassword", "Hack123!"), ("ConfirmarPassword", "Hack123!"))),
        new("Seguridad/CreateRol", "roles.create", _ => "/Seguridad/CreateRol",
            _ => F(("Nombre", "SuperAdmin2"), ("Name", "SuperAdmin2"))),
        new("Seguridad/DeleteRol", "roles.delete", t => $"/Seguridad/DeleteRol?id={t._adminRoleId}",
            t => F(("id", t._adminRoleId))),
        new("Seguridad/SavePermisosRol", "roles.assignpermissions", _ => "/Seguridad/SavePermisosRol",
            t => F(("roleId", t._adminRoleId), ("accionIds", "1"), ("accionIds", "2"))),
        new("Cliente/LimitesPorPuntajeGuardar", "clientes.managecreditlimits", _ => "/Cliente/LimitesPorPuntajeGuardar",
            _ => Enumerable.Range(0, 6).SelectMany(p => new[]
            {
                new KeyValuePair<string, string>($"Items[{p}].Puntaje", p.ToString()),
                new KeyValuePair<string, string>($"Items[{p}].LimiteMonto", "9999999"),
                new KeyValuePair<string, string>($"Items[{p}].Activo", "true"),
            }).ToList()),
        new("Cliente/AsignarLimiteCredito", "clientes.managecreditlimits", _ => "/Cliente/AsignarLimiteCredito",
            _ => F(("clienteId", "1"), ("limiteCredito", "9999999"), ("motivo", "qa"))),
        new("Cliente/AsignarNivelCreditoManual", "clientes.managecreditlimits", _ => "/Cliente/AsignarNivelCreditoManual",
            _ => F(("clienteId", "1"), ("nivelCreditoManual", "5"), ("motivo", "qa"))),
        new("Venta/Autorizar", "ventas.authorize", _ => "/Venta/Autorizar/1",
            _ => F(("id", "1"), ("motivo", "qa"))),
        new("ConfiguracionPago/CreditoPersonal", "configuracion.update", _ => "/ConfiguracionPago/CreditoPersonal",
            _ => F(("DefaultsGlobales.TasaMensual", "0"), ("DefaultsGlobales.GastosAdministrativos", "0"),
                   ("DefaultsGlobales.MinCuotas", "1"), ("DefaultsGlobales.MaxCuotas", "12"))),
    };

    public static IEnumerable<object[]> SensiblesPorRol() =>
        RolesSistema.SelectMany(r => Enumerable.Range(0, Sensibles.Length).Select(i => new object[] { r, i }));

    [Theory]
    [MemberData(nameof(SensiblesPorRol))]
    public async Task AccionSensibleDenegada_RespondeDenegacion_YNoModificaNingunDato(string rol, int indice)
    {
        var s = Sensibles[indice];
        if (_grants[rol].Contains(s.Permiso))
        {
            _out.WriteLine($"{rol} SÍ tiene {s.Permiso}: {s.Nombre} no es una denegación para este rol (no se envía).");
            return;
        }

        var antes = await HuellaAsync();
        var client = _factory.CreateClientWithUserId(_userIdPorRol[rol]);
        var token = await TokenAsync(client);
        var campos = s.Campos(this);
        campos.Add(new("__RequestVerificationToken", token));

        using var req = new HttpRequestMessage(HttpMethod.Post, s.Ruta(this)) { Content = new FormUrlEncodedContent(campos) };
        req.Headers.Add("RequestVerificationToken", token);
        using var resp = await client.SendAsync(req);

        Assert.True(Denegado(resp), $"{rol} -> {s.Nombre}: se esperaba denegación y fue HTTP {(int)resp.StatusCode}.");
        Assert.Equal(antes, await HuellaAsync());
    }

    [Fact]
    public async Task Administrador_NoPuedeAplicarSobreSuperAdminLasAccionesSensibles_YNoQuedanCambios()
    {
        // Alternativas por ruta equivalente a las ya cubiertas: el Administrador SÍ pasa el permiso, pero la guardia por
        // rol reservado debe frenar toda escritura sobre la cuenta o el rol SuperAdmin.
        var antes = await HuellaAsync();
        var client = _factory.CreateClientWithUserId(_userIdPorRol[Roles.Administrador]);
        var token = await TokenAsync(client);

        async Task Post(string ruta, params (string, string)[] campos)
        {
            var c = F(campos); c.Add(new("__RequestVerificationToken", token));
            using var req = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = new FormUrlEncodedContent(c) };
            req.Headers.Add("RequestVerificationToken", token);
            using var resp = await client.SendAsync(req);
            _out.WriteLine($"POST {ruta} -> {(int)resp.StatusCode}");
        }

        await Post("/Usuarios/BulkAction", ("ids", _superAdminUserId), ("accion", "desactivar"));
        await Post("/Usuarios/BulkAction", ("ids", _superAdminUserId), ("accion", "bloquear"));
        await Post("/Usuarios/BulkAction", ("ids", _superAdminUserId), ("accion", "quitarrol"), ("rol", Roles.SuperAdmin));
        await Post("/Usuarios/BulkAction", ("ids", _usuarioObjetivoId), ("accion", "asignarrol"), ("rol", Roles.SuperAdmin));

        Assert.Equal(antes, await HuellaAsync());
    }

    // ───────────── Soporte ─────────────

    /// <summary>Huella de lo que una acción sensible podría alterar: usuarios, roles, vínculos, permisos y cupos.</summary>
    private async Task<string> HuellaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var c = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sb = new StringBuilder();
        foreach (var u in await c.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.UserName, u.Email, u.Activo, u.LockoutEnd, u.PasswordHash, u.AccessFailedCount }).ToListAsync())
            sb.Append($"U|{u.Id}|{u.UserName}|{u.Email}|{u.Activo}|{u.LockoutEnd}|{u.PasswordHash}|{u.AccessFailedCount};");
        foreach (var r in await c.Roles.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Name }).ToListAsync())
            sb.Append($"R|{r.Id}|{r.Name};");
        foreach (var ur in await c.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).ToListAsync())
            sb.Append($"UR|{ur.UserId}|{ur.RoleId};");
        foreach (var p in await c.RolPermisos.AsNoTracking().OrderBy(p => p.RoleId).ThenBy(p => p.ClaimValue).Select(p => new { p.RoleId, p.ClaimValue, p.IsDeleted }).ToListAsync())
            sb.Append($"P|{p.RoleId}|{p.ClaimValue}|{p.IsDeleted};");
        foreach (var l in await c.PuntajesCreditoLimite.AsNoTracking().OrderBy(l => l.Puntaje).ToListAsync())
            sb.Append($"L|{l.Puntaje}|{l.LimiteMonto}|{l.Activo};");
        foreach (var cl in await c.Clientes.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.LimiteCredito, x.NivelRiesgo, x.PuntajeRiesgo }).ToListAsync())
            sb.Append($"C|{cl.Id}|{cl.LimiteCredito}|{cl.NivelRiesgo}|{cl.PuntajeRiesgo};");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed record Accion(string Nombre, string Ruta, string Metodo, List<(string Modulo, string Accion)> Permisos, List<string[]> RolesRequeridos);

    private static readonly HashSet<string> ControllersExcluidos = new() { "NotaRapidaApi", "Home", "MercadoLibreWebhook" };

    private List<Accion> Descubrir()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var lista = new List<Accion>();
        foreach (var d in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (d.ControllerTypeInfo.Namespace?.StartsWith("TheBuryProject.Controllers", StringComparison.Ordinal) != true) continue;
            if (ControllersExcluidos.Contains(d.ControllerName)) continue;
            var metodo = d.ActionConstraints?.OfType<HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? "GET";
            var ruta = "/" + (d.AttributeRouteInfo?.Template ?? $"{d.ControllerName}/{d.ActionName}").TrimStart('/');
            var permisos = d.ControllerTypeInfo.GetCustomAttributes<PermisoRequeridoAttribute>(true)
                .Concat(d.MethodInfo.GetCustomAttributes<PermisoRequeridoAttribute>(true))
                .Select(p => (p.Modulo, p.Accion)).Distinct().ToList();
            var roles = d.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(d.MethodInfo.GetCustomAttributes<AuthorizeAttribute>(true))
                .Where(a => a is not PermisoRequeridoAttribute && !string.IsNullOrWhiteSpace(a.Roles))
                .Select(a => a.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToList();
            lista.Add(new Accion($"{d.ControllerName}.{d.ActionName}", ruta, metodo, permisos, roles));
        }
        return lista;
    }

    private static bool DebeDenegar(Accion a, HashSet<string> claims, string rol) =>
        a.RolesRequeridos.Any(req => !req.Contains(rol, StringComparer.OrdinalIgnoreCase))
        || a.Permisos.Any(p => !claims.Any(c => PermissionAliasHelper.MatchesPermissionClaim(c, p.Modulo, p.Accion)));

    private static bool Denegado(HttpResponseMessage r) =>
        r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
        || ((int)r.StatusCode is >= 300 and < 400
            && (r.Headers.Location?.ToString().Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ?? false));

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var login = await client.GetAsync("/Identity/Account/Login");
        var m = Regex.Match(await login.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, $"Sin token antiforgery (HTTP {(int)login.StatusCode}).");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
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
