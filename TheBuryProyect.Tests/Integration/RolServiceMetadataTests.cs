using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class RolServiceMetadataTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly RolService _service;

    private const string TestRoleId = "test-role-id-001";
    private const string TestRoleName = "TestRole";

    public RolServiceMetadataTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        // RolService requiere RoleManager y UserManager en constructor,
        // pero los tests de metadata solo usan _context — pasamos null.
        _service = new RolService(null!, null!, _context);
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
    // 1. GetRoleMetadataAsync — devuelve null si no existe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleMetadata_NoExiste_RetornaNull()
    {
        var result = await _service.GetRoleMetadataAsync("no-existe");

        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // 2. EnsureRoleMetadataAsync — crea metadata si no existe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRoleMetadata_NoExiste_CreaMetadata()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        var metadata = await _service.EnsureRoleMetadataAsync(TestRoleId, TestRoleName);
        await _context.SaveChangesAsync();

        Assert.NotNull(metadata);
        Assert.Equal(TestRoleId, metadata.RoleId);
        Assert.True(metadata.Activo);
        Assert.False(metadata.IsDeleted);

        // Verificar que persiste en DB
        var enDb = await _context.RolMetadatas.FirstOrDefaultAsync(m => m.RoleId == TestRoleId);
        Assert.NotNull(enDb);
    }

    // -------------------------------------------------------------------------
    // 3. EnsureRoleMetadataAsync — no duplica si ya existe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRoleMetadata_YaExiste_NoDuplica()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        // Primera llamada — crea
        await _service.EnsureRoleMetadataAsync(TestRoleId, TestRoleName);
        await _context.SaveChangesAsync();

        // Segunda llamada — no debe duplicar
        var metadata = await _service.EnsureRoleMetadataAsync(TestRoleId, TestRoleName);
        await _context.SaveChangesAsync();

        var count = await _context.RolMetadatas.CountAsync(m => m.RoleId == TestRoleId);
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // 4. Metadata creada tiene RoleId correcto y Descripcion asignada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnsureRoleMetadata_AsignaRoleIdYDescripcion()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        var metadata = await _service.EnsureRoleMetadataAsync(TestRoleId, TestRoleName);
        await _context.SaveChangesAsync();

        Assert.Equal(TestRoleId, metadata.RoleId);
        Assert.False(string.IsNullOrWhiteSpace(metadata.Descripcion));
    }

    // -------------------------------------------------------------------------
    // 5. Respeta !IsDeleted — GetRoleMetadataAsync no devuelve soft-deleted,
    //    EnsureRoleMetadataAsync sí restaura soft-deleted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRoleMetadata_SoftDeleted_RetornaNull()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        // Crear y luego soft-delete
        var metadata = await _service.EnsureRoleMetadataAsync(TestRoleId, TestRoleName);
        await _context.SaveChangesAsync();

        metadata.IsDeleted = true;
        await _context.SaveChangesAsync();

        // GetRoleMetadataAsync respeta query filter → null
        var result = await _service.GetRoleMetadataAsync(TestRoleId);
        Assert.Null(result);
    }

    [Fact]
    public async Task EnsureRoleMetadata_SoftDeleted_Restaura()
    {
        await SeedIdentityRole(TestRoleId, TestRoleName);

        // Crear y luego soft-delete
        var metadata = await _service.EnsureRoleMetadataAsync(TestRoleId, TestRoleName);
        await _context.SaveChangesAsync();

        metadata.IsDeleted = true;
        await _context.SaveChangesAsync();

        // EnsureRoleMetadataAsync usa IgnoreQueryFilters → restaura
        var restaurada = await _service.EnsureRoleMetadataAsync(TestRoleId, TestRoleName);
        await _context.SaveChangesAsync();

        Assert.False(restaurada.IsDeleted);

        // Ahora GetRoleMetadataAsync debe encontrarla
        var result = await _service.GetRoleMetadataAsync(TestRoleId);
        Assert.NotNull(result);
    }
}
