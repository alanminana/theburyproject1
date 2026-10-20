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
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// PROMPT 54: (A) un no-SuperAdmin no puede alterar cuentas SuperAdmin por ninguna vía administrativa y
/// (B) ninguna operación puede dejar al sistema sin un SuperAdmin utilizable. Cada test arma la petición
/// HTTP a mano y verifica en BD que el rechazo no dejó escrituras (ni parciales en las masivas).
/// Esta clase usa su propia fábrica, de modo que puede fijar el conjunto exacto de SuperAdmin.
/// </summary>
[Collection("HttpIntegration")]
public class SuperAdminProteccionHttpTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly string _sfx = Guid.NewGuid().ToString("N")[..8];

    private string _adminId = null!;
    private string _gerenteId = null!;
    private string _super1Id = null!;
    private string _super2Id = null!;
    private string _normalId = null!;

    public SuperAdminProteccionHttpTests(CustomWebApplicationFactory factory)
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

        _adminId = await CrearUsuarioAsync(scope, $"adm_{_sfx}", Roles.Administrador);
        _gerenteId = await CrearUsuarioAsync(scope, $"ger_{_sfx}", Roles.Gerente);
        _super1Id = await CrearUsuarioAsync(scope, $"super1_{_sfx}", Roles.SuperAdmin);
        _super2Id = await CrearUsuarioAsync(scope, $"super2_{_sfx}", Roles.SuperAdmin);
        _normalId = await CrearUsuarioAsync(scope, $"normal_{_sfx}", Roles.Vendedor);

        // Estado de partida determinista: solo estos dos SuperAdmin (los de otros tests quedan fuera).
        await FijarSuperAdminsAsync(_super1Id, _super2Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ───────────────────────── A. un no-SuperAdmin no altera SuperAdmin ─────────────────────────

    [Theory]
    [InlineData("/Usuarios/Desactivar")]
    [InlineData("/Usuarios/Delete")]
    public async Task Administrador_NoPuedeDesactivarNiEliminarUnSuperAdmin(string ruta)
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync(ruta, Form(("__RequestVerificationToken", token), ("id", _super1Id)));

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        var estado = await EstadoAsync(_super1Id);
        Assert.True(estado.Activo);
        Assert.Null(estado.MotivoDesactivacion);
    }

    [Fact]
    public async Task Administrador_NoPuedeBloquearNiDesbloquearNiReactivarUnSuperAdmin()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var bloquear = await client.PostAsync("/Usuarios/Bloquear", Form(
            ("__RequestVerificationToken", token), ("UserId", _super1Id), ("UserName", $"super1_{_sfx}"),
            ("MotivoBloqueo", "intento")));
        Assert.Equal(HttpStatusCode.Forbidden, bloquear.StatusCode);
        Assert.Null((await EstadoAsync(_super1Id)).LockoutEnd);

        // Un SuperAdmin ya bloqueado (por otro SuperAdmin) tampoco lo desbloquea un no-SuperAdmin.
        await BloquearEnBdAsync(_super2Id);
        var desbloquear = await client.PostAsync("/Usuarios/Desbloquear", Form(
            ("__RequestVerificationToken", token), ("id", _super2Id)));
        Assert.True(desbloquear.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.NotNull((await EstadoAsync(_super2Id)).LockoutEnd);

        await DesactivarEnBdAsync(_super2Id);
        var reactivar = await client.PostAsync("/Usuarios/Reactivar", Form(
            ("__RequestVerificationToken", token), ("id", _super2Id)));
        Assert.True(reactivar.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden);
        Assert.False((await EstadoAsync(_super2Id)).Activo);
    }

    [Theory]
    [InlineData("desactivar")]
    [InlineData("bloquear")]
    [InlineData("cambiarSucursal")]
    public async Task BulkAction_AdministradorNoAfectaASuperAdminYNoDejaEscriturasParciales(string accion)
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);
        var campos = new List<(string, string)>
        {
            ("__RequestVerificationToken", token),
            // El usuario ordinario va primero: sin "todo o nada" se desactivaría antes de llegar al SuperAdmin.
            ("ids", $"{_normalId},{_super1Id}"), ("accion", accion)
        };
        if (accion == "cambiarSucursal") campos.Add(("sucursalId", "1"));

        var response = await client.PostAsync("/Usuarios/BulkAction", Form(campos.ToArray()));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var normal = await EstadoAsync(_normalId);
        var super = await EstadoAsync(_super1Id);
        Assert.True(normal.Activo);
        Assert.Null(normal.LockoutEnd);
        Assert.True(super.Activo);
        Assert.Null(super.LockoutEnd);
    }

    [Fact]
    public async Task EditUsuario_AdministradorNoPuedeDesactivarNiQuitarRolAUnSuperAdmin()
    {
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _super1Id, $"super1_{_sfx}", roles: [Roles.Vendedor], activo: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True((await EstadoAsync(_super1Id)).Activo);
        Assert.Contains(Roles.SuperAdmin, await RolesDeUsuarioAsync(_super1Id));
    }

    // ───────────────────────── A+. el SuperAdmin conserva su capacidad legítima ─────────────────────────

    [Fact]
    public async Task SuperAdmin_AdministraUsuariosOrdinariosYOtroSuperAdminMientrasQuedeUno()
    {
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        // Usuario ordinario: desactivar, reactivar, bloquear, desbloquear.
        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _normalId)));
        Assert.False((await EstadoAsync(_normalId)).Activo);
        await client.PostAsync("/Usuarios/Reactivar", Form(("__RequestVerificationToken", token), ("id", _normalId)));
        Assert.True((await EstadoAsync(_normalId)).Activo);

        var bloquear = await client.PostAsync("/Usuarios/Bloquear", Form(
            ("__RequestVerificationToken", token), ("UserId", _normalId), ("UserName", $"normal_{_sfx}"), ("MotivoBloqueo", "x")));
        Assert.Equal(HttpStatusCode.OK, bloquear.StatusCode);
        Assert.NotNull((await EstadoAsync(_normalId)).LockoutEnd);
        await client.PostAsync("/Usuarios/Desbloquear", Form(("__RequestVerificationToken", token), ("id", _normalId)));
        Assert.Null((await EstadoAsync(_normalId)).LockoutEnd);

        // Otro SuperAdmin, existiendo uno más utilizable (el actor): se puede.
        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _super2Id)));
        Assert.False((await EstadoAsync(_super2Id)).Activo);
        await client.PostAsync("/Usuarios/Reactivar", Form(("__RequestVerificationToken", token), ("id", _super2Id)));
        Assert.True((await EstadoAsync(_super2Id)).Activo);

        // Edición con cambio de datos conservando rol y estado.
        var edit = await PostEditUsuarioAsync(client, token, _super2Id, $"super2_{_sfx}", roles: [Roles.SuperAdmin], activo: true, nombre: "Nuevo");
        Assert.Equal(HttpStatusCode.Found, edit.StatusCode);
        Assert.Equal("Nuevo", await NombreAsync(_super2Id));
    }

    [Fact]
    public async Task SuperAdmin_PuedeAsignarElRolSuperAdminYConservarVariosSuperAdmin()
    {
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _normalId, $"normal_{_sfx}",
            roles: [Roles.Vendedor, Roles.SuperAdmin], activo: true);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(Roles.SuperAdmin, await RolesDeUsuarioAsync(_normalId));
        Assert.Equal(3, await ContarSuperAdminUtilizablesAsync());
    }

    // ───────────────────────── B. último SuperAdmin utilizable ─────────────────────────

    [Fact]
    public async Task UltimoSuperAdmin_Desactivar_SePermiteElPrimeroYSeRechazaElUltimo()
    {
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _super2Id)));
        Assert.False((await EstadoAsync(_super2Id)).Activo);
        Assert.Equal(1, await ContarSuperAdminUtilizablesAsync());

        // Ahora el actor intenta desactivarse a sí mismo: es el último.
        var antes = await EstadoAsync(_super1Id);
        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _super1Id)));
        var despues = await EstadoAsync(_super1Id);
        Assert.True(despues.Activo);
        Assert.Equal(antes.SecurityStamp, despues.SecurityStamp);
        Assert.Null(despues.MotivoDesactivacion);
        Assert.Equal(1, await ContarSuperAdminUtilizablesAsync());
    }

    [Fact]
    public async Task UltimoSuperAdmin_Eliminar_SeRechaza()
    {
        await DesactivarEnBdAsync(_super2Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/Delete", Form(
            ("__RequestVerificationToken", token), ("id", _super1Id), ("motivo", "baja")));

        Assert.True((await EstadoAsync(_super1Id)).Activo);
        Assert.Equal(1, await ContarSuperAdminUtilizablesAsync());
    }

    [Fact]
    public async Task UltimoSuperAdmin_Bloquear_SeRechazaYUnoDeDosSePermite()
    {
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        var primero = await client.PostAsync("/Usuarios/Bloquear", Form(
            ("__RequestVerificationToken", token), ("UserId", _super2Id), ("UserName", $"super2_{_sfx}"), ("MotivoBloqueo", "x")));
        Assert.Equal(HttpStatusCode.OK, primero.StatusCode);
        Assert.NotNull((await EstadoAsync(_super2Id)).LockoutEnd);

        var ultimo = await client.PostAsync("/Usuarios/Bloquear", Form(
            ("__RequestVerificationToken", token), ("UserId", _super1Id), ("UserName", $"super1_{_sfx}"), ("MotivoBloqueo", "x")));
        Assert.Equal(HttpStatusCode.Forbidden, ultimo.StatusCode);
        Assert.Null((await EstadoAsync(_super1Id)).LockoutEnd);
        Assert.Equal(1, await ContarSuperAdminUtilizablesAsync());
    }

    [Fact]
    public async Task UltimoSuperAdmin_EditarQuitandoRolODesactivando_SeRechazaSinEscrituras()
    {
        await DesactivarEnBdAsync(_super2Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        var quitarRol = await PostEditUsuarioAsync(client, token, _super1Id, $"super1_{_sfx}",
            roles: [Roles.Vendedor], activo: true, nombre: "Intruso");
        Assert.Equal(HttpStatusCode.Forbidden, quitarRol.StatusCode);

        var desactivar = await PostEditUsuarioAsync(client, token, _super1Id, $"super1_{_sfx}",
            roles: [Roles.SuperAdmin], activo: false, nombre: "Intruso");
        Assert.Equal(HttpStatusCode.Forbidden, desactivar.StatusCode);

        Assert.True((await EstadoAsync(_super1Id)).Activo);
        Assert.Contains(Roles.SuperAdmin, await RolesDeUsuarioAsync(_super1Id));
        Assert.NotEqual("Intruso", await NombreAsync(_super1Id));
        Assert.Equal(1, await ContarSuperAdminUtilizablesAsync());
    }

    [Fact]
    public async Task UltimoSuperAdmin_EditarSinCambiarAcceso_SigueFuncionando()
    {
        await DesactivarEnBdAsync(_super2Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        var response = await PostEditUsuarioAsync(client, token, _super1Id, $"super1_{_sfx}",
            roles: [Roles.SuperAdmin], activo: true, nombre: "Renombrado");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("Renombrado", await NombreAsync(_super1Id));
    }

    [Theory]
    [InlineData("desactivar")]
    [InlineData("bloquear")]
    public async Task UltimoSuperAdmin_AccionMasivaSobreTodosLosSuperAdmin_SeRechazaTodaSinEscriturasParciales(string accion)
    {
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        // Ordinario primero: si la operación no fuera atómica quedaría modificado tras el rechazo.
        var response = await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token),
            ("ids", $"{_normalId},{_super2Id},{_super1Id}"), ("accion", accion)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var normal = await EstadoAsync(_normalId);
        Assert.True(normal.Activo);
        Assert.Null(normal.LockoutEnd);
        Assert.Equal(2, await ContarSuperAdminUtilizablesAsync());
        Assert.True((await EstadoAsync(_super1Id)).Activo);
        Assert.True((await EstadoAsync(_super2Id)).Activo);
    }

    [Fact]
    public async Task AccionMasivaSobreUnSoloSuperAdminHabiendoOtro_SePermite()
    {
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token), ("ids", $"{_normalId},{_super2Id}"), ("accion", "desactivar")));

        Assert.False((await EstadoAsync(_normalId)).Activo);
        Assert.False((await EstadoAsync(_super2Id)).Activo);
        Assert.Equal(1, await ContarSuperAdminUtilizablesAsync());
    }

    [Fact]
    public async Task UltimoSuperAdmin_UnBloqueoVigenteNoCuentaComoUtilizable()
    {
        // super2 ya está bloqueado: super1 es el único utilizable aunque existan dos cuentas.
        await BloquearEnBdAsync(_super2Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _super1Id)));

        Assert.True((await EstadoAsync(_super1Id)).Activo);
    }

    // ───────────────────────── PROMPT 56-C: la invariante es ESTRUCTURAL (el lockout no la apaga) ─────────────────────────

    [Fact]
    public async Task UltimoSuperAdmin_TemporalmenteBloqueado_SigueProtegidoContraDesactivacionEliminacionYQuitarRol()
    {
        // Único SuperAdmin activo, con un bloqueo de login vigente (p. ej. intentos fallidos). Con la
        // definición "utilizable" esto apagaba la protección y cualquier SuperAdmin podía dejar el sistema en cero.
        await DesactivarEnBdAsync(_super2Id);
        await BloquearTemporalEnBdAsync(_super1Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _super1Id)));
        await client.PostAsync("/Usuarios/Delete", Form(("__RequestVerificationToken", token), ("id", _super1Id), ("motivo", "baja")));
        var quitarRol = await PostEditUsuarioAsync(client, token, _super1Id, $"super1_{_sfx}",
            roles: [Roles.Vendedor], activo: true, nombre: "Intruso");
        var desactivarPorEdicion = await PostEditUsuarioAsync(client, token, _super1Id, $"super1_{_sfx}",
            roles: [Roles.SuperAdmin], activo: false, nombre: "Intruso");

        Assert.Equal(HttpStatusCode.Forbidden, quitarRol.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, desactivarPorEdicion.StatusCode);
        Assert.True((await EstadoAsync(_super1Id)).Activo);
        Assert.Contains(Roles.SuperAdmin, await RolesDeUsuarioAsync(_super1Id));
        Assert.NotEqual("Intruso", await NombreAsync(_super1Id));
        Assert.Equal(1, await ContarSuperAdminActivosAsync());
    }

    [Theory]
    [InlineData("desactivar")]
    public async Task UltimoSuperAdmin_TemporalmenteBloqueado_AccionMasivaSeRechazaTodaSinEscriturasParciales(string accion)
    {
        await DesactivarEnBdAsync(_super2Id);
        await BloquearTemporalEnBdAsync(_super1Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token), ("ids", $"{_normalId},{_super1Id}"), ("accion", accion)));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var normal = await EstadoAsync(_normalId);
        Assert.True(normal.Activo);
        Assert.Null(normal.LockoutEnd);
        Assert.True((await EstadoAsync(_super1Id)).Activo);
        Assert.Equal(1, await ContarSuperAdminActivosAsync());
    }

    [Fact]
    public async Task DosSuperAdmin_UnoBloqueado_AccionMasivaSobreAmbos_SeRechazaTodaSinEscriturasParciales()
    {
        await BloquearTemporalEnBdAsync(_super2Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token),
            ("ids", $"{_normalId},{_super1Id},{_super2Id}"), ("accion", "desactivar")));

        Assert.True((await EstadoAsync(_normalId)).Activo);
        Assert.True((await EstadoAsync(_super1Id)).Activo);
        Assert.True((await EstadoAsync(_super2Id)).Activo);
        Assert.Equal(2, await ContarSuperAdminActivosAsync());
    }

    [Fact]
    public async Task DosSuperAdmin_UnoBloqueado_SePuedeDesactivarElBloqueadoPeroNoElQueQuedaHabilitado()
    {
        await BloquearTemporalEnBdAsync(_super2Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        // Apagar al único que puede iniciar sesión se rechaza (el otro seguiría bloqueado).
        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _super1Id)));
        Assert.True((await EstadoAsync(_super1Id)).Activo);

        // Desactivar al bloqueado no reduce el acceso real ni deja el sistema en cero.
        await client.PostAsync("/Usuarios/Desactivar", Form(("__RequestVerificationToken", token), ("id", _super2Id)));
        Assert.False((await EstadoAsync(_super2Id)).Activo);
        Assert.Equal(1, await ContarSuperAdminActivosAsync());
    }

    [Fact]
    public async Task RecuperacionLegitima_UnSuperAdminReactivaYDesbloqueaAOtro()
    {
        await DesactivarEnBdAsync(_super2Id);
        await BloquearTemporalEnBdAsync(_super1Id);
        var client = _factory.CreateClientWithUserId(_super1Id);
        var token = await TokenAsync(client);

        await client.PostAsync("/Usuarios/Reactivar", Form(("__RequestVerificationToken", token), ("id", _super2Id)));
        Assert.True((await EstadoAsync(_super2Id)).Activo);

        var otro = _factory.CreateClientWithUserId(_super2Id);
        var token2 = await TokenAsync(otro);
        await otro.PostAsync("/Usuarios/Desbloquear", Form(("__RequestVerificationToken", token2), ("id", _super1Id)));
        Assert.Null((await EstadoAsync(_super1Id)).LockoutEnd);
        Assert.Equal(2, await ContarSuperAdminActivosAsync());
    }

    [Fact]
    public async Task InstalacionSinSuperAdminActivo_LaAdministracionDeUsuariosNoQuedaTrabada()
    {
        // Instalación recién sembrada (o que ya perdió todas sus cuentas): no hay nada que conservar y la
        // administración ordinaria no se bloquea. El SuperAdmin se aprovisiona/recupera por DbInitializer.
        await DesactivarEnBdAsync(_super1Id);
        await DesactivarEnBdAsync(_super2Id);
        var client = _factory.CreateClientWithUserId(_adminId);
        var token = await TokenAsync(client);

        // El Administrador sembrado no tiene usuarios.delete: se usa una acción masiva de bloqueo (update).
        await client.PostAsync("/Usuarios/BulkAction", Form(
            ("__RequestVerificationToken", token), ("ids", _normalId), ("accion", "bloquear")));

        Assert.NotNull((await EstadoAsync(_normalId)).LockoutEnd);
        Assert.Equal(0, await ContarSuperAdminActivosAsync());
    }

    // ───────────────────────── B. el rol SuperAdmin es del sistema ─────────────────────────

    [Fact]
    public async Task RolSuperAdmin_NoSePuedeEliminarRenombrarNiDesactivar_NiPorUnSuperAdmin()
    {
        // Sin usuarios activos en el rol, DeleteRoleAsync lo eliminaría: la guardia debe impedirlo antes.
        await DesactivarEnBdAsync(_super1Id);
        await DesactivarEnBdAsync(_super2Id);
        await DesactivarEnBdAsync(TheBuryProject.Tests.Infrastructure.TestAuthHandler.UserId);
        var roleId = await IdDeRolAsync(Roles.SuperAdmin);

        var client = _factory.CreateClientWithUserId(TheBuryProject.Tests.Infrastructure.TestAuthHandler.UserId);
        var token = await TokenAsync(client);

        await client.PostAsync("/Seguridad/DeleteRol", Form(("__RequestVerificationToken", token), ("id", roleId)));
        Assert.Equal(roleId, await IdDeRolOrNullAsync(Roles.SuperAdmin));

        var renombrar = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token), ("Id", roleId), ("Nombre", "Otro"), ("Activo", "true")));
        Assert.Equal(HttpStatusCode.Forbidden, renombrar.StatusCode);

        var desactivar = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token), ("Id", roleId), ("Nombre", Roles.SuperAdmin), ("Activo", "false")));
        Assert.Equal(HttpStatusCode.Forbidden, desactivar.StatusCode);

        // Cambiar solo la capitalización también deshabilitaría el bypass (IsInRole distingue mayúsculas).
        var capitalizacion = await client.PostAsync("/Seguridad/EditRol", Form(
            ("__RequestVerificationToken", token), ("Id", roleId), ("Nombre", "superadmin"), ("Activo", "true")));
        Assert.Equal(HttpStatusCode.Forbidden, capitalizacion.StatusCode);

        await client.PostAsync("/Seguridad/ToggleRolActivo", Form(
            ("__RequestVerificationToken", token), ("id", roleId), ("activo", "false")));

        Assert.Equal(roleId, await IdDeRolOrNullAsync(Roles.SuperAdmin));
        Assert.Equal(Roles.SuperAdmin, await NombreDeRolAsync(roleId));
        Assert.True(await RolActivoAsync(roleId));
    }

    // ───────────────────────── Autorizaciones: autoaprobación autorizada (decisión funcional) ─────────────────────────

    [Fact]
    public async Task AprobarSolicitud_GerenteConPermisoPuedeAprobarSuPropiaSolicitud()
    {
        var solicitudId = await CrearSolicitudAsync(solicitante: $"ger_{_sfx}");
        var client = _factory.CreateClientWithUserId(_gerenteId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Autorizacion/AprobarSolicitud", Form(
            ("__RequestVerificationToken", token), ("id", solicitudId.ToString()), ("comentario", "propia")));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(EstadoSolicitud.Aprobada, await EstadoSolicitudAsync(solicitudId));
    }

    [Fact]
    public async Task AprobarSolicitud_SinPermisoDeAprobarSigueRechazadoAunqueSeaLaPropia()
    {
        var solicitudId = await CrearSolicitudAsync(solicitante: $"normal_{_sfx}");
        var client = _factory.CreateClientWithUserId(_normalId);
        var token = await TokenAsync(client);

        var response = await client.PostAsync("/Autorizacion/AprobarSolicitud", Form(
            ("__RequestVerificationToken", token), ("id", solicitudId.ToString()), ("comentario", "propia")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(EstadoSolicitud.Pendiente, await EstadoSolicitudAsync(solicitudId));
    }

    // ───────────────────────── Helpers ─────────────────────────

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
        HttpClient client, string token, string userId, string userName, string[] roles, bool activo, string? nombre = null)
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("Id", userId),
            new("UserName", userName),
            new("Email", $"{userName}@test.com"),
            new("Activo", activo ? "true" : "false"),
            new("RowVersion", Convert.ToBase64String(await RowVersionAsync(userId)))
        };
        if (nombre != null) campos.Add(new("Nombre", nombre));
        campos.AddRange(roles.Select(r => new KeyValuePair<string, string>("RolesSeleccionados", r)));
        return await client.PostAsync("/Seguridad/EditUsuario", new FormUrlEncodedContent(campos));
    }

    private static async Task<string> CrearUsuarioAsync(IServiceScope scope, string userName, string rol)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.com", EmailConfirmed = true, Activo = true };
        var result = await userManager.CreateAsync(user, "Test123!");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        var addRole = await userManager.AddToRoleAsync(user, rol);
        Assert.True(addRole.Succeeded, string.Join(", ", addRole.Errors.Select(e => e.Description)));
        return user.Id;
    }

    /// <summary>Deja como SuperAdmin utilizables exactamente a <paramref name="ids"/> (más el usuario semilla de tests).</summary>
    private async Task FijarSuperAdminsAsync(params string[] ids)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var superRoleId = await context.Roles.Where(r => r.Name == Roles.SuperAdmin).Select(r => r.Id).SingleAsync();
        var conservar = ids.Append(TheBuryProject.Tests.Infrastructure.TestAuthHandler.UserId).ToHashSet();

        var sobrantes = await context.UserRoles
            .Where(ur => ur.RoleId == superRoleId && !conservar.Contains(ur.UserId)).ToListAsync();
        context.UserRoles.RemoveRange(sobrantes);

        // El usuario semilla de tests no cuenta como SuperAdmin utilizable en estos escenarios.
        var semilla = await context.Users.SingleAsync(u => u.Id == TheBuryProject.Tests.Infrastructure.TestAuthHandler.UserId);
        semilla.Activo = false;
        await context.SaveChangesAsync();
    }

    private async Task DesactivarEnBdAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await context.Users.SingleAsync(u => u.Id == userId);
        user.Activo = false;
        user.MotivoDesactivacion = null;
        await context.SaveChangesAsync();
    }

    private async Task BloquearEnBdAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await context.Users.SingleAsync(u => u.Id == userId);
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        await context.SaveChangesAsync();
    }

    private async Task BloquearTemporalEnBdAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await context.Users.SingleAsync(u => u.Id == userId);
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        await context.SaveChangesAsync();
    }

    /// <summary>Estructuralmente activos: cuenta activa con el rol SuperAdmin; el lockout no cuenta.</summary>
    private async Task<int> ContarSuperAdminActivosAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await (from u in context.Users.AsNoTracking()
                      join ur in context.UserRoles on u.Id equals ur.UserId
                      join r in context.Roles on ur.RoleId equals r.Id
                      where r.Name == Roles.SuperAdmin && u.Activo
                      select u.Id).CountAsync();
    }

    private sealed record EstadoUsuario(bool Activo, DateTimeOffset? LockoutEnd, string? MotivoDesactivacion, string? SecurityStamp);

    private async Task<EstadoUsuario> EstadoAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new EstadoUsuario(u.Activo, u.LockoutEnd, u.MotivoDesactivacion, u.SecurityStamp)).SingleAsync();
    }

    private async Task<string?> NombreAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Nombre).SingleAsync();
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

    private async Task<int> ContarSuperAdminUtilizablesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var candidatos = await (from u in context.Users.AsNoTracking()
                                join ur in context.UserRoles on u.Id equals ur.UserId
                                join r in context.Roles on ur.RoleId equals r.Id
                                where r.Name == Roles.SuperAdmin && u.Activo
                                select new { u.LockoutEnabled, u.LockoutEnd }).ToListAsync();
        return candidatos.Count(c => !c.LockoutEnabled || c.LockoutEnd == null || c.LockoutEnd <= ahora);
    }

    private async Task<string> IdDeRolAsync(string nombre) =>
        await IdDeRolOrNullAsync(nombre) ?? throw new InvalidOperationException($"Rol {nombre} inexistente.");

    private async Task<string?> IdDeRolOrNullAsync(string nombre)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Roles.Where(r => r.Name == nombre).Select(r => r.Id).SingleOrDefaultAsync();
    }

    private async Task<string?> NombreDeRolAsync(string roleId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Roles.AsNoTracking().Where(r => r.Id == roleId).Select(r => r.Name).SingleAsync();
    }

    private async Task<bool> RolActivoAsync(string roleId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activo = await context.RolMetadatas.AsNoTracking().Where(m => m.RoleId == roleId).Select(m => (bool?)m.Activo).SingleOrDefaultAsync();
        return activo ?? true;
    }

    private async Task<int> CrearSolicitudAsync(string solicitante)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var solicitud = new SolicitudAutorizacion
        {
            UsuarioSolicitante = solicitante,
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
}
