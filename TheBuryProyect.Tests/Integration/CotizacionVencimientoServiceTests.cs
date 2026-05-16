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

public sealed class CotizacionVencimientoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CotizacionService _service;

    private static readonly DateTime Referencia = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    public CotizacionVencimientoServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new CotizacionService(_context, new StubCalculator(), NullLogger<CotizacionService>.Instance);
    }

    // ── TRANSICIONES CORRECTAS ────────────────────────────────────────────

    [Fact]
    public async Task VencerEmitidasAsync_CotizacionEmitidaVencida_CambiaAVencida()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(-1));
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerEmitidasAsync(Referencia, "sistema");

        Assert.True(resultado.Exitoso);
        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Vencida, entity!.Estado);
    }

    [Fact]
    public async Task VencerEmitidasAsync_CotizacionEmitidaNoVencida_NoCambia()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(1));
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerEmitidasAsync(Referencia, "sistema");

        Assert.True(resultado.Exitoso);
        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Emitida, entity!.Estado);
    }

    // ── ESTADOS NO VENCIBLES ──────────────────────────────────────────────

    [Fact]
    public async Task VencerEmitidasAsync_CotizacionCanceladaVencida_NoCambia()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(-1));
        cotizacion.Estado = EstadoCotizacion.Cancelada;
        cotizacion.MotivoCancelacion = "Motivo original";
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        await _service.VencerEmitidasAsync(Referencia, "sistema");

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Cancelada, entity!.Estado);
    }

    [Fact]
    public async Task VencerEmitidasAsync_CotizacionConvertidaVencida_NoCambia()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(-1));
        cotizacion.Estado = EstadoCotizacion.ConvertidaAVenta;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        await _service.VencerEmitidasAsync(Referencia, "sistema");

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.ConvertidaAVenta, entity!.Estado);
    }

    [Fact]
    public async Task VencerEmitidasAsync_CotizacionBorradorVencida_NoCambia()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(-1));
        cotizacion.Estado = EstadoCotizacion.Borrador;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        await _service.VencerEmitidasAsync(Referencia, "sistema");

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Borrador, entity!.Estado);
    }

    [Fact]
    public async Task VencerEmitidasAsync_CotizacionYaVencida_NoCambia()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(-1));
        cotizacion.Estado = EstadoCotizacion.Vencida;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerEmitidasAsync(Referencia, "sistema");

        Assert.Equal(0, resultado.CantidadVencidas);
        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Vencida, entity!.Estado);
    }

    [Fact]
    public async Task VencerEmitidasAsync_SinFechaVencimiento_NoCambia()
    {
        var cotizacion = CotizacionSinFecha();
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerEmitidasAsync(Referencia, "sistema");

        Assert.Equal(0, resultado.CantidadVencidas);
        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Emitida, entity!.Estado);
    }

    // ── RESULTADO Y CONTEO ────────────────────────────────────────────────

    [Fact]
    public async Task VencerEmitidasAsync_DevuelveCantidadVencidas()
    {
        _context.Cotizaciones.AddRange(
            CotizacionEmitidaConFecha(Referencia.AddDays(-2)),
            CotizacionEmitidaConFecha(Referencia.AddDays(-1)),
            CotizacionEmitidaConFecha(Referencia.AddDays(1))  // no vence
        );
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerEmitidasAsync(Referencia, "sistema");

        Assert.True(resultado.Exitoso);
        Assert.Equal(2, resultado.CantidadVencidas);
        Assert.Equal(2, resultado.CotizacionesVencidasIds.Count);
    }

    [Fact]
    public async Task VencerEmitidasAsync_DevuelveCantidadEvaluadas_SoloEmitidas()
    {
        _context.Cotizaciones.AddRange(
            CotizacionEmitidaConFecha(Referencia.AddDays(-1)),    // emitida vencida → vence
            CotizacionEmitidaConFecha(Referencia.AddDays(-1)),    // emitida vencida → vence
            CotizacionCanceladaConFecha(Referencia.AddDays(-1))   // cancelada → no se evalúa
        );
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerEmitidasAsync(Referencia, "sistema");

        Assert.Equal(2, resultado.CantidadEvaluadas);
        Assert.Equal(2, resultado.CantidadVencidas);
    }

    // ── INVARIANTES ───────────────────────────────────────────────────────

    [Fact]
    public async Task VencerEmitidasAsync_NoTocaMotivoCancelacion()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(-1));
        cotizacion.MotivoCancelacion = null;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        await _service.VencerEmitidasAsync(Referencia, "sistema");

        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Null(entity!.MotivoCancelacion);
    }

    [Fact]
    public async Task VencerEmitidasAsync_RespetaIsDeleted()
    {
        var cotizacion = CotizacionEmitidaConFecha(Referencia.AddDays(-1));
        cotizacion.IsDeleted = true;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.VencerEmitidasAsync(Referencia, "sistema");

        Assert.Equal(0, resultado.CantidadVencidas);
        var entity = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Emitida, entity!.Estado);
    }

    // ── HELPERS ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static Cotizacion CotizacionEmitidaConFecha(DateTime fechaVencimiento) => new()
    {
        Numero = $"COT-{Guid.NewGuid():N}",
        Fecha = DateTime.UtcNow,
        Estado = EstadoCotizacion.Emitida,
        Subtotal = 100m,
        DescuentoTotal = 0m,
        TotalBase = 100m,
        FechaVencimiento = fechaVencimiento
    };

    private static Cotizacion CotizacionSinFecha() => new()
    {
        Numero = $"COT-{Guid.NewGuid():N}",
        Fecha = DateTime.UtcNow,
        Estado = EstadoCotizacion.Emitida,
        Subtotal = 100m,
        DescuentoTotal = 0m,
        TotalBase = 100m,
        FechaVencimiento = null
    };

    private static Cotizacion CotizacionCanceladaConFecha(DateTime fechaVencimiento) => new()
    {
        Numero = $"COT-{Guid.NewGuid():N}",
        Fecha = DateTime.UtcNow,
        Estado = EstadoCotizacion.Cancelada,
        Subtotal = 100m,
        DescuentoTotal = 0m,
        TotalBase = 100m,
        FechaVencimiento = fechaVencimiento,
        MotivoCancelacion = "Cancelada previamente"
    };

    private sealed class StubCalculator : ICotizacionPagoCalculator
    {
        public Task<CotizacionSimulacionResultado> SimularAsync(
            CotizacionSimulacionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionSimulacionResultado { Exitoso = true });
    }
}
