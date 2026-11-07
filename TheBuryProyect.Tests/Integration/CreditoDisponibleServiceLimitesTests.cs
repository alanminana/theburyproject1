using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

public class CreditoDisponibleServiceLimitesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoDisponibleService _service;

    public CreditoDisponibleServiceLimitesTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new CreditoDisponibleService(_context, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.CreditoDisponibleService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)>
        ItemsCompletos(decimal monto = 10_000m) =>
        Enumerable.Range(0, 6)
            .Select(p => (p, monto, true))
            .ToList()
            .AsReadOnly();

    // -------------------------------------------------------------------------
    // 1. Guarda todos los puntajes correctamente (INSERT desde cero)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_LimiteNoCrecienteONegativo_SeRechazaSinGuardar()
    {
        // Puntaje 2 con un limite menor que el del puntaje 1: un puntaje mayor no puede tener menos credito.
        var noCreciente = new[] { (0, 0m, true), (1, 100_000m, true), (2, 50_000m, true), (3, 300_000m, true), (4, 400_000m, true), (5, 500_000m, true) }
            .ToList().AsReadOnly();
        var (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(noCreciente, "admin");
        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("puntaje 2") && e.Contains("puntaje 1"));
        Assert.DoesNotContain(await _context.PuntajesCreditoLimite.ToListAsync(), p => p.LimiteMonto is 50_000m or 100_000m);   // nada se guardo

        var negativo = new[] { (0, 0m, true), (1, -1m, true), (2, 0m, true), (3, 0m, true), (4, 0m, true), (5, 0m, true) }
            .ToList().AsReadOnly();
        (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(negativo, "admin");
        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("negativos"));

        // Iguales o crecientes: validos.
        var valido = new[] { (0, 0m, true), (1, 100_000m, true), (2, 100_000m, true), (3, 300_000m, true), (4, 400_000m, true), (5, 500_000m, true) }
            .ToList().AsReadOnly();
        (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(valido, "admin");
        Assert.True(ok);
        Assert.Empty(errores);
    }

    [Fact]
    public async Task GuardarLimitesPorPuntaje_InsertaDesde0_TodosLosPuntajes()
    {
        var items = ItemsCompletos(5_000m);

        var (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(items, "admin");

        Assert.True(ok);
        Assert.Empty(errores);

        var guardados = await _context.PuntajesCreditoLimite.ToListAsync();
        Assert.Equal(6, guardados.Count);
        Assert.All(guardados, g => Assert.Equal(5_000m, g.LimiteMonto));
    }

    // -------------------------------------------------------------------------
    // 2. Falla si falta un puntaje del enum
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_FaltaUnPuntaje_RetornaError()
    {
        var todosLosPuntajes = Enumerable.Range(0, 6).ToList();
        // Omitir el primer puntaje
        var itemsIncompletos = todosLosPuntajes
            .Skip(1)
            .Select(p => (p, 10_000m, true))
            .ToList()
            .AsReadOnly();

        var (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(itemsIncompletos, "admin");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("exactamente los puntajes"));
    }

    // -------------------------------------------------------------------------
    // 3. Falla si hay duplicados
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_PuntajesDuplicados_RetornaError()
    {
        var puntajeRepetido = 1;
        var lista = Enumerable.Range(0, 6)
            .Select(p => (p, 10_000m, true))
            .ToList();
        lista.Add((puntajeRepetido, 20_000m, true));  // duplicado explícito

        var items = lista.AsReadOnly();

        var (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(items, "admin");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("duplicados"));
    }

    // -------------------------------------------------------------------------
    // 4. Falla si hay decimales en LimiteMonto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_LimiteConDecimales_RetornaError()
    {
        var items = Enumerable.Range(0, 6)
            .Select(p => (p, 10_000.50m, true))   // con decimales
            .ToList()
            .AsReadOnly();

        var (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(items, "admin");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("numeros enteros"));
    }

    // -------------------------------------------------------------------------
    // 5. Actualiza registros existentes — no duplica filas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_Upsert_NoCreaDuplicados()
    {
        // Primera carga
        await _service.GuardarLimitesPorPuntajeAsync(ItemsCompletos(5_000m), "admin");

        // Segunda carga con montos distintos
        await _service.GuardarLimitesPorPuntajeAsync(ItemsCompletos(15_000m), "admin");

        var guardados = await _context.PuntajesCreditoLimite.ToListAsync();
        var cantEsperada = 6;

        Assert.Equal(cantEsperada, guardados.Count);  // sin duplicados
        Assert.All(guardados, g => Assert.Equal(15_000m, g.LimiteMonto));  // actualizados
    }

    // -------------------------------------------------------------------------
    // 6a. Auditoría — FechaActualizacion seteada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_FechaActualizacion_EsSetada()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);

        await _service.GuardarLimitesPorPuntajeAsync(ItemsCompletos(), "admin");

        var guardados = await _context.PuntajesCreditoLimite.ToListAsync();
        Assert.All(guardados, g => Assert.True(g.FechaActualizacion >= antes));
    }

    // -------------------------------------------------------------------------
    // 6b. Auditoría — UsuarioActualizacion correcto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_UsuarioActualizacion_EsElUsuarioRecibido()
    {
        const string usuarioEsperado = "test_user@empresa.com";

        await _service.GuardarLimitesPorPuntajeAsync(ItemsCompletos(), usuarioEsperado);

        var guardados = await _context.PuntajesCreditoLimite.ToListAsync();
        Assert.All(guardados, g => Assert.Equal(usuarioEsperado, g.UsuarioActualizacion));
    }

    // -------------------------------------------------------------------------
    // Extra: lista vacía retorna error sin persistir nada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarLimitesPorPuntaje_ListaVacia_RetornaErrorSinPersistir()
    {
        var countAntes = await _context.PuntajesCreditoLimite.CountAsync();

        var items = Array.Empty<(int, decimal, bool)>()
            .ToList().AsReadOnly();

        var (ok, errores) = await _service.GuardarLimitesPorPuntajeAsync(items, "admin");

        Assert.False(ok);
        Assert.Contains(errores, e => e.Contains("registros"));

        var countDespues = await _context.PuntajesCreditoLimite.CountAsync();
        Assert.Equal(countAntes, countDespues);
    }
}
