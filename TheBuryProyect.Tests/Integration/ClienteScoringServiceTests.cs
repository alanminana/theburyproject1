using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

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

    // -----------------------------------------------------------------------
    // PUN-ML10-E: capital saldado + punitorio pendiente no contamina el score
    // persistido del cliente, e idempotencia de recálculos consecutivos.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RecalcularAsync_CapitalSaldadoConPunitorioPendiente_NoCuentaComoAtraso()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-10));

        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-PUN-1",
            Estado = EstadoCredito.Activo,
            Cuotas = new List<Cuota>
            {
                // Estado=Parcial + MontoPagado==MontoTotal: capital saldado, sólo queda pendiente
                // un punitorio aplicado (contrato EstadoCuotaResolver.Resolver).
                new()
                {
                    Estado = EstadoCuota.Parcial,
                    FechaVencimiento = ahora.AddDays(-5),
                    MontoPagado = 100m,
                    MontoTotal = 100m
                }
            }
        });
        await _context.SaveChangesAsync();

        var resultado = await _service.RecalcularAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(0, resultado!.Snapshot.CreditosConAtraso);

        var persistido = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(0, persistido.CreditosConAtraso);
    }

    [Fact]
    public async Task DosRecalculosIguales_SonIdempotentes()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-400));
        // Puntaje inicial distinto del que resultará del recálculo, para que el primer
        // recálculo sí produzca un cambio real (y así poder demostrar que el segundo no duplica).
        cliente.PuntajeCliente = 3;
        await _context.SaveChangesAsync();

        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-IDEMP-1",
            Estado = EstadoCredito.Activo,
            Cuotas = new List<Cuota>
            {
                new()
                {
                    Estado = EstadoCuota.Vencida,
                    FechaVencimiento = ahora.AddDays(-5),
                    MontoPagado = 0m,
                    MontoTotal = 100m
                }
            }
        });
        await _context.SaveChangesAsync();

        var primero = await _service.RecalcularYAuditarAsync(cliente.Id, origen: "RecalculoManual");
        var segundo = await _service.RecalcularYAuditarAsync(cliente.Id, origen: "RecalculoManual");

        Assert.NotNull(primero);
        Assert.NotNull(segundo);
        Assert.Equal(primero!.Puntaje, segundo!.Puntaje);
        Assert.Equal(primero.Snapshot.CreditosConAtraso, segundo.Snapshot.CreditosConAtraso);

        // El segundo recálculo no cambia el puntaje respecto del primero => no duplica historial.
        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking()
            .Where(h => h.ClienteId == cliente.Id)
            .ToListAsync();

        Assert.Single(historial); // sólo el cambio inicial (puntaje base -> penalizado), no 2.
    }

    // -----------------------------------------------------------------------
    // PUN-ML10-G: recálculo global de scoring (mecanismo de mantenimiento).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RecalcularTodosAsync_CeroClientes_DevuelveResumenVacio()
    {
        var resultado = await _service.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones { Origen = "Test" });

        Assert.Equal(0, resultado.Examinados);
        Assert.Equal(0, resultado.Recalculados);
        Assert.Equal(0, resultado.SinCambios);
        Assert.Equal(0, resultado.Fallidos);
        Assert.Empty(resultado.IdsFallidos);
        Assert.False(resultado.Preview);
        Assert.False(resultado.Interrumpido);
    }

    [Fact]
    public async Task RecalcularTodosAsync_UnCliente_LoRecalculaYAuditaConElOrigenIndicado()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-400));
        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-G1",
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

        var resultado = await _service.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones { Origen = "RecalculoGlobalTest" });

        Assert.Equal(1, resultado.Examinados);
        Assert.Equal(1, resultado.Recalculados);
        Assert.Equal(0, resultado.SinCambios);
        Assert.Equal(0, resultado.Fallidos);

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking().Where(h => h.ClienteId == cliente.Id).ToListAsync();
        var registro = Assert.Single(historial);
        Assert.Equal("RecalculoGlobalTest", registro.Origen);
    }

    [Fact]
    public async Task RecalcularTodosAsync_VariosClientes_ExaminaATodosLosElegibles()
    {
        await SeedClienteAsync(DateTime.UtcNow);
        await SeedClienteAsync(DateTime.UtcNow);
        await SeedClienteAsync(DateTime.UtcNow);

        var resultado = await _service.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones { Origen = "Test" });

        Assert.Equal(3, resultado.Examinados);
        Assert.Equal(0, resultado.Fallidos);
    }

    [Fact]
    public async Task RecalcularTodosAsync_ProcesaEnLotes_SegunBatchSizeSinPerderClientes()
    {
        for (var i = 0; i < 5; i++)
            await SeedClienteAsync(DateTime.UtcNow);

        var resultado = await _service.RecalcularTodosAsync(
            new RecalculoGlobalScoringOpciones { Origen = "Test", BatchSize = 2 });

        Assert.Equal(5, resultado.Examinados);
        Assert.False(resultado.Interrumpido);
    }

    [Fact]
    public async Task RecalcularTodosAsync_SoloClientesActivosNoEliminados()
    {
        await SeedClienteAsync(DateTime.UtcNow); // activo, elegible

        var inactivo = await SeedClienteAsync(DateTime.UtcNow);
        inactivo.Activo = false;

        var eliminado = await SeedClienteAsync(DateTime.UtcNow);
        eliminado.IsDeleted = true;

        await _context.SaveChangesAsync();

        var resultado = await _service.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones { Origen = "Test" });

        Assert.Equal(1, resultado.Examinados);
    }

    [Fact]
    public async Task RecalcularTodosAsync_TokenYaCancelado_NoProcesaNadaYQuedaInterrumpido()
    {
        for (var i = 0; i < 5; i++)
            await SeedClienteAsync(DateTime.UtcNow);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var resultado = await _service.RecalcularTodosAsync(
            new RecalculoGlobalScoringOpciones { Origen = "Test" }, cts.Token);

        Assert.True(resultado.Interrumpido);
        Assert.Equal(0, resultado.Examinados);
    }

    [Fact]
    public async Task RecalcularTodosAsync_UnClienteFalla_ContinuaConElRestoYLoReportaEnIdsFallidos()
    {
        await SeedClienteAsync(DateTime.UtcNow);
        await SeedClienteAsync(DateTime.UtcNow);
        var servicio = new ClienteScoringServiceConIdFantasma(_context, idFantasma: 999_999);

        var resultado = await servicio.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones { Origen = "Test" });

        Assert.Equal(3, resultado.Examinados); // 2 reales + 1 fantasma
        Assert.Equal(1, resultado.Fallidos);
        Assert.Equal(2, resultado.Recalculados + resultado.SinCambios);
        Assert.Contains(999_999, resultado.IdsFallidos);
        Assert.False(resultado.Interrumpido); // política default: continúa
    }

    [Fact]
    public async Task RecalcularTodosAsync_PoliticaDetenerEnPrimerFallo_InterrumpeAntesDeExaminarElResto()
    {
        await SeedClienteAsync(DateTime.UtcNow);
        await SeedClienteAsync(DateTime.UtcNow);
        var servicio = new ClienteScoringServiceConIdFantasma(_context, idFantasma: 999_999, alPrincipio: true);

        var resultado = await servicio.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones
        {
            Origen = "Test",
            PoliticaErrores = PoliticaErroresRecalculoGlobal.DetenerEnPrimerFallo
        });

        Assert.True(resultado.Interrumpido);
        Assert.Equal(1, resultado.Fallidos);
        Assert.Equal(1, resultado.Examinados); // sólo el fantasma; los 2 reales nunca se examinaron
    }

    [Fact]
    public async Task RecalcularTodosAsync_ModoPreview_NoPersisteNiAuditaHistorial()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-400));
        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-PREV-1",
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

        var resultado = await _service.RecalcularTodosAsync(
            new RecalculoGlobalScoringOpciones { Origen = "Test", Preview = true });

        Assert.True(resultado.Preview);
        Assert.Equal(1, resultado.Recalculados); // hubiera cambiado

        var persistido = await _context.Clientes.AsNoTracking().FirstAsync(c => c.Id == cliente.Id);
        Assert.Equal(0, persistido.PuntajeCliente); // nada se persistió

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking().Where(h => h.ClienteId == cliente.Id).ToListAsync();
        Assert.Empty(historial);
    }

    [Fact]
    public async Task RecalcularTodosAsync_ScoreContaminado_SeCorrigeYSegundaCorridaNoDuplicaHistorial()
    {
        var ahora = DateTime.UtcNow;
        var cliente = await SeedClienteAsync(ahora.AddDays(-10));
        cliente.PuntajeCliente = 5; // score contaminado / stale respecto de la regla vigente
        await _context.SaveChangesAsync();

        _context.Creditos.Add(new Credito
        {
            ClienteId = cliente.Id,
            Numero = "C-CONT-1",
            Estado = EstadoCredito.Activo,
            Cuotas = new List<Cuota>
            {
                new()
                {
                    Estado = EstadoCuota.Vencida,
                    FechaVencimiento = ahora.AddDays(-5),
                    MontoPagado = 0m,
                    MontoTotal = 100m
                }
            }
        });
        await _context.SaveChangesAsync();

        var primera = await _service.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones { Origen = "RecalculoGlobalTest" });
        Assert.Equal(1, primera.Recalculados);
        Assert.Equal(0, primera.SinCambios);

        var segunda = await _service.RecalcularTodosAsync(new RecalculoGlobalScoringOpciones { Origen = "RecalculoGlobalTest" });
        Assert.Equal(0, segunda.Recalculados);
        Assert.Equal(1, segunda.SinCambios);

        var historial = await _context.ClientesPuntajeHistorial
            .AsNoTracking().Where(h => h.ClienteId == cliente.Id).ToListAsync();
        Assert.Single(historial); // sólo la primera corrida produjo un cambio real
    }

    /// <summary>
    /// Subclase de test: inyecta un id inexistente en la lista de elegibles (al principio o al
    /// final) para simular de forma determinística un fallo individual dentro del lote, sin
    /// depender de fragilidad de base de datos real (borrado concurrente, etc.).
    /// </summary>
    private sealed class ClienteScoringServiceConIdFantasma : ClienteScoringService
    {
        private readonly int _idFantasma;
        private readonly bool _alPrincipio;

        public ClienteScoringServiceConIdFantasma(AppDbContext context, int idFantasma, bool alPrincipio = false)
            : base(context, NullLogger<ClienteScoringService>.Instance)
        {
            _idFantasma = idFantasma;
            _alPrincipio = alPrincipio;
        }

        protected internal override async Task<List<int>> ObtenerClienteIdsElegiblesAsync(CancellationToken ct)
        {
            var ids = await base.ObtenerClienteIdsElegiblesAsync(ct);
            if (_alPrincipio)
                ids.Insert(0, _idFantasma);
            else
                ids.Add(_idFantasma);
            return ids;
        }
    }
}
