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
/// SEC-S-001 / SEC-S-002 / FIN-S-001 (QA-AUDITORIA, PROMPT 53): escalada de privilegios y
/// autorización sin permisos efectivos. Cada test arma la petición HTTP a mano (como un
/// atacante con la cookie de un usuario real, sin pasar por la UI) y además verifica en BD
/// que la operación rechazada no dejó escrituras.
/// </summary>
[Collection("HttpIntegration")]
public class PrivilegeEscalationHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string ClaimConfiguracionUpdate = "configuracion.update";

    private readonly CustomWebApplicationFactory _factory;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];

    private string _adminId = null!;
    private string _soloUpdateId = null!;
    private string _soloViewAutId = null!;
    private string _gerenteId = null!;
    private string _rolesAdminId = null!;
    private string _targetNormalId = null!;
    private string _targetSuperId = null!;

    public PrivilegeEscalationHttpTests(CustomWebApplicationFactory factory)
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

        var rolSoloUpdate = await CrearRolConPermisosAsync(context, $"RolSoloUpdate{_sfx}",
            "usuarios.view", "usuarios.update", "roles.view");
        var rolSoloViewAut = await CrearRolConPermisosAsync(context, $"RolSoloViewAut{_sfx}",
            "autorizaciones.view");
        var rolAsignaPermisos = await CrearRolConPermisosAsync(context, $"RolAsignaPermisos{_sfx}",
            "roles.view", "roles.create", "roles.update", "roles.assignpermissions", "usuarios.view");

        _adminId = await CrearUsuarioAsync(scope, $"adm_{_sfx}", Roles.Administrador);
        _soloUpdateId = await CrearUsuarioAsync(scope, $"upd_{_sfx}", rolSoloUpdate);
        _soloViewAutId = await CrearUsuarioAsync(scope, $"viewaut_{_sfx}", rolSoloViewAut);
        _gerenteId = await CrearUsuarioAsync(scope, $"ger_{_sfx}", Roles.Gerente);
        _rolesAdminId = await CrearUsuarioAsync(scope, $"rolesadm_{_sfx}", rolAsignaPermisos);
        _targetNormalId = await CrearUsuarioAsync(scope, $"tgt_{_sfx}", null);
        _targetSuperId = await CrearUsuarioAsync(scope, $"tgtsuper_{_sfx}", Roles.SuperAdmin);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ───────────────────────── SEC-S-001: escalada a SuperAdmin ─────────────────────────

    [Fact]
    public async Task Create_AdministradorNoPuedeCrearSuperAdmin()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);
        var userName = $"nuevo_super_{_sfx}";

        var response = await client.PostAsync("/Usuarios/Create", Form(
            ("__RequestVerificationToken", token),
            ("UserName", userName), ("Email", $"{userName}@test.com"),
            ("Password", "Test123!"), ("ConfirmPassword", "Test123!"),
            ("RolesSeleccionados", Roles.SuperAdmin), ("Activo", "true"), ("EmailConfirmed", "true")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await ExisteUsuarioAsync(userName), "El usuario rechazado no debe persistirse.");
    }

    [Fact]
    public async Task Create_AdministradorSiPuedeCrearUsuarioConRolLegitimo()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);
        var userName = $"nuevo_vend_{_sfx}";

        var response = await client.PostAsync("/Usuarios/Create", Form(
            ("__RequestVerificationToken", token),
            ("UserName", userName), ("Email", $"{userName}@test.com"),
            ("Password", "Test123!"), ("ConfirmPassword", "Test123!"),
            ("RolesSeleccionados", Roles.Vendedor), ("Activo", "true"), ("EmailConfirmed", "true")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(Roles.Vendedor, await RolesDeUsuarioPorNombreAsync(userName));
    }

    [Fact]
    public async Task Create_SinAssignRolesNoPuedeAsignarNingunRol()
    {
        // Rol con usuarios.create pero SIN usuarios.assignroles.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rol = await CrearRolConPermisosAsync(context, $"RolSoloCreate{_sfx}", "usuarios.view", "usuarios.create");
            _ = await CrearUsuarioAsync(scope, $"creator_{_sfx}", rol);
        }

        var creatorId = await IdDeUsuarioAsync($"creator_{_sfx}");
        var client = _factory.CreateClientWithUserId(creatorId);
        var token = await TokenAsync(client);
        var userName = $"conrol_{_sfx}";

        var response = await client.PostAsync("/Usuarios/Create", Form(
            ("__RequestVerificationToken", token),
            ("UserName", userName), ("Email", $"{userName}@test.com"),
            ("Password", "Test123!"), ("ConfirmPassword", "Test123!"),
            ("RolesSeleccionados", Roles.Vendedor), ("Activo", "true"), ("EmailConfirmed", "true")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await ExisteUsuarioAsync(userName));
    }

    [Fact]
    public async Task EditUsuario_ConSoloUpdateNoPuedeAutoAsignarseSuperAdmin()
    {
        var client = _factory.CreateClientWithUserId(_soloUpdateId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _soloUpdateId, $"upd_{_sfx}", Roles.SuperAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(Roles.SuperAdmin, await RolesDeUsuarioAsync(_soloUpdateId));
    }

    [Fact]
    public async Task EditUsuario_AdministradorNoPuedeAsignarSuperAdminAOtroUsuario()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _targetNormalId, $"tgt_{_sfx}", Roles.SuperAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(Roles.SuperAdmin, await RolesDeUsuarioAsync(_targetNormalId));
    }

    [Fact]
    public async Task EditUsuario_ConSoloUpdateNoPuedeAsignarRolesAunqueNoSeanSuperAdmin()
    {
        var client = _factory.CreateClientWithUserId(_soloUpdateId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _targetNormalId, $"tgt_{_sfx}", Roles.Vendedor);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await RolesDeUsuarioAsync(_targetNormalId));
    }

    [Fact]
    public async Task EditUsuario_ConSoloUpdateSigueEditandoDatosSinTocarRoles()
    {
        var client = _factory.CreateClientWithUserId(_soloUpdateId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _targetNormalId, $"tgt_{_sfx}", roleToSet: null,
            nombre: "Pedro");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("Pedro", await NombreDeUsuarioAsync(_targetNormalId));
    }

    [Fact]
    public async Task EditUsuario_AdministradorSiPuedeAsignarRolLegitimo()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _targetNormalId, $"tgt_{_sfx}", Roles.Cajero);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(Roles.Cajero, await RolesDeUsuarioAsync(_targetNormalId));
    }

    [Fact]
    public async Task EditUsuario_AdministradorNoPuedeEditarUnSuperAdmin()
    {
        // Cambiar el email de un SuperAdmin + recuperar contraseña = toma de cuenta.
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _targetSuperId, $"tgtsuper_{_sfx}", Roles.SuperAdmin,
            email: $"atacante_{_sfx}@evil.test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal($"tgtsuper_{_sfx}@test.com", await EmailDeUsuarioAsync(_targetSuperId));
    }

    [Fact]
    public async Task BulkAction_AsignarRolSuperAdminComoAdministradorNoAsigna()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token),
            ("ids", _targetNormalId), ("accion", "asignarRol"), ("rol", Roles.SuperAdmin)));

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.DoesNotContain(Roles.SuperAdmin, await RolesDeUsuarioAsync(_targetNormalId));
    }

    [Fact]
    public async Task CambiarPassword_AdministradorNoPuedeResetearLaClaveDeUnSuperAdmin()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);
        var hashAntes = await PasswordHashAsync(_targetSuperId);

        var response = await client.PostAsync("/Usuarios/CambiarPassword", Form(
            ("__RequestVerificationToken", token),
            ("UserId", _targetSuperId), ("NewPassword", "Hack3d!Pass"), ("ConfirmPassword", "Hack3d!Pass")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(hashAntes, await PasswordHashAsync(_targetSuperId));
    }

    // ───────────────────────── SEC-S-002: concesión indebida de permisos ─────────────────────────

    [Fact]
    public async Task CambiarPassword_AdministradorNoPuedeAdoptarLaCuentaDeUnUsuarioConMasPermisos()
    {
        var rolPoderosoNombre = $"RolConConfig{_sfx}";
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CrearRolConPermisosAsync(context, rolPoderosoNombre, "configuracion.update", "usuarios.view");
            await CrearUsuarioAsync(scope, $"poderoso_{_sfx}", rolPoderosoNombre);
        }
        var poderosoId = await IdDeUsuarioAsync($"poderoso_{_sfx}");
        var hashAntes = await PasswordHashAsync(poderosoId);

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Usuarios/CambiarPassword", Form(
            ("__RequestVerificationToken", token),
            ("UserId", poderosoId), ("NewPassword", "Hack3d!Pass"), ("ConfirmPassword", "Hack3d!Pass")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(hashAntes, await PasswordHashAsync(poderosoId));

        // Legítimo: el Administrador sí resetea a un usuario dentro de su alcance.
        var normalHashAntes = await PasswordHashAsync(_targetNormalId);
        var ok = await client.PostAsync("/Usuarios/CambiarPassword", Form(
            ("__RequestVerificationToken", token),
            ("UserId", _targetNormalId), ("NewPassword", "Nuev0!Pass1"), ("ConfirmPassword", "Nuev0!Pass1")));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotEqual(normalHashAntes, await PasswordHashAsync(_targetNormalId));
    }

    [Fact]
    public async Task ConfirmarEmail_AdministradorNoPuedeConfirmarElEmailDeUnSuperAdmin()
    {
        string idSuperSinConfirmar;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            idSuperSinConfirmar = await CrearUsuarioAsync(scope, $"supersinconf_{_sfx}", Roles.SuperAdmin);
            var u = await context.Users.SingleAsync(x => x.Id == idSuperSinConfirmar);
            u.EmailConfirmed = false;
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Usuarios/ConfirmarEmail", Form(
            ("__RequestVerificationToken", token), ("id", idSuperSinConfirmar)));

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        using var scope2 = _factory.Services.CreateScope();
        var ctx = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await ctx.Users.Where(x => x.Id == idSuperSinConfirmar).Select(x => x.EmailConfirmed).SingleAsync());
    }

    [Fact]
    public async Task ToggleRolActivo_AdministradorNoPuedeReactivarUnRolConPermisosQueNoTiene()
    {
        var nombre = $"RolDormido{_sfx}";
        string roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CrearRolConPermisosAsync(context, nombre, "configuracion.update");
            roleId = await context.Roles.Where(r => r.Name == nombre).Select(r => r.Id).SingleAsync();
            context.RolMetadatas.Add(new RolMetadata { RoleId = roleId, Activo = false, RowVersion = new byte[8] });
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/ToggleRolActivo", Form(
            ("__RequestVerificationToken", token), ("id", roleId), ("activo", "true")));

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        using var scope2 = _factory.Services.CreateScope();
        var ctx = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await ctx.RolMetadatas.Where(m => m.RoleId == roleId).Select(m => m.Activo).SingleAsync());
    }

    [Fact]
    public async Task EditRol_AdministradorNoPuedeReactivarUnRolConPermisosQueNoTiene()
    {
        var nombre = $"RolDormidoEdit{_sfx}";
        string roleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CrearRolConPermisosAsync(context, nombre, "configuracion.update");
            roleId = await context.Roles.Where(r => r.Name == nombre).Select(r => r.Id).SingleAsync();
            context.RolMetadatas.Add(new RolMetadata { RoleId = roleId, Activo = false, RowVersion = new byte[8] });
            await context.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token),
            ("Id", roleId), ("Nombre", nombre), ("Activo", "true")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope2 = _factory.Services.CreateScope();
        var ctx = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await ctx.RolMetadatas.Where(m => m.RoleId == roleId).Select(m => m.Activo).SingleAsync());
    }

    [Fact]
    public async Task SavePermisosRol_AdministradorNoPuedeOtorgarseConfiguracionUpdate()
    {
        var adminRoleId = await IdDeRolAsync(Roles.Administrador);
        var accionesActuales = await AccionIdsDeRolAsync(adminRoleId);
        var configUpdateId = await AccionIdAsync(ClaimConfiguracionUpdate);

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostSavePermisosAsync(client, token, adminRoleId, accionesActuales.Append(configUpdateId));

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.False(await RolTienePermisoAsync(adminRoleId, ClaimConfiguracionUpdate),
            "El Administrador no debe poder otorgarse configuracion.update.");
    }

    [Fact]
    public async Task SavePermisosRol_AdministradorNoPuedeOtorgarPermisoRestringidoEnUnRolNuevoYAsignarselo()
    {
        var rolId = await CrearRolAsync($"RolPuente{_sfx}");
        var configUpdateId = await AccionIdAsync(ClaimConfiguracionUpdate);

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostSavePermisosAsync(client, token, rolId, [configUpdateId]);

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.False(await RolTienePermisoAsync(rolId, ClaimConfiguracionUpdate));
    }

    [Fact]
    public async Task SavePermisosRol_RolConSoloAssignPermissionsNoPuedeOtorgarPermisosQueNoTiene()
    {
        var rolId = await CrearRolAsync($"RolAjeno{_sfx}");
        var usuariosDeleteId = await AccionIdAsync("usuarios.delete");

        var client = _factory.CreateClientWithUserId(_rolesAdminId);
        var token = await TokenAsync(client);

        var response = await PostSavePermisosAsync(client, token, rolId, [usuariosDeleteId]);

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.False(await RolTienePermisoAsync(rolId, "usuarios.delete"));
    }

    [Fact]
    public async Task SavePermisosRol_AdministradorNoPuedeModificarElRolSuperAdmin()
    {
        var superRoleId = await IdDeRolAsync(Roles.SuperAdmin);
        var antes = (await AccionIdsDeRolAsync(superRoleId)).Count;

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostSavePermisosAsync(client, token, superRoleId, []);

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.Equal(antes, (await AccionIdsDeRolAsync(superRoleId)).Count);
    }

    [Fact]
    public async Task SavePermisosRol_AdministradorSiPuedeOtorgarPermisosQueTiene()
    {
        var rolId = await CrearRolAsync($"RolLegitimo{_sfx}");
        var ventasViewId = await AccionIdAsync("ventas.view");

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostSavePermisosAsync(client, token, rolId, [ventasViewId]);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.True(await RolTienePermisoAsync(rolId, "ventas.view"));
    }

    [Fact]
    public async Task SavePermisosRol_SuperAdminSigueSiendoCapazDeOtorgarCualquierPermiso()
    {
        var rolId = await CrearRolAsync($"RolPorSuper{_sfx}");
        var configUpdateId = await AccionIdAsync(ClaimConfiguracionUpdate);

        var client = _factory.CreateAuthenticatedClient(); // usuario de test = SuperAdmin
        var token = await TokenAsync(client);

        var response = await PostSavePermisosAsync(client, token, rolId, [configUpdateId]);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.True(await RolTienePermisoAsync(rolId, ClaimConfiguracionUpdate));
    }

    [Fact]
    public async Task CopyPermisosRol_AdministradorNoPuedeCopiarLosPermisosDelRolSuperAdmin()
    {
        var superRoleId = await IdDeRolAsync(Roles.SuperAdmin);
        var destinoId = await CrearRolAsync($"RolCopia{_sfx}");

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/CopyPermisosRol", Form(
            ("__RequestVerificationToken", token),
            ("RolOrigenId", superRoleId), ("RolDestinoId", destinoId)));

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden);
        Assert.Empty(await AccionIdsDeRolAsync(destinoId));
    }

    [Fact]
    public async Task DuplicateRol_AdministradorNoPuedeDuplicarElRolSuperAdmin()
    {
        var superRoleId = await IdDeRolAsync(Roles.SuperAdmin);
        var nombre = $"SuperClon{_sfx}";

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Seguridad/DuplicateRol", Form(
            ("__RequestVerificationToken", token),
            ("RolOrigenId", superRoleId), ("Nombre", nombre), ("Activo", "true")));

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden);
        Assert.Null(await IdDeRolOrNullAsync(nombre));
    }

    [Fact]
    public async Task EditRolYDeleteRol_AdministradorNoPuedeRenombrarNiBorrarElRolSuperAdmin()
    {
        var superRoleId = await IdDeRolAsync(Roles.SuperAdmin);

        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var rename = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token),
            ("Id", superRoleId), ("Nombre", $"Renombrado{_sfx}"), ("Activo", "true")));
        Assert.True(rename.StatusCode is HttpStatusCode.OK or HttpStatusCode.Forbidden);

        var delete = await client.PostAsync("/Seguridad/DeleteRol", Form(
            ("__RequestVerificationToken", token), ("id", superRoleId)));
        Assert.True(delete.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);

        Assert.Equal(superRoleId, await IdDeRolOrNullAsync(Roles.SuperAdmin));
        Assert.Null(await IdDeRolOrNullAsync($"Renombrado{_sfx}"));
    }

    // ───────────────────────── FIN-S-001: Autorización sin permisos efectivos ─────────────────────────

    [Fact]
    public async Task AprobarSolicitud_ConSoloViewNoPuedeAprobar()
    {
        var solicitudId = await CrearSolicitudPendienteAsync();
        var client = _factory.CreateClientWithUserId(_soloViewAutId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Autorizacion/AprobarSolicitud", Form(
            ("__RequestVerificationToken", token), ("id", solicitudId.ToString())));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(EstadoSolicitud.Pendiente, await EstadoSolicitudAsync(solicitudId));
    }

    [Fact]
    public async Task RechazarSolicitud_ConSoloViewNoPuedeRechazar()
    {
        var solicitudId = await CrearSolicitudPendienteAsync();
        var client = _factory.CreateClientWithUserId(_soloViewAutId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Autorizacion/RechazarSolicitud", Form(
            ("__RequestVerificationToken", token), ("id", solicitudId.ToString()), ("comentario", "no")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(EstadoSolicitud.Pendiente, await EstadoSolicitudAsync(solicitudId));
    }

    [Fact]
    public async Task Umbrales_ConSoloViewNoPuedeGestionarlos()
    {
        var client = _factory.CreateClientWithUserId(_soloViewAutId);
        var token = await TokenAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Autorizacion/Index")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Autorizacion/CrearUmbral")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Autorizacion/EditarUmbral?id=1")).StatusCode);

        var crear = await client.PostAsync("/Autorizacion/CrearUmbral", Form(
            ("__RequestVerificationToken", token),
            ("Rol", Roles.Vendedor), ("TipoUmbral", "0"), ("ValorMaximo", "1000"), ("Activo", "true")));
        Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);

        var editar = await client.PostAsync("/Autorizacion/EditarUmbral", Form(
            ("__RequestVerificationToken", token), ("Id", "1"), ("ValorMaximo", "999999"), ("Activo", "true")));
        Assert.Equal(HttpStatusCode.Forbidden, editar.StatusCode);

        var eliminar = await client.PostAsync("/Autorizacion/EliminarUmbral", Form(
            ("__RequestVerificationToken", token), ("id", "1")));
        Assert.Equal(HttpStatusCode.Forbidden, eliminar.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.UmbralesAutorizacion.AnyAsync(u => u.ValorMaximo == 999999m));
    }

    [Fact]
    public async Task AprobarSolicitud_ConPermisoApproveSigueFuncionando()
    {
        var solicitudId = await CrearSolicitudPendienteAsync();
        var client = _factory.CreateClientWithUserId(_gerenteId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Autorizacion/AprobarSolicitud", Form(
            ("__RequestVerificationToken", token), ("id", solicitudId.ToString()), ("comentario", "ok")));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(EstadoSolicitud.Aprobada, await EstadoSolicitudAsync(solicitudId));
    }

    // ───────────────────────── Defensa en profundidad (estructural) ─────────────────────────

    [Fact]
    public void Controllers_NingunAtributoQuedaDentroDeUnComentarioXmlDoc()
    {
        // Causa raíz de FIN-S-001: "/// </summary>    [PermisoRequerido(...)]" -> el atributo es comentario.
        var root = BuscarRaizDelRepo();
        var patron = new Regex(@"</\w+>\s*\[[A-Za-z]", RegexOptions.CultureInvariant);
        var ofensores = Directory
            .EnumerateFiles(Path.Combine(root, "Controllers"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Select((linea, i) => (f, linea, n: i + 1)))
            .Where(x => patron.IsMatch(x.linea))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.n}: {x.linea.Trim()}")
            .ToList();

        Assert.True(ofensores.Count == 0, "Atributos dentro de comentarios XML:\n" + string.Join("\n", ofensores));
    }

    [Fact]
    public void AutorizacionController_TodaAccionTienePermisoPropioYLasDeEscrituraNoSonSoloView()
    {
        var acciones = typeof(TheBuryProject.Controllers.AutorizacionController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => typeof(Microsoft.AspNetCore.Mvc.IActionResult).IsAssignableFrom(m.ReturnType) ||
                        typeof(Task<Microsoft.AspNetCore.Mvc.IActionResult>).IsAssignableFrom(m.ReturnType))
            .ToList();

        Assert.NotEmpty(acciones);

        string? AccionExigida(System.Reflection.MethodInfo m) => m
            .GetCustomAttributes(typeof(TheBuryProject.Filters.PermisoRequeridoAttribute), inherit: false)
            .Cast<TheBuryProject.Filters.PermisoRequeridoAttribute>()
            .Select(a => a.Accion)
            .FirstOrDefault();

        foreach (var m in acciones)
            Assert.True(AccionExigida(m) != null, $"{m.Name} no declara [PermisoRequerido] propio.");

        void Exige(string nombre, string accion) =>
            Assert.All(acciones.Where(m => m.Name == nombre), m => Assert.Equal(accion, AccionExigida(m)));

        Exige("AprobarSolicitud", AutorizacionesConstants.Acciones.Aprobar);
        Exige("RechazarSolicitud", AutorizacionesConstants.Acciones.Rechazar);
        foreach (var n in new[] { "Index", "CrearUmbral", "EditarUmbral", "EliminarUmbral" })
            Exige(n, AutorizacionesConstants.Acciones.GestionarUmbrales);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static string BuscarRaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TheBuryProyect.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

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

    private async Task<HttpResponseMessage> PostEditUsuarioAsync(
        HttpClient client, string token, string userId, string userName, string? roleToSet,
        string? email = null, string? nombre = null)
    {
        var rowVersion = await RowVersionAsync(userId);
        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("Id", userId),
            new("UserName", userName),
            new("Email", email ?? await EmailDeUsuarioAsync(userId)),
            new("Activo", "true"),
            new("RowVersion", Convert.ToBase64String(rowVersion))
        };
        if (nombre != null) campos.Add(new("Nombre", nombre));
        if (roleToSet != null) campos.Add(new("RolesSeleccionados", roleToSet));
        // Conservar los roles actuales que no son el que se quiere "setear" simula un form real.
        foreach (var actual in await RolesDeUsuarioAsync(userId))
            if (!string.Equals(actual, roleToSet, StringComparison.OrdinalIgnoreCase))
                campos.Add(new("RolesSeleccionados", actual));

        return await client.PostAsync("/Seguridad/EditUsuario", new FormUrlEncodedContent(campos));
    }

    private static Task<HttpResponseMessage> PostSavePermisosAsync(
        HttpClient client, string token, string roleId, IEnumerable<int> accionIds)
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("roleId", roleId)
        };
        campos.AddRange(accionIds.Select(id => new KeyValuePair<string, string>("accionIds", id.ToString())));
        return client.PostAsync("/Seguridad/SavePermisosRol", new FormUrlEncodedContent(campos));
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
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@test.com",
            EmailConfirmed = true,
            Activo = true
        };
        var result = await userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        if (rol != null)
        {
            var addRole = await userManager.AddToRoleAsync(user, rol);
            Assert.True(addRole.Succeeded, string.Join(", ", addRole.Errors.Select(e => e.Description)));
        }
        return user.Id;
    }

    private async Task<string> CrearRolAsync(string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var result = await roleManager.CreateAsync(new IdentityRole(nombre));
        Assert.True(result.Succeeded);
        return (await roleManager.FindByNameAsync(nombre))!.Id;
    }

    private async Task<int> CrearSolicitudPendienteAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var solicitud = new SolicitudAutorizacion
        {
            UsuarioSolicitante = "solicitante",
            RolSolicitante = Roles.Vendedor,
            TipoUmbral = TipoUmbral.DescuentoVenta,
            ValorSolicitado = 50m,
            ValorPermitido = 10m,
            TipoOperacion = "Venta",
            Justificacion = "test",
            Estado = EstadoSolicitud.Pendiente,
            RowVersion = new byte[8]
        };
        context.SolicitudesAutorizacion.Add(solicitud);
        await context.SaveChangesAsync();
        return solicitud.Id;
    }

    private async Task<EstadoSolicitud> EstadoSolicitudAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.SolicitudesAutorizacion.AsNoTracking().Where(s => s.Id == id).Select(s => s.Estado).SingleAsync();
    }

    private async Task<bool> ExisteUsuarioAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AnyAsync(u => u.UserName == userName);
    }

    private async Task<string> IdDeUsuarioAsync(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
    }

    private async Task<string> EmailDeUsuarioAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync();
    }

    private async Task<string?> NombreDeUsuarioAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.Where(u => u.Id == userId).Select(u => u.Nombre).SingleAsync();
    }

    private async Task<string?> PasswordHashAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();
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

    private async Task<List<string>> RolesDeUsuarioPorNombreAsync(string userName) =>
        await RolesDeUsuarioAsync(await IdDeUsuarioAsync(userName));

    private async Task<string> IdDeRolAsync(string nombre) =>
        await IdDeRolOrNullAsync(nombre) ?? throw new InvalidOperationException($"Rol {nombre} inexistente.");

    private async Task<string?> IdDeRolOrNullAsync(string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Roles.Where(r => r.Name == nombre).Select(r => r.Id).SingleOrDefaultAsync();
    }

    private async Task<int> AccionIdAsync(string claim)
    {
        var partes = claim.Split('.', 2);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.AccionesModulo
            .Where(a => a.Modulo.Clave == partes[0] && a.Clave == partes[1])
            .Select(a => a.Id)
            .SingleAsync();
    }

    private async Task<List<int>> AccionIdsDeRolAsync(string roleId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.RolPermisos
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .Select(rp => rp.AccionId)
            .ToListAsync();
    }

    private async Task<bool> RolTienePermisoAsync(string roleId, string claim)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.RolPermisos.AnyAsync(rp => rp.RoleId == roleId && rp.ClaimValue == claim && !rp.IsDeleted);
    }
}
