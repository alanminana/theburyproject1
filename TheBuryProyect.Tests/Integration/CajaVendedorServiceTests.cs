using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stub de IUsuarioService — solo resuelve la lista de vendedores por rol.
// ---------------------------------------------------------------------------
file sealed class StubUsuarioServiceVend : IUsuarioService
{
    private readonly List<UsuarioSelectItem> _vendedores;
    public StubUsuarioServiceVend(List<UsuarioSelectItem> vendedores) => _vendedores = vendedores;

    public Task<List<UsuarioSelectItem>> GetUsuariosPorRolAsync(string roleName)
        => Task.FromResult(_vendedores);

    public Task<UsuarioUpdateResult> UpdateUsuarioAsync(UsuarioUpdateRequest request) => throw new NotImplementedException();
    public Task<UsuarioDashboardStats> GetDashboardStatsAsync() => throw new NotImplementedException();
    public Task<List<UsuarioResumen>> GetUsuariosConRolesAsync(bool incluirInactivos = false) => throw new NotImplementedException();
    public Task<List<SucursalOptionViewModel>> GetSucursalOptionsAsync() => throw new NotImplementedException();
    public Task<bool> ExistsSucursalActivaAsync(int sucursalId) => throw new NotImplementedException();
    public Task<Sucursal?> GetSucursalByIdAsync(int? sucursalId) => throw new NotImplementedException();
    public Task<UsuarioEdicionData?> GetUsuarioParaEdicionAsync(string userId) => throw new NotImplementedException();
    public Task<List<UsuarioValidacionError>> ValidarUnicidadUsuarioAsync(string userId, string userName, string email) => throw new NotImplementedException();
    public Task<List<UsuarioSelectItem>> GetUsuarioSelectListAsync() => throw new NotImplementedException();
    public Task<List<ApplicationUser>> GetUsuariosByIdsAsync(IEnumerable<string> ids) => throw new NotImplementedException();
}

/// <summary>
/// Tests de integración para CajaVendedorService (padrón de vendedores por caja).
/// Cubren alta, reemplazo (replace), limpieza, validación de rol y caja inexistente.
/// </summary>
public class CajaVendedorServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public CajaVendedorServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private CajaVendedorService CreateService(params string[] vendedoresValidos)
    {
        var stub = new StubUsuarioServiceVend(
            vendedoresValidos.Select(id => new UsuarioSelectItem(id, id)).ToList());
        return new CajaVendedorService(_context, stub, NullLogger<CajaVendedorService>.Instance);
    }

    private async Task<Caja> SeedCajaAsync()
    {
        var caja = new Caja
        {
            Codigo = "C-" + Guid.NewGuid().ToString("N")[..6],
            Nombre = "Caja test",
            Activa = true
        };
        _context.Cajas.Add(caja);
        await _context.SaveChangesAsync();
        return caja;
    }

    private async Task SeedUsersAsync(params string[] ids)
    {
        foreach (var id in ids)
        {
            _context.Users.Add(new ApplicationUser
            {
                Id = id,
                UserName = id,
                NormalizedUserName = id.ToUpperInvariant(),
                Email = $"{id}@test.com",
                NormalizedEmail = $"{id}@test.com".ToUpperInvariant(),
                RowVersion = new byte[8]
            });
        }
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task Asignar_AgregaVendedoresNuevos()
    {
        var caja = await SeedCajaAsync();
        await SeedUsersAsync("v1", "v2");
        var service = CreateService("v1", "v2");

        await service.AsignarVendedoresAsync(caja.Id, new[] { "v1", "v2" }, "admin");

        var asignados = await service.ObtenerVendedorIdsAsignadosAsync(caja.Id);
        Assert.Equal(new[] { "v1", "v2" }, asignados.OrderBy(x => x));
    }

    [Fact]
    public async Task Asignar_ReemplazaSeleccionAnterior()
    {
        var caja = await SeedCajaAsync();
        await SeedUsersAsync("v1", "v2", "v3");
        var service = CreateService("v1", "v2", "v3");

        await service.AsignarVendedoresAsync(caja.Id, new[] { "v1", "v2" }, "admin");
        // Reemplazo: queda solo v3
        await service.AsignarVendedoresAsync(caja.Id, new[] { "v3" }, "admin");

        var asignados = await service.ObtenerVendedorIdsAsignadosAsync(caja.Id);
        Assert.Equal(new[] { "v3" }, asignados);
        // No quedan filas físicas de v1/v2 (hard delete → índice único limpio)
        Assert.Equal(1, await _context.CajaVendedores.CountAsync(cv => cv.CajaId == caja.Id));
    }

    [Fact]
    public async Task Asignar_SeleccionVacia_LimpiaPadron()
    {
        var caja = await SeedCajaAsync();
        await SeedUsersAsync("v1");
        var service = CreateService("v1");

        await service.AsignarVendedoresAsync(caja.Id, new[] { "v1" }, "admin");
        await service.AsignarVendedoresAsync(caja.Id, Array.Empty<string>(), "admin");

        Assert.Empty(await service.ObtenerVendedorIdsAsignadosAsync(caja.Id));
    }

    [Fact]
    public async Task Asignar_UsuarioSinRolVendedor_Lanza()
    {
        var caja = await SeedCajaAsync();
        await SeedUsersAsync("v1", "intruso");
        var service = CreateService("v1"); // solo v1 es vendedor

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AsignarVendedoresAsync(caja.Id, new[] { "intruso" }, "admin"));
    }

    [Fact]
    public async Task Asignar_CajaInexistente_Lanza()
    {
        await SeedUsersAsync("v1");
        var service = CreateService("v1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AsignarVendedoresAsync(99999, new[] { "v1" }, "admin"));
    }

    [Fact]
    public async Task Asignar_IgnoraDuplicadosEnSeleccion()
    {
        var caja = await SeedCajaAsync();
        await SeedUsersAsync("v1");
        var service = CreateService("v1");

        await service.AsignarVendedoresAsync(caja.Id, new[] { "v1", "v1", "" }, "admin");

        Assert.Equal(new[] { "v1" }, await service.ObtenerVendedorIdsAsignadosAsync(caja.Id));
    }
}
