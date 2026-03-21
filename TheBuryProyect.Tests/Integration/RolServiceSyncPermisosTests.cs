using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class RolServiceSyncPermisosTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;

    public RolServiceSyncPermisosTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var roleStore = new RoleStore<IdentityRole>(_context);
        var roleManager = new RoleManager<IdentityRole>(
            roleStore,
            new[] { new RoleValidator<IdentityRole>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

        _service = new RolService(roleManager, null!, _context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string> SeedRoleViaService(string nombre)
    {
        var (_, _, roleId, _) = await _service.CreateRoleWithMetadataAsync(nombre, null, true);
        return roleId!;
    }

    private async Task<(int moduloId, int accionId)> SeedModuloYAccion(string moduloClave, string accionClave, bool activa = true)
    {
        var modulo = _context.ModulosSistema.Local
            .FirstOrDefault(m => m.Clave == moduloClave);

        if (modulo == null)
        {
            modulo = new ModuloSistema { Nombre = moduloClave, Clave = moduloClave, Orden = 1 };
            _context.ModulosSistema.Add(modulo);
            await _context.SaveChangesAsync();
        }

        var accion = new AccionModulo
        {
            ModuloId = modulo.Id,
            Nombre = accionClave,
            Clave = accionClave,
            Orden = 1,
            Activa = activa
        };
        _context.AccionesModulo.Add(accion);
        await _context.SaveChangesAsync();

        return (modulo.Id, accion.Id);
    }

    // =========================================================================
    // SyncPermisosForRoleAsync
    // =========================================================================

    // -------------------------------------------------------------------------
    // 1. Asigna permisos correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncPermisos_AsignaPermisosCorrectos()
    {
        var roleId = await SeedRoleViaService("Editor");
        var (_, accId1) = await SeedModuloYAccion("ventas", "view");
        var (_, accId2) = await SeedModuloYAccion("ventas", "create");

        var (ok, error, count) = await _service.SyncPermisosForRoleAsync(roleId, [accId1, accId2]);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, count);

        var permisos = await _service.GetPermissionsForRoleAsync(roleId);
        Assert.Equal(2, permisos.Count);
    }

    // -------------------------------------------------------------------------
    // 2. Reemplaza permisos previos (clear + assign)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncPermisos_ReemplazaPermisosPrevios()
    {
        var roleId = await SeedRoleViaService("Cajero");
        var (modId, accId1) = await SeedModuloYAccion("caja", "view");
        var (_, accId2) = await SeedModuloYAccion("caja", "create");
        var (_, accId3) = await SeedModuloYAccion("caja", "delete");

        // Asignar primero 2 permisos
        await _service.SyncPermisosForRoleAsync(roleId, [accId1, accId2]);

        // Sync con solo 1 permiso diferente
        var (ok, _, count) = await _service.SyncPermisosForRoleAsync(roleId, [accId3]);

        Assert.True(ok);
        Assert.Equal(1, count);

        var permisos = await _service.GetPermissionsForRoleAsync(roleId);
        Assert.Single(permisos);
        Assert.Equal(accId3, permisos[0].AccionId);
    }

    // -------------------------------------------------------------------------
    // 3. Lista vacía limpia todos los permisos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncPermisos_ListaVacia_LimpiaTodo()
    {
        var roleId = await SeedRoleViaService("Temporal");
        var (_, accId1) = await SeedModuloYAccion("stock", "view");
        await _service.SyncPermisosForRoleAsync(roleId, [accId1]);

        var (ok, _, count) = await _service.SyncPermisosForRoleAsync(roleId, []);

        Assert.True(ok);
        Assert.Equal(0, count);

        var permisos = await _service.GetPermissionsForRoleAsync(roleId);
        Assert.Empty(permisos);
    }

    // -------------------------------------------------------------------------
    // 4. Filtra acciones inactivas o eliminadas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncPermisos_FiltraAccionesInactivas()
    {
        var roleId = await SeedRoleViaService("Filtrado");
        var (_, accIdActiva) = await SeedModuloYAccion("reportes", "view", activa: true);
        var (_, accIdInactiva) = await SeedModuloYAccion("reportes", "export", activa: false);

        var (ok, _, count) = await _service.SyncPermisosForRoleAsync(roleId, [accIdActiva, accIdInactiva]);

        Assert.True(ok);
        Assert.Equal(1, count); // Solo la activa
    }

    // -------------------------------------------------------------------------
    // 5. Rol no encontrado retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SyncPermisos_RolNoExiste_RetornaError()
    {
        var (ok, error, count) = await _service.SyncPermisosForRoleAsync("inexistente", [1, 2]);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Equal(0, count);
    }

    // =========================================================================
    // CopyPermisosFromRoleAsync
    // =========================================================================

    // -------------------------------------------------------------------------
    // 6. Copia permisos correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CopyPermisos_CopiaPermisosCorrectamente()
    {
        var sourceId = await SeedRoleViaService("Origen");
        var targetId = await SeedRoleViaService("Destino");

        var (_, accId1) = await SeedModuloYAccion("compras", "view");
        var (_, accId2) = await SeedModuloYAccion("compras", "create");
        await _service.AssignPermissionToRoleAsync(sourceId, _context.AccionesModulo.First(a => a.Id == accId1).ModuloId, accId1);
        await _service.AssignPermissionToRoleAsync(sourceId, _context.AccionesModulo.First(a => a.Id == accId2).ModuloId, accId2);

        var (ok, error, count) = await _service.CopyPermisosFromRoleAsync(sourceId, targetId);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(2, count);

        var permisosTarget = await _service.GetPermissionsForRoleAsync(targetId);
        Assert.Equal(2, permisosTarget.Count);
    }

    // -------------------------------------------------------------------------
    // 7. Reemplaza permisos previos del destino
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CopyPermisos_ReemplazaPreviosDelDestino()
    {
        var sourceId = await SeedRoleViaService("Fuente");
        var targetId = await SeedRoleViaService("Receptor");

        var (modId1, accId1) = await SeedModuloYAccion("proveedores", "view");
        var (modId2, accId2) = await SeedModuloYAccion("proveedores", "create");
        var (modId3, accId3) = await SeedModuloYAccion("proveedores", "delete");

        // Target tiene 1 permiso previo
        await _service.AssignPermissionToRoleAsync(targetId, modId3, accId3);

        // Source tiene 2 permisos distintos
        await _service.AssignPermissionToRoleAsync(sourceId, modId1, accId1);
        await _service.AssignPermissionToRoleAsync(sourceId, modId2, accId2);

        var (ok, _, count) = await _service.CopyPermisosFromRoleAsync(sourceId, targetId);

        Assert.True(ok);
        Assert.Equal(2, count);

        var permisosTarget = await _service.GetPermissionsForRoleAsync(targetId);
        Assert.Equal(2, permisosTarget.Count);
        Assert.DoesNotContain(permisosTarget, p => p.AccionId == accId3);
    }

    // -------------------------------------------------------------------------
    // 8. Rol origen no existe retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CopyPermisos_OrigenNoExiste_RetornaError()
    {
        var targetId = await SeedRoleViaService("SoloDestino");

        var (ok, error, _) = await _service.CopyPermisosFromRoleAsync("fantasma", targetId);

        Assert.False(ok);
        Assert.Contains("origen", error!, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // 9. Rol destino no existe retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CopyPermisos_DestinoNoExiste_RetornaError()
    {
        var sourceId = await SeedRoleViaService("SoloOrigen");

        var (ok, error, _) = await _service.CopyPermisosFromRoleAsync(sourceId, "fantasma");

        Assert.False(ok);
        Assert.Contains("destino", error!, StringComparison.OrdinalIgnoreCase);
    }
}
