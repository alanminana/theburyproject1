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
        // base 1 + antigüedad 1 + actividad 1 + buen pagador 2 = 5 (tope)
        Assert.Equal(5, resultado.Puntaje);

        var persistido = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(5, persistido.PuntajeCliente);
        Assert.Equal(1, persistido.CreditosEnTermino);
        Assert.Equal(0, persistido.CreditosConAtraso);
        Assert.NotNull(persistido.UltimaVentaFecha);
        Assert.True(persistido.AntiguedadDias >= 399);
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
        Assert.Equal(1, resultado.Puntaje); // 1 - 2 = -1, clamp a 1
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
}
