using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ClienteScoringService.RecalcularAsync:
/// verifican que los snapshots y el PuntajeCliente se calculan y persisten.
/// </summary>
[Trait("Category", "Scoring")]
public sealed class ClienteScoringServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ClienteScoringService _service;

    public ClienteScoringServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ClienteScoringService(_context, NullLogger<ClienteScoringService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Cliente> SeedClienteAsync(DateTime createdAt)
    {
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Scoring",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Telefono = "1122334455",
            Domicilio = "Calle Falsa 123",
            CreatedAt = createdAt
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    [Fact]
    public async Task RecalcularAsync_ClienteInexistente_DevuelveNull()
    {
        var resultado = await _service.RecalcularAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RecalcularAsync_BuenClienteAntiguoYActivo_PersisteSnapshotsYPuntaje()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-400)); // > 12 meses

        _context.Ventas.Add(new Venta
        {
            Numero = "V-1",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Facturada,
            FechaVenta = ahora.AddDays(-10) // dentro de 6 meses
        });

        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-1",
            Estado = EstadoCredito.Activo,
            Cuotas = new List<Cuota>
            {
                // Pagada antes del vencimiento => en término.
                new()
                {
                    Estado = EstadoCuota.Pagada,
                    FechaVencimiento = ahora.AddDays(-40),
                    FechaPago = ahora.AddDays(-45),
                    MontoPagado = 100m,
                    MontoTotal = 100m
                }
            }
        });

        await _context.SaveChangesAsync();

        var resultado = await _service.RecalcularAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(1, resultado!.Snapshot.CreditosEnTermino);
        Assert.Equal(0, resultado.Snapshot.CreditosConAtraso);
        Assert.Equal(1, resultado.Snapshot.CantidadComprasCliente);
        // base 0 + antigüedad 1 + actividad 1 + buen pagador 2 = 4
        Assert.Equal(4, resultado.Puntaje);

        var persistido = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(4, persistido.PuntajeCliente);
        Assert.Equal(1, persistido.CantidadComprasCliente);
        Assert.Equal(1, persistido.CreditosEnTermino);
        Assert.Equal(0, persistido.CreditosConAtraso);
        Assert.NotNull(persistido.UltimaVentaFecha);
        Assert.True(persistido.AntiguedadDias >= 399);
    }

    [Fact]
    public async Task RecalcularAsync_ClienteNuevoSinHistorial_NoLoMarcaBuenPagador()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora);

        var resultado = await _service.RecalcularAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(0, resultado!.Snapshot.CantidadComprasCliente);
        Assert.False(resultado.Snapshot.TieneHistorialCredito);
        Assert.False(resultado.Snapshot.PagaEnTermino);

        var persistido = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(0, persistido.CantidadComprasCliente);
        Assert.False(persistido.TieneHistorialCredito);
        Assert.False(persistido.PagaCreditosEnTermino);
    }

    [Fact]
    public async Task RecalcularAsync_ClienteConAtraso_PenalizaAlMinimo()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-10)); // nuevo, sin bonus antigüedad

        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-2",
            Estado = EstadoCredito.Activo,
            Cuotas = new List<Cuota>
            {
                new()
                {
                    Estado = EstadoCuota.Vencida,
                    FechaVencimiento = ahora.AddDays(-5),
                    MontoTotal = 100m
                }
            }
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.RecalcularAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(1, resultado!.Snapshot.CreditosConAtraso);
        Assert.Equal(0, resultado.Puntaje); // 0 - 2 = -2, clamp a 0
    }

    [Fact]
    public async Task ClienteNuevo_PersisteConPuntajeCero()
    {
        // Modelo 0–5: un cliente recién dado de alta (sin fijar PuntajeCliente)
        // debe quedar persistido en 0, no en 1 (default de BD = 0 + ValueGeneratedNever).
        var cliente = await SeedClienteAsync(DateTime.UtcNow);

        var persistido = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(0, persistido.PuntajeCliente);
    }

    [Fact]
    public async Task RecalcularAsync_ConfiguracionPersistida_FactoresApagados_DaPuntajeBase()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-400));

        _context.Ventas.Add(new Venta
        {
            Numero = "V-3",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Facturada,
            FechaVenta = ahora.AddDays(-1)
        });

        var config = ConfiguracionScoringCliente.CrearDefault();
        config.AntiguedadActiva = false;
        config.ActividadActiva = false;
        config.PagoEnTerminoActivo = false;
        config.SueldoActivo = false;
        config.PuntajeBase = 1;
        _context.ConfiguracionesScoringCliente.Add(config);
        await _context.SaveChangesAsync();

        var resultado = await _service.RecalcularAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(1, resultado!.Puntaje);
    }

    [Fact]
    public async Task RecalcularYAuditarAsync_ClienteInexistente_DevuelveNull()
    {
        var resultado = await _service.RecalcularYAuditarAsync(99999, origen: "RecalculoManual");

        Assert.Null(resultado);
    }

    [Fact]
    public async Task RecalcularYAuditarAsync_PuntajeCambia_RegistraHistorialConOrigenManual()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-400)); // > 12 meses, PuntajeCliente inicial = 0

        _context.Ventas.Add(new Venta
        {
            Numero = "V-MAN-1",
            ClienteId = cliente.Id,
            Estado = EstadoVenta.Facturada,
            FechaVenta = ahora.AddDays(-10)
        });

        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-MAN-1",
            Estado = EstadoCredito.Activo,
            Cuotas = new List<Cuota>
            {
                new()
                {
                    Estado = EstadoCuota.Pagada,
                    FechaVencimiento = ahora.AddDays(-40),
                    FechaPago = ahora.AddDays(-45),
                    MontoPagado = 100m,
                    MontoTotal = 100m
                }
            }
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.RecalcularYAuditarAsync(
            cliente.Id,
            origen: "RecalculoManual",
            observacion: "Recálculo manual desde ficha de cliente",
            registradoPor: "admin");

        Assert.NotNull(resultado);
        Assert.NotEqual(0, resultado!.Puntaje); // precondición: debe haber cambio real de puntaje

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking()
            .Where(h => h.ClienteId == cliente.Id)
            .ToListAsync();

        var registro = Assert.Single(historial);
        Assert.Equal(resultado.Puntaje, registro.Puntaje);
        Assert.Equal("RecalculoManual", registro.Origen);
        Assert.Equal("admin", registro.RegistradoPor);
        Assert.Equal("Recálculo manual desde ficha de cliente", registro.Observacion);
    }

    [Fact]
    public async Task RecalcularYAuditarAsync_PuntajeNoCambia_NoRegistraHistorial()
    {
        // Cliente recién creado sin ventas ni créditos: recalcular mantiene PuntajeCliente en 0.
        var cliente = await SeedClienteAsync(DateTime.UtcNow);

        var resultado = await _service.RecalcularYAuditarAsync(
            cliente.Id,
            origen: "RecalculoManual",
            registradoPor: "admin");

        Assert.NotNull(resultado);
        Assert.Equal(0, resultado!.Puntaje);

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking()
            .Where(h => h.ClienteId == cliente.Id)
            .ToListAsync();

        Assert.Empty(historial);
    }
}
