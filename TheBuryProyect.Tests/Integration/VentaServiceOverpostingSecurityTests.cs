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
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs locales (file-scoped para evitar colisiones con otros archivos)
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceOverposting : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaServiceOverposting(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario)
        => Task.FromResult<MovimientoCaja?>(null);
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario, int? ventaId = null)
        => Task.FromResult<MovimientoCaja?>(null);
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario)
        => Task.FromResult(new MovimientoCaja());

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
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<Dictionary<int, DateTime>> ObtenerUltimosCierresPorCajaAsync() => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubCurrentUserOverposting : ICurrentUserService
{
    // SuperAdmin para que ValidarVendedorHabilitadoEnCajaAsync sea bypass.
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => role is "SuperAdmin";
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

// No es file-scoped: se usa como tipo concreto en el campo del test para acceder a SalidasRegistradas.
sealed class StubMovimientoStockOverposting : IMovimientoStockService
{
    public bool SalidasRegistradas { get; private set; }

    public Task<List<MovimientoStock>> RegistrarSalidasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> salidas,
        string motivo,
        string? usuarioActual = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
    {
        SalidasRegistradas = true;
        return Task.FromResult(new List<MovimientoStock>());
    }

    public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
    public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
    public Task<(IEnumerable<MovimientoStock> Items, int Total, decimal TotalEntradas, decimal TotalSalidas, int TotalAjustes)> SearchPaginadoAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc", int pageNumber = 1, int pageSize = 20) => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null, Guid? requestId = null) => throw new NotImplementedException();
    public Task<List<MovimientoStock>> RegistrarEntradasAsync(List<(int productoId, decimal cantidad, string? referencia)> entradas, string motivo, string? usuarioActual = null, int? ordenCompraId = null, IReadOnlyList<MovimientoStockCostoLinea>? costos = null) => Task.FromResult(new List<MovimientoStock>());
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
}

file sealed class StubAlertaStockOverposting : IAlertaStockService
{
    public Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds) => Task.FromResult(0);
    public Task<int> GenerarAlertasStockBajoAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<List<AlertaStock>> GetAlertasPendientesAsync() => throw new NotImplementedException();
    public Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<(int Pendientes, int Criticas)> ContarPorEstadoAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<AlertaStockViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> MarcarEnProcesoAsync(int id, string usuario, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync() => throw new NotImplementedException();
    public Task<List<AlertaStockViewModel>> GetAlertasByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId) => throw new NotImplementedException();
    public Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync() => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleOverposting : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken ct = default) => Task.FromResult(0m);
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken ct = default) => Task.FromResult(0m);
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken ct = default)
        => Task.FromResult(new CreditoDisponibleResultado { Limite = 0m, Disponible = 999_999m });
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubValidacionVentaOverposting : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });
    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para FIN-S-002: overposting en Venta/Create(Ajax).
///
/// Verifica que VentaService.CreateAsync ignora Estado y campos de workflow
/// que el cliente envía en el POST, y siempre fija Estado = Cotizacion.
///
/// Regresiones:
/// 1. Estado manipulado → venta persiste como Cotizacion
/// 2. Campos de autorización manipulados → persisten con valores seguros
/// 3. CreditoId manipulado → ignorado (fijado en null)
/// 4. FechaVenta retroactiva → corregida al momento actual
/// 5. Flujo legítimo Efectivo → no rompe por la corrección
/// </summary>
public class VentaServiceOverpostingSecurityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private readonly StubMovimientoStockOverposting _stockStub;
    private readonly AperturaCaja _apertura;

    private static int _counter = 3000;

    public VentaServiceOverpostingSecurityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _apertura = SeedCajaSinc();
        _stockStub = new StubMovimientoStockOverposting();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockOverposting(),
            _stockStub,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserOverposting(),
            new StubValidacionVentaOverposting(),
            new StubCajaServiceOverposting(_apertura),
            new StubCreditoDisponibleOverposting(),
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
        var caja = new Caja { Codigo = "CS1", Nombre = "Caja Overpost", IsDeleted = false, RowVersion = new byte[8] };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id, UsuarioApertura = "testuser",
            MontoInicial = 0m, Cerrada = false, IsDeleted = false, RowVersion = new byte[8]
        };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();
        return apertura;
    }

    private async Task<(int ClienteId, VentaDetalleViewModel Detalle)> SeedClienteYProducto()
    {
        var n = Interlocked.Increment(ref _counter).ToString();

        var cat = new Categoria { Nombre = $"Cat{n}" };
        var mar = new Marca { Nombre = $"Mar{n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(mar);
        await _context.SaveChangesAsync();

        var cli = new Cliente
        {
            Nombre = "Test", Apellido = "OvP", NumeroDocumento = $"7{n}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal, IsDeleted = false
        };
        _context.Clientes.Add(cli);

        var prod = new Producto
        {
            Codigo = $"OP{n}", Nombre = $"ProdOvP{n}", PrecioVenta = 1_000m,
            StockActual = 10, CategoriaId = cat.Id, MarcaId = mar.Id, IsDeleted = false
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalleViewModel
        {
            ProductoId = prod.Id, Cantidad = 1,
            PrecioUnitario = 1_000m, Subtotal = 1_000m
        };

        return (cli.Id, detalle);
    }

    private async Task<VentaViewModel> BuildVmConEstadoManipulado(EstadoVenta estadoManipulado)
    {
        var (clienteId, detalle) = await SeedClienteYProducto();
        return new VentaViewModel
        {
            ClienteId = clienteId,
            FechaVenta = DateTime.UtcNow,
            TipoPago = TipoPago.Efectivo,
            Estado = estadoManipulado,           // <-- campo manipulado
            EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada,
            RequiereAutorizacion = true,
            UsuarioAutoriza = "atacante",
            FechaAutorizacion = new DateTime(2020, 1, 1),
            FechaConfirmacion = new DateTime(2020, 1, 1),
            FechaFacturacion = new DateTime(2020, 1, 1),
            FechaEntrega = new DateTime(2020, 1, 1),
            FechaCancelacion = new DateTime(2020, 1, 1),
            MotivoCancelacion = "bypass",
            RazonesAutorizacionJson = "[\"fake\"]",
            CreditoId = 9999,
            Detalles = new List<VentaDetalleViewModel> { detalle }
        };
    }

    // -------------------------------------------------------------------------
    // FIN-S-002: Estado manipulado → siempre Cotizacion
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(EstadoVenta.Confirmada)]
    [InlineData(EstadoVenta.Facturada)]
    [InlineData(EstadoVenta.Entregada)]
    [InlineData(EstadoVenta.Cancelada)]
    public async Task CreateAsync_EstadoManipuladoEnPost_VentaPersisteCotizacion(EstadoVenta estadoManipulado)
    {
        var vm = await BuildVmConEstadoManipulado(estadoManipulado);

        var resultado = await _service.CreateAsync(vm);

        var ventaEnDb = await _context.Ventas.FindAsync(resultado.Id);
        Assert.NotNull(ventaEnDb);
        Assert.Equal(EstadoVenta.Cotizacion, ventaEnDb!.Estado);
    }

    [Theory]
    [InlineData(EstadoVenta.Confirmada)]
    [InlineData(EstadoVenta.Facturada)]
    [InlineData(EstadoVenta.Entregada)]
    public async Task CreateAsync_EstadoManipuladoEnPost_NoRegistraMovimientosDeStock(EstadoVenta estadoManipulado)
    {
        // FIN-S-002: el stock solo debe descontarse al Confirmar, no al Crear.
        var vm = await BuildVmConEstadoManipulado(estadoManipulado);

        await _service.CreateAsync(vm);

        Assert.False(_stockStub.SalidasRegistradas,
            "El stock no debe descontarse en el alta; solo al Confirmar la venta.");
    }

    // -------------------------------------------------------------------------
    // FIN-S-002: Campos de autorización/workflow manipulados → valores seguros
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_CamposWorkflowManipulados_PersistenComoDefecto()
    {
        var vm = await BuildVmConEstadoManipulado(EstadoVenta.Cotizacion);
        // Sobreescribir campos de workflow con valores manipulados
        vm.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        vm.RequiereAutorizacion = true;
        vm.UsuarioAutoriza = "atacante";
        vm.FechaAutorizacion = new DateTime(2020, 1, 1);
        vm.FechaConfirmacion = new DateTime(2020, 1, 1);
        vm.FechaFacturacion = new DateTime(2020, 1, 1);
        vm.FechaEntrega = new DateTime(2020, 1, 1);
        vm.FechaCancelacion = new DateTime(2020, 1, 1);
        vm.MotivoCancelacion = "bypass";
        vm.RazonesAutorizacionJson = "[\"fake\"]";

        var resultado = await _service.CreateAsync(vm);

        var v = await _context.Ventas.FindAsync(resultado.Id);
        Assert.NotNull(v);
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, v!.EstadoAutorizacion);
        Assert.False(v.RequiereAutorizacion);
        Assert.Null(v.UsuarioAutoriza);
        Assert.Null(v.FechaAutorizacion);
        Assert.Null(v.FechaConfirmacion);
        Assert.Null(v.FechaFacturacion);
        Assert.Null(v.FechaEntrega);
        Assert.Null(v.FechaCancelacion);
        Assert.Null(v.MotivoCancelacion);
        Assert.Null(v.RazonesAutorizacionJson);
    }

    // -------------------------------------------------------------------------
    // FIN-S-002: CreditoId manipulado → ignorado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_CreditoIdManipulado_SeIgnora()
    {
        // FIN-S-003 (alcance parcial): el CreditoId del formulario no debe asociarse a la venta.
        var vm = await BuildVmConEstadoManipulado(EstadoVenta.Cotizacion);
        vm.CreditoId = 9999;
        vm.TipoPago = TipoPago.Efectivo;   // no-crédito: no debe usar CreditoId de ninguna manera

        var resultado = await _service.CreateAsync(vm);

        var v = await _context.Ventas.FindAsync(resultado.Id);
        Assert.NotNull(v);
        Assert.Null(v!.CreditoId);
    }

    // -------------------------------------------------------------------------
    // FIN-S-002: FechaVenta retroactiva → corregida al servidor
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_FechaVentaRetroactiva_EsCorregida()
    {
        var (clienteId, detalle) = await SeedClienteYProducto();
        var vm = new VentaViewModel
        {
            ClienteId = clienteId,
            FechaVenta = new DateTime(2015, 1, 1),  // retroactiva
            TipoPago = TipoPago.Efectivo,
            Estado = EstadoVenta.Cotizacion,
            Detalles = new List<VentaDetalleViewModel> { detalle }
        };

        var resultado = await _service.CreateAsync(vm);

        var v = await _context.Ventas.FindAsync(resultado.Id);
        Assert.NotNull(v);
        // Debe ser cercana a hoy, no la fecha manipulada
        Assert.True(v!.FechaVenta >= DateTime.UtcNow.AddDays(-2),
            $"FechaVenta debería ser cercana a hoy. Fue: {v.FechaVenta}");
    }

    // -------------------------------------------------------------------------
    // Regresión: flujo legítimo Efectivo → no se rompe
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_FlujoLegitimo_EfectivoConEstadoCotizacion_Exitoso()
    {
        var (clienteId, detalle) = await SeedClienteYProducto();
        var vm = new VentaViewModel
        {
            ClienteId = clienteId,
            FechaVenta = DateTime.UtcNow,
            TipoPago = TipoPago.Efectivo,
            Estado = EstadoVenta.Cotizacion,
            Detalles = new List<VentaDetalleViewModel> { detalle }
        };

        var resultado = await _service.CreateAsync(vm);

        var v = await _context.Ventas.FindAsync(resultado.Id);
        Assert.NotNull(v);
        Assert.Equal(EstadoVenta.Cotizacion, v!.Estado);
        Assert.Equal(TipoPago.Efectivo, v.TipoPago);
    }
}
