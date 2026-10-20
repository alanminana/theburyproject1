using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PROMPT 56-A: <c>UsuariosController.BulkAction</c> es todo o nada. Cada test provoca un fallo individual
/// determinista (UserManager de pruebas) en una posición concreta y verifica EN BD que no quedó ningún
/// usuario, rol, bloqueo ni sucursal confirmado, y que la auditoría refleja lo persistido.
/// </summary>
public sealed class FaultyUserManagerFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<UserManager<ApplicationUser>>();
            services.AddScoped<UserManager<ApplicationUser>, FaultyUserManager>();
        });
    }
}

/// <summary>Falla de forma controlada según el usuario y la operación registrados en <see cref="Faults"/>.</summary>
public sealed class FaultyUserManager : UserManager<ApplicationUser>
{
    public const string Update = "update";
    public const string LockoutEnabled = "lockoutEnabled";
    public const string LockoutEnd = "lockoutEnd";
    public const string AddRole = "addRole";
    public const string Concurrency = "concurrency";

    /// <summary>userName → operación a hacer fallar.</summary>
    public static ConcurrentDictionary<string, string> Faults { get; } = new();

    public FaultyUserManager(
        IUserStore<ApplicationUser> store, IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<ApplicationUser> passwordHasher, IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators, ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors, IServiceProvider services, ILogger<UserManager<ApplicationUser>> logger)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
    }

    private bool Debe(ApplicationUser user, string operacion) =>
        user.UserName != null && Faults.TryGetValue(user.UserName, out var op) && op == operacion;

    public override async Task<IdentityResult> UpdateAsync(ApplicationUser user)
    {
        if (Debe(user, Update))
            return IdentityResult.Failed(ErrorDescriber.DefaultError());

        // Es el resultado que UserStore devuelve ante DbUpdateConcurrencyException. SQLite no aplica el
        // RowVersion como SQL Server: el conflicto real contra SQL Server se prueba en
        // BulkActionAtomicidadSqlServerTests.
        if (Debe(user, Concurrency))
            return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());

        return await base.UpdateAsync(user);
    }

    public override Task<IdentityResult> SetLockoutEnabledAsync(ApplicationUser user, bool enabled) =>
        Debe(user, LockoutEnabled)
            ? Task.FromResult(IdentityResult.Failed(ErrorDescriber.DefaultError()))
            : base.SetLockoutEnabledAsync(user, enabled);

    public override Task<IdentityResult> SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd) =>
        Debe(user, LockoutEnd)
            ? Task.FromResult(IdentityResult.Failed(ErrorDescriber.DefaultError()))
            : base.SetLockoutEndDateAsync(user, lockoutEnd);

    public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role) =>
        Debe(user, AddRole)
            ? Task.FromResult(IdentityResult.Failed(ErrorDescriber.DefaultError()))
            : base.AddToRoleAsync(user, role);
}

[Collection("HttpIntegration")]
public class BulkActionAtomicidadHttpTests : IClassFixture<FaultyUserManagerFactory>, IAsyncLifetime
{
    private readonly FaultyUserManagerFactory _factory;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];

    private string _superId = null!;
    private string _adminId = null!;
    private string _sinPermisosId = null!;
    private string _superObjetivoId = null!;
    private string[] _ids = null!; // a, b, c: el orden por Id coincide con el de inserción
    private string[] _nombres = null!;
    private int _sucursalId;

    public BulkActionAtomicidadHttpTests(FaultyUserManagerFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedTestUserAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await RolesPermisosSeeder.SeedAsync(context, roleManager);

        _superId = await CrearUsuarioAsync(scope, $"super_{_sfx}", Roles.SuperAdmin);
        _adminId = await CrearUsuarioAsync(scope, $"adm_{_sfx}", Roles.Administrador);
        _sinPermisosId = await CrearUsuarioAsync(scope, $"sinperm_{_sfx}", null);
        _superObjetivoId = await CrearUsuarioAsync(scope, $"superobj_{_sfx}", Roles.SuperAdmin);

        _nombres = new[] { "a", "b", "c" }.Select(n => $"bulk{n}_{_sfx}").ToArray();
        _ids = new[] { "a", "b", "c" }.Select(n => $"{n}{_sfx}").ToArray();
        for (var i = 0; i < 3; i++)
            await CrearUsuarioAsync(scope, _nombres[i], Roles.Vendedor, _ids[i]);

        var sucursal = new Sucursal { Nombre = $"Suc{_sfx}", Activa = true, RowVersion = new byte[8] };
        context.Add(sucursal);
        await context.SaveChangesAsync();
        _sucursalId = sucursal.Id;
    }

    public Task DisposeAsync()
    {
        foreach (var nombre in _nombres ?? [])
            FaultyUserManager.Faults.TryRemove(nombre, out _);
        return Task.CompletedTask;
    }

    // ───────────────────────── 1. camino feliz ─────────────────────────

    [Theory]
    [InlineData("desactivar")]
    [InlineData("bloquear")]
    [InlineData("asignarRol")]
    [InlineData("cambiarSucursal")]
    public async Task TodosCorrectos_SeConfirmaTodo(string accion)
    {
        var antes = await EventosAuditoriaAsync("Acción masiva");

        var response = await PostBulkAsync(_superId, _ids, accion);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        foreach (var id in _ids)
        {
            var e = await EstadoAsync(id);
            switch (accion)
            {
                case "desactivar": Assert.False(e.Activo); break;
                case "bloquear": Assert.True(e.LockoutEnd > DateTimeOffset.UtcNow.AddYears(1)); break;
                case "asignarRol": Assert.Contains(Roles.Cajero, e.Roles); break;
                case "cambiarSucursal": Assert.Equal(_sucursalId, e.SucursalId); break;
            }
        }

        // La auditoría refleja lo persistido: un evento de éxito, ninguno de reversión.
        Assert.Equal(antes + 1, await EventosAuditoriaAsync("Acción masiva"));
    }

    // ───────────────────────── 2-4 y 5. falla UpdateAsync en primer/intermedio/último usuario ─────────────────────────

    [Theory]
    [InlineData("desactivar", 0)]
    [InlineData("desactivar", 1)]
    [InlineData("desactivar", 2)]
    [InlineData("cambiarSucursal", 1)]
    public async Task FallaUpdateAsync_RevierteTodoSinEscriturasParciales(string accion, int posicionQueFalla)
    {
        FaultyUserManager.Faults[_nombres[posicionQueFalla]] = FaultyUserManager.Update;
        var antes = await SnapshotAsync();
        var revertidosAntes = await EventosAuditoriaAsync("Revertido");
        var exitosAntes = await EventosAuditoriaAsync("Acción masiva");

        var response = await PostBulkAsync(_superId, _ids, accion);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(antes, await SnapshotAsync());
        Assert.Equal(revertidosAntes + 1, await EventosAuditoriaAsync("Revertido"));
        Assert.Equal(exitosAntes, await EventosAuditoriaAsync("Acción masiva"));
    }

    // ───────────────────────── 6. falla un método de bloqueo de Identity ─────────────────────────

    [Theory]
    [InlineData(FaultyUserManager.LockoutEnabled, 1)]
    [InlineData(FaultyUserManager.LockoutEnd, 1)]
    [InlineData(FaultyUserManager.LockoutEnd, 2)]
    public async Task FallaMetodoDeBloqueo_RevierteTodo(string operacion, int posicionQueFalla)
    {
        FaultyUserManager.Faults[_nombres[posicionQueFalla]] = operacion;
        var antes = await SnapshotAsync();

        var response = await PostBulkAsync(_superId, _ids, "bloquear");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(antes, await SnapshotAsync());
        foreach (var id in _ids)
            Assert.Null((await EstadoAsync(id)).LockoutEnd);
    }

    // ───────────────────────── 7. falla la asignación de un rol ─────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task FallaAsignacionDeRol_RevierteTodo(int posicionQueFalla)
    {
        FaultyUserManager.Faults[_nombres[posicionQueFalla]] = FaultyUserManager.AddRole;
        var antes = await SnapshotAsync();

        var response = await PostBulkAsync(_superId, _ids, "asignarRol");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(antes, await SnapshotAsync());
        foreach (var id in _ids)
            Assert.DoesNotContain(Roles.Cajero, (await EstadoAsync(id)).Roles);
    }

    // ───────────────────────── 8. conflicto de concurrencia optimista real ─────────────────────────

    [Fact]
    public async Task ResultadoDeConcurrenciaOptimista_NoDejaEscriturasParciales()
    {
        FaultyUserManager.Faults[_nombres[2]] = FaultyUserManager.Concurrency;
        var antes = await SnapshotAsync();

        var response = await PostBulkAsync(_superId, _ids, "desactivar");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(antes, await SnapshotAsync());
        foreach (var id in _ids)
            Assert.True((await EstadoAsync(id)).Activo);
    }

    // ───────────────────────── 9. rechazo total por autorización ─────────────────────────

    [Theory]
    [InlineData("desactivar")]
    [InlineData("bloquear")]
    [InlineData("asignarRol")]
    [InlineData("cambiarSucursal")]
    public async Task AdministradorConUnSuperAdminEnLaSeleccion_RechazoTotal(string accion)
    {
        var ids = new[] { _ids[0], _superObjetivoId, _ids[1] };
        var antes = await SnapshotAsync([.. ids]);

        var response = await PostBulkAsync(_adminId, ids, accion);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(antes, await SnapshotAsync([.. ids]));
    }

    [Theory]
    [InlineData("desactivar")]
    [InlineData("bloquear")]
    [InlineData("asignarRol")]
    [InlineData("cambiarSucursal")]
    public async Task UsuarioSinPermisos_RechazoTotal(string accion)
    {
        var antes = await SnapshotAsync();

        var response = await PostBulkAsync(_sinPermisosId, _ids, accion);

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.Equal(antes, await SnapshotAsync());
    }

    [Fact]
    public async Task IdInexistenteEnLaSeleccion_NoAplicaANingunoDeLosExistentes()
    {
        var antes = await SnapshotAsync();

        var response = await PostBulkAsync(_superId, [_ids[0], _ids[1], $"no-existe-{_sfx}"], "desactivar");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(antes, await SnapshotAsync());
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task<HttpResponseMessage> PostBulkAsync(string actorId, IEnumerable<string> ids, string accion)
    {
        var client = _factory.CreateClientWithUserId(actorId);
        var html = await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);

        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value)),
            new("ids", string.Join(",", ids)),
            new("accion", accion)
        };
        if (accion == "asignarRol") campos.Add(new("rol", Roles.Cajero));
        if (accion == "cambiarSucursal") campos.Add(new("sucursalId", _sucursalId.ToString()));
        return await client.PostAsync("/Usuarios/BulkAction", new FormUrlEncodedContent(campos));
    }

    private static async Task<string> CrearUsuarioAsync(IServiceScope scope, string userName, string? rol, string? id = null)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.com", EmailConfirmed = true, Activo = true };
        if (id != null) user.Id = id;
        var result = await userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        if (rol != null)
        {
            var add = await userManager.AddToRoleAsync(user, rol);
            Assert.True(add.Succeeded, string.Join(", ", add.Errors.Select(e => e.Description)));
        }
        return user.Id;
    }

    private sealed record Estado(
        bool Activo, string? SecurityStamp, bool LockoutEnabled, DateTimeOffset? LockoutEnd,
        int? SucursalId, string? Sucursal, string? DesactivadoPor, string? Motivo, List<string> Roles);

    private async Task<Estado> EstadoAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var u = await context.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
        var roles = await (from ur in context.UserRoles
                           join r in context.Roles on ur.RoleId equals r.Id
                           where ur.UserId == userId
                           orderby r.Name
                           select r.Name!).ToListAsync();
        return new Estado(u.Activo, u.SecurityStamp, u.LockoutEnabled, u.LockoutEnd, u.SucursalId, u.Sucursal,
            u.DesactivadoPor, u.MotivoDesactivacion, roles);
    }

    private async Task<string> SnapshotAsync(params string[] ids)
    {
        var lista = ids.Length > 0 ? ids : _ids;
        var partes = new List<string>();
        foreach (var id in lista)
        {
            var e = await EstadoAsync(id);
            partes.Add($"{id}|{e.Activo}|{e.SecurityStamp}|{e.LockoutEnabled}|{e.LockoutEnd:O}|{e.SucursalId}|{e.Sucursal}|{e.DesactivadoPor}|{e.Motivo}|{string.Join(",", e.Roles)}");
        }
        return string.Join("\n", partes);
    }

    private async Task<int> EventosAuditoriaAsync(string accion)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.SeguridadEventosAuditoria.AsNoTracking().CountAsync(e => e.Modulo == "Seguridad" && e.Accion == accion);
    }
}
