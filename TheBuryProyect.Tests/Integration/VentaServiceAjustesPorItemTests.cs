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
using TheBuryProject.Services.Validators;
using TheBuryProject.Tests;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

public class VentaServiceAjustesPorItemTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    private static int _counter = 7000;

    public VentaServiceAjustesPorItemTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateAsync_TipoPagoGeneralGuardaDetalleSinPagoPropio()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();
        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id,
            TipoPago.Transferencia,
            new[] { DetalleVm(producto.Id, cantidad: 2) }));

        Assert.Equal(TipoPago.Transferencia, resultado.TipoPago);
        Assert.Equal(200m, resultado.Total);

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Null(detalle.TipoPago);
        Assert.Null(detalle.ProductoCondicionPagoPlanId);
        Assert.Null(detalle.PorcentajeAjustePlanAplicado);
        Assert.Null(detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task CreateAsync_IgnoraCamposLegacyPorDetalleSinAplicarAjusteNiBloquear()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id,
            TipoPago.TarjetaCredito,
            ajustePorcentaje: 25m,
            planActivo: false);
        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id,
            TipoPago.Efectivo,
            new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId) }));

        Assert.Equal(100m, resultado.Total);

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Null(detalle.TipoPago);
        Assert.Null(detalle.ProductoCondicionPagoPlanId);
        Assert.Null(detalle.PorcentajeAjustePlanAplicado);
        Assert.Null(detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task CreateAsync_NoBloqueaVentaNormalPorCondicionProducto()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();
        await SeedCondicionPagoAsync(producto.Id, TipoPago.Transferencia, permitido: false);
        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id,
            TipoPago.Transferencia,
            new[] { DetalleVm(producto.Id) }));

        Assert.Equal(TipoPago.Transferencia, resultado.TipoPago);
        Assert.Equal(100m, resultado.Total);
    }

    [Fact]
    public async Task Preview_IgnoraPagoPorDetalleLegacy()
    {
        var (apertura, _, producto) = await SeedBaseAsync();
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id,
            TipoPago.TarjetaCredito,
            ajustePorcentaje: 10m);
        var svc = BuildService(apertura);

        var result = await svc.CalcularTotalesPreviewAsync(
            new List<DetalleCalculoVentaRequest>
            {
                new()
                {
                    ProductoId = producto.Id,
                    Cantidad = 1,
                    PrecioUnitario = 100m,
                    Descuento = 0m,
                    TipoPago = TipoPago.TarjetaCredito,
                    ProductoCondicionPagoPlanId = planId
                }
            },
            0m,
            false);

        Assert.Equal(100m, result.Total);
        Assert.Equal(0m, result.AjusteItemsAplicado);
        Assert.Null(result.TotalConAjusteItems);
    }

    private VentaService BuildService(AperturaCaja apertura) =>
        new(
            _context,
            _mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserServicePagoGeneral(),
            null!,
            new StubCajaServicePagoGeneral(apertura),
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());

    private async Task<(AperturaCaja apertura, Cliente cliente, Producto producto)> SeedBaseAsync()
    {
        var n = Interlocked.Increment(ref _counter).ToString();

        var caja = new Caja
        {
            Codigo = $"CJ{n}",
            Nombre = $"Caja {n}",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Cajas.Add(caja);
        await _context.SaveChangesAsync();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "testuser",
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.AperturasCaja.Add(apertura);

        var categoria = new Categoria
        {
            Codigo = $"CA{n}",
            Nombre = $"Cat {n}",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        var marca = new Marca
        {
            Codigo = $"MA{n}",
            Nombre = $"Marca {n}",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "A",
            TipoDocumento = "DNI",
            NumeroDocumento = $"D{n}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Clientes.Add(cliente);

        var producto = new Producto
        {
            Nombre = $"Prod {n}",
            Codigo = $"P{n}",
            PrecioCompra = 50m,
            PrecioVenta = 100m,
            PorcentajeIVA = 0m,
            ComisionPorcentaje = 0m,
            StockActual = 100,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        return (apertura, cliente, producto);
    }

    private async Task<ProductoCondicionPago> SeedCondicionPagoAsync(
        int productoId,
        TipoPago tipoPago,
        bool permitido = true)
    {
        var condicion = new ProductoCondicionPago
        {
            ProductoId = productoId,
            TipoPago = tipoPago,
            Permitido = permitido,
            Activo = true,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.ProductoCondicionesPago.Add(condicion);
        await _context.SaveChangesAsync();
        return condicion;
    }

    private async Task<(int condicionId, int planId)> SeedCondicionPlanAsync(
        int productoId,
        TipoPago tipoPago,
        decimal ajustePorcentaje,
        bool planActivo = true)
    {
        var condicion = await SeedCondicionPagoAsync(productoId, tipoPago);
        var plan = new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 1,
            AjustePorcentaje = ajustePorcentaje,
            TipoAjuste = TipoAjustePagoPlan.Porcentaje,
            Activo = planActivo,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.ProductoCondicionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();
        return (condicion.Id, plan.Id);
    }

    private static VentaViewModel VentaVm(
        int clienteId,
        TipoPago tipoPagoGlobal,
        IEnumerable<VentaDetalleViewModel> detalles) => new()
    {
        ClienteId = clienteId,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = tipoPagoGlobal,
        Descuento = 0m,
        Detalles = detalles.ToList()
    };

    private static VentaDetalleViewModel DetalleVm(
        int productoId,
        int cantidad = 1,
        TipoPago? tipoPagoItem = null,
        int? planId = null) => new()
    {
        ProductoId = productoId,
        Cantidad = cantidad,
        PrecioUnitario = 0m,
        Descuento = 0m,
        TipoPago = tipoPagoItem,
        ProductoCondicionPagoPlanId = planId
    };
}

file sealed class StubCurrentUserServicePagoGeneral : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubCajaServicePagoGeneral : ICajaService
{
    private readonly AperturaCaja _apertura;

    public StubCajaServicePagoGeneral(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

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
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}
