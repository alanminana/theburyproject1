using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para PrecioService — gestión de ListasPrecio.
/// Cubren GetAllListasAsync, GetListaPredeterminadaAsync, CreateListaAsync
/// (solo una predeterminada, otras pierden el flag), UpdateListaAsync
/// (no existe, RowVersion vacío, cambio a predeterminada), DeleteListaAsync.
/// </summary>
public class PrecioServiceListaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioService _service;

    public PrecioServiceListaTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder().Build();
        _service = new PrecioService(_context, NullLogger<PrecioService>.Instance,
            new StubCurrentUserServicePrecio(), config);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<ListaPrecio> SeedListaAsync(
        string? nombre = null,
        bool activa = true,
        bool esPredeterminada = false)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var lista = new ListaPrecio
        {
            Nombre = nombre ?? "Lista-" + codigo,
            Codigo = codigo,
            Tipo = TipoListaPrecio.Contado,
            Activa = activa,
            EsPredeterminada = esPredeterminada,
            Orden = 1
        };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        await _context.Entry(lista).ReloadAsync();
        return lista;
    }

    // -------------------------------------------------------------------------
    // GetAllListasAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAllListas_SoloActivas_ExcluyeInactivas()
    {
        await SeedListaAsync(activa: true);
        await SeedListaAsync(activa: false);

        var resultado = await _service.GetAllListasAsync(soloActivas: true);

        Assert.DoesNotContain(resultado, l => !l.Activa);
    }

    [Fact]
    public async Task GetAllListas_TodasIncluidas_IncluirInactivas()
    {
        await SeedListaAsync(activa: true);
        await SeedListaAsync(activa: false);

        var resultado = await _service.GetAllListasAsync(soloActivas: false);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task GetAllListas_EliminadasExcluidas()
    {
        var lista = await SeedListaAsync();
        lista.IsDeleted = true;
        await _context.SaveChangesAsync();

        var resultado = await _service.GetAllListasAsync(soloActivas: false);

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // GetListaPredeterminadaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetListaPredeterminada_SinPredeterminada_RetornaNull()
    {
        await SeedListaAsync(esPredeterminada: false);

        var resultado = await _service.GetListaPredeterminadaAsync();

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetListaPredeterminada_ConPredeterminada_RetornaLista()
    {
        var lista = await SeedListaAsync(esPredeterminada: true);

        var resultado = await _service.GetListaPredeterminadaAsync();

        Assert.NotNull(resultado);
        Assert.Equal(lista.Id, resultado!.Id);
    }

    // -------------------------------------------------------------------------
    // CreateListaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_DatosValidos_Persiste()
    {
        var lista = new ListaPrecio
        {
            Nombre = "Contado", Codigo = "CONT-001",
            Tipo = TipoListaPrecio.Contado, Activa = true, Orden = 1
        };

        var resultado = await _service.CreateListaAsync(lista);

        Assert.True(resultado.Id > 0);
        var bd = await _context.ListasPrecios.FirstOrDefaultAsync(l => l.Id == resultado.Id);
        Assert.NotNull(bd);
        Assert.Equal("Contado", bd!.Nombre);
    }

    [Fact]
    public async Task Create_NuevaPredeterminada_OtrasPierdenElFlag()
    {
        var existente = await SeedListaAsync(esPredeterminada: true);

        var nueva = new ListaPrecio
        {
            Nombre = "Nueva Lista", Codigo = Guid.NewGuid().ToString("N")[..8],
            Tipo = TipoListaPrecio.Tarjeta,
            Activa = true, EsPredeterminada = true, Orden = 2
        };

        await _service.CreateListaAsync(nueva);

        _context.ChangeTracker.Clear();
        var existenteBd = await _context.ListasPrecios.FirstAsync(l => l.Id == existente.Id);
        Assert.False(existenteBd.EsPredeterminada);
    }

    [Fact]
    public async Task Create_NoPredeterminada_NoAfectaOtrasPredeterminadas()
    {
        var existente = await SeedListaAsync(esPredeterminada: true);

        var nueva = new ListaPrecio
        {
            Nombre = "No Predeterminada", Codigo = Guid.NewGuid().ToString("N")[..8],
            Tipo = TipoListaPrecio.Mayorista,
            Activa = true, EsPredeterminada = false, Orden = 3
        };

        await _service.CreateListaAsync(nueva);

        _context.ChangeTracker.Clear();
        var existenteBd = await _context.ListasPrecios.FirstAsync(l => l.Id == existente.Id);
        Assert.True(existenteBd.EsPredeterminada); // no debe cambiar
    }

    // -------------------------------------------------------------------------
    // UpdateListaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Update_CamposValidos_Actualiza()
    {
        var lista = await SeedListaAsync("Nombre Original");

        lista.Nombre = "Nombre Nuevo";
        var resultado = await _service.UpdateListaAsync(lista, lista.RowVersion!);

        Assert.Equal("Nombre Nuevo", resultado.Nombre);
    }

    [Fact]
    public async Task Update_NoExiste_LanzaExcepcion()
    {
        var lista = new ListaPrecio { Id = 99999, Nombre = "X", Codigo = "X", Tipo = TipoListaPrecio.Contado };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateListaAsync(lista, new byte[8]));
    }

    [Fact]
    public async Task Update_RowVersionVacio_LanzaExcepcion()
    {
        var lista = await SeedListaAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateListaAsync(lista, Array.Empty<byte>()));
    }

    [Fact]
    public async Task Create_NuevaPredeterminada_SoloUnaPredeterminadaExiste()
    {
        await SeedListaAsync(esPredeterminada: true);
        await SeedListaAsync(esPredeterminada: false);

        var nueva = new ListaPrecio
        {
            Nombre = "Nueva Predeterminada", Codigo = Guid.NewGuid().ToString("N")[..8],
            Tipo = TipoListaPrecio.Tarjeta, Activa = true, EsPredeterminada = true, Orden = 2
        };
        await _service.CreateListaAsync(nueva);

        _context.ChangeTracker.Clear();
        var predeterminadas = await _context.ListasPrecios.CountAsync(l => l.EsPredeterminada && !l.IsDeleted);
        Assert.Equal(1, predeterminadas);
    }

    // -------------------------------------------------------------------------
    // DeleteListaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_Existente_SoftDelete()
    {
        var lista = await SeedListaAsync();

        var resultado = await _service.DeleteListaAsync(lista.Id, lista.RowVersion!);

        Assert.True(resultado);
        var bd = await _context.ListasPrecios.IgnoreQueryFilters().FirstAsync(l => l.Id == lista.Id);
        Assert.True(bd.IsDeleted);
        Assert.False(bd.Activa);
    }

    [Fact]
    public async Task Delete_NoExiste_RetornaFalse()
    {
        var resultado = await _service.DeleteListaAsync(99999, new byte[8]);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Delete_RowVersionVacio_LanzaExcepcion()
    {
        var lista = await SeedListaAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeleteListaAsync(lista.Id, Array.Empty<byte>()));
    }
}

file sealed class StubCurrentUserServicePrecio : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}
