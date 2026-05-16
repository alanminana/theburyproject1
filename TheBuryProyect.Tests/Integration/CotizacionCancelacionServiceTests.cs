using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

public sealed class CotizacionCancelacionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CotizacionService _service;

    public CotizacionCancelacionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var stub = new StubCalculator();
        _service = new CotizacionService(_context, stub, NullLogger<CotizacionService>.Instance);
    }

    // ── CASOS EXITOSOS ────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_CotizacionEmitida_ExitosaConMotivo()
    {
        var cotizacion = CotizacionEmitida();
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = "Cliente desistió" },
            "operador1");

        Assert.True(resultado.Exitoso);
        Assert.Equal("Cliente desistió", resultado.MotivoCancelacion);

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Cancelada, entity!.Estado);
        Assert.Equal("Cliente desistió", entity.MotivoCancelacion);
    }

    [Fact]
    public async Task Cancelar_CotizacionVencida_ExitosaConMotivo()
    {
        var cotizacion = CotizacionEmitida();
        cotizacion.Estado = EstadoCotizacion.Vencida;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = "Vencida sin respuesta del cliente" },
            "admin");

        Assert.True(resultado.Exitoso);

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Cancelada, entity!.Estado);
    }

    [Fact]
    public async Task Cancelar_MotivoLargo_TruncaA500()
    {
        var cotizacion = CotizacionEmitida();
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var motivoLargo = new string('x', 600);

        var resultado = await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = motivoLargo },
            "admin");

        Assert.True(resultado.Exitoso);
        Assert.Equal(500, resultado.MotivoCancelacion!.Length);
    }

    // ── CASOS DE ERROR ────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_SinMotivo_DevuelveError()
    {
        var cotizacion = CotizacionEmitida();
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = "   " },
            "admin");

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("motivo"));

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Emitida, entity!.Estado);
    }

    [Fact]
    public async Task Cancelar_YaCancelada_DevuelveError()
    {
        var cotizacion = CotizacionEmitida();
        cotizacion.Estado = EstadoCotizacion.Cancelada;
        cotizacion.MotivoCancelacion = "Motivo original";
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = "Intentar cancelar de nuevo" },
            "admin");

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("cancelada"));

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal("Motivo original", entity!.MotivoCancelacion);
    }

    [Fact]
    public async Task Cancelar_ConvertidaAVenta_DevuelveError()
    {
        var cotizacion = CotizacionEmitida();
        cotizacion.Estado = EstadoCotizacion.ConvertidaAVenta;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = "Quiero cancelar la venta" },
            "admin");

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("convertida"));
    }

    [Fact]
    public async Task Cancelar_Borrador_DevuelveError()
    {
        var cotizacion = CotizacionEmitida();
        cotizacion.Estado = EstadoCotizacion.Borrador;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = "No aplica" },
            "admin");

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("borrador") || e.Contains("Borrador"));
    }

    [Fact]
    public async Task Cancelar_NoExiste_DevuelveError()
    {
        var resultado = await _service.CancelarAsync(
            9999,
            new CotizacionCancelacionRequest { Motivo = "No existe" },
            "admin");

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("9999"));
    }

    // ── TRAZABILIDAD ──────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerCotizacion_TrasCancelacion_ExposeMotivoCancelacion()
    {
        var cotizacion = CotizacionEmitida();
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        await _service.CancelarAsync(
            cotizacion.Id,
            new CotizacionCancelacionRequest { Motivo = "Cambio de decisión" },
            "vendedor");

        var detalle = await _service.ObtenerAsync(cotizacion.Id);

        Assert.NotNull(detalle);
        Assert.Equal(EstadoCotizacion.Cancelada, detalle.Estado);
        Assert.Equal("Cambio de decisión", detalle.MotivoCancelacion);
    }

    // ── HELPERS ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static Cotizacion CotizacionEmitida() => new()
    {
        Numero = $"COT-{Guid.NewGuid():N}",
        Fecha = DateTime.UtcNow,
        Estado = EstadoCotizacion.Emitida,
        Subtotal = 100m,
        DescuentoTotal = 0m,
        TotalBase = 100m
    };

    private sealed class StubCalculator : ICotizacionPagoCalculator
    {
        public Task<CotizacionSimulacionResultado> SimularAsync(
            CotizacionSimulacionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionSimulacionResultado { Exitoso = true });
    }
}
