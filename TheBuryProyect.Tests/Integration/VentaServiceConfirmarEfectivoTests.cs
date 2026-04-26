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
// Stubs reutilizados en el mismo archivo (file scope = no colisión con otros)
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceEfectivo : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaServiceEfectivo(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(
        int creditoId, string creditoNumero, decimal montoAnticipo, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());

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
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubMovimientoStockEfectivo : IMovimientoStockService
{
    public Task<List<MovimientoStock>> RegistrarSalidasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> salidas,
        string motivo,
        string? usuarioActual = null)
        => Task.FromResult(new List<MovimientoStock>());

    public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
    public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null) => throw new NotImplementedException();
    public Task<List<MovimientoStock>> RegistrarEntradasAsync(List<(int productoId, decimal cantidad, string? referencia)> entradas, string motivo, string? usuarioActual = null, int? ordenCompraId = null) => throw new NotImplementedException();
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
}

file sealed class StubAlertaStockEfectivo : IAlertaStockService
{
    public Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds) => Task.FromResult(0);
    public Task<int> GenerarAlertasStockBajoAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<List<AlertaStock>> GetAlertasPendientesAsync() => throw new NotImplementedException();
    public Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<AlertaStockViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync() => throw new NotImplementedException();
    public Task<List<AlertaStock>> GetAlertasByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId) => throw new NotImplementedException();
    public Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync() => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleEfectivo : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CreditoDisponibleResultado { Limite = 0m, Disponible = 999_999m });
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserEfectivo : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubValidacionVentaEfectivo : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
        int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });

    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para VentaService.ConfirmarVentaAsync (flujo no-crédito: Efectivo, Tarjeta, etc.)
///
/// Cubre:
/// - Happy path: venta → Confirmada, stock descontado via stub
/// - Guard: venta inexistente → false
/// - Guard: estado inválido (Cotizacion) → InvalidOperationException
/// - Guard: stock insuficiente → InvalidOperationException
/// - Guard: requiere autorización no autorizada → InvalidOperationException
/// - PendienteRequisitos también puede confirmarse (es estado válido)
/// </summary>
public class VentaServiceConfirmarEfectivoTests : IDisposable
{
    private const string TestUser = "testuser";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private readonly AperturaCaja _apertura;

    private static int _counter = 200;

    public VentaServiceConfirmarEfectivoTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _apertura = SeedCajaSinc();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockEfectivo(),
            new StubMovimientoStockEfectivo(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            null!,                                       // IPrecioService — no se llama en Confirmar
            new StubCurrentUserEfectivo(),
            new StubValidacionVentaEfectivo(),
            new StubCajaServiceEfectivo(_apertura),
            new StubCreditoDisponibleEfectivo(),
            new StubContratoVentaCreditoService());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private AperturaCaja SeedCajaSinc()
    {
        var caja = new Caja
        {
            Codigo = "CE1", Nombre = "Caja Efectivo Test",
            IsDeleted = false, RowVersion = new byte[8]
        };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();
        return apertura;
    }

    private async Task<(Venta venta, Producto producto)> SeedVentaEfectivo(
        TipoPago tipoPago = TipoPago.Efectivo,
        EstadoVenta estado = EstadoVenta.Presupuesto,
        int stock = 10,
        int cantidad = 1,
        bool requiereAutorizacion = false,
        EstadoAutorizacionVenta estadoAutorizacion = EstadoAutorizacionVenta.NoRequiere)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var categoria = new Categoria { Nombre = $"Cat{suffix}" };
        var marca = new Marca { Nombre = $"Marca{suffix}" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "E",
            NumeroDocumento = $"6{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PE{suffix}",
            Nombre = $"Prod{suffix}",
            PrecioVenta = 1_000m,
            StockActual = stock,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            IsDeleted = false
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VE{suffix}",
            Estado = estado,
            TipoPago = tipoPago,
            Total = 1_000m * cantidad,
            RequiereAutorizacion = requiereAutorizacion,
            EstadoAutorizacion = estadoAutorizacion,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = cantidad,
            PrecioUnitario = 1_000m,
            Subtotal = 1_000m * cantidad,
            IsDeleted = false
        };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        return (venta, producto);
    }

    // -------------------------------------------------------------------------
    // Tests — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_Efectivo_HappyPath_RetornaTrue()
    {
        var (venta, _) = await SeedVentaEfectivo();

        var result = await _service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_Efectivo_HappyPath_VentaTransicionaAConfirmada()
    {
        var (venta, _) = await SeedVentaEfectivo();

        await _service.ConfirmarVentaAsync(venta.Id);

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
        Assert.NotNull(ventaActualizada.FechaConfirmacion);
    }

    [Fact]
    public async Task ConfirmarVenta_DesdePendienteRequisitos_TransicionaAConfirmada()
    {
        // PendienteRequisitos también es estado válido para confirmar (ValidarEstadoParaConfirmacion)
        var (venta, _) = await SeedVentaEfectivo(estado: EstadoVenta.PendienteRequisitos);

        await _service.ConfirmarVentaAsync(venta.Id);

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    [Fact]
    public async Task ConfirmarVenta_Autorizada_SinRequerirseMas_TransicionaAConfirmada()
    {
        var (venta, _) = await SeedVentaEfectivo(
            requiereAutorizacion: true,
            estadoAutorizacion: EstadoAutorizacionVenta.Autorizada);

        await _service.ConfirmarVentaAsync(venta.Id);

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    // -------------------------------------------------------------------------
    // Tests — guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_VentaInexistente_RetornaFalse()
    {
        var result = await _service.ConfirmarVentaAsync(99_999);

        Assert.False(result);
    }

    [Fact]
    public async Task ConfirmarVenta_EstadoCotizacion_LanzaInvalidOperation()
    {
        var (venta, _) = await SeedVentaEfectivo(estado: EstadoVenta.Cotizacion);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("Presupuesto", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVenta_StockInsuficiente_LanzaInvalidOperation()
    {
        // stock=0, cantidad=1 → ValidarStock lanza
        var (venta, _) = await SeedVentaEfectivo(stock: 0, cantidad: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("Stock insuficiente", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVenta_RequiereAutorizacionNoAutorizada_LanzaInvalidOperation()
    {
        var (venta, _) = await SeedVentaEfectivo(
            requiereAutorizacion: true,
            estadoAutorizacion: EstadoAutorizacionVenta.PendienteAutorizacion);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("autorización", ex.Message);
    }
}
