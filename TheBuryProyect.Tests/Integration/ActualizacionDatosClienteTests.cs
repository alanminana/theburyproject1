using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Aviso periódico "actualizar datos del cliente": regla de vencimiento, configuración global
/// y sellado de la fecha al crear/editar un cliente.
/// </summary>
public class ActualizacionDatosClienteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ConfiguracionActualizacionDatosClienteService _config;
    private readonly ClienteService _clientes;

    public ActualizacionDatosClienteTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _config = new ConfiguracionActualizacionDatosClienteService(_context);
        _clientes = new ClienteService(_context, NullLogger<ClienteService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static Cliente NuevoCliente() => new()
    {
        Nombre = "Ana",
        Apellido = "Gomez",
        TipoDocumento = "DNI",
        NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
        Telefono = "555",
        Domicilio = "Calle 1",
        Activo = true
    };

    [Fact]
    public void Regla_ApagadaPorDefecto_NuncaRequiere()
    {
        var cfg = ConfiguracionActualizacionDatosCliente.CrearDefault();

        Assert.False(cfg.Activa);
        Assert.False(cfg.RequiereActualizacion(null, DateTime.UtcNow.AddDays(-2000), DateTime.UtcNow, out _));
    }

    [Theory]
    [InlineData(179, false)]
    [InlineData(180, true)]
    [InlineData(400, true)]
    public void Regla_ActivaVenceAlCumplirLosDias(int diasDesdeActualizacion, bool esperado)
    {
        var ahora = DateTime.UtcNow;
        var cfg = new ConfiguracionActualizacionDatosCliente { Activa = true, DiasRevision = 180 };

        var requiere = cfg.RequiereActualizacion(ahora.AddDays(-diasDesdeActualizacion), ahora.AddDays(-1000), ahora, out var dias);

        Assert.Equal(esperado, requiere);
        Assert.Equal(diasDesdeActualizacion, dias);
    }

    [Fact]
    public void Regla_SinFechaDeActualizacion_UsaLaFechaDeAlta()
    {
        var ahora = DateTime.UtcNow;
        var cfg = new ConfiguracionActualizacionDatosCliente { Activa = true, DiasRevision = 30 };

        Assert.True(cfg.RequiereActualizacion(null, ahora.AddDays(-31), ahora, out _));
        Assert.False(cfg.RequiereActualizacion(null, ahora.AddDays(-5), ahora, out _));
    }

    [Fact]
    public async Task Configuracion_SinFilaDevuelveDefaultYGuardaUnaUnicaFila()
    {
        var inicial = await _config.GetConfiguracionAsync();
        Assert.False(inicial.Activa);
        Assert.Equal(180, inicial.DiasRevision);

        await _config.SaveConfiguracionAsync(true, 90);
        await _config.SaveConfiguracionAsync(true, 60);

        var guardada = await _config.GetConfiguracionAsync();
        Assert.True(guardada.Activa);
        Assert.Equal(60, guardada.DiasRevision);
        Assert.Equal(1, await _context.ConfiguracionesActualizacionDatosCliente.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public async Task Configuracion_DiasFueraDeRango_Rechaza(int dias)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _config.SaveConfiguracionAsync(true, dias));
        Assert.Equal(0, await _context.ConfiguracionesActualizacionDatosCliente.CountAsync());
    }

    [Fact]
    public async Task Create_SellaLaFechaDeActualizacion()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);

        var creado = await _clientes.CreateAsync(NuevoCliente());

        Assert.NotNull(creado.FechaUltimaActualizacionDatos);
        Assert.True(creado.FechaUltimaActualizacionDatos >= antes);
    }

    [Fact]
    public async Task Update_ReiniciaElContadorDeActualizacion()
    {
        var cliente = await _clientes.CreateAsync(NuevoCliente());
        cliente.FechaUltimaActualizacionDatos = DateTime.UtcNow.AddDays(-400);
        await _context.SaveChangesAsync();
        await _config.SaveConfiguracionAsync(true, 180);
        Assert.True((await _config.EvaluarClienteAsync(cliente.Id)).Requiere);

        cliente.Telefono = "999";
        await _clientes.UpdateAsync(cliente);

        var estado = await _config.EvaluarClienteAsync(cliente.Id);
        Assert.False(estado.Requiere);
    }

    [Fact]
    public async Task Evaluar_ClienteInactivoOEliminado_NoRequiere()
    {
        await _config.SaveConfiguracionAsync(true, 1);
        var cliente = await _clientes.CreateAsync(NuevoCliente());
        cliente.FechaUltimaActualizacionDatos = DateTime.UtcNow.AddDays(-50);
        cliente.Activo = false;
        await _context.SaveChangesAsync();

        Assert.False((await _config.EvaluarClienteAsync(cliente.Id)).Requiere);
        Assert.False((await _config.EvaluarClienteAsync(99999)).Requiere);
    }
}
