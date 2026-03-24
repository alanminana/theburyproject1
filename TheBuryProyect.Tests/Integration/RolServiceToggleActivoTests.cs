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

public class RolServiceToggleActivoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;

    private const string TestRoleId = "toggle-role-001";
    private const string TestRoleName = "TestToggleRole";

    public RolServiceToggleActivoTests()
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
            Array.Empty<IRoleValidator<IdentityRole>>(),
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

    private async Task SeedIdentityRole(string roleId, string roleName)
    {
        _context.Roles.Add(new IdentityRole
        {
            Id = roleId,
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        });
        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // 1. Activa correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ToggleRoleActivo_Activar_RetornaNombreYMetadataActiva()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        var result = await _service.ToggleRoleActivoAsync(TestRoleId, true);

        Assert.Equal(TestRoleName, result);

        var metadata = await _context.RolMetadatas
            .FirstOrDefaultAsync(m => m.RoleId == TestRoleId);
        Assert.NotNull(metadata);
        Assert.True(metadata!.Activo);
        Assert.False(metadata.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // 2. Desactiva correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ToggleRoleActivo_Desactivar_RetornaNombreYMetadataInactiva()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        // Primero activar para crear metadata
        await _service.ToggleRoleActivoAsync(TestRoleId, true);

        // Luego desactivar
        var result = await _service.ToggleRoleActivoAsync(TestRoleId, false);

        Assert.Equal(TestRoleName, result);

        var metadata = await _context.RolMetadatas
            .FirstOrDefaultAsync(m => m.RoleId == TestRoleId);
        Assert.NotNull(metadata);
        Assert.False(metadata!.Activo);
        Assert.False(metadata.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // 3. Rol no existe → retorna null
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ToggleRoleActivo_RolNoExiste_RetornaNull()
    {
        var result = await _service.ToggleRoleActivoAsync("no-existe", true);

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // 4. Crea metadata si no existía previamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ToggleRoleActivo_CreaMetadataSiNoExiste()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        // Verificar que no hay metadata antes
        var antes = await _context.RolMetadatas
            .IgnoreQueryFilters()
            .CountAsync(m => m.RoleId == TestRoleId);
        Assert.Equal(0, antes);

        await _service.ToggleRoleActivoAsync(TestRoleId, true);

        var despues = await _context.RolMetadatas
            .CountAsync(m => m.RoleId == TestRoleId);
        Assert.Equal(1, despues);
    }

    // -------------------------------------------------------------------------
    // 5. No duplica metadata en llamadas sucesivas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ToggleRoleActivo_NoDuplicaMetadata()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        await _service.ToggleRoleActivoAsync(TestRoleId, true);
        await _service.ToggleRoleActivoAsync(TestRoleId, false);
        await _service.ToggleRoleActivoAsync(TestRoleId, true);

        var count = await _context.RolMetadatas
            .IgnoreQueryFilters()
            .CountAsync(m => m.RoleId == TestRoleId);
        Assert.Equal(1, count);
    }
}
