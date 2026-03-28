using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs (file-scoped para evitar conflictos con otros archivos de test)
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceFacturacion : ICajaService
{
    private readonly AperturaCaja _apertura;
    private readonly bool[] _movimientoRegistrado;

    public StubCajaServiceFacturacion(AperturaCaja apertura, bool[] movimientoRegistrado)
    {
        _apertura = apertura;
        _movimientoRegistrado = movimientoRegistrado;
    }

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(
        int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario)
    {
        _movimientoRegistrado[0] = true;
        return Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    }

    // Implementaciones no utilizadas en estos tests
    public Task<List<Caja>> ObtenerTodasCajasAsync() => throw new NotImplementedException();
    public Task<Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<Caja> CrearCajaAsync(CajaViewModel model) => throw new NotImplementedException();
    public Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model) => throw new NotImplementedException();
    public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotImplementedException();
    public Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotImplementedException();
    public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotImplementedException();
    public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubCurrentUserFacturacion : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para VentaService.FacturarVentaAsync y AnularFacturaAsync.
///
/// FacturarVentaAsync contratos verificados:
/// - Venta inexistente → false
/// - Estado != Confirmada → throws InvalidOperationException
/// - Happy path Efectivo: retorna true, venta→Facturada, FechaFacturacion asignada
/// - Happy path Efectivo: crea Factura con Numero generado
/// - TipoPago no-crédito: registra movimiento de caja (vía stub)
/// - TipoPago CreditoPersonal: NO registra movimiento de caja
///
/// AnularFacturaAsync contratos verificados:
/// - Motivo vacío → ArgumentException
/// - Factura inexistente → null
/// - Factura ya anulada → throws InvalidOperationException
/// - Happy path: retorna ventaId, factura queda Anulada, FechaAnulacion y MotivoAnulacion asignados
/// - Única factura activa → venta regresa a Confirmada
/// - Otra factura activa → venta permanece Facturada
/// </summary>
public class VentaServiceFacturacionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly bool[] _movimientoCajaRegistrado = { false };
    private readonly VentaService _service;

    private static int _counter = 500;

    public VentaServiceFacturacionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        // Seed Caja y AperturaCaja para satisfacer la FK Venta.AperturaCajaId
        var caja = new Caja { Codigo = "TEST", Nombre = "Caja Test", Activa = true };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();

        ICajaService cajaStub = new StubCajaServiceFacturacion(apertura, _movimientoCajaRegistrado);

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,   // IAlertaStockService — no requerido en Facturar/Anular
            null!,   // IMovimientoStockService
            null!,   // IFinancialCalculationService
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            null!,   // IPrecioService
            new StubCurrentUserFacturacion(),
            null!,   // IValidacionVentaService
            cajaStub,
            null!);  // ICreditoDisponibleService
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Venta> SeedVentaConfirmada(
        TipoPago tipoPago = TipoPago.Efectivo,
        EstadoVenta estado = EstadoVenta.Confirmada)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Facturacion",
            NumeroDocumento = $"7{suffix}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VF{suffix}",
            Estado = estado,
            TipoPago = tipoPago,
            Total = 2_000m,
            RequiereAutorizacion = false,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private async Task<Factura> SeedFactura(int ventaId, bool anulada = false)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var factura = new Factura
        {
            VentaId = ventaId,
            Numero = $"F{suffix}",
            Tipo = TipoFactura.B,
            FechaEmision = DateTime.UtcNow,
            Total = 2_000m,
            Anulada = anulada
        };
        _context.Facturas.Add(factura);
        await _context.SaveChangesAsync();
        return factura;
    }

    // -------------------------------------------------------------------------
    // Tests — FacturarVentaAsync: guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Facturar_VentaInexistente_RetornaFalse()
    {
        var facturaVm = new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

        var result = await _service.FacturarVentaAsync(id: 99_999, facturaViewModel: facturaVm);

        Assert.False(result);
    }

    [Fact]
    public async Task Facturar_EstadoPresupuesto_LanzaInvalidOperationException()
    {
        var venta = await SeedVentaConfirmada(estado: EstadoVenta.Presupuesto);
        var facturaVm = new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.FacturarVentaAsync(venta.Id, facturaVm));
    }

    // -------------------------------------------------------------------------
    // Tests — FacturarVentaAsync: happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Facturar_HappyPath_RetornaTrue()
    {
        var venta = await SeedVentaConfirmada();
        var facturaVm = new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

        var result = await _service.FacturarVentaAsync(venta.Id, facturaVm);

        Assert.True(result);
    }

    [Fact]
    public async Task Facturar_HappyPath_VentaQuedaFacturada()
    {
        var venta = await SeedVentaConfirmada();
        var facturaVm = new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

        await _service.FacturarVentaAsync(venta.Id, facturaVm);

        var actualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(actualizada);
        Assert.Equal(EstadoVenta.Facturada, actualizada!.Estado);
        Assert.NotNull(actualizada.FechaFacturacion);
    }

    [Fact]
    public async Task Facturar_HappyPath_CreaFacturaConNumero()
    {
        var venta = await SeedVentaConfirmada();
        var facturaVm = new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

        await _service.FacturarVentaAsync(venta.Id, facturaVm);

        var factura = await _context.Facturas.FirstOrDefaultAsync(f => f.VentaId == venta.Id);
        Assert.NotNull(factura);
        Assert.False(string.IsNullOrWhiteSpace(factura!.Numero));
    }

    [Fact]
    public async Task Facturar_TipoPagoEfectivo_RegistraMovimientoEnCaja()
    {
        var venta = await SeedVentaConfirmada(tipoPago: TipoPago.Efectivo);
        var facturaVm = new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

        await _service.FacturarVentaAsync(venta.Id, facturaVm);

        Assert.True(_movimientoCajaRegistrado[0]);
    }

    [Fact]
    public async Task Facturar_TipoPagoCreditoPersonal_NoRegistraMovimientoEnCaja()
    {
        var venta = await SeedVentaConfirmada(tipoPago: TipoPago.CreditoPersonal);
        var facturaVm = new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow };

        await _service.FacturarVentaAsync(venta.Id, facturaVm);

        Assert.False(_movimientoCajaRegistrado[0]);
    }

    // -------------------------------------------------------------------------
    // Tests — AnularFacturaAsync: guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnularFactura_MotivoVacio_LanzaArgumentException()
    {
        var venta = await SeedVentaConfirmada(estado: EstadoVenta.Facturada);
        var factura = await SeedFactura(venta.Id);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AnularFacturaAsync(factura.Id, motivo: ""));
    }

    [Fact]
    public async Task AnularFactura_FacturaInexistente_RetornaNull()
    {
        var result = await _service.AnularFacturaAsync(facturaId: 99_999, motivo: "Motivo");

        Assert.Null(result);
    }

    [Fact]
    public async Task AnularFactura_YaAnulada_LanzaInvalidOperationException()
    {
        var venta = await SeedVentaConfirmada(estado: EstadoVenta.Facturada);
        var factura = await SeedFactura(venta.Id, anulada: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AnularFacturaAsync(factura.Id, motivo: "Motivo"));
    }

    // -------------------------------------------------------------------------
    // Tests — AnularFacturaAsync: happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnularFactura_HappyPath_RetornaVentaId()
    {
        var venta = await SeedVentaConfirmada(estado: EstadoVenta.Facturada);
        var factura = await SeedFactura(venta.Id);

        var result = await _service.AnularFacturaAsync(factura.Id, motivo: "Error de carga");

        Assert.Equal(venta.Id, result);
    }

    [Fact]
    public async Task AnularFactura_HappyPath_PersisteMotivoYFechaAnulacion()
    {
        var venta = await SeedVentaConfirmada(estado: EstadoVenta.Facturada);
        var factura = await SeedFactura(venta.Id);
        var antes = DateTime.UtcNow;

        await _service.AnularFacturaAsync(factura.Id, motivo: "  Error de tipeo  ");

        var facturaActualizada = await _context.Facturas.FindAsync(factura.Id);
        Assert.NotNull(facturaActualizada);
        Assert.True(facturaActualizada!.Anulada);
        Assert.NotNull(facturaActualizada.FechaAnulacion);
        Assert.True(facturaActualizada.FechaAnulacion >= antes);
        // Motivo normalizado con Trim()
        Assert.Equal("Error de tipeo", facturaActualizada.MotivoAnulacion);
    }

    [Fact]
    public async Task AnularFactura_UnicaFacturaActiva_VentaRegresaAConfirmada()
    {
        var venta = await SeedVentaConfirmada(estado: EstadoVenta.Facturada);
        var factura = await SeedFactura(venta.Id);

        await _service.AnularFacturaAsync(factura.Id, motivo: "Anulación");

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
        Assert.Null(ventaActualizada.FechaFacturacion);
    }

    [Fact]
    public async Task AnularFactura_OtraFacturaActiva_VentaPermaneceFecturada()
    {
        var venta = await SeedVentaConfirmada(estado: EstadoVenta.Facturada);
        var facturaAnular = await SeedFactura(venta.Id);
        await SeedFactura(venta.Id); // segunda factura activa

        await _service.AnularFacturaAsync(facturaAnular.Id, motivo: "Anulación parcial");

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.Equal(EstadoVenta.Facturada, ventaActualizada!.Estado);
    }
}
