using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ClienteLookupService.
/// Cubren GetClientesSelectListAsync: base vacía, retorna activos ordenados,
/// excluye inactivos y soft-deleted, selectedId marca selected, limitarACliente
/// retorna solo ese cliente, limitarACliente con id inexistente retorna vacío.
/// Cubren GetClienteDisplayNameAsync: existe, no existe, cliente inactivo.
/// Cubren comportamiento de cache: segunda llamada usa cache, no llama DB dos veces.
/// </summary>
public class ClienteLookupServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ClienteLookupService _service;
    private readonly IMemoryCache _cache;
    private readonly DbContextOptions<AppDbContext> _options;

    public ClienteLookupServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(_options);
        _context.Database.EnsureCreated();

        _cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new DbContextFactoryLookup(_options);

        _service = new ClienteLookupService(factory, _cache);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync(
        string apellido = "Apellido", string nombre = "Nombre",
        bool activo = true, bool deleted = false)
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = nombre, Apellido = apellido,
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com",
            Activo = activo,
            IsDeleted = deleted
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    // -------------------------------------------------------------------------
    // GetClientesSelectListAsync — base vacía
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetClientesSelectList_BaseVacia_RetornaVacio()
    {
        var result = await _service.GetClientesSelectListAsync();

        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // GetClientesSelectListAsync — retorna activos y ordena
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetClientesSelectList_RetornaClientesActivos()
    {
        await SeedClienteAsync(apellido: "Zárate", nombre: "Pedro");
        await SeedClienteAsync(apellido: "Álvarez", nombre: "Ana");

        var result = await _service.GetClientesSelectListAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetClientesSelectList_OrdenadosPorApellidoNombre()
    {
        await SeedClienteAsync(apellido: "Zarate", nombre: "Pedro");
        await SeedClienteAsync(apellido: "Alvarez", nombre: "Ana");

        var result = await _service.GetClientesSelectListAsync();

        // Debe estar ordenado: Alvarez antes que Zarate (ASCII collation en SQLite)
        Assert.Contains("Alvarez", result[0].Text);
        Assert.Contains("Zarate", result[1].Text);
    }

    [Fact]
    public async Task GetClientesSelectList_ExcluyeInactivos()
    {
        await SeedClienteAsync(apellido: "Activo");
        await SeedClienteAsync(apellido: "Inactivo", activo: false);

        var result = await _service.GetClientesSelectListAsync();

        Assert.Single(result);
        Assert.Contains("Activo", result[0].Text);
    }

    [Fact]
    public async Task GetClientesSelectList_ExcluySoftDeleted()
    {
        await SeedClienteAsync(apellido: "Vivo");
        await SeedClienteAsync(apellido: "Eliminado", deleted: true);

        var result = await _service.GetClientesSelectListAsync();

        Assert.Single(result);
    }

    // -------------------------------------------------------------------------
    // GetClientesSelectListAsync — selectedId
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetClientesSelectList_ConSelectedId_MarcaSelected()
    {
        var c1 = await SeedClienteAsync(apellido: "Primero");
        var c2 = await SeedClienteAsync(apellido: "Segundo");

        var result = await _service.GetClientesSelectListAsync(selectedId: c2.Id);

        var selected = result.Where(i => i.Selected).ToList();
        Assert.Single(selected);
        Assert.Equal(c2.Id.ToString(), selected[0].Value);
    }

    // -------------------------------------------------------------------------
    // GetClientesSelectListAsync — limitarACliente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetClientesSelectList_LimitarACliente_RetornaSoloEse()
    {
        var c1 = await SeedClienteAsync(apellido: "Uno");
        await SeedClienteAsync(apellido: "Dos");

        var result = await _service.GetClientesSelectListAsync(selectedId: c1.Id, limitarACliente: true);

        Assert.Single(result);
        Assert.Equal(c1.Id.ToString(), result[0].Value);
        Assert.True(result[0].Selected);
    }

    [Fact]
    public async Task GetClientesSelectList_LimitarAClienteInexistente_RetornaVacio()
    {
        var result = await _service.GetClientesSelectListAsync(selectedId: 99999, limitarACliente: true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetClientesSelectList_LimitarAClienteSinSelectedId_RetornaLista()
    {
        // limitarACliente=true pero sin selectedId → debe devolver lista completa
        await SeedClienteAsync(apellido: "Test");

        var result = await _service.GetClientesSelectListAsync(limitarACliente: true);

        // Sin selectedId, limitarACliente no aplica — devuelve lista normal
        Assert.Single(result);
    }

    // -------------------------------------------------------------------------
    // GetClienteDisplayNameAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetClienteDisplayName_ClienteExiste_RetornaNombre()
    {
        var cliente = await SeedClienteAsync(apellido: "García", nombre: "Luis");

        var display = await _service.GetClienteDisplayNameAsync(cliente.Id);

        Assert.NotNull(display);
        Assert.Contains("García", display);
        Assert.Contains("Luis", display);
    }

    [Fact]
    public async Task GetClienteDisplayName_ClienteInexistente_RetornaNull()
    {
        var display = await _service.GetClienteDisplayNameAsync(99999);

        Assert.Null(display);
    }

    [Fact]
    public async Task GetClienteDisplayName_ClienteInactivo_RetornaNull()
    {
        var cliente = await SeedClienteAsync(activo: false);

        var display = await _service.GetClienteDisplayNameAsync(cliente.Id);

        Assert.Null(display);
    }

    // -------------------------------------------------------------------------
    // Cache — segunda llamada usa cache (verifica que Items no crece)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetClientesSelectList_SegundaLlamada_UsaCache()
    {
        await SeedClienteAsync(apellido: "CacheTest");

        var result1 = await _service.GetClientesSelectListAsync();
        // Agrego un cliente nuevo DESPUÉS de la primera llamada (debería no aparecer por cache)
        await SeedClienteAsync(apellido: "NuevoPostCache");

        var result2 = await _service.GetClientesSelectListAsync();

        // El cache debe devolver el resultado original (1 cliente, no 2)
        Assert.Single(result2);
    }
}

internal sealed class DbContextFactoryLookup : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    public DbContextFactoryLookup(DbContextOptions<AppDbContext> options) => _options = options;
    public AppDbContext CreateDbContext() => new AppDbContext(_options);
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new AppDbContext(_options));
}
