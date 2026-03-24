using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class RolServiceUpdateRoleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;

    public RolServiceUpdateRoleTests()
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

        _service = new RolService(roleManager, null!, _context, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.RolService>.Instance);
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

    // -------------------------------------------------------------------------
    // 1. Actualiza nombre y metadata correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRoleWithMetadata_ActualizaNombreYMetadata()
    {
        var roleId = await SeedRoleViaService("Vendedor", "Rol vendedor");

        var (ok, error, roleName) = await _service.UpdateRoleWithMetadataAsync(
            roleId, "SuperVendedor", "Rol super vendedor", true);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("SuperVendedor", roleName);

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        Assert.Equal("SuperVendedor", role!.Name);
        Assert.Equal("SUPERVENDEDOR", role.NormalizedName);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.Equal("Rol super vendedor", metadata!.Descripcion);
        Assert.True(metadata.Activo);
        Assert.False(metadata.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // 2. Descripción null usa default
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRoleWithMetadata_DescripcionNullUsaDefault()
    {
        var roleId = await SeedRoleViaService("Cajero", "Desc original");

        var (ok, _, _) = await _service.UpdateRoleWithMetadataAsync(
            roleId, "Cajero", null, true);

        Assert.True(ok);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrWhiteSpace(metadata!.Descripcion));
        Assert.NotEqual("Desc original", metadata.Descripcion);
    }

    // -------------------------------------------------------------------------
    // 3. Respeta flag activo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRoleWithMetadata_RespetaActivo()
    {
        var roleId = await SeedRoleViaService("Editor", null, true);

        var (ok, _, _) = await _service.UpdateRoleWithMetadataAsync(
            roleId, "Editor", "Desc", false);

        Assert.True(ok);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.False(metadata!.Activo);
    }

    // -------------------------------------------------------------------------
    // 4. Rol no existe retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRoleWithMetadata_RolNoExiste_RetornaError()
    {
        var (ok, error, roleName) = await _service.UpdateRoleWithMetadataAsync(
            "no-existe", "Nuevo", null, true);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(roleName);
    }

    // -------------------------------------------------------------------------
    // 5. Nombre duplicado retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRoleWithMetadata_NombreDuplicado_RetornaError()
    {
        await SeedRoleViaService("Admin");
        var roleId = await SeedRoleViaService("Vendedor");

        var (ok, error, roleName) = await _service.UpdateRoleWithMetadataAsync(
            roleId, "Admin", null, true);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(roleName);

        // Verificar que el rol original no cambió
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        Assert.Equal("Vendedor", role!.Name);
    }

    // -------------------------------------------------------------------------
    // 6. Atomicidad: rol y metadata se actualizan juntos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateRoleWithMetadata_MantieneAtomicidad()
    {
        var roleId = await SeedRoleViaService("Operador", "Desc inicial", true);

        var (ok, _, _) = await _service.UpdateRoleWithMetadataAsync(
            roleId, "OperadorSenior", "Desc actualizada", false);

        Assert.True(ok);

        var roleCount = await _context.Roles.CountAsync(r => r.Id == roleId && r.Name == "OperadorSenior");
        var metadataCount = await _context.RolMetadatas.CountAsync(m =>
            m.RoleId == roleId && m.Descripcion == "Desc actualizada" && !m.Activo);

        Assert.Equal(1, roleCount);
        Assert.Equal(1, metadataCount);
    }
}
