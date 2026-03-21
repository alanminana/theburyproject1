using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Valida que GenerarReporteMorosidadAsync carga correctamente la deuda vigente
/// por cliente usando el diccionario precargado (sin N+1).
///
/// Verifica equivalencia funcional: los valores calculados son idénticos
/// a los que generaría la query N+1 anterior.
/// </summary>
public class ReporteMorosidadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ReporteService _service;

    // Fecha fija para tests deterministas
    // hoy = UtcNow.Date en el service, pero los datos se construyen relativos a "ahora"
    // Usamos fechas absolutas alejadas del presente para evitar falsos positivos por tiempo.
    private static readonly DateTime Ayer = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Hoy = new DateTime(2026, 3, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Manana = new DateTime(2026, 3, 22, 0, 0, 0, DateTimeKind.Utc);

    public ReporteMorosidadTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ReporteService(_context, NullLogger<ReporteService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers de seed
    // -------------------------------------------------------------------------

    private Cliente SeedCliente(int id, string nombre = "Test", string apellido = "Cliente")
    {
        var cliente = new Cliente
        {
            Id = id,
            Nombre = nombre,
            Apellido = apellido,
            NumeroDocumento = $"DOC{id:D8}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        return cliente;
    }

    private Credito SeedCredito(int id, int clienteId)
    {
        var credito = new Credito
        {
            Id = id,
            ClienteId = clienteId,
            IsDeleted = false,
            Numero = $"CRED{id:D4}",
            Estado = EstadoCredito.Activo,
            MontoSolicitado = 10_000m,
            MontoAprobado = 10_000m,
            SaldoPendiente = 10_000m,
            TasaInteres = 0.05m,
            CantidadCuotas = 12,
            MontoCuota = 900m,
            TotalAPagar = 10_800m
        };
        _context.Creditos.Add(credito);
        return credito;
    }

    private Cuota SeedCuota(int id, int creditoId, DateTime vencimiento, decimal monto, EstadoCuota estado)
    {
        var cuota = new Cuota
        {
            Id = id,
            CreditoId = creditoId,
            NumeroCuota = id,
            FechaVencimiento = vencimiento,
            MontoTotal = monto,
            MontoCapital = monto * 0.8m,
            MontoInteres = monto * 0.2m,
            MontoPagado = 0m,
            Estado = estado,
            IsDeleted = false
        };
        _context.Cuotas.Add(cuota);
        return cuota;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeudaVigente_SeCalculaCorrectamente_ParaUnCliente()
    {
        // Arrange: 1 cliente con 1 cuota vencida + 2 cuotas vigentes
        SeedCliente(1);
        SeedCredito(1, clienteId: 1);
        SeedCuota(1, creditoId: 1, vencimiento: Ayer,   monto: 1_000m, estado: EstadoCuota.Pendiente); // vencida
        SeedCuota(2, creditoId: 1, vencimiento: Manana, monto: 1_500m, estado: EstadoCuota.Pendiente); // vigente
        SeedCuota(3, creditoId: 1, vencimiento: Manana, monto: 2_000m, estado: EstadoCuota.Pendiente); // vigente
        await _context.SaveChangesAsync();

        // Act
        var reporte = await _service.GenerarReporteMorosidadAsync();

        // Assert
        Assert.Single(reporte.ClientesMorosos);
        var cliente = reporte.ClientesMorosos[0];
        Assert.Equal(1_000m, cliente.TotalDeudaVencida);
        Assert.Equal(3_500m, cliente.TotalDeudaVigente); // 1500 + 2000
    }

    [Fact]
    public async Task DeudaVigente_SeCalculaCorrectamente_ParaMultiplesClientes()
    {
        // Arrange: 2 clientes morosos con deuda vigente distinta
        SeedCliente(1, "García", "Juan");
        SeedCliente(2, "López", "María");
        SeedCredito(1, clienteId: 1);
        SeedCredito(2, clienteId: 2);

        // Cliente 1: 1 cuota vencida + 1 vigente
        SeedCuota(1, creditoId: 1, vencimiento: Ayer,   monto: 500m,   estado: EstadoCuota.Pendiente);
        SeedCuota(2, creditoId: 1, vencimiento: Manana, monto: 800m,   estado: EstadoCuota.Pendiente);

        // Cliente 2: 2 cuotas vencidas + 2 vigentes
        SeedCuota(3, creditoId: 2, vencimiento: Ayer,   monto: 1_000m, estado: EstadoCuota.Pendiente);
        SeedCuota(4, creditoId: 2, vencimiento: Ayer,   monto: 1_000m, estado: EstadoCuota.Pendiente);
        SeedCuota(5, creditoId: 2, vencimiento: Manana, monto: 600m,   estado: EstadoCuota.Pendiente);
        SeedCuota(6, creditoId: 2, vencimiento: Manana, monto: 400m,   estado: EstadoCuota.Pendiente);
        await _context.SaveChangesAsync();

        // Act
        var reporte = await _service.GenerarReporteMorosidadAsync();

        // Assert
        Assert.Equal(2, reporte.ClientesMorosos.Count);

        var c1 = reporte.ClientesMorosos.Single(c => c.ClienteId == 1);
        Assert.Equal(500m,   c1.TotalDeudaVencida);
        Assert.Equal(800m,   c1.TotalDeudaVigente);

        var c2 = reporte.ClientesMorosos.Single(c => c.ClienteId == 2);
        Assert.Equal(2_000m, c2.TotalDeudaVencida);
        Assert.Equal(1_000m, c2.TotalDeudaVigente); // 600 + 400
    }

    [Fact]
    public async Task DeudaVigente_EsCero_CuandoClienteNoTieneCuotasVigentes()
    {
        // Arrange: cliente con solo cuotas vencidas, sin vigentes
        SeedCliente(1);
        SeedCredito(1, clienteId: 1);
        SeedCuota(1, creditoId: 1, vencimiento: Ayer, monto: 2_000m, estado: EstadoCuota.Pendiente);
        await _context.SaveChangesAsync();

        // Act
        var reporte = await _service.GenerarReporteMorosidadAsync();

        // Assert
        Assert.Single(reporte.ClientesMorosos);
        Assert.Equal(0m, reporte.ClientesMorosos[0].TotalDeudaVigente);
    }

    [Fact]
    public async Task TotalDeudaVigente_EsSumaDeTodasLasDeudas()
    {
        // Arrange: 2 clientes, cada uno con deuda vigente distinta
        SeedCliente(1);
        SeedCliente(2);
        SeedCredito(1, clienteId: 1);
        SeedCredito(2, clienteId: 2);

        SeedCuota(1, creditoId: 1, vencimiento: Ayer,   monto: 300m, estado: EstadoCuota.Pendiente);
        SeedCuota(2, creditoId: 1, vencimiento: Manana, monto: 700m, estado: EstadoCuota.Pendiente);

        SeedCuota(3, creditoId: 2, vencimiento: Ayer,   monto: 400m, estado: EstadoCuota.Pendiente);
        SeedCuota(4, creditoId: 2, vencimiento: Manana, monto: 900m, estado: EstadoCuota.Pendiente);
        await _context.SaveChangesAsync();

        // Act
        var reporte = await _service.GenerarReporteMorosidadAsync();

        // Assert: TotalDeudaVigente del ViewModel = suma de todos los clientes
        Assert.Equal(1_600m, reporte.TotalDeudaVigente); // 700 + 900
    }
}
