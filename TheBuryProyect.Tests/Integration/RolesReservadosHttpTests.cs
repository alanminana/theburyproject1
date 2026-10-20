using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PROMPT 56-B: los nombres Administrador/Gerente/SuperAdmin otorgan autoridad implícita (IsInRole en Caja,
/// Venta, MercadoLibre). Quien administra roles sin ser SuperAdmin no puede crearlos, renombrarlos,
/// eliminarlos, duplicarlos hacia ese nombre ni asignarlos sin tener esa misma autoridad. Cada test arma
/// la petición HTTP a mano y verifica en BD.
/// </summary>
[Collection("HttpIntegration")]
public class RolesReservadosHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];

    private string _superId = null!;
    private string _adminId = null!;
    private string _gerenteId = null!;
    private string _gestorRolesId = null!;   // custom: administra roles y asigna roles, sin ser Administrador
    private string _objetivoId = null!;      // usuario ordinario
    private string _sinRolId = null!;        // usuario sin roles: el gestor sí puede administrarlo
    private string _rolBase = null!;         // rol personalizado legítimo (subset de permisos del gestor)

    public RolesReservadosHttpTests(CustomWebApplicationFactory factory)
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

        var rolGestor = await CrearRolConPermisosAsync(context, $"RolGestorRoles{_sfx}",
            "usuarios.view", "usuarios.update", "usuarios.create", "usuarios.assignroles",
            "roles.view", "roles.create", "roles.update", "roles.delete");
        _rolBase = await CrearRolConPermisosAsync(context, $"RolBase{_sfx}", "usuarios.view");

        _superId = await CrearUsuarioAsync(scope, $"super_{_sfx}", Roles.SuperAdmin);
        _adminId = await CrearUsuarioAsync(scope, $"adm_{_sfx}", Roles.Administrador);
        _gerenteId = await CrearUsuarioAsync(scope, $"ger_{_sfx}", Roles.Gerente);
        _gestorRolesId = await CrearUsuarioAsync(scope, $"gestor_{_sfx}", rolGestor);
        _objetivoId = await CrearUsuarioAsync(scope, $"objetivo_{_sfx}", Roles.Vendedor);
        _sinRolId = await CrearUsuarioAsync(scope, $"sinrol_{_sfx}", null);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ───────────────────────── crear con nombre reservado ─────────────────────────

    [Theory]
    [InlineData("Administrador")]
    [InlineData("administrador")]
    [InlineData("ADMINISTRADOR")]
    [InlineData("  Gerente  ")]
    [InlineData("gerente")]
    [InlineData("GERENTE")]
    [InlineData("SuperAdmin")]
    [InlineData("superadmin")]
    public async Task CrearRol_ConNombreReservadoYCualquierCapitalizacion_SeRechazaYNoSePersiste(string nombre)
    {
        var rolesAntes = await ContarRolesAsync();
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/CreateRol", Form(
            ("__RequestVerificationToken", token), ("Nombre", nombre), ("Activo", "true")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(rolesAntes, await ContarRolesAsync());
    }

    [Fact]
    public async Task RolReservadoLiberado_UnNoSuperAdminNoPuedeRecrearloParaSuplantarlo()
    {
        var client = _factory.CreateClientWithUserId(_superId);
        var token = await TokenAsync(client);
        var gerenteId = await IdDeRolAsync(Roles.Gerente);
        var liberado = $"Gerente_viejo_{_sfx}";

        try
        {
            // Un SuperAdmin renombra legítimamente el rol Gerente: el nombre queda libre.
            var renombrar = await client.PostAsync("/Seguridad/EditRol", Form(
                ("__RequestVerificationToken", token), ("Id", gerenteId), ("Nombre", liberado), ("Activo", "true")));
            Assert.Equal(HttpStatusCode.OK, renombrar.StatusCode);
            Assert.Null(await IdDeRolOrNullAsync(Roles.Gerente));

            // El gestor intenta crear "Gerente" y asignárselo: no puede ni crearlo.
            var gestor = _factory.CreateClientWithUserId(_gestorRolesId);
            var tokenGestor = await TokenAsync(gestor);
            var crear = await gestor.PostAsync("/Seguridad/CreateRol", Form(
                ("__RequestVerificationToken", tokenGestor), ("Nombre", Roles.Gerente), ("Activo", "true")));
            Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
            Assert.Null(await IdDeRolOrNullAsync(Roles.Gerente));

            // Tampoco renombrando un rol propio hacia el nombre liberado.
            var rolPropio = await IdDeRolAsync(_rolBase);
            var renombrarPropio = await gestor.PostAsync("/Seguridad/EditRol", Form(
                ("__RequestVerificationToken", tokenGestor), ("Id", rolPropio), ("Nombre", Roles.Gerente), ("Activo", "true")));
            Assert.Equal(HttpStatusCode.Forbidden, renombrarPropio.StatusCode);
            Assert.Null(await IdDeRolOrNullAsync(Roles.Gerente));
            Assert.NotNull(await IdDeRolOrNullAsync(_rolBase));
        }
        finally
        {
            await client.PostAsync("/Seguridad/EditRol", Form(
                ("__RequestVerificationToken", token), ("Id", gerenteId), ("Nombre", Roles.Gerente), ("Activo", "true")));
        }

        Assert.Equal(gerenteId, await IdDeRolOrNullAsync(Roles.Gerente));
    }

    [Fact]
    public async Task SuperAdmin_PuedeCrearRolesPersonalizados()
    {
        var client = _factory.CreateClientWithUserId(_superId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/CreateRol", Form(
            ("__RequestVerificationToken", token), ("Nombre", $"Supervisor{_sfx}"), ("Activo", "true")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await IdDeRolOrNullAsync($"Supervisor{_sfx}"));
    }

    [Fact]
    public async Task Gestor_SigueCreandoYRenombrandoRolesPersonalizados()
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var crear = await client.PostAsync("/Seguridad/CreateRol", Form(
            ("__RequestVerificationToken", token), ("Nombre", $"Propio{_sfx}"), ("Activo", "true")));
        Assert.Equal(HttpStatusCode.OK, crear.StatusCode);
        var id = await IdDeRolAsync($"Propio{_sfx}");

        var renombrar = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token), ("Id", id), ("Nombre", $"PropioRenombrado{_sfx}"), ("Activo", "true")));
        Assert.Equal(HttpStatusCode.OK, renombrar.StatusCode);
        Assert.Equal(id, await IdDeRolOrNullAsync($"PropioRenombrado{_sfx}"));
    }

    // ───────────────────────── renombrar / eliminar roles reservados ─────────────────────────

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Gerente")]
    public async Task EditRol_NoSuperAdminNoPuedeRenombrarUnRolReservado(string reservado)
    {
        var id = await IdDeRolAsync(reservado);
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var renombrar = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token), ("Id", id), ("Nombre", $"Otro{_sfx}"), ("Activo", "true")));
        var capitalizacion = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token), ("Id", id), ("Nombre", reservado.ToUpperInvariant()), ("Activo", "true")));

        Assert.Equal(HttpStatusCode.Forbidden, renombrar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, capitalizacion.StatusCode);
        Assert.Equal(id, await IdDeRolOrNullAsync(reservado));
        Assert.Null(await IdDeRolOrNullAsync($"Otro{_sfx}"));
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Gerente")]
    public async Task DeleteRol_NoSuperAdminNoPuedeEliminarUnRolReservado(string reservado)
    {
        var id = await IdDeRolAsync(reservado);
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        await client.PostAsync("/Seguridad/DeleteRol", Form(("__RequestVerificationToken", token), ("id", id)));

        Assert.Equal(id, await IdDeRolOrNullAsync(reservado));
    }

    [Fact]
    public async Task DeleteRol_UnRolPersonalizadoSigueSiendoEliminablePorElGestor()
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);
        var id = await CrearRolSimpleAsync($"Descartable{_sfx}");

        await client.PostAsync("/Seguridad/DeleteRol", Form(("__RequestVerificationToken", token), ("id", id)));

        Assert.Null(await IdDeRolOrNullAsync($"Descartable{_sfx}"));
    }

    [Fact]
    public async Task EditRol_ConservarElNombreExactoDeUnRolReservadoNoSeBloquea()
    {
        // Editar descripción/estado sin tocar la identidad no es una suplantación.
        var id = await IdDeRolAsync(Roles.Gerente);
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token), ("Id", id), ("Nombre", Roles.Gerente),
            ("Descripcion", $"Descripción {_sfx}"), ("Activo", "true")));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(id, await IdDeRolOrNullAsync(Roles.Gerente));
    }

    // ───────────────────────── duplicar / copiar ─────────────────────────

    [Theory]
    [InlineData("Administrador")]
    [InlineData("gerente")]
    [InlineData(" SUPERADMIN ")]
    public async Task DuplicarRol_HaciaUnNombreReservado_SeRechazaYNoSePersiste(string nombre)
    {
        var rolesAntes = await ContarRolesAsync();
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/DuplicateRol", Form(
            ("__RequestVerificationToken", token), ("RolOrigenId", await IdDeRolAsync(_rolBase)),
            ("Nombre", nombre), ("Activo", "true")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(rolesAntes, await ContarRolesAsync());
    }

    [Fact]
    public async Task DuplicarRol_ConNombreComunSigueFuncionando()
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/DuplicateRol", Form(
            ("__RequestVerificationToken", token), ("RolOrigenId", await IdDeRolAsync(_rolBase)),
            ("Nombre", $"Copia{_sfx}"), ("Activo", "true")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await IdDeRolOrNullAsync($"Copia{_sfx}"));
    }

    // ───────────────────────── asignar / quitar roles reservados ─────────────────────────

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Gerente")]
    public async Task EditUsuario_GestorSinLaAutoridadNoPuedeAsignarseNiAsignarUnRolReservado(string reservado)
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        // A sí mismo.
        var propio = await PostEditUsuarioAsync(client, token, _gestorRolesId, $"gestor_{_sfx}", agregar: reservado);
        // A otro usuario.
        var ajeno = await PostEditUsuarioAsync(client, token, _objetivoId, $"objetivo_{_sfx}", agregar: reservado);

        Assert.Equal(HttpStatusCode.Forbidden, propio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.DoesNotContain(reservado, await RolesDeUsuarioAsync(_gestorRolesId));
        Assert.DoesNotContain(reservado, await RolesDeUsuarioAsync(_objetivoId));
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Gerente")]
    public async Task BulkAction_GestorSinLaAutoridadNoPuedeAsignarUnRolReservado(string reservado)
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token), ("ids", $"{_objetivoId},{_gestorRolesId}"),
            ("accion", "asignarRol"), ("rol", reservado)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.DoesNotContain(reservado, await RolesDeUsuarioAsync(_objetivoId));
        Assert.DoesNotContain(reservado, await RolesDeUsuarioAsync(_gestorRolesId));
    }

    [Fact]
    public async Task Create_GestorSinLaAutoridadNoPuedeCrearUsuarioConRolReservado()
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);
        var userName = $"nuevo_adm_{_sfx}";

        var response = await client.PostAsync("/Usuarios/Create", Form(
            ("__RequestVerificationToken", token),
            ("UserName", userName), ("Email", $"{userName}@test.com"),
            ("Password", "Test123!"), ("ConfirmPassword", "Test123!"),
            ("RolesSeleccionados", Roles.Administrador), ("Activo", "true"), ("EmailConfirmed", "true")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await ExisteUsuarioAsync(userName));
    }

    [Fact]
    public async Task EditUsuario_GestorNoPuedeQuitarUnRolReservadoAOtroUsuario()
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _adminId, $"adm_{_sfx}", quitar: Roles.Administrador);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Roles.Administrador, await RolesDeUsuarioAsync(_adminId));
    }

    [Fact]
    public async Task Gestor_SigueAsignandoRolesPersonalizados()
    {
        var client = _factory.CreateClientWithUserId(_gestorRolesId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _sinRolId, $"sinrol_{_sfx}", agregar: _rolBase);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(_rolBase, await RolesDeUsuarioAsync(_sinRolId));
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Gerente")]
    public async Task Administrador_ConservaLaAutoridadDeAsignarAdministradorYGerente(string reservado)
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _objetivoId, $"objetivo_{_sfx}", agregar: reservado);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(reservado, await RolesDeUsuarioAsync(_objetivoId));
    }

    [Fact]
    public async Task SuperAdmin_AsignaRolesReservadosSinRestriccion()
    {
        var client = _factory.CreateClientWithUserId(_superId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _objetivoId, $"objetivo_{_sfx}", agregar: Roles.Administrador);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(Roles.Administrador, await RolesDeUsuarioAsync(_objetivoId));
    }

    [Fact]
    public async Task BulkAction_AdministradorAsignaGerenteAUsuariosOrdinarios()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token), ("ids", _objetivoId), ("accion", "asignarRol"), ("rol", Roles.Gerente)));

        Assert.Contains(Roles.Gerente, await RolesDeUsuarioAsync(_objetivoId));
    }

    // ───────────────────────── helpers ─────────────────────────

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] campos) =>
        new(campos.Select(c => new KeyValuePair<string, string>(c.Key, c.Value)));

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Identity/Account/Login");
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"Sin token antiforgery (HTTP {(int)response.StatusCode}).");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Edita al usuario conservando sus roles actuales y agregando/quitando uno.</summary>
    private async Task<HttpResponseMessage> PostEditUsuarioAsync(
        HttpClient client, string token, string userId, string userName, string? agregar = null, string? quitar = null)
    {
        var actuales = await RolesDeUsuarioAsync(userId);
        var roles = actuales.Where(r => !string.Equals(r, quitar, StringComparison.OrdinalIgnoreCase)).ToList();
        if (agregar != null && !roles.Contains(agregar)) roles.Add(agregar);

        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("Id", userId),
            new("UserName", userName),
            new("Email", $"{userName}@test.com"),
            new("Activo", "true"),
            new("RowVersion", Convert.ToBase64String(await RowVersionAsync(userId)))
        };
        campos.AddRange(roles.Select(r => new KeyValuePair<string, string>("RolesSeleccionados", r)));
        return await client.PostAsync("/Seguridad/EditUsuario", new FormUrlEncodedContent(campos));
    }

    private static async Task<string> CrearRolConPermisosAsync(AppDbContext context, string nombre, params string[] claims)
    {
        var roleId = Guid.NewGuid().ToString();
        context.Roles.Add(new IdentityRole { Id = roleId, Name = nombre, NormalizedName = nombre.ToUpperInvariant() });
        await context.SaveChangesAsync();

        foreach (var claim in claims)
        {
            var partes = claim.Split('.', 2);
            var accion = await context.AccionesModulo
                .Include(a => a.Modulo)
                .SingleAsync(a => a.Modulo.Clave == partes[0] && a.Clave == partes[1]);
            context.RolPermisos.Add(new RolPermiso
            {
                RoleId = roleId,
                ModuloId = accion.ModuloId,
                AccionId = accion.Id,
                ClaimValue = claim,
                IsDeleted = false,
                RowVersion = new byte[8]
            });
        }

        await context.SaveChangesAsync();
        return nombre;
    }

    private static async Task<string> CrearUsuarioAsync(IServiceScope scope, string userName, string? rol)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.com", EmailConfirmed = true, Activo = true };
        var result = await userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        if (rol != null)
        {
            var addRole = await userManager.AddToRoleAsync(user, rol);
            Assert.True(addRole.Succeeded, string.Join(", ", addRole.Errors.Select(e => e.Description)));
        }
        return user.Id;
    }

    private async Task<string> CrearRolSimpleAsync(string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.True((await roleManager.CreateAsync(new IdentityRole(nombre))).Succeeded);
        return (await roleManager.FindByNameAsync(nombre))!.Id;
    }

    private async Task<int> ContarRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Roles.CountAsync();
    }

    private async Task<string> IdDeRolAsync(string nombre) =>
        await IdDeRolOrNullAsync(nombre) ?? throw new InvalidOperationException($"Rol {nombre} inexistente.");

    private async Task<string?> IdDeRolOrNullAsync(string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Roles.AsNoTracking().Where(r => r.Name == nombre).Select(r => r.Id).SingleOrDefaultAsync();
    }

    private async Task<bool> ExisteUsuarioAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.UserName == userName);
    }

    private async Task<byte[]> RowVersionAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.RowVersion).SingleAsync();
    }

    private async Task<List<string>> RolesDeUsuarioAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await (from ur in context.UserRoles
                      join r in context.Roles on ur.RoleId equals r.Id
                      where ur.UserId == userId
                      select r.Name!).ToListAsync();
    }
}
