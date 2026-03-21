using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class RolServiceCreateRoleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;

    public RolServiceCreateRoleTests()
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
    // 1. Crea rol con metadata correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateRoleWithMetadata_Ok_CreaRolYMetadata()
    {
        var (ok, error, roleId, roleName) = await _service.CreateRoleWithMetadataAsync(
            "Vendedor", "Rol para vendedores", true);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(roleId);
        Assert.Equal("Vendedor", roleName);

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        Assert.NotNull(role);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.Equal("Rol para vendedores", metadata!.Descripcion);
        Assert.True(metadata.Activo);
        Assert.False(metadata.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // 2. Crea rol inactivo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateRoleWithMetadata_Inactivo_MetadataInactiva()
    {
        var (ok, _, roleId, _) = await _service.CreateRoleWithMetadataAsync(
            "Observador", null, false);

        Assert.True(ok);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.False(metadata!.Activo);
    }

    // -------------------------------------------------------------------------
    // 3. Descripción null usa default
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateRoleWithMetadata_DescripcionNull_UsaDefault()
    {
        var (ok, _, roleId, _) = await _service.CreateRoleWithMetadataAsync(
            "NuevoRol", null, true);

        Assert.True(ok);

        var metadata = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == roleId);
        Assert.NotNull(metadata);
        Assert.False(string.IsNullOrWhiteSpace(metadata!.Descripcion));
    }

    // -------------------------------------------------------------------------
    // 4. Rol duplicado retorna error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateRoleWithMetadata_RolDuplicado_RetornaError()
    {
        await _service.CreateRoleWithMetadataAsync("Admin", null, true);

        var (ok, error, roleId, roleName) = await _service.CreateRoleWithMetadataAsync(
            "Admin", null, true);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(roleId);
        Assert.Null(roleName);
    }

    // -------------------------------------------------------------------------
    // 5. Transacción atómica: rol + metadata en una sola operación
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateRoleWithMetadata_Atomico_RolYMetadataExistenJuntos()
    {
        var (ok, _, roleId, _) = await _service.CreateRoleWithMetadataAsync(
            "Cajero", "Maneja caja", true);

        Assert.True(ok);

        var roleCount = await _context.Roles.CountAsync(r => r.Id == roleId);
        var metadataCount = await _context.RolMetadatas.CountAsync(m => m.RoleId == roleId);

        Assert.Equal(1, roleCount);
        Assert.Equal(1, metadataCount);
    }
}
