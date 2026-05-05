using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de idempotencia para VentaService.GuardarDatosChequeAsync.
///
/// Los casos base (primera llamada y venta inexistente) ya existen en
/// VentaServiceDeleteUpdateTests. Este archivo cubre exclusivamente los
/// casos de segunda llamada introducidos en Fase 6.5.
/// </summary>
public class VentaServiceGuardarDatosChequeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;

    private static int _counter = 2000;

    public VentaServiceGuardarDatosChequeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = BuildService(_context, mapper);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── Infraestructura ──────────────────────────────────────────────

    private static VentaService BuildService(AppDbContext ctx, IMapper mapper) =>
        new VentaService(
            ctx,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,   // IAlertaStockService — no usado aquí
            null!,   // IMovimientoStockService — no usado aquí
            new FinancialCalculationService(),
            null!,   // IVentaValidator — no usado aquí
            null!,   // VentaNumberGenerator — no usado aquí
            null!,   // IPrecioVigenteResolver — no usado aquí
            null!,   // ICurrentUserService — no usado aquí
            null!,   // IValidacionVentaService — no usado aquí
            null!,   // ICajaService — no usado aquí
            null!,   // ICreditoDisponibleService — no usado aquí
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());

    private async Task<Venta> SeedVenta()
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cheque",
            NumeroDocumento = $"C{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VC{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Cheque,
            Total = 5_000m,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private static DatosChequeViewModel BuildViewModel(string numero = "00012345") =>
        new DatosChequeViewModel
        {
            NumeroCheque = numero,
            Banco = "Banco Nación",
            Titular = "Juan Pérez",
            FechaEmision = DateTime.Today,
            FechaVencimiento = DateTime.Today.AddDays(30),
            Monto = 1_500m
        };

    // ── Tests ────────────────────────────────────────────────────────

    // -------------------------------------------------------------------------
    // 1. Segunda llamada idéntica: retorna false, count de DatosCheque queda en 1.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosCheque_SegundaLlamada_RetornaFalseYNoDuplicaRegistro()
    {
        var venta = await SeedVenta();
        var vm = BuildViewModel();

        var primera = await _service.GuardarDatosChequeAsync(venta.Id, vm);
        var segunda = await _service.GuardarDatosChequeAsync(venta.Id, vm);

        Assert.True(primera);
        Assert.False(segunda);

        var count = await _context.DatosCheque.CountAsync(d => d.VentaId == venta.Id && !d.IsDeleted);
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // 2. Segunda llamada con datos distintos: el snapshot original no se reemplaza.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuardarDatosCheque_SegundaLlamada_NoReemplazaSnapshotOriginal()
    {
        var venta = await SeedVenta();
        var vmOriginal = BuildViewModel(numero: "ORIGINAL-001");
        var vmAlternativo = BuildViewModel(numero: "ALTERNATIVO-002");

        var primera = await _service.GuardarDatosChequeAsync(venta.Id, vmOriginal);
        var segunda = await _service.GuardarDatosChequeAsync(venta.Id, vmAlternativo);

        Assert.True(primera);
        Assert.False(segunda);

        var cheque = await _context.DatosCheque.SingleAsync(d => d.VentaId == venta.Id && !d.IsDeleted);
        Assert.Equal("ORIGINAL-001", cheque.NumeroCheque);
    }
}
