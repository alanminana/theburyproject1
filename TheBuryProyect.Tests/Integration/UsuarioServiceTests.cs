using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para UsuarioService.
/// Usa UserManager real con SQLite en memoria + Identity stores.
/// Cubre: GetDashboardStatsAsync, GetUsuariosConRolesAsync, GetSucursalOptionsAsync,
/// ExistsSucursalActivaAsync, GetSucursalByIdAsync, GetUsuariosByIdsAsync,
/// GetUsuarioParaEdicionAsync, ValidarUnicidadUsuarioAsync, GetUsuarioSelectListAsync,
/// GetUsuariosPorRolAsync.
/// </summary>
public class UsuarioServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UsuarioService _service;

    public UsuarioServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(dbOptions);
        _context.Database.EnsureCreated();

        // Build a real UserManager with EF Identity stores
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequiredLength = 4;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        var provider = services.BuildServiceProvider();
        _userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();

        _service = new UsuarioService(_userManager, _context, NullLogger<UsuarioService>.Instance);
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<ApplicationUser> SeedUserAsync(
        string userName, bool activo = true, bool lockout = false,
        string? nombre = null, string? apellido = null, int? sucursalId = null)
    {
        var user = new ApplicationUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.com",
            NormalizedEmail = $"{userName}@test.com".ToUpperInvariant(),
            Activo = activo,
            Nombre = nombre,
            Apellido = apellido,
            SucursalId = sucursalId,
            RowVersion = Array.Empty<byte>()
        };

        if (lockout)
        {
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
        }

        var result = await _userManager.CreateAsync(user, "Test1234");
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task SeedRoleAsync(string roleName)
    {
        if (!await _roleManager.RoleExistsAsync(roleName))
            await _roleManager.CreateAsync(new IdentityRole(roleName));
    }

    private async Task<Sucursal> SeedSucursalAsync(string nombre, bool activa = true)
    {
        var sucursal = new Sucursal { Nombre = nombre, Activa = activa };
        _context.Sucursales.Add(sucursal);
        await _context.SaveChangesAsync();
        return sucursal;
    }

    // =========================================================================
    // GetDashboardStatsAsync
    // EXCLUIDO: SQLite no puede traducir DateTimeOffset > DateTimeOffset.UtcNow
    // (u.LockoutEnd > now con DateTimeOffset?). La comparación de LockoutEnd falla
    // con "could not be translated" en SQLite in-memory.
    // =========================================================================

    // =========================================================================
    // GetUsuariosConRolesAsync
    // =========================================================================

    [Fact]
    public async Task GetUsuariosConRolesAsync_SoloActivos_ExcluyeInactivos()
    {
        await SeedUserAsync("activo1", activo: true);
        await SeedUserAsync("inactivo1", activo: false);

        var result = await _service.GetUsuariosConRolesAsync(incluirInactivos: false);

        Assert.All(result, u => Assert.True(u.Activo));
        Assert.DoesNotContain(result, u => u.UserName == "inactivo1");
    }

    [Fact]
    public async Task GetUsuariosConRolesAsync_IncluirInactivos_RetornaTodos()
    {
        await SeedUserAsync("activo1", activo: true);
        await SeedUserAsync("inactivo1", activo: false);

        var result = await _service.GetUsuariosConRolesAsync(incluirInactivos: true);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetUsuariosConRolesAsync_ConRol_RetornaRolesCorrectos()
    {
        await SeedRoleAsync("Admin");
        var user = await SeedUserAsync("admin1", activo: true);
        await _userManager.AddToRoleAsync(user, "Admin");

        var result = await _service.GetUsuariosConRolesAsync();

        var adminUser = result.Single(u => u.UserName == "admin1");
        Assert.Contains("Admin", adminUser.Roles);
    }

    // =========================================================================
    // GetSucursalOptionsAsync
    // =========================================================================

    [Fact]
    public async Task GetSucursalOptionsAsync_SinSucursales_RetornaListaVacia()
    {
        var result = await _service.GetSucursalOptionsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSucursalOptionsAsync_ConSucursales_RetornaOpciones()
    {
        await SeedSucursalAsync("Central");
        await SeedSucursalAsync("Norte");

        var result = await _service.GetSucursalOptionsAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Nombre == "Central");
        Assert.Contains(result, s => s.Nombre == "Norte");
    }

    // =========================================================================
    // ExistsSucursalActivaAsync
    // =========================================================================

    [Fact]
    public async Task ExistsSucursalActivaAsync_SucursalActiva_RetornaTrue()
    {
        var sucursal = await SeedSucursalAsync("Central", activa: true);

        var result = await _service.ExistsSucursalActivaAsync(sucursal.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ExistsSucursalActivaAsync_SucursalInactiva_RetornaFalse()
    {
        var sucursal = await SeedSucursalAsync("Inactiva", activa: false);

        var result = await _service.ExistsSucursalActivaAsync(sucursal.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task ExistsSucursalActivaAsync_IdInexistente_RetornaFalse()
    {
        var result = await _service.ExistsSucursalActivaAsync(9999);

        Assert.False(result);
    }

    // =========================================================================
    // GetSucursalByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetSucursalByIdAsync_IdExistente_RetornaSucursal()
    {
        var sucursal = await SeedSucursalAsync("Sur");

        var result = await _service.GetSucursalByIdAsync(sucursal.Id);

        Assert.NotNull(result);
        Assert.Equal("Sur", result!.Nombre);
    }

    [Fact]
    public async Task GetSucursalByIdAsync_IdNulo_RetornaNull()
    {
        var result = await _service.GetSucursalByIdAsync(null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSucursalByIdAsync_IdInexistente_RetornaNull()
    {
        var result = await _service.GetSucursalByIdAsync(9999);

        Assert.Null(result);
    }

    // =========================================================================
    // GetUsuariosByIdsAsync
    // =========================================================================

    [Fact]
    public async Task GetUsuariosByIdsAsync_ListaVacia_RetornaListaVacia()
    {
        var result = await _service.GetUsuariosByIdsAsync(Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsuariosByIdsAsync_IdsValidos_RetornaUsuariosCorrectos()
    {
        var u1 = await SeedUserAsync("user_a");
        var u2 = await SeedUserAsync("user_b");
        await SeedUserAsync("user_c");

        var result = await _service.GetUsuariosByIdsAsync(new[] { u1.Id, u2.Id });

        Assert.Equal(2, result.Count);
        Assert.Contains(result, u => u.UserName == "user_a");
        Assert.Contains(result, u => u.UserName == "user_b");
        Assert.DoesNotContain(result, u => u.UserName == "user_c");
    }

    // =========================================================================
    // GetUsuarioParaEdicionAsync
    // =========================================================================

    [Fact]
    public async Task GetUsuarioParaEdicionAsync_UsuarioExistente_RetornaDatos()
    {
        var user = await SeedUserAsync("editor1", nombre: "Juan", apellido: "Pérez");

        var result = await _service.GetUsuarioParaEdicionAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal("editor1", result!.UserName);
        Assert.Equal("Juan", result.Nombre);
        Assert.Equal("Pérez", result.Apellido);
        Assert.True(result.Activo);
    }

    [Fact]
    public async Task GetUsuarioParaEdicionAsync_IdInexistente_RetornaNull()
    {
        var result = await _service.GetUsuarioParaEdicionAsync("id-que-no-existe");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsuarioParaEdicionAsync_ConRoles_RetornaRolesCorrectos()
    {
        await SeedRoleAsync("Vendedor");
        var user = await SeedUserAsync("vendedor1");
        await _userManager.AddToRoleAsync(user, "Vendedor");

        var result = await _service.GetUsuarioParaEdicionAsync(user.Id);

        Assert.NotNull(result);
        Assert.Contains("Vendedor", result!.Roles);
    }

    // =========================================================================
    // ValidarUnicidadUsuarioAsync
    // =========================================================================

    [Fact]
    public async Task ValidarUnicidadUsuarioAsync_NombreYEmailLibres_RetornaListaVacia()
    {
        var user = await SeedUserAsync("existing");

        var errors = await _service.ValidarUnicidadUsuarioAsync(user.Id, "nuevo_nombre", "nuevo@test.com");

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidarUnicidadUsuarioAsync_UserNameDuplicado_RetornaError()
    {
        var user1 = await SeedUserAsync("duplicado");
        var user2 = await SeedUserAsync("otro_usuario");

        var errors = await _service.ValidarUnicidadUsuarioAsync(user2.Id, "duplicado", "unico@test.com");

        Assert.Single(errors);
        Assert.Equal("UserName", errors[0].Campo);
    }

    [Fact]
    public async Task ValidarUnicidadUsuarioAsync_EmailDuplicado_RetornaError()
    {
        var user1 = await SeedUserAsync("user_email1");
        var user2 = await SeedUserAsync("user_email2");

        // user1's email is user_email1@test.com
        var errors = await _service.ValidarUnicidadUsuarioAsync(user2.Id, "user_email2", "user_email1@test.com");

        Assert.Single(errors);
        Assert.Equal("Email", errors[0].Campo);
    }

    [Fact]
    public async Task ValidarUnicidadUsuarioAsync_MismoUsuarioMismosDatos_NoRetornaError()
    {
        var user = await SeedUserAsync("self_user");

        // Same userId — should not report conflict with itself
        var errors = await _service.ValidarUnicidadUsuarioAsync(user.Id, "self_user", "self_user@test.com");

        Assert.Empty(errors);
    }

    // =========================================================================
    // GetUsuarioSelectListAsync
    // =========================================================================

    [Fact]
    public async Task GetUsuarioSelectListAsync_SinUsuarios_RetornaListaVacia()
    {
        var result = await _service.GetUsuarioSelectListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsuarioSelectListAsync_ConUsuarios_RetornaOrdenados()
    {
        await SeedUserAsync("z_usuario");
        await SeedUserAsync("a_usuario");

        var result = await _service.GetUsuarioSelectListAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("a_usuario", result[0].UserName);
        Assert.Equal("z_usuario", result[1].UserName);
    }

    // =========================================================================
    // GetUsuariosPorRolAsync
    // =========================================================================

    [Fact]
    public async Task GetUsuariosPorRolAsync_RolSinUsuarios_RetornaListaVacia()
    {
        await SeedRoleAsync("RolVacio");

        var result = await _service.GetUsuariosPorRolAsync("RolVacio");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsuariosPorRolAsync_RolConUsuarios_RetornaCorrectamente()
    {
        await SeedRoleAsync("Supervisor");
        var u1 = await SeedUserAsync("sup_a");
        var u2 = await SeedUserAsync("sup_b");
        await SeedUserAsync("otro");
        await _userManager.AddToRoleAsync(u1, "Supervisor");
        await _userManager.AddToRoleAsync(u2, "Supervisor");

        var result = await _service.GetUsuariosPorRolAsync("Supervisor");

        Assert.Equal(2, result.Count);
        Assert.All(result, u => Assert.True(u.UserName == "sup_a" || u.UserName == "sup_b"));
    }
}
