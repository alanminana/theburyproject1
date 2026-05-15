using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
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
    public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotImplementedException();
    public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario)
        => Task.FromResult<MovimientoCaja?>(null);
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

sealed class StubMovimientoStockEfectivo : IMovimientoStockService
{
    public IReadOnlyList<MovimientoStockCostoLinea>? UltimosCostosSalidas { get; private set; }
    public IReadOnlyList<MovimientoStockCostoLinea>? UltimosCostosEntradas { get; private set; }

    public Task<List<MovimientoStock>> RegistrarSalidasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> salidas,
        string motivo,
        string? usuarioActual = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
    {
        UltimosCostosSalidas = costos;
        return Task.FromResult(new List<MovimientoStock>());
    }

    public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
    public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null) => throw new NotImplementedException();
    public Task<List<MovimientoStock>> RegistrarEntradasAsync(List<(int productoId, decimal cantidad, string? referencia)> entradas, string motivo, string? usuarioActual = null, int? ordenCompraId = null, IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
    {
        UltimosCostosEntradas = costos;
        return Task.FromResult(new List<MovimientoStock>());
    }
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
    private readonly StubMovimientoStockEfectivo _movimientoStock;

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

        _movimientoStock = new StubMovimientoStockEfectivo();

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockEfectivo(),
            _movimientoStock,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserEfectivo(),
            new StubValidacionVentaEfectivo(),
            new StubCajaServiceEfectivo(_apertura),
            new StubCreditoDisponibleEfectivo(),
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
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
        decimal comisionPorcentaje = 0m,
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
            ComisionPorcentaje = comisionPorcentaje,
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
    public async Task CreateAsync_DosProductosConComisionDistinta_CalculaComisionPorLinea()
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var categoria = new Categoria { Nombre = $"CatComision{suffix}" };
        var marca = new Marca { Nombre = $"MarcaComision{suffix}" };
        var cliente = new Cliente
        {
            Nombre = "Cliente",
            Apellido = "Comision",
            NumeroDocumento = $"7{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        var productoA = new Producto
        {
            Codigo = $"PCA{suffix}",
            Nombre = $"Producto A {suffix}",
            PrecioVenta = 100m,
            ComisionPorcentaje = 10m,
            StockActual = 10,
            Categoria = categoria,
            Marca = marca,
            IsDeleted = false
        };
        var productoB = new Producto
        {
            Codigo = $"PCB{suffix}",
            Nombre = $"Producto B {suffix}",
            PrecioVenta = 200m,
            ComisionPorcentaje = 20m,
            StockActual = 10,
            Categoria = categoria,
            Marca = marca,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        _context.Productos.AddRange(productoA, productoB);
        await _context.SaveChangesAsync();

        var creada = await _service.CreateAsync(new VentaViewModel
        {
            ClienteId = cliente.Id,
            FechaVenta = DateTime.UtcNow,
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Efectivo,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = productoA.Id, Cantidad = 1, PrecioUnitario = 100m },
                new() { ProductoId = productoB.Id, Cantidad = 1, PrecioUnitario = 200m }
            }
        });

        var detalles = await _context.VentaDetalles
            .AsNoTracking()
            .Where(d => d.VentaId == creada.Id && !d.IsDeleted)
            .OrderBy(d => d.ProductoId)
            .ToListAsync();

        Assert.Equal(2, detalles.Count);
        var detalleA = detalles.Single(d => d.ProductoId == productoA.Id);
        var detalleB = detalles.Single(d => d.ProductoId == productoB.Id);
        Assert.Equal(10m, detalleA.ComisionPorcentajeAplicada);
        Assert.Equal(10m, detalleA.ComisionMonto);
        Assert.Equal(20m, detalleB.ComisionPorcentajeAplicada);
        Assert.Equal(40m, detalleB.ComisionMonto);
        Assert.Equal(50m, detalles.Sum(d => d.ComisionMonto));
    }

    [Fact]
    public async Task CreateAsync_TipoPagoTarjetaHistorico_RechazaVentaNueva()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(new VentaViewModel
            {
                TipoPago = TipoPago.Tarjeta,
                FechaVenta = DateTime.UtcNow,
                Estado = EstadoVenta.Presupuesto,
                Detalles = new List<VentaDetalleViewModel>()
            }));

        Assert.Contains("historico", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tarjeta Credito", ex.Message);
        Assert.Contains("Tarjeta Debito", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_EfectivoAMercadoPagoConPlanGlobal_LuegoConfirmarVenta_RetornaTrue()
    {
        var (venta, producto) = await SeedVentaEfectivo(
            tipoPago: TipoPago.Efectivo,
            estado: EstadoVenta.Presupuesto,
            stock: 10,
            cantidad: 1);
        var plan = await SeedPlanGlobalConfirmacionAsync(TipoPago.MercadoPago, ajustePorcentaje: 10m, cantidadCuotas: 2);

        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        var updateVm = new VentaViewModel
        {
            Id = venta.Id,
            ClienteId = ventaOriginal.ClienteId,
            FechaVenta = ventaOriginal.FechaVenta,
            Estado = ventaOriginal.Estado,
            TipoPago = TipoPago.MercadoPago,
            RowVersion = ventaOriginal.RowVersion,
            DatosTarjeta = new DatosTarjetaViewModel
            {
                NombreTarjeta = "Mercado Pago",
                TipoTarjeta = TipoTarjeta.Debito,
                CantidadCuotas = 2,
                ConfiguracionPagoPlanId = plan.Id
            },
            Detalles = new List<VentaDetalleViewModel>
            {
                new()
                {
                    ProductoId = producto.Id,
                    Cantidad = 1,
                    PrecioUnitario = 1_000m,
                    Descuento = 0m
                }
            }
        };

        var update = await _service.UpdateAsync(venta.Id, updateVm);
        var result = await _service.ConfirmarVentaAsync(venta.Id);

        Assert.NotNull(update);
        Assert.True(result);

        var ventaConfirmada = await _context.Ventas
            .Include(v => v.DatosTarjeta)
            .SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaConfirmada.Estado);
        Assert.Equal(TipoPago.MercadoPago, ventaConfirmada.TipoPago);
        Assert.NotNull(ventaConfirmada.DatosTarjeta);
        Assert.Equal(TipoTarjeta.Debito, ventaConfirmada.DatosTarjeta!.TipoTarjeta);
        Assert.Equal(plan.Id, ventaConfirmada.DatosTarjeta!.ConfiguracionPagoPlanId);
        Assert.Null(ventaConfirmada.DatosTarjeta.ProductoCondicionPagoPlanId);
    }

    [Fact]
    public async Task UpdateAsync_PlanGlobalConRecargo_NoAlteraComisionPorProducto()
    {
        var (venta, producto) = await SeedVentaEfectivo(
            tipoPago: TipoPago.Efectivo,
            estado: EstadoVenta.Presupuesto,
            stock: 10,
            cantidad: 1,
            comisionPorcentaje: 10m);
        var plan = await SeedPlanGlobalConfirmacionAsync(TipoPago.MercadoPago, ajustePorcentaje: 10m, cantidadCuotas: 2);

        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaOriginal = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        var update = await _service.UpdateAsync(venta.Id, new VentaViewModel
        {
            Id = venta.Id,
            ClienteId = ventaOriginal.ClienteId,
            FechaVenta = ventaOriginal.FechaVenta,
            Estado = ventaOriginal.Estado,
            TipoPago = TipoPago.MercadoPago,
            RowVersion = ventaOriginal.RowVersion,
            DatosTarjeta = new DatosTarjetaViewModel
            {
                NombreTarjeta = "Mercado Pago",
                TipoTarjeta = TipoTarjeta.Debito,
                CantidadCuotas = 2,
                ConfiguracionPagoPlanId = plan.Id
            },
            Detalles = new List<VentaDetalleViewModel>
            {
                new()
                {
                    ProductoId = producto.Id,
                    Cantidad = 1,
                    PrecioUnitario = 1_000m,
                    Descuento = 0m
                }
            }
        });

        Assert.NotNull(update);

        var ventaActualizada = await _context.Ventas
            .AsNoTracking()
            .Include(v => v.DatosTarjeta)
            .Include(v => v.Detalles)
            .SingleAsync(v => v.Id == venta.Id);
        var detalle = Assert.Single(ventaActualizada.Detalles.Where(d => !d.IsDeleted));

        Assert.Equal(1_100m, ventaActualizada.Total);
        Assert.Equal(10m, ventaActualizada.DatosTarjeta!.PorcentajeAjustePagoAplicado);
        Assert.Equal(100m, ventaActualizada.DatosTarjeta.MontoAjustePagoAplicado);
        Assert.Equal(1_000m, detalle.SubtotalFinal);
        Assert.Equal(10m, detalle.ComisionPorcentajeAplicada);
        Assert.Equal(100m, detalle.ComisionMonto);
    }

    private async Task<ConfiguracionPagoPlan> SeedPlanGlobalConfirmacionAsync(
        TipoPago tipoPago,
        decimal ajustePorcentaje,
        int cantidadCuotas)
    {
        var medio = new ConfiguracionPago
        {
            TipoPago = tipoPago,
            Nombre = $"Medio {tipoPago} {Interlocked.Increment(ref _counter)}",
            Activo = true,
            IsDeleted = false
        };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();

        var plan = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id,
            TipoPago = tipoPago,
            CantidadCuotas = cantidadCuotas,
            AjustePorcentaje = ajustePorcentaje,
            Etiqueta = $"{cantidadCuotas} cuotas",
            Activo = true,
            IsDeleted = false
        };
        _context.ConfiguracionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();
        return plan;
    }

    [Fact]
    public async Task ConfirmarVenta_Efectivo_PasaCostoSnapshotAMovimientoStock()
    {
        var (venta, producto) = await SeedVentaEfectivo(cantidad: 2);
        var detalle = await _context.VentaDetalles.SingleAsync(d => d.VentaId == venta.Id && !d.IsDeleted);
        detalle.CostoUnitarioAlMomento = 123.45m;
        detalle.CostoTotalAlMomento = 246.90m;
        await _context.SaveChangesAsync();

        await _service.ConfirmarVentaAsync(venta.Id);

        var costo = Assert.Single(_movimientoStock.UltimosCostosSalidas!);
        Assert.Equal(producto.Id, costo.ProductoId);
        Assert.Equal(2m, costo.Cantidad);
        Assert.Equal(123.45m, costo.CostoUnitario);
        Assert.Equal("VentaDetalleSnapshot", costo.FuenteCosto);
    }

    [Fact]
    public async Task CancelarVenta_Efectivo_PasaCostoSnapshotAMovimientoStock()
    {
        var (venta, producto) = await SeedVentaEfectivo(estado: EstadoVenta.Confirmada, cantidad: 2);
        var detalle = await _context.VentaDetalles.SingleAsync(d => d.VentaId == venta.Id && !d.IsDeleted);
        detalle.CostoUnitarioAlMomento = 98.76m;
        detalle.CostoTotalAlMomento = 197.52m;
        await _context.SaveChangesAsync();

        await _service.CancelarVentaAsync(venta.Id, "Test");

        var costo = Assert.Single(_movimientoStock.UltimosCostosEntradas!);
        Assert.Equal(producto.Id, costo.ProductoId);
        Assert.Equal(2m, costo.Cantidad);
        Assert.Equal(98.76m, costo.CostoUnitario);
        Assert.Equal("VentaDetalleSnapshot", costo.FuenteCosto);
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
    public async Task ConfirmarVenta_Efectivo_NoRecalculaNiAlteraSnapshotIva()
    {
        var (venta, _) = await SeedVentaEfectivo();
        var detalle = await _context.VentaDetalles.SingleAsync(d => d.VentaId == venta.Id && !d.IsDeleted);

        detalle.PorcentajeIVA = 10.5m;
        detalle.AlicuotaIVAId = 123;
        detalle.AlicuotaIVANombre = "IVA 10.5 snapshot";
        detalle.PrecioUnitarioNeto = 904.98m;
        detalle.IVAUnitario = 95.02m;
        detalle.SubtotalNeto = 904.98m;
        detalle.SubtotalIVA = 95.02m;
        detalle.DescuentoGeneralProrrateado = 12.34m;
        detalle.SubtotalFinalNeto = 893.81m;
        detalle.SubtotalFinalIVA = 93.85m;
        detalle.SubtotalFinal = 987.66m;
        await _context.SaveChangesAsync();

        await _service.ConfirmarVentaAsync(venta.Id);

        var detalleConfirmado = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => d.VentaId == venta.Id && !d.IsDeleted);

        Assert.Equal(10.5m, detalleConfirmado.PorcentajeIVA);
        Assert.Equal(123, detalleConfirmado.AlicuotaIVAId);
        Assert.Equal("IVA 10.5 snapshot", detalleConfirmado.AlicuotaIVANombre);
        Assert.Equal(904.98m, detalleConfirmado.PrecioUnitarioNeto);
        Assert.Equal(95.02m, detalleConfirmado.IVAUnitario);
        Assert.Equal(904.98m, detalleConfirmado.SubtotalNeto);
        Assert.Equal(95.02m, detalleConfirmado.SubtotalIVA);
        Assert.Equal(12.34m, detalleConfirmado.DescuentoGeneralProrrateado);
        Assert.Equal(893.81m, detalleConfirmado.SubtotalFinalNeto);
        Assert.Equal(93.85m, detalleConfirmado.SubtotalFinalIVA);
        Assert.Equal(987.66m, detalleConfirmado.SubtotalFinal);
    }

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

    // -------------------------------------------------------------------------
    // Tests — cuotas sin interes enforcement (Fase 5.0.4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_TarjetaSinInteres_CuotasExceden_ConfirmaSinValidacionProductoLegacy()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        await SeedCondicionPago(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 3);
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 6);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaSinInteres_CuotasPermitidas_Confirma()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        await SeedCondicionPago(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 3);
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 3);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaSinInteres_SinRestriccionProducto_Confirma()
    {
        // Producto sin MaxCuotasSinInteresPermitidas → no hay restriccion por producto
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 6);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 6);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaConInteres_IgnoraRestriccionProducto()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        await SeedCondicionPago(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 1);
        var tarjeta = await SeedTarjetaConInteres(maxCuotas: 12);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 6);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaSinInteres_NoGuardaSnapshotRestriccionProductoLegacy()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        await SeedCondicionPago(producto.Id, TipoPago.TarjetaCredito, maxCuotasSinInteres: 3);
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 3);

        var service = CreateServiceConCondicionesPago();
        await service.ConfirmarVentaAsync(venta.Id);

        var datos = await _context.DatosTarjeta.FirstAsync(d => d.VentaId == venta.Id);
        Assert.Null(datos.MaxCuotasSinInteresEfectivoAplicado);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaSinInteres_SnapshotNullSinLimitePorProducto()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 6);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 4);

        var service = CreateServiceConCondicionesPago();
        await service.ConfirmarVentaAsync(venta.Id);

        var datos = await _context.DatosTarjeta.FirstAsync(d => d.VentaId == venta.Id);
        Assert.Null(datos.MaxCuotasSinInteresEfectivoAplicado);
    }

    [Fact]
    public async Task ConfirmarVenta_MedioBloqueadoPorProductoLegacy_Confirma()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.Transferencia);
        await SeedCondicionPago(producto.Id, TipoPago.Transferencia, permitido: false);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_Multiproducto_CondicionProductoLegacyNoBloquea()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.Efectivo);
        var productoBloqueante = await SeedProductoExtraAsync(stock: 10);
        _context.VentaDetalles.Add(new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = productoBloqueante.Id,
            Cantidad = 1,
            PrecioUnitario = 500m,
            Subtotal = 500m,
            IsDeleted = false
        });
        venta.Total += 500m;
        await _context.SaveChangesAsync();
        await SeedCondicionPago(productoBloqueante.Id, TipoPago.Efectivo, permitido: false);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaEspecificaBloqueadaLegacy_Confirma()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        var condicion = await SeedCondicionPago(producto.Id, TipoPago.TarjetaCredito, permitido: true);
        await SeedReglaTarjeta(condicion.Id, tarjeta.Id, permitido: false);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 1);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaCredito_NoConsultaResolverProductoLegacy()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        var tarjeta = await SeedTarjetaSinInteres(maxCuotas: 12);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 6);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_OtraTarjetaNoBloqueada_Confirma()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        var tarjetaBloqueada = await SeedTarjetaSinInteres(maxCuotas: 12);
        var tarjetaPermitida = await SeedTarjetaSinInteres(maxCuotas: 12);
        var condicion = await SeedCondicionPago(producto.Id, TipoPago.TarjetaCredito, permitido: true);
        await SeedReglaTarjeta(condicion.Id, tarjetaBloqueada.Id, permitido: false);
        await SeedDatosTarjeta(venta, tarjetaPermitida, cantidadCuotas: 1);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaConInteres_CuotasExceden_ConfirmaSinValidacionProductoLegacy()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        await SeedCondicionPago(producto.Id, TipoPago.TarjetaCredito, maxCuotasConInteres: 4);
        var tarjeta = await SeedTarjetaConInteres(maxCuotas: 12);
        await SeedDatosTarjeta(venta, tarjeta, cantidadCuotas: 6);

        var service = CreateServiceConCondicionesPago();
        var result = await service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVenta_AjustesInformativos_NoModificanTotal()
    {
        var (venta, producto) = await SeedVentaEfectivo(tipoPago: TipoPago.Efectivo);
        await SeedCondicionPago(
            producto.Id,
            TipoPago.Efectivo,
            porcentajeRecargo: 10m,
            porcentajeDescuentoMaximo: 5m);
        var totalAntes = venta.Total;

        var service = CreateServiceConCondicionesPago();
        await service.ConfirmarVentaAsync(venta.Id);

        var ventaActualizada = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(totalAntes, ventaActualizada.Total);
    }

    // -------------------------------------------------------------------------
    // Helpers — cuotas sin interes
    // -------------------------------------------------------------------------

    private VentaService CreateServiceConCondicionesPago()
    {
        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        var configuracionPago = new ConfiguracionPagoService(
            _context,
            mapper,
            NullLogger<ConfiguracionPagoService>.Instance);

        return new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockEfectivo(),
            _movimientoStock,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserEfectivo(),
            new StubValidacionVentaEfectivo(),
            new StubCajaServiceEfectivo(_apertura),
            new StubCreditoDisponibleEfectivo(),
            new StubContratoVentaCreditoService(),
            configuracionPago);
    }

    private async Task<int> SeedConfigPagoTarjeta()
    {
        var existente = await _context.ConfiguracionesPago
            .FirstOrDefaultAsync(c => c.TipoPago == TipoPago.TarjetaCredito && !c.IsDeleted);
        if (existente != null)
        {
            return existente.Id;
        }

        var config = new ConfiguracionPago
        {
            TipoPago = TipoPago.TarjetaCredito,
            Nombre = "Tarjeta Credito",
            Activo = true
        };
        _context.ConfiguracionesPago.Add(config);
        await _context.SaveChangesAsync();
        return config.Id;
    }

    private async Task<ConfiguracionTarjeta> SeedTarjetaSinInteres(int maxCuotas)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var configPagoId = await SeedConfigPagoTarjeta();
        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = configPagoId,
            NombreTarjeta = $"TarjetaSI{suffix}",
            TipoTarjeta = TipoTarjeta.Credito,
            TipoCuota = TipoCuotaTarjeta.SinInteres,
            PermiteCuotas = true,
            CantidadMaximaCuotas = maxCuotas,
            Activa = true,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task<ConfiguracionTarjeta> SeedTarjetaConInteres(int maxCuotas)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var configPagoId = await SeedConfigPagoTarjeta();
        var tarjeta = new ConfiguracionTarjeta
        {
            ConfiguracionPagoId = configPagoId,
            NombreTarjeta = $"TarjetaCI{suffix}",
            TipoTarjeta = TipoTarjeta.Credito,
            TipoCuota = TipoCuotaTarjeta.ConInteres,
            PermiteCuotas = true,
            CantidadMaximaCuotas = maxCuotas,
            TasaInteresesMensual = 3m,
            Activa = true,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.ConfiguracionesTarjeta.Add(tarjeta);
        await _context.SaveChangesAsync();
        return tarjeta;
    }

    private async Task SeedDatosTarjeta(Venta venta, ConfiguracionTarjeta tarjeta, int cantidadCuotas)
    {
        var datos = new DatosTarjeta
        {
            VentaId = venta.Id,
            ConfiguracionTarjetaId = tarjeta.Id,
            NombreTarjeta = tarjeta.NombreTarjeta,
            TipoTarjeta = tarjeta.TipoTarjeta,
            TipoCuota = tarjeta.TipoCuota,
            CantidadCuotas = cantidadCuotas,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.DatosTarjeta.Add(datos);
        await _context.SaveChangesAsync();
    }

    private async Task<ProductoCondicionPago> SeedCondicionPago(
        int productoId,
        TipoPago tipoPago,
        bool? permitido = null,
        int? maxCuotasSinInteres = null,
        int? maxCuotasConInteres = null,
        decimal? porcentajeRecargo = null,
        decimal? porcentajeDescuentoMaximo = null)
    {
        var condicion = new ProductoCondicionPago
        {
            ProductoId = productoId,
            TipoPago = tipoPago,
            Permitido = permitido,
            MaxCuotasSinInteres = maxCuotasSinInteres,
            MaxCuotasConInteres = maxCuotasConInteres,
            PorcentajeRecargo = porcentajeRecargo,
            PorcentajeDescuentoMaximo = porcentajeDescuentoMaximo,
            Activo = true,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.ProductoCondicionesPago.Add(condicion);
        await _context.SaveChangesAsync();
        return condicion;
    }

    private async Task<Producto> SeedProductoExtraAsync(int stock)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var categoria = new Categoria { Codigo = $"CEX{suffix}", Nombre = $"CatExtra{suffix}" };
        var marca = new Marca { Codigo = $"MEX{suffix}", Nombre = $"MarcaExtra{suffix}" };
        var producto = new Producto
        {
            Codigo = $"PEX{suffix}",
            Nombre = $"ProdExtra{suffix}",
            PrecioVenta = 500m,
            StockActual = stock,
            Categoria = categoria,
            Marca = marca,
            IsDeleted = false
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task SeedReglaTarjeta(
        int productoCondicionPagoId,
        int configuracionTarjetaId,
        bool? permitido = null)
    {
        _context.ProductoCondicionesPagoTarjeta.Add(new ProductoCondicionPagoTarjeta
        {
            ProductoCondicionPagoId = productoCondicionPagoId,
            ConfiguracionTarjetaId = configuracionTarjetaId,
            Permitido = permitido,
            Activo = true,
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Tests — Fase 7.1: Guard DatosTarjeta en ConfirmarVentaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVenta_TarjetaCredito_SinDatosTarjeta_LanzaInvalidOperation()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("datos de tarjeta", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaDebito_SinDatosTarjeta_LanzaInvalidOperation()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaDebito);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("datos de tarjeta", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVenta_MercadoPago_SinDatosTarjeta_LanzaInvalidOperation()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.MercadoPago);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("datos de tarjeta", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVenta_TarjetaCredito_ConDatosTarjeta_Confirma()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        _context.DatosTarjeta.Add(new DatosTarjeta
        {
            VentaId = venta.Id,
            NombreTarjeta = "Visa",
            TipoTarjeta = TipoTarjeta.Credito,
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await _context.SaveChangesAsync();

        var result = await _service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    [Fact]
    public async Task ConfirmarVenta_Transferencia_SinDatosTarjeta_Confirma()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.Transferencia);

        var result = await _service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    [Fact]
    public async Task ConfirmarVenta_CuentaCorriente_SinDatosTarjeta_Confirma()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.CuentaCorriente);

        var result = await _service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    [Fact]
    public async Task ConfirmarVenta_MercadoPago_ConDatosTarjeta_Confirma()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.MercadoPago);
        _context.DatosTarjeta.Add(new DatosTarjeta
        {
            VentaId = venta.Id,
            NombreTarjeta = "Mercado Pago",
            TipoTarjeta = TipoTarjeta.Debito,
            CantidadCuotas = 1,
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await _context.SaveChangesAsync();

        var result = await _service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    /// <summary>
    /// Fase 6.5 — Los campos legacy de DatosTarjeta (PorcentajeAjustePlanAplicado,
    /// MontoAjustePlanAplicado del flujo por producto) no deben alterar Venta.Total
    /// ni bloquear la confirmación. ConfirmarVentaAsync no lee ni aplica esos campos.
    /// </summary>
    [Fact]
    public async Task ConfirmarVenta_TarjetaCredito_CamposLegacyEnDatosTarjeta_NoRecalculanTotal()
    {
        var (venta, _) = await SeedVentaEfectivo(tipoPago: TipoPago.TarjetaCredito);
        var totalOriginal = venta.Total; // 1000m

        // DatosTarjeta con campos legacy seteados (flujo por producto, no global)
        _context.DatosTarjeta.Add(new DatosTarjeta
        {
            VentaId = venta.Id,
            NombreTarjeta = "Visa Legacy",
            TipoTarjeta = TipoTarjeta.Credito,
            PorcentajeAjustePlanAplicado = 8m,   // legacy snapshot por producto
            MontoAjustePlanAplicado = 80m,         // legacy snapshot por producto
            PorcentajeAjustePagoAplicado = null,   // sin ajuste global
            MontoAjustePagoAplicado = null,
            ProductoCondicionPagoPlanId = null,
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await _context.SaveChangesAsync();

        var result = await _service.ConfirmarVentaAsync(venta.Id);

        Assert.True(result);

        var ventaConfirmada = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        // ConfirmarVentaAsync no recalcula Total basándose en los campos legacy de DatosTarjeta
        Assert.Equal(totalOriginal, ventaConfirmada.Total);
        Assert.Equal(EstadoVenta.Confirmada, ventaConfirmada.Estado);

        // Los campos legacy quedan intactos — el confirm no los toca
        var datos = await _context.DatosTarjeta.AsNoTracking().SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(8m, datos.PorcentajeAjustePlanAplicado);
        Assert.Equal(80m, datos.MontoAjustePlanAplicado);
        Assert.Null(datos.PorcentajeAjustePagoAplicado);
        Assert.Null(datos.MontoAjustePagoAplicado);
    }
}
