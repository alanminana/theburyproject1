using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fases C/D/H: orden ML → venta interna idempotente con descuento de
/// stock canónico, liquidación a caja y devoluciones con decisión manual.
/// Usa el MovimientoStockService REAL para validar la integración de stock.
/// </summary>
public class MercadoLibreOrderServiceTests : IDisposable
{
    private sealed class FakeAuthService : IMercadoLibreAuthService
    {
        public bool EstaConfigurado => true;
        public string BuildAuthorizationUrl() => throw new NotSupportedException();
        public bool ValidarState(string? state) => throw new NotSupportedException();
        public Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult("token-test");
    }

    /// <summary>
    /// Fake de ICajaService: solo los miembros que usa la liquidación ML.
    /// </summary>
    private sealed class FakeCajaService : ICajaService
    {
        public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
        private readonly AppDbContext _context;

        public FakeCajaService(AppDbContext context) => _context = context;

        public AperturaCaja? AperturaActiva { get; set; }

        public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
            => Task.FromResult(AperturaActiva);

        public async Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario)
        {
            var movimiento = new MovimientoCaja
            {
                AperturaCajaId = model.AperturaCajaId,
                Tipo = model.Tipo,
                Concepto = model.Concepto,
                Monto = model.Monto,
                Descripcion = model.Descripcion,
                Referencia = model.Referencia,
                Observaciones = model.Observaciones,
                Usuario = usuario
            };

            _context.MovimientosCaja.Add(movimiento);
            await _context.SaveChangesAsync();
            return movimiento;
        }

        // Miembros no usados por el módulo ML
        public Task<List<Caja>> ObtenerTodasCajasAsync() => throw new NotSupportedException();
        public Task<Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotSupportedException();
        public Task<Caja> CrearCajaAsync(CajaViewModel model) => throw new NotSupportedException();
        public Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model) => throw new NotSupportedException();
        public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotSupportedException();
        public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotSupportedException();
        public Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario) => throw new NotSupportedException();
        public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotSupportedException();
        public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotSupportedException();
        public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotSupportedException();
        public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotSupportedException();
        public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotSupportedException();
        public Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotSupportedException();
        public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotSupportedException();
        public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotSupportedException();
        public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotSupportedException();
        public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotSupportedException();
        public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotSupportedException();
        public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotSupportedException();
        public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotSupportedException();
        public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotSupportedException();
        public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotSupportedException();
        public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotSupportedException();
        public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotSupportedException();
        public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotSupportedException();
        public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotSupportedException();
        public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotSupportedException();
        public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotSupportedException();
    }

    private readonly TestDbContextFactory _factory;
    private readonly AppDbContext _context;
    private readonly FakeMercadoLibreApiClient _api;
    private readonly FakeCajaService _caja;
    private readonly MercadoLibreOrderService _servicio;
    private readonly MercadoLibreConfiguracionService _configService;

    public MercadoLibreOrderServiceTests()
    {
        (_factory, _) = MercadoLibreTestDb.Create();

        // Contexto SCOPED compartido: el descuento de stock comparte transacción
        // con la creación de la venta (mismo patrón que producción).
        _context = _factory.CreateDbContext();
        _api = new FakeMercadoLibreApiClient();
        _caja = new FakeCajaService(_context);
        _configService = new MercadoLibreConfiguracionService(
            _factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        _servicio = new MercadoLibreOrderService(
            _context,
            _api,
            new FakeAuthService(),
            _configService,
            new MovimientoStockService(_context, NullLogger<MovimientoStockService>.Instance),
            new ProductoUnidadService(_context, NullLogger<ProductoUnidadService>.Instance),
            _caja,
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            NullLogger<MercadoLibreOrderService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // -----------------------------------------------------------------------
    // Seeds
    // -----------------------------------------------------------------------

    private async Task<(int AccountId, int ClienteId, int ProductoId, string ItemId)> SembrarBaseAsync(
        decimal stockInicial = 10m, bool vincularListing = true)
    {
        var cuenta = new MercadoLibreAccount
        {
            MeliUserId = Random.Shared.NextInt64(1, long.MaxValue),
            Nickname = "VENDEDOR",
            AccessTokenEncrypted = "x",
            RefreshTokenEncrypted = "x",
            Activa = true
        };
        _context.MercadoLibreAccounts.Add(cuenta);

        var cliente = new Cliente
        {
            NumeroDocumento = $"D{Random.Shared.Next(10000000, 99999999)}",
            Apellido = "Mercado",
            Nombre = "Libre",
            Telefono = "0",
            Domicilio = "Canal online"
        };
        _context.Clientes.Add(cliente);

        var marca = new Marca { Codigo = $"M-{Guid.NewGuid():N}"[..10], Nombre = "Marca", Activo = true };
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"P-{Guid.NewGuid():N}"[..10],
            Nombre = "Producto ML",
            CategoriaId = 1,
            MarcaId = marca.Id,
            PrecioCompra = 500m,
            PrecioVenta = 1100m,
            PorcentajeIVA = 21m,
            StockActual = stockInicial,
            Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var itemId = $"MLA{Random.Shared.Next(100000, 999999)}";

        _context.MercadoLibreListings.Add(new MercadoLibreListing
        {
            AccountId = cuenta.Id,
            ItemId = itemId,
            Titulo = "Listing orden",
            Precio = 1210m,
            Status = "active",
            ProductoId = vincularListing ? producto.Id : null
        });

        var config = await _configService.GetAsync();
        await using (var ctx = _factory.CreateDbContext())
        {
            var c = await ctx.MercadoLibreConfiguraciones.FirstAsync(x => x.Id == config.Id);
            c.ClienteMercadoLibreId = cliente.Id;
            c.AccountId = cuenta.Id;
            await ctx.SaveChangesAsync();
        }

        await _context.SaveChangesAsync();

        return (cuenta.Id, cliente.Id, producto.Id, itemId);
    }

    private async Task<int> SembrarOrdenAsync(
        string itemId, long meliOrderId, string status = "paid", int cantidad = 2, decimal precio = 1210m)
    {
        var orden = new MercadoLibreOrder
        {
            AccountId = (await _context.MercadoLibreAccounts.FirstAsync()).Id,
            MeliOrderId = meliOrderId,
            Status = status,
            TotalAmount = precio * cantidad,
            FechaCreacionUtc = DateTime.UtcNow.AddHours(-1)
        };

        orden.Items.Add(new MercadoLibreOrderItem
        {
            ItemId = itemId,
            Titulo = "Item de prueba",
            Cantidad = cantidad,
            PrecioUnitario = precio
        });

        _context.MercadoLibreOrders.Add(orden);
        await _context.SaveChangesAsync();

        return orden.Id;
    }

    private async Task<AperturaCaja> AbrirCajaParaUsuarioAsync(string usuario)
    {
        var caja = new Caja { Codigo = $"C-{Guid.NewGuid():N}"[..10], Nombre = "Caja test" };
        _context.Cajas.Add(caja);
        await _context.SaveChangesAsync();

        var apertura = new AperturaCaja { CajaId = caja.Id, MontoInicial = 0, UsuarioApertura = usuario };
        _context.AperturasCaja.Add(apertura);
        await _context.SaveChangesAsync();

        _caja.AperturaActiva = apertura;
        return apertura;
    }

    private static MeliOrderDto OrdenDto(long id, string itemId, string status = "paid", int cantidad = 2, decimal precio = 1210m) => new()
    {
        Id = id,
        Status = status,
        DateCreated = DateTimeOffset.UtcNow.AddHours(-2),
        TotalAmount = precio * cantidad,
        PaidAmount = precio * cantidad,
        CurrencyId = "ARS",
        Buyer = new MeliOrderBuyerDto { Id = 999, Nickname = "COMPRADOR" },
        OrderItems = new List<MeliOrderItemDto>
        {
            new()
            {
                Item = new MeliOrderItemRefDto { Id = itemId, Title = "Item de prueba" },
                Quantity = cantidad,
                UnitPrice = precio,
                SaleFee = 100m
            }
        },
        Shipping = new MeliOrderShippingDto { Id = 555 }
    };

    // -----------------------------------------------------------------------
    // QA local
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CrearOrdenSimulada_SinModoSimulacionNiDevelopment_Rechaza()
    {
        var config = await _configService.GetAsync();
        await using (var ctx = _factory.CreateDbContext())
        {
            var c = await ctx.MercadoLibreConfiguraciones.FirstAsync(x => x.Id == config.Id);
            c.ModoSimulacion = false;
            await ctx.SaveChangesAsync();
        }

        var resultado = await _servicio.CrearOrdenSimuladaAsync("tester");

        Assert.False(resultado.Ok);
        Assert.Contains("ModoSimulacion=true", resultado.Mensaje);
        Assert.Equal(0, await _context.MercadoLibreOrders.CountAsync());
    }

    [Fact]
    public async Task CrearOrdenSimulada_SinPublicacionVinculada_DevuelveMensaje()
    {
        await SembrarBaseAsync(vincularListing: false);

        var resultado = await _servicio.CrearOrdenSimuladaAsync("tester");

        Assert.False(resultado.Ok);
        Assert.Contains("Vinculá una publicación", resultado.Mensaje);
        Assert.Equal(0, await _context.MercadoLibreOrders.CountAsync());
    }

    [Fact]
    public async Task CrearOrdenSimulada_ConModoSimulacion_CreaOrdenLocalSinApi()
    {
        var (accountId, _, productoId, itemId) = await SembrarBaseAsync();

        var resultado = await _servicio.CrearOrdenSimuladaAsync("tester");

        Assert.True(resultado.Ok, resultado.Mensaje);
        Assert.NotNull(resultado.OrderId);
        Assert.Empty(_api.GetOrderCalls);

        var orden = await _context.MercadoLibreOrders
            .Include(o => o.Items)
            .SingleAsync(o => o.Id == resultado.OrderId);

        Assert.Equal(accountId, orden.AccountId);
        Assert.Equal("paid", orden.Status);
        Assert.Equal("QA SIMULADA", orden.BuyerNickname);
        Assert.Contains("\"simuladaQa\":true", orden.RawJson);

        var item = Assert.Single(orden.Items);
        Assert.Equal(itemId, item.ItemId);
        Assert.Equal(productoId, item.ProductoId);

        Assert.True(await _context.MercadoLibreSyncLogs.AnyAsync(l =>
            l.Operacion == "OrderQASimulada" && l.Exito));
    }

    [Fact]
    public async Task CrearOrdenOperativaSimulada_ConModoSimulacion_CreaOrdenLocalProcesableSinApi()
    {
        var (accountId, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);

        var resultadoOrden = await _servicio.CrearOrdenOperativaSimuladaAsync("tester");

        Assert.True(resultadoOrden.Ok, resultadoOrden.Mensaje);
        Assert.NotNull(resultadoOrden.OrderId);
        Assert.Empty(_api.GetOrderCalls);
        Assert.Null(_api.UltimoAccessTokenUsado);

        var orden = await _context.MercadoLibreOrders
            .Include(o => o.Items)
            .SingleAsync(o => o.Id == resultadoOrden.OrderId);

        Assert.Equal(accountId, orden.AccountId);
        Assert.Equal("paid", orden.Status);
        Assert.Equal("QA OPERATIVA LOCAL", orden.BuyerNickname);
        Assert.Contains("\"simuladaOperativaLocal\":true", orden.RawJson);
        Assert.DoesNotContain("\"simuladaQa\":true", orden.RawJson);

        var item = Assert.Single(orden.Items);
        Assert.Equal(itemId, item.ItemId);
        Assert.Equal(productoId, item.ProductoId);
    }

    [Fact]
    public async Task CrearVenta_OrdenOperativaSimulada_CreaVentaStockSinCajaNiApi()
    {
        var (_, _, productoId, _) = await SembrarBaseAsync(stockInicial: 10m);
        var resultadoOrden = await _servicio.CrearOrdenOperativaSimuladaAsync("tester");

        var resultadoVenta = await _servicio.CrearVentaInternaAsync(resultadoOrden.OrderId!.Value, "tester");
        var segundoIntento = await _servicio.CrearVentaInternaAsync(resultadoOrden.OrderId!.Value, "tester");

        Assert.True(resultadoVenta.VentaCreada, resultadoVenta.Mensaje);
        Assert.False(segundoIntento.VentaCreada);
        Assert.Contains("ya tiene una venta", segundoIntento.Mensaje);
        Assert.Empty(_api.GetOrderCalls);
        Assert.Null(_api.UltimoAccessTokenUsado);

        var venta = await _context.Ventas
            .Include(v => v.Detalles)
            .SingleAsync();

        Assert.Equal(EstadoVenta.Confirmada, venta.Estado);
        Assert.Equal(TipoPago.MercadoPago, venta.TipoPago);
        Assert.Null(venta.AperturaCajaId);
        Assert.Single(venta.Detalles);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == resultadoOrden.OrderId);
        Assert.Equal(venta.Id, orden.VentaId);
        Assert.Equal(MercadoLibreOrderEstadoInterno.VentaCreada, orden.EstadoInterno);
        Assert.Null(orden.MovimientoCajaId);
        Assert.Null(orden.FechaLiquidacionUtc);

        Assert.Equal(1, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida));
        Assert.Equal(9m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);

        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task CrearVenta_OrdenSimulada_NoCreaVentaNiDescuentaStock()
    {
        var (_, _, productoId, _) = await SembrarBaseAsync(stockInicial: 10m);
        var resultadoOrden = await _servicio.CrearOrdenSimuladaAsync("tester");

        var resultadoVenta = await _servicio.CrearVentaInternaAsync(resultadoOrden.OrderId!.Value, "tester");

        Assert.False(resultadoVenta.VentaCreada);
        Assert.Contains("Orden QA simulada", resultadoVenta.Mensaje);
        Assert.Equal(0, await _context.Ventas.CountAsync());
        Assert.Equal(10m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
    }

    // -----------------------------------------------------------------------
    // Fase C — Venta interna
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CrearVenta_OrdenPaga_CreaVentaConfirmadaYDescuentaStock()
    {
        var (_, clienteId, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 1001, cantidad: 2, precio: 1210m);

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.True(resultado.VentaCreada);
        Assert.NotNull(resultado.VentaNumero);

        var venta = await _context.Ventas.Include(v => v.Detalles).SingleAsync();
        Assert.Equal(EstadoVenta.Confirmada, venta.Estado);
        Assert.Equal(TipoPago.MercadoPago, venta.TipoPago);
        Assert.Equal(clienteId, venta.ClienteId);
        Assert.Null(venta.AperturaCajaId); // ML no toca caja al crear la venta
        Assert.Equal(2420m, venta.Total);

        var detalle = Assert.Single(venta.Detalles);
        Assert.Equal(productoId, detalle.ProductoId);
        Assert.Equal(2, detalle.Cantidad);
        Assert.Equal(1210m, detalle.PrecioUnitario);
        Assert.Equal(21m, detalle.PorcentajeIVA);
        Assert.Equal(1000m, detalle.PrecioUnitarioNeto); // 1210 / 1.21
        Assert.Equal(500m, detalle.CostoUnitarioAlMomento);

        var producto = await _context.Productos.SingleAsync(p => p.Id == productoId);
        Assert.Equal(8m, producto.StockActual); // 10 - 2

        var movimiento = await _context.MovimientosStock.SingleAsync();
        Assert.Equal(TipoMovimiento.Salida, movimiento.Tipo);
        Assert.Equal(2m, movimiento.Cantidad);

        var orden = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreOrderEstadoInterno.VentaCreada, orden.EstadoInterno);
        Assert.Equal(venta.Id, orden.VentaId);

        Assert.True(await _context.MercadoLibreSyncLogs.AnyAsync(l => l.Operacion == "OrderToVenta" && l.Exito));
    }

    [Fact]
    public async Task CrearVenta_DosVeces_NoDuplicaVentaNiStock()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 1002, cantidad: 2);

        var primera = await _servicio.CrearVentaInternaAsync(orderId, "tester");
        var segunda = await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.True(primera.VentaCreada);
        Assert.False(segunda.VentaCreada);
        Assert.Contains("ya tiene una venta", segunda.Mensaje);

        Assert.Equal(1, await _context.Ventas.CountAsync());
        Assert.Equal(1, await _context.MovimientosStock.CountAsync());

        var producto = await _context.Productos.SingleAsync(p => p.Id == productoId);
        Assert.Equal(8m, producto.StockActual); // descontado UNA sola vez
    }

    [Fact]
    public async Task CrearVenta_ItemSinVincular_QuedaPendienteSinTocarStock()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m, vincularListing: false);
        var orderId = await SembrarOrdenAsync(itemId, 1003);

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.False(resultado.VentaCreada);
        Assert.Equal(MercadoLibreOrderEstadoInterno.PendienteVinculacion, resultado.EstadoInterno);
        Assert.Contains("sin producto vinculado", resultado.Mensaje);

        Assert.Equal(0, await _context.Ventas.CountAsync());
        Assert.Equal(10m, (await _context.Productos.SingleAsync(p => p.Id == productoId)).StockActual);
    }

    [Fact]
    public async Task CrearVenta_OrdenNoPaga_NoCreaVenta()
    {
        var (_, _, _, itemId) = await SembrarBaseAsync();
        var orderId = await SembrarOrdenAsync(itemId, 1004, status: "cancelled");

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.False(resultado.VentaCreada);
        Assert.Contains("pagas", resultado.Mensaje);
        Assert.Equal(0, await _context.Ventas.CountAsync());
    }

    [Fact]
    public async Task CrearVenta_StockInsuficiente_MarcaErrorSinVentaNiDescuento()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 1m);
        var orderId = await SembrarOrdenAsync(itemId, 1005, cantidad: 5);

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.False(resultado.VentaCreada);
        Assert.Equal(MercadoLibreOrderEstadoInterno.Error, resultado.EstadoInterno);
        Assert.Contains("Stock insuficiente", resultado.Mensaje);

        Assert.Equal(0, await _context.Ventas.CountAsync());
        Assert.Equal(1m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreOrderEstadoInterno.Error, orden.EstadoInterno);
        Assert.Contains("Stock insuficiente", orden.ErrorProcesamiento);
    }

    // -----------------------------------------------------------------------
    // Fase C — Importación idempotente
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ImportarOrden_DosVeces_UnSoloRegistroActualizado()
    {
        var (accountId, _, _, itemId) = await SembrarBaseAsync();

        _api.Ordenes[2001] = OrdenDto(2001, itemId);
        await _servicio.ImportarOrdenAsync(accountId, 2001);

        _api.Ordenes[2001] = OrdenDto(2001, itemId, precio: 1300m); // ML cambió el precio
        await _servicio.ImportarOrdenAsync(accountId, 2001);

        var orden = await _context.MercadoLibreOrders.Include(o => o.Items).SingleAsync();
        Assert.Equal(2001, orden.MeliOrderId);
        Assert.Equal(2600m, orden.TotalAmount);
        Assert.Single(orden.Items.Where(i => !i.IsDeleted));
        Assert.Equal(200m, orden.MontoComision); // sale_fee 100 × 2 unidades
        Assert.Equal("COMPRADOR", orden.BuyerNickname);
    }

    [Fact]
    public async Task ImportarOrden_ConVentaAutomatica_CreaVentaYStock()
    {
        var (accountId, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);

        await using (var ctx = _factory.CreateDbContext())
        {
            var c = await ctx.MercadoLibreConfiguraciones.FirstAsync();
            c.CrearVentaAutomatica = true;
            await ctx.SaveChangesAsync();
        }

        _api.Ordenes[2002] = OrdenDto(2002, itemId, cantidad: 3);
        await _servicio.ImportarOrdenAsync(accountId, 2002);

        var orden = await _context.MercadoLibreOrders.SingleAsync();
        Assert.Equal(MercadoLibreOrderEstadoInterno.VentaCreada, orden.EstadoInterno);
        Assert.NotNull(orden.VentaId);
        Assert.Equal(7m, (await _context.Productos.SingleAsync(p => p.Id == productoId)).StockActual);
    }

    [Fact]
    public async Task ImportarOrden_CancelacionConVentaCreada_MarcaDevolucionPendienteSinTocarStock()
    {
        var (accountId, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);

        _api.Ordenes[2003] = OrdenDto(2003, itemId);
        await _servicio.ImportarOrdenAsync(accountId, 2003);

        var orden = await _context.MercadoLibreOrders.SingleAsync();
        await _servicio.CrearVentaInternaAsync(orden.Id, "tester");

        // Llega la cancelación desde ML
        _api.Ordenes[2003] = OrdenDto(2003, itemId, status: "cancelled");
        await _servicio.ImportarOrdenAsync(accountId, 2003);

        var actualizada = await _context.MercadoLibreOrders.SingleAsync();
        Assert.Equal(MercadoLibreDevolucionEstado.PendienteRevision, actualizada.DevolucionEstado);

        // El stock NO se reingresó automáticamente.
        Assert.Equal(8m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
    }

    // -----------------------------------------------------------------------
    // Fase D — Liquidación
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Liquidacion_RegistraIngresoEnCajaYMarcaLiquidada()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync();
        var orderId = await SembrarOrdenAsync(itemId, 3001, cantidad: 2, precio: 1210m);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");

        var ventaId = (await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).VentaId!.Value;
        var stockAntes = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual;
        var ventasAntes = await _context.Ventas.CountAsync();
        var salidasStockAntes = await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida);

        var ordenParaLiquidar = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        ordenParaLiquidar.NetoEstimado = 2200m;
        await _context.SaveChangesAsync();

        // Caja abierta del usuario
        await AbrirCajaParaUsuarioAsync("tester");

        await _servicio.RegistrarLiquidacionAsync(orderId, 2100m, "tester");

        var orden = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreOrderEstadoInterno.Liquidada, orden.EstadoInterno);
        Assert.Equal(2100m, orden.NetoReal);
        Assert.NotNull(orden.MovimientoCajaId);

        var movimiento = await _context.MovimientosCaja.SingleAsync();
        Assert.Equal(TipoMovimientoCaja.Ingreso, movimiento.Tipo);
        Assert.Equal(ConceptoMovimientoCaja.LiquidacionMercadoLibre, movimiento.Concepto);
        Assert.Equal(TipoPago.MercadoPago, movimiento.TipoPago);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, movimiento.EstadoAcreditacion);
        Assert.Equal(ventaId, movimiento.VentaId);
        Assert.Equal(orderId, movimiento.ReferenciaId);
        Assert.Equal("Mercado Pago", movimiento.MedioPagoDetalle);
        Assert.Equal(2100m, movimiento.Monto);
        Assert.Contains("3001", movimiento.Descripcion);
        Assert.Contains("diferencia", movimiento.Observaciones);
        Assert.Contains("-100", movimiento.Observaciones);

        Assert.Equal(ventasAntes, await _context.Ventas.CountAsync());
        Assert.Equal(salidasStockAntes, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida));
        Assert.Equal(stockAntes, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);

        // Idempotencia: una segunda liquidación se rechaza.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _servicio.RegistrarLiquidacionAsync(orderId, 2100m, "tester"));

        Assert.Single(await _context.MovimientosCaja.ToListAsync());
    }

    [Fact]
    public async Task Liquidacion_SinCajaAbierta_Lanza()
    {
        var (_, _, _, itemId) = await SembrarBaseAsync();
        var orderId = await SembrarOrdenAsync(itemId, 3002);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");

        _caja.AperturaActiva = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _servicio.RegistrarLiquidacionAsync(orderId, 1000m, "tester"));

        Assert.Contains("caja abierta", ex.Message);
    }

    // -----------------------------------------------------------------------
    // Fase H — Devolución con decisión manual
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Liquidacion_SinVentaInterna_Lanza()
    {
        var (_, _, _, itemId) = await SembrarBaseAsync();
        var orderId = await SembrarOrdenAsync(itemId, 3003);
        await AbrirCajaParaUsuarioAsync("tester");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _servicio.RegistrarLiquidacionAsync(orderId, 1000m, "tester"));

        Assert.Contains("venta interna creada", ex.Message);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task Liquidacion_OrdenQaSimulada_Lanza()
    {
        await SembrarBaseAsync();
        var resultadoOrden = await _servicio.CrearOrdenSimuladaAsync("tester");
        await AbrirCajaParaUsuarioAsync("tester");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _servicio.RegistrarLiquidacionAsync(resultadoOrden.OrderId!.Value, 1000m, "tester"));

        Assert.Contains("QA simulada", ex.Message);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task Liquidacion_OrdenOperativaSimulada_MarcaMovimientoSimuladoSinApiDuplicadosNiStock()
    {
        var (_, _, productoId, _) = await SembrarBaseAsync(stockInicial: 10m);
        var resultadoOrden = await _servicio.CrearOrdenOperativaSimuladaAsync("tester");
        var resultadoVenta = await _servicio.CrearVentaInternaAsync(resultadoOrden.OrderId!.Value, "tester");
        Assert.True(resultadoVenta.VentaCreada, resultadoVenta.Mensaje);

        var ventaId = resultadoVenta.VentaId!.Value;
        var stockAntes = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual;
        var ventasAntes = await _context.Ventas.CountAsync();
        var salidasStockAntes = await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida);

        await AbrirCajaParaUsuarioAsync("tester");
        await _servicio.RegistrarLiquidacionAsync(resultadoOrden.OrderId!.Value, 900m, "tester");

        Assert.Empty(_api.GetOrderCalls);
        Assert.Null(_api.UltimoAccessTokenUsado);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == resultadoOrden.OrderId);
        Assert.Equal(MercadoLibreOrderEstadoInterno.Liquidada, orden.EstadoInterno);
        Assert.Equal(900m, orden.NetoReal);
        Assert.NotNull(orden.MovimientoCajaId);

        var movimiento = await _context.MovimientosCaja.SingleAsync();
        Assert.Equal(ConceptoMovimientoCaja.LiquidacionMercadoLibre, movimiento.Concepto);
        Assert.Equal(TipoPago.MercadoPago, movimiento.TipoPago);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, movimiento.EstadoAcreditacion);
        Assert.Equal(ventaId, movimiento.VentaId);
        Assert.Contains("[SIMULACION]", movimiento.Descripcion);
        Assert.Contains("orden operativa simulada local", movimiento.Observaciones);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _servicio.RegistrarLiquidacionAsync(resultadoOrden.OrderId!.Value, 900m, "tester"));

        Assert.Equal(1, await _context.MovimientosCaja.CountAsync());
        Assert.Equal(ventasAntes, await _context.Ventas.CountAsync());
        Assert.Equal(salidasStockAntes, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida));
        Assert.Equal(stockAntes, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
    }

    // -----------------------------------------------------------------------
    // Fase H - Envios / tracking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ActualizarEnvio_GuardaTrackingCostoEstadoRawYNoTocaVentaStockCaja()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4101, cantidad: 2, precio: 1210m);
        var fechaDespacho = DateTimeOffset.UtcNow.AddHours(-3);
        var fechaActualizacion = DateTimeOffset.UtcNow.AddMinutes(-15);

        var ordenSeed = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        ordenSeed.ShipmentId = 555;
        ordenSeed.MontoComision = 200m;
        ordenSeed.MontoEnvio = 100m;
        ordenSeed.NetoEstimado = 2120m;
        await _context.SaveChangesAsync();

        _api.Shipments[555] = new MeliShipmentDto
        {
            Id = 555,
            Status = "shipped",
            Substatus = "in_transit",
            TrackingNumber = "TRK-555",
            TrackingMethod = "Correo Test",
            Mode = "me2",
            LogisticType = "cross_docking",
            LastUpdated = fechaActualizacion,
            StatusHistory = new MeliShipmentStatusHistoryDto { DateShipped = fechaDespacho },
            ShippingOption = new MeliShippingOptionDto { ListCost = 350m },
            RawJson = "{\"id\":555,\"status\":\"shipped\",\"tracking_number\":\"TRK-555\"}"
        };

        var ventasAntes = await _context.Ventas.CountAsync();
        var stockAntes = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual;
        var stockMovimientosAntes = await _context.MovimientosStock.CountAsync();
        var cajaAntes = await _context.MovimientosCaja.CountAsync();

        await _servicio.ActualizarEnvioAsync(orderId);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal("shipped", orden.ShipmentStatus);
        Assert.Equal("in_transit", orden.ShipmentSubStatus);
        Assert.Equal("TRK-555", orden.TrackingNumber);
        Assert.Equal("Correo Test", orden.TrackingMethod);
        Assert.Equal("me2", orden.ShippingMode);
        Assert.Equal("cross_docking", orden.ShippingType);
        Assert.Equal(MercadoLibreShipmentEstadoInterno.EnCamino, orden.EstadoEnvioInterno);
        Assert.Equal(fechaDespacho.UtcDateTime, orden.FechaDespachoUtc);
        Assert.Equal(fechaActualizacion.UtcDateTime, orden.FechaUltimaActualizacionEnvioUtc);
        Assert.Contains("\"tracking_number\":\"TRK-555\"", orden.RawShipmentJson);
        Assert.Equal(350m, orden.MontoEnvio);
        Assert.Equal(1870m, orden.NetoEstimado);

        Assert.Equal(ventasAntes, await _context.Ventas.CountAsync());
        Assert.Equal(stockMovimientosAntes, await _context.MovimientosStock.CountAsync());
        Assert.Equal(stockAntes, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(cajaAntes, await _context.MovimientosCaja.CountAsync());

        var log = await _context.MercadoLibreSyncLogs.AsNoTracking()
            .SingleAsync(l => l.Operacion == "ShipmentUpdate" && l.Exito);
        Assert.Contains("555", log.Detalle);
        Assert.DoesNotContain("token-test", log.Detalle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SimularEnvio_ConModoSimulacion_NoLlamaApiNiTocaVentaStockCaja()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4102, cantidad: 2, precio: 1210m);

        var stockAntes = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual;

        var resultado = await _servicio.SimularEnvioAsync(orderId, "despachado", "tester");

        Assert.True(resultado.Ok, resultado.Mensaje);
        Assert.Null(_api.UltimoAccessTokenUsado);
        Assert.Empty(_api.GetOrderCalls);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.NotNull(orden.ShipmentId);
        Assert.Equal("shipped", orden.ShipmentStatus);
        Assert.Equal(MercadoLibreShipmentEstadoInterno.EnCamino, orden.EstadoEnvioInterno);
        Assert.StartsWith("QA-", orden.TrackingNumber);
        Assert.Contains("\"simulada\":true", orden.RawShipmentJson);

        Assert.Equal(0, await _context.Ventas.CountAsync());
        Assert.Equal(0, await _context.MovimientosStock.CountAsync());
        Assert.Equal(stockAntes, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task ActualizarEnvio_Entregado_NoLiquidaCajaAutomaticamente()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4103, cantidad: 2, precio: 1210m);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");

        var stockAntes = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual;
        var salidasAntes = await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida);
        var ordenSeed = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        ordenSeed.ShipmentId = 777;
        await _context.SaveChangesAsync();

        _api.Shipments[777] = new MeliShipmentDto
        {
            Id = 777,
            Status = "delivered",
            Substatus = "receiver_absent",
            TrackingNumber = "TRK-777",
            StatusHistory = new MeliShipmentStatusHistoryDto
            {
                DateDelivered = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
        };

        await _servicio.ActualizarEnvioAsync(orderId);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreShipmentEstadoInterno.Entregado, orden.EstadoEnvioInterno);
        Assert.Equal(MercadoLibreOrderEstadoInterno.VentaCreada, orden.EstadoInterno);
        Assert.Null(orden.MovimientoCajaId);
        Assert.Null(orden.FechaLiquidacionUtc);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
        Assert.Equal(salidasAntes, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida));
        Assert.Equal(stockAntes, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
    }

    [Fact]
    public async Task ActualizarEnvio_Cancelado_NoReingresaStockAutomaticamente()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4104, cantidad: 2, precio: 1210m);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");

        var stockDespuesVenta = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual;
        var ordenSeed = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        ordenSeed.ShipmentId = 888;
        await _context.SaveChangesAsync();

        _api.Shipments[888] = new MeliShipmentDto
        {
            Id = 888,
            Status = "cancelled",
            Substatus = "returning_to_sender",
            TrackingNumber = "TRK-888"
        };

        await _servicio.ActualizarEnvioAsync(orderId);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreShipmentEstadoInterno.Cancelado, orden.EstadoEnvioInterno);
        Assert.Equal(MercadoLibreDevolucionEstado.Ninguna, orden.DevolucionEstado);
        Assert.Equal(stockDespuesVenta, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task ActualizarEnvio_EstadoDesconocido_QuedaVisibleComoErrorControlado()
    {
        var (_, _, _, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4105, cantidad: 1, precio: 1210m);

        var ordenSeed = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        ordenSeed.ShipmentId = 999;
        await _context.SaveChangesAsync();

        _api.Shipments[999] = new MeliShipmentDto
        {
            Id = 999,
            Status = "alien_state",
            Substatus = "odd_substatus"
        };

        await _servicio.ActualizarEnvioAsync(orderId);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreShipmentEstadoInterno.Desconocido, orden.EstadoEnvioInterno);
        Assert.Equal("alien_state", orden.ShipmentStatus);
        Assert.Equal("odd_substatus", orden.ShipmentSubStatus);

        var detalle = await _servicio.GetOrdenAsync(orderId);
        Assert.NotNull(detalle);
        Assert.Equal(MercadoLibreShipmentEstadoInterno.Desconocido, detalle.EstadoEnvioInterno);
        Assert.True(detalle.EnvioRequiereAtencion);
    }

    [Fact]
    public async Task Devolucion_ReingresoManual_RestauraStockUnaSolaVez()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4001, cantidad: 2);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.Equal(8m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);

        await _servicio.DecidirDevolucionAsync(
            orderId, MercadoLibreDevolucionEstado.StockReingresado, "producto OK", "tester");

        var producto = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId);
        Assert.Equal(10m, producto.StockActual);

        Assert.Equal(1, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));

        // No se puede reingresar dos veces.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _servicio.DecidirDevolucionAsync(
            orderId, MercadoLibreDevolucionEstado.StockReingresado, null, "tester"));
    }

    [Fact]
    public async Task Devolucion_DecisionNoReingresa_NoTocaStock()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4002, cantidad: 2);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");

        await _servicio.DecidirDevolucionAsync(
            orderId, MercadoLibreDevolucionEstado.Danado, "llegó roto", "tester");

        var orden = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreDevolucionEstado.Danado, orden.DevolucionEstado);
        Assert.Equal("llegó roto", orden.DevolucionNota);

        Assert.Equal(8m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));
    }

    [Fact]
    public async Task SimularClaim_ConModoSimulacion_CreaPendienteLocalSinApiNiStockCaja()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4201, cantidad: 2);

        var resultado = await _servicio.SimularClaimAsync(
            orderId,
            MercadoLibreClaimTipo.Devolucion,
            "Comprador inicio devolucion",
            "tester");

        Assert.True(resultado.Ok, resultado.Mensaje);
        Assert.Empty(_api.GetOrderCalls);
        Assert.Null(_api.UltimoAccessTokenUsado);

        var claim = await _context.MercadoLibreClaims.AsNoTracking().SingleAsync();
        Assert.Equal(orderId, claim.MercadoLibreOrderId);
        Assert.Equal(MercadoLibreClaimTipo.Devolucion, claim.Tipo);
        Assert.Equal(MercadoLibreClaimEstado.PendienteRevision, claim.Estado);
        Assert.Equal(MercadoLibreClaimAccionStock.NoReingresar, claim.AccionStock);
        Assert.Equal(MercadoLibreClaimAccionEconomica.SinImpacto, claim.AccionEconomica);
        Assert.True(claim.EsSimuladoLocal);
        Assert.Contains("simuladoLocal", claim.RawJson);

        var orden = await _context.MercadoLibreOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(MercadoLibreDevolucionEstado.PendienteRevision, orden.DevolucionEstado);

        Assert.Equal(10m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.MovimientosStock.CountAsync());
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task SimularClaim_SinModoSimulacionNiDevelopment_Rechaza()
    {
        var (_, _, _, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4202, cantidad: 1);

        var config = await _configService.GetAsync();
        await using (var ctx = _factory.CreateDbContext())
        {
            var c = await ctx.MercadoLibreConfiguraciones.FirstAsync(x => x.Id == config.Id);
            c.ModoSimulacion = false;
            await ctx.SaveChangesAsync();
        }

        var resultado = await _servicio.SimularClaimAsync(
            orderId,
            MercadoLibreClaimTipo.Reclamo,
            "QA bloqueado",
            "tester");

        Assert.False(resultado.Ok);
        Assert.Contains("ModoSimulacion=true", resultado.Mensaje);
        Assert.Equal(0, await _context.MercadoLibreClaims.CountAsync());
        Assert.Empty(_api.GetOrderCalls);
    }

    [Fact]
    public async Task ResolverClaim_NoReingresar_NoTocaStockNiCaja()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4203, cantidad: 2);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");
        var claimId = (await _servicio.SimularClaimAsync(
            orderId, MercadoLibreClaimTipo.Devolucion, "Producto no vuelve", "tester")).OrderId!.Value;
        var claim = await _context.MercadoLibreClaims.SingleAsync(c => c.MercadoLibreOrderId == claimId);

        await _servicio.ResolverClaimAsync(
            claim.Id,
            MercadoLibreClaimAccionStock.NoReingresar,
            MercadoLibreClaimAccionEconomica.SinImpacto,
            "No se reingresa por decision manual",
            "Sin impacto operativo",
            "tester");

        var resuelto = await _context.MercadoLibreClaims.AsNoTracking().SingleAsync(c => c.Id == claim.Id);
        Assert.Equal(MercadoLibreClaimEstado.Resuelto, resuelto.Estado);
        Assert.Equal(MercadoLibreClaimAccionStock.NoReingresar, resuelto.AccionStock);
        Assert.Null(resuelto.MovimientoStockId);
        Assert.Null(resuelto.MovimientoCajaId);

        Assert.Equal(8m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task ResolverClaim_Reingresar_CreaMovimientoStockUnaSolaVez()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4204, cantidad: 2);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");
        var resultadoClaim = await _servicio.SimularClaimAsync(
            orderId, MercadoLibreClaimTipo.Devolucion, "Producto OK", "tester");
        var claim = await _context.MercadoLibreClaims.SingleAsync(c => c.MercadoLibreOrderId == resultadoClaim.OrderId!.Value);

        await _servicio.ResolverClaimAsync(
            claim.Id,
            MercadoLibreClaimAccionStock.ReingresarStock,
            MercadoLibreClaimAccionEconomica.SinImpacto,
            "Producto revisado OK",
            "Reingreso manual",
            "tester");

        var resuelto = await _context.MercadoLibreClaims.AsNoTracking().SingleAsync(c => c.Id == claim.Id);
        Assert.Equal(MercadoLibreClaimEstado.Resuelto, resuelto.Estado);
        Assert.Equal(MercadoLibreClaimAccionStock.ReingresarStock, resuelto.AccionStock);
        Assert.NotNull(resuelto.MovimientoStockId);

        Assert.Equal(10m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(1, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _servicio.ResolverClaimAsync(
            claim.Id,
            MercadoLibreClaimAccionStock.ReingresarStock,
            MercadoLibreClaimAccionEconomica.SinImpacto,
            "Segundo intento",
            null,
            "tester"));

        Assert.Equal(10m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(1, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task ResolverClaim_Danado_NoVuelveAStockDisponible()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4205, cantidad: 2);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");
        var resultadoClaim = await _servicio.SimularClaimAsync(
            orderId, MercadoLibreClaimTipo.Reclamo, "Producto danado", "tester");
        var claim = await _context.MercadoLibreClaims.SingleAsync(c => c.MercadoLibreOrderId == resultadoClaim.OrderId!.Value);

        await _servicio.ResolverClaimAsync(
            claim.Id,
            MercadoLibreClaimAccionStock.Danado,
            MercadoLibreClaimAccionEconomica.SinImpacto,
            "Producto danado, no disponible",
            null,
            "tester");

        Assert.Equal(8m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task ResolverClaim_DevolucionEconomicaPendiente_NoCreaCajaAutomatica()
    {
        var (_, _, _, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4206, cantidad: 1);
        await _servicio.CrearVentaInternaAsync(orderId, "tester");
        var resultadoClaim = await _servicio.SimularClaimAsync(
            orderId, MercadoLibreClaimTipo.Devolucion, "Reembolso pendiente", "tester");
        var claim = await _context.MercadoLibreClaims.SingleAsync(c => c.MercadoLibreOrderId == resultadoClaim.OrderId!.Value);

        await _servicio.ResolverClaimAsync(
            claim.Id,
            MercadoLibreClaimAccionStock.NoReingresar,
            MercadoLibreClaimAccionEconomica.DevolucionPendiente,
            "Reembolso se gestiona fuera de caja ERP",
            "Pendiente MercadoPago",
            "tester");

        var resuelto = await _context.MercadoLibreClaims.AsNoTracking().SingleAsync(c => c.Id == claim.Id);
        Assert.Equal(MercadoLibreClaimAccionEconomica.DevolucionPendiente, resuelto.AccionEconomica);
        Assert.Null(resuelto.MovimientoCajaId);
        Assert.Equal(0, await _context.MovimientosCaja.CountAsync());
    }

    [Fact]
    public async Task ResolverClaim_SinVentaAsociada_BloqueaReingreso()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 10m);
        var orderId = await SembrarOrdenAsync(itemId, 4207, cantidad: 2);
        var resultadoClaim = await _servicio.SimularClaimAsync(
            orderId, MercadoLibreClaimTipo.Devolucion, "Sin venta interna", "tester");
        var claim = await _context.MercadoLibreClaims.SingleAsync(c => c.MercadoLibreOrderId == resultadoClaim.OrderId!.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _servicio.ResolverClaimAsync(
            claim.Id,
            MercadoLibreClaimAccionStock.ReingresarStock,
            MercadoLibreClaimAccionEconomica.SinImpacto,
            "No debe reingresar",
            null,
            "tester"));

        var pendiente = await _context.MercadoLibreClaims.AsNoTracking().SingleAsync(c => c.Id == claim.Id);
        Assert.Equal(MercadoLibreClaimEstado.PendienteRevision, pendiente.Estado);
        Assert.Equal(10m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Entrada));
    }

    // -----------------------------------------------------------------------
    // Checkpoint 4/7 — Trazabilidad: unidades físicas en órdenes ML
    // -----------------------------------------------------------------------

    private async Task MarcarTrazableAsync(int productoId)
    {
        var producto = await _context.Productos.FirstAsync(p => p.Id == productoId);
        producto.RequiereNumeroSerie = true;
        await _context.SaveChangesAsync();
    }

    private async Task<List<int>> SembrarUnidadesAsync(int productoId, int cantidad)
    {
        var ids = new List<int>();

        for (var i = 0; i < cantidad; i++)
        {
            var unidad = new ProductoUnidad
            {
                ProductoId = productoId,
                CodigoInternoUnidad = $"U-{productoId}-{i + 1}",
                Estado = EstadoUnidad.EnStock,
                FechaIngreso = DateTime.UtcNow.AddDays(-cantidad + i) // orden FIFO determinista
            };
            _context.ProductoUnidades.Add(unidad);
            await _context.SaveChangesAsync();
            ids.Add(unidad.Id);
        }

        return ids;
    }

    [Fact]
    public async Task CrearVenta_TrazableConUnidades_AsignaFifoYMarcaVendidas()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 5m);
        await MarcarTrazableAsync(productoId);
        var unidades = await SembrarUnidadesAsync(productoId, 3);
        var orderId = await SembrarOrdenAsync(itemId, 5001, cantidad: 2);

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.True(resultado.VentaCreada);

        // Trazable: una línea de venta por unidad física, cantidad 1 cada una.
        var venta = await _context.Ventas.Include(v => v.Detalles).SingleAsync();
        Assert.Equal(2, venta.Detalles.Count);
        Assert.All(venta.Detalles, d => Assert.Equal(1, d.Cantidad));

        // FIFO: las dos unidades más antiguas.
        var asignadas = venta.Detalles.Select(d => d.ProductoUnidadId!.Value).OrderBy(x => x).ToList();
        Assert.Equal(new[] { unidades[0], unidades[1] }.OrderBy(x => x).ToList(), asignadas);

        Assert.Equal(2, await _context.ProductoUnidades.CountAsync(u => u.Estado == EstadoUnidad.Vendida));
        Assert.Equal(EstadoUnidad.EnStock,
            (await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidades[2])).Estado);

        // Stock agregado descontado UNA sola vez (2 unidades).
        Assert.Equal(3m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);

        // Auditoría en la línea de la orden.
        var item = await _context.MercadoLibreOrderItems.SingleAsync();
        Assert.Equal($"{unidades[0]},{unidades[1]}", item.UnidadesAsignadas);
    }

    [Fact]
    public async Task CrearVenta_TrazableSinUnidadesSuficientes_QuedaPendienteAsignarUnidad()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 5m);
        await MarcarTrazableAsync(productoId);
        await SembrarUnidadesAsync(productoId, 1); // 1 unidad para cantidad 2
        var orderId = await SembrarOrdenAsync(itemId, 5002, cantidad: 2);

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");

        Assert.False(resultado.VentaCreada);
        Assert.Equal(MercadoLibreOrderEstadoInterno.PendienteAsignarUnidad, resultado.EstadoInterno);

        // Nada se tocó: ni venta, ni stock, ni unidades.
        Assert.Equal(0, await _context.Ventas.CountAsync());
        Assert.Equal(0, await _context.MovimientosStock.CountAsync());
        Assert.Equal(5m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == productoId)).StockActual);
        Assert.Equal(0, await _context.ProductoUnidades.CountAsync(u => u.Estado == EstadoUnidad.Vendida));

        var orden = await _context.MercadoLibreOrders.SingleAsync(o => o.Id == orderId);
        Assert.Contains("unidad", orden.ErrorProcesamiento!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AsignarUnidades_ManualYLuegoVenta_UsaEsasUnidades()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 5m);
        await MarcarTrazableAsync(productoId);
        var unidades = await SembrarUnidadesAsync(productoId, 3);
        var orderId = await SembrarOrdenAsync(itemId, 5003, cantidad: 2);

        var item = await _context.MercadoLibreOrderItems.SingleAsync();

        // Manual: la 2ª y la 3ª (anula el FIFO).
        await _servicio.AsignarUnidadesAsync(orderId, item.Id, new[] { unidades[1], unidades[2] }, "tester");

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");
        Assert.True(resultado.VentaCreada);

        var venta = await _context.Ventas.Include(v => v.Detalles).SingleAsync();
        var asignadas = venta.Detalles.Select(d => d.ProductoUnidadId!.Value).OrderBy(x => x).ToList();
        Assert.Equal(new[] { unidades[1], unidades[2] }.OrderBy(x => x).ToList(), asignadas);

        // La unidad más antigua quedó EnStock (la asignación manual mandó).
        Assert.Equal(EstadoUnidad.EnStock,
            (await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidades[0])).Estado);
    }

    [Fact]
    public async Task AsignarUnidades_CantidadIncorrecta_Lanza()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 5m);
        await MarcarTrazableAsync(productoId);
        var unidades = await SembrarUnidadesAsync(productoId, 3);
        var orderId = await SembrarOrdenAsync(itemId, 5004, cantidad: 2);

        var item = await _context.MercadoLibreOrderItems.SingleAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _servicio.AsignarUnidadesAsync(orderId, item.Id, new[] { unidades[0] }, "tester"));

        Assert.Contains("se requieren 2", ex.Message);
    }

    [Fact]
    public async Task CrearVenta_OrigenUnidadEspecifica_UsaLaUnidadDelListing()
    {
        var (_, _, productoId, itemId) = await SembrarBaseAsync(stockInicial: 5m);
        var unidades = await SembrarUnidadesAsync(productoId, 2);

        var listing = await _context.MercadoLibreListings.FirstAsync(l => l.ItemId == itemId);
        listing.OrigenStockOverride = MercadoLibreOrigenStock.UnidadFisicaEspecifica;
        listing.ProductoUnidadId = unidades[1];
        await _context.SaveChangesAsync();

        var orderId = await SembrarOrdenAsync(itemId, 5005, cantidad: 1);

        var resultado = await _servicio.CrearVentaInternaAsync(orderId, "tester");
        Assert.True(resultado.VentaCreada);

        var detalle = (await _context.Ventas.Include(v => v.Detalles).SingleAsync()).Detalles.Single();
        Assert.Equal(unidades[1], detalle.ProductoUnidadId);

        Assert.Equal(EstadoUnidad.Vendida,
            (await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidades[1])).Estado);
        Assert.Equal(EstadoUnidad.EnStock,
            (await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidades[0])).Estado);
    }
}
