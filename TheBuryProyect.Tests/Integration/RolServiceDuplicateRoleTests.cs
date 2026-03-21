using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class RolServiceDuplicateRoleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;

    public RolServiceDuplicateRoleTests()
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

    private async Task<string> SeedRoleViaService(string nombre, string? descripcion = null, bool activo = true)
    {
        var (_, _, roleId, _) = await _service.CreateRoleWithMetadataAsync(nombre, descripcion, activo);
        return roleId!;
    }

    private async Task<(int moduloId, int accionId)> SeedModuloYAccion(string moduloClave, string accionClave)
    {
        var modulo = new ModuloSistema
        {
            Nombre = moduloClave,
            Clave = moduloClave,
            Orden = 1
        };
        _context.ModulosSistema.Add(modulo);
        await _context.SaveChangesAsync();

        var accion = new AccionModulo
        {
            ModuloId = modulo.Id,
            Nombre = accionClave,
            Clave = accionClave,
            Orden = 1
        };
        _context.AccionesModulo.Add(accion);
        await _context.SaveChangesAsync();

        return (modulo.Id, accion.Id);
    }

    // -------------------------------------------------------------------------
    // 1. Crea nuevo rol con metadata
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateRoleAsync_CreaNuevoRolConMetadata()
    {
        var sourceId = await SeedRoleViaService("Admin", "Rol admin");

        var (ok, error, roleId, roleName, _) = await _service.DuplicateRoleAsync(
            sourceId, "AdminCopia", "Copia del admin", true);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(roleId);
        Assert.Equal("AdminCopia", roleName);

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        Assert.NotNull(role);
        Assert.Equal("AdminCopia", role!.Name);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.Equal("Copia del admin", metadata!.Descripcion);
        Assert.True(metadata.Activo);
    }

    // -------------------------------------------------------------------------
    // 2. Copia permisos del rol origen
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateRoleAsync_CopiaPermisosDelRolOrigen()
    {
        var sourceId = await SeedRoleViaService("Vendedor");
        var (modId1, accId1) = await SeedModuloYAccion("ventas", "view");
        var (modId2, accId2) = await SeedModuloYAccion("clientes", "create");

        await _service.AssignPermissionToRoleAsync(sourceId, modId1, accId1);
        await _service.AssignPermissionToRoleAsync(sourceId, modId2, accId2);

        var (ok, _, roleId, _, permisosCopiados) = await _service.DuplicateRoleAsync(
            sourceId, "VendedorCopia", null, true);

        Assert.True(ok);
        Assert.Equal(2, permisosCopiados);

        var permisosNuevo = await _context.RolPermisos
            .Where(rp => rp.RoleId == roleId && !rp.IsDeleted)
            .ToListAsync();
        Assert.Equal(2, permisosNuevo.Count);
    }

    // -------------------------------------------------------------------------
    // 3. Descripción null usa default
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateRoleAsync_DescripcionNullUsaDefault()
    {
        var sourceId = await SeedRoleViaService("Editor");

        var (ok, _, roleId, _, _) = await _service.DuplicateRoleAsync(
            sourceId, "EditorCopia", null, true);

        Assert.True(ok);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrWhiteSpace(metadata!.Descripcion));
    }

    // -------------------------------------------------------------------------
    // 4. Rol origen no existe retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateRoleAsync_RolOrigenNoExiste_RetornaError()
    {
        var (ok, error, roleId, roleName, _) = await _service.DuplicateRoleAsync(
            "no-existe", "Nuevo", null, true);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(roleId);
        Assert.Null(roleName);
    }

    // -------------------------------------------------------------------------
    // 5. Nombre duplicado retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateRoleAsync_NombreDuplicado_RetornaError()
    {
        var sourceId = await SeedRoleViaService("Admin");
        await SeedRoleViaService("AdminCopia");

        var (ok, error, roleId, roleName, _) = await _service.DuplicateRoleAsync(
            sourceId, "AdminCopia", null, true);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(roleId);
        Assert.Null(roleName);
    }

    // -------------------------------------------------------------------------
    // 6. Atomicidad: rol + metadata + permisos consistentes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateRoleAsync_MantieneAtomicidadBasica()
    {
        var sourceId = await SeedRoleViaService("Cajero");
        var (modId, accId) = await SeedModuloYAccion("caja", "view");
        await _service.AssignPermissionToRoleAsync(sourceId, modId, accId);

        var (ok, _, roleId, _, _) = await _service.DuplicateRoleAsync(
            sourceId, "CajeroCopia", "Copia de cajero", false);

        Assert.True(ok);

        var roleCount = await _context.Roles.CountAsync(r => r.Id == roleId);
        var metadataCount = await _context.RolMetadatas.CountAsync(m => m.RoleId == roleId);
        var permisoCount = await _context.RolPermisos.CountAsync(rp => rp.RoleId == roleId && !rp.IsDeleted);

        Assert.Equal(1, roleCount);
        Assert.Equal(1, metadataCount);
        Assert.Equal(1, permisoCount);

        var metadata = await _context.RolMetadatas.FirstAsync(m => m.RoleId == roleId);
        Assert.False(metadata.Activo);
        Assert.Equal("Copia de cajero", metadata.Descripcion);
    }
}
