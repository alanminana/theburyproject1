using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Unit;

// ---------------------------------------------------------------------------
// Stubs internos — sin dependencia de Moq
// ---------------------------------------------------------------------------

/// <summary>
/// Stub de IPrecioService que devuelve una lista predeterminada configurada en el test,
/// o null si no se configura. Solo implementa GetListaPredeterminadaAsync (usado por
/// AplicarPrecioVigenteADetallesAsync). El resto lanza NotImplementedException.
/// </summary>
file sealed class StubPrecioService : IPrecioService
{
    private readonly ListaPrecio? _lista;
    public StubPrecioService(ListaPrecio? lista) => _lista = lista;

    public Task<ListaPrecio?> GetListaPredeterminadaAsync() => Task.FromResult(_lista);

    // Resto de la interfaz — no usado en estos tests
    public Task<List<ListaPrecio>> GetAllListasAsync(bool soloActivas = true) => throw new NotImplementedException();
    public Task<ListaPrecio?> GetListaByIdAsync(int id) => throw new NotImplementedException();
    public Task<ListaPrecio> CreateListaAsync(ListaPrecio lista) => throw new NotImplementedException();
    public Task<ListaPrecio> UpdateListaAsync(ListaPrecio lista, byte[] rowVersion) => throw new NotImplementedException();
    public Task<bool> DeleteListaAsync(int id, byte[] rowVersion) => throw new NotImplementedException();
    public Task<ProductoPrecioLista?> GetPrecioVigenteAsync(int productoId, int listaId, DateTime? fecha = null) => throw new NotImplementedException();
    public Task<Dictionary<int, ProductoPrecioLista>> GetPreciosVigentesBatchAsync(IEnumerable<int> productoIds, int listaId, DateTime? fecha = null) => throw new NotImplementedException();
    public Task<List<ProductoPrecioLista>> GetPreciosProductoAsync(int productoId, DateTime? fecha = null) => throw new NotImplementedException();
    public Task<List<ProductoPrecioLista>> GetHistorialPreciosAsync(int productoId, int listaId) => throw new NotImplementedException();
    public Task<ProductoPrecioLista> SetPrecioManualAsync(int productoId, int listaId, decimal precio, decimal costo, DateTime? vigenciaDesde = null, string? notas = null) => throw new NotImplementedException();
    public Task<decimal> CalcularPrecioAutomaticoAsync(int productoId, int listaId, decimal costo) => throw new NotImplementedException();
    public Task<ResultadoAplicacionPrecios> AplicarCambioPrecioDirectoAsync(AplicarCambioPrecioDirectoViewModel model) => throw new NotImplementedException();
    public Task<List<CambioPrecioEvento>> GetCambioPrecioEventosAsync(int take = 200) => throw new NotImplementedException();
    public Task<List<CambioPrecioDetalle>> GetCambiosPrecioProductoAsync(int productoId, int take = 50) => throw new NotImplementedException();
    public Task<Dictionary<int, UltimoCambioProductoResumen>> GetUltimoCambioPorProductosAsync(IEnumerable<int> productoIds) => throw new NotImplementedException();
    public Task<CambioPrecioEvento?> GetCambioPrecioEventoAsync(int eventoId) => throw new NotImplementedException();
    public Task<(bool Exitoso, string Mensaje, int? EventoReversionId)> RevertirCambioPrecioEventoAsync(int eventoId) => throw new NotImplementedException();
    public Task<PriceChangeBatch> SimularCambioMasivoAsync(string nombre, TipoCambio tipoCambio, TipoAplicacion tipoAplicacion, decimal valorCambio, List<int> listasIds, List<int>? categoriaIds = null, List<int>? marcaIds = null, List<int>? productoIds = null) => throw new NotImplementedException();
    public Task<PriceChangeBatch?> GetSimulacionAsync(int batchId) => throw new NotImplementedException();
    public Task<List<PriceChangeItem>> GetItemsSimulacionAsync(int batchId, int skip = 0, int take = 50) => throw new NotImplementedException();
    public Task<PriceChangeBatch> AprobarBatchAsync(int batchId, string aprobadoPor, byte[] rowVersion, string? notas = null) => throw new NotImplementedException();
    public Task<PriceChangeBatch> RechazarBatchAsync(int batchId, string rechazadoPor, byte[] rowVersion, string motivo) => throw new NotImplementedException();
    public Task<PriceChangeBatch> CancelarBatchAsync(int batchId, string canceladoPor, byte[] rowVersion, string? motivo = null) => throw new NotImplementedException();
    public Task<bool> RequiereAutorizacionAsync(int batchId) => throw new NotImplementedException();
    public Task<PriceChangeBatch> AplicarBatchAsync(int batchId, string aplicadoPor, byte[] rowVersion, DateTime? fechaVigencia = null) => throw new NotImplementedException();
    public Task<PriceChangeBatch> RevertirBatchAsync(int batchId, string revertidoPor, byte[] rowVersion, string motivo) => throw new NotImplementedException();
    public Task<List<PriceChangeBatch>> GetBatchesAsync(EstadoBatch? estado = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, int skip = 0, int take = 50) => throw new NotImplementedException();
    public Task<Dictionary<string, object>> GetEstadisticasBatchAsync(int batchId) => throw new NotImplementedException();
    public Task<byte[]> ExportarHistorialPreciosAsync(List<int> productoIds, DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<(bool esValido, string? mensaje)> ValidarMargenMinimoAsync(decimal precio, decimal costo, int listaId) => throw new NotImplementedException();
    public decimal CalcularMargen(decimal precio, decimal costo) => throw new NotImplementedException();
    public decimal AplicarRedondeo(decimal precio, string? reglaRedondeo = null) => throw new NotImplementedException();
    public Task<List<int>> GetBatchIdsByProductoAsync(int productoId) => throw new NotImplementedException();
}

/// <summary>
/// Stub de IVentaValidator que no hace nada (sin-op). Permite testear métodos que
/// invocan el validator sin que fallen por estado de la venta.
/// </summary>
file sealed class NoOpVentaValidator : IVentaValidator
{
    public void ValidarEstadoParaEdicion(Venta venta) { }
    public void ValidarEstadoParaEliminacion(Venta venta) { }
    public void ValidarEstadoParaConfirmacion(Venta venta) { }
    public void ValidarEstadoParaFacturacion(Venta venta) { }
    public void ValidarAutorizacion(Venta venta) { }
    public void ValidarStock(Venta venta) { }
    public void ValidarNoEstaCancelada(Venta venta) { }
    public void ValidarEstadoAutorizacion(Venta venta, EstadoAutorizacionVenta estadoEsperado) { }
}

file sealed class StubCurrentUserService : ICurrentUserService
{
    public string GetUsername() => "TestUser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

// ---------------------------------------------------------------------------
// Helpers compartidos
// ---------------------------------------------------------------------------

file static class VentaServiceFactory
{
    /// <summary>
    /// Crea VentaService con el contexto y precioService provistos.
    /// Todas las demás dependencias son no-op o nulls seguros para los métodos del lote 1.
    /// </summary>
    public static VentaService Create(AppDbContext ctx, IPrecioService? precioService = null)
    {
        var financialService = new FinancialCalculationService();
        var logger = NullLogger<VentaService>.Instance;
        var validator = new NoOpVentaValidator();
        var numberGenerator = new VentaNumberGenerator(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.VentaNumberGenerator>.Instance);

        // Dependencias no usadas en el lote 1 — se pasan como null! (no se invocan en estos tests)
        return new VentaService(
            ctx,
            null!,                                          // IMapper
            logger,
            null!,                                          // IAlertaStockService
            null!,                                          // IMovimientoStockService
            financialService,
            validator,
            numberGenerator,
            precioService ?? new StubPrecioService(null),
            new StubCurrentUserService(),
            null!,                                          // IValidacionVentaService
            null!,                                          // ICajaService
            null!);
    }

    private static (AppDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    public static (AppDbContext ctx, SqliteConnection conn) CreateDb() => CreateContext();
}

// ---------------------------------------------------------------------------
// A. CalcularTotalesPreview — unit tests puros, sin DB
// ---------------------------------------------------------------------------

public class VentaService_CalcularTotalesPreview
{
    // Ratio IVA_DIVISOR = 1.21m (constante en VentaConstants)
    private const decimal IVA_DIVISOR = 1.21m;

    private static VentaService BuildService()
    {
        var (ctx, _) = VentaServiceFactory.CreateDb();
        return VentaServiceFactory.Create(ctx);
    }

    private static DetalleCalculoVentaRequest Item(decimal precio, decimal cantidad = 1, decimal descuento = 0) =>
        new() { ProductoId = 1, PrecioUnitario = precio, Cantidad = cantidad, Descuento = descuento };

    [Fact]
    public void SinDetalles_DevuelveTotalesEnCero()
    {
        var svc = BuildService();
        var result = svc.CalcularTotalesPreview(new List<DetalleCalculoVentaRequest>(), 0, false);

        Assert.Equal(0, result.Subtotal);
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.IVA);
        Assert.Equal(0, result.DescuentoGeneralAplicado);
    }

    [Fact]
    public void UnItem_SinDescuento_SubtotalIgualATotal()
    {
        var svc = BuildService();
        var result = svc.CalcularTotalesPreview(
            new List<DetalleCalculoVentaRequest> { Item(1210m) },
            descuentoGeneral: 0, descuentoEsPorcentaje: false);

        Assert.Equal(1210m, result.Subtotal);
        Assert.Equal(1210m, result.Total);
    }

    [Fact]
    public void DesgloseIVA_EsConsistente_ConTotalDivididoPorDivisor()
    {
        var svc = BuildService();
        var result = svc.CalcularTotalesPreview(
            new List<DetalleCalculoVentaRequest> { Item(1210m) },
            descuentoGeneral: 0, descuentoEsPorcentaje: false);

        var baseEsperada = result.Total / IVA_DIVISOR;
        var ivaEsperado = result.Total - baseEsperada;

        Assert.Equal(ivaEsperado, result.IVA);
        // IVA debería ser ~21% del precio sin IVA (210 sobre 1000)
        Assert.True(result.IVA > 0);
    }

    [Fact]
    public void DescuentoAbsolutoEnDetalle_SeAplicaSobreTotalDelItem()
    {
        var svc = BuildService();
        // Item: (1000 * 2) - 100 = 1900. El Descuento del detalle es absoluto sobre precio*cantidad.
        var result = svc.CalcularTotalesPreview(
            new List<DetalleCalculoVentaRequest> { Item(1000m, cantidad: 2, descuento: 100m) },
            descuentoGeneral: 0, descuentoEsPorcentaje: false);

        Assert.Equal(1900m, result.Subtotal);
        Assert.Equal(1900m, result.Total);
    }

    [Fact]
    public void DescuentoGeneralAbsoluto_SeRestaDelSubtotal()
    {
        var svc = BuildService();
        var result = svc.CalcularTotalesPreview(
            new List<DetalleCalculoVentaRequest> { Item(1000m), Item(500m) },
            descuentoGeneral: 200m, descuentoEsPorcentaje: false);

        Assert.Equal(1500m, result.Subtotal);
        Assert.Equal(200m, result.DescuentoGeneralAplicado);
        Assert.Equal(1300m, result.Total);
    }

    [Fact]
    public void DescuentoGeneralPorcentaje_SeCalculaSobreSubtotal()
    {
        var svc = BuildService();
        // Subtotal = 1000. Descuento 10% = 100. Total = 900.
        var result = svc.CalcularTotalesPreview(
            new List<DetalleCalculoVentaRequest> { Item(1000m) },
            descuentoGeneral: 10m, descuentoEsPorcentaje: true);

        Assert.Equal(1000m, result.Subtotal);
        Assert.Equal(100m, result.DescuentoGeneralAplicado);
        Assert.Equal(900m, result.Total);
    }

    [Fact]
    public void DescuentoDetalleIgualAPrecio_NoProdueceNegativos()
    {
        var svc = BuildService();
        // Precio 500, descuento 500 → neto 0 por Math.Max(0,...)
        var result = svc.CalcularTotalesPreview(
            new List<DetalleCalculoVentaRequest> { Item(500m, descuento: 500m) },
            descuentoGeneral: 0, descuentoEsPorcentaje: false);

        Assert.Equal(0, result.Subtotal);
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.IVA);
    }

    [Fact]
    public void DescuentoGeneralMayorQueSubtotal_TotalNoEsNegativo()
    {
        var svc = BuildService();
        var result = svc.CalcularTotalesPreview(
            new List<DetalleCalculoVentaRequest> { Item(100m) },
            descuentoGeneral: 500m, descuentoEsPorcentaje: false);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void MultipleItems_SubtotalEsSumaDeNetosIndividuales()
    {
        var svc = BuildService();
        // Item A: 200 * 3 = 600. Item B: 150 * 2 - descuento 50 = 250. Subtotal = 850.
        var items = new List<DetalleCalculoVentaRequest>
        {
            new() { ProductoId = 1, PrecioUnitario = 200m, Cantidad = 3, Descuento = 0 },
            new() { ProductoId = 2, PrecioUnitario = 150m, Cantidad = 2, Descuento = 50m },
        };
        var result = svc.CalcularTotalesPreview(items, 0, false);

        Assert.Equal(850m, result.Subtotal);
        Assert.Equal(850m, result.Total);
    }
}

// ---------------------------------------------------------------------------
// B. CalcularCreditoPersonallAsync — integración SQLite
// ---------------------------------------------------------------------------

public class VentaService_CalcularCreditoPersonallAsync
{
    private static (AppDbContext ctx, SqliteConnection conn) CreateDb()
        => VentaServiceFactory.CreateDb();

    private static Cliente BaseCliente(int id) => new()
    {
        Id = id,
        Nombre = "Test",
        Apellido = "Cliente",
        TipoDocumento = "DNI",
        NumeroDocumento = $"2000000{id}",
        NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
        IsDeleted = false,
        RowVersion = new byte[8]
    };

    private static Credito CreditoActivo(int id, int clienteId, decimal saldo, decimal tasa = 2m) => new()
    {
        Id = id,
        ClienteId = clienteId,
        Numero = $"C-{id:000}",
        MontoAprobado = saldo,
        SaldoPendiente = saldo,
        TasaInteres = tasa,    // tasa mensual en %
        Estado = EstadoCredito.Activo,
        IsDeleted = false,
        RowVersion = new byte[8]
    };

    [Fact]
    public async Task CreditoActivo_MontoValido_DevuelvePlanCorrecto()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            ctx.Creditos.Add(CreditoActivo(1, clienteId: 1, saldo: 50_000m, tasa: 2m));
            await ctx.SaveChangesAsync();

            var svc = VentaServiceFactory.Create(ctx);
            var fecha = DateTime.Today.AddMonths(1);

            var result = await svc.CalcularCreditoPersonallAsync(
                creditoId: 1, montoAFinanciar: 10_000m, cuotas: 12, fechaPrimeraCuota: fecha);

            Assert.Equal(1, result.CreditoId);
            Assert.Equal(12, result.CantidadCuotas);
            Assert.True(result.MontoCuota > 0, "La cuota calculada debe ser positiva");
            Assert.True(result.TotalAPagar >= 10_000m, "El total a pagar debe ser >= monto financiado");
            Assert.Equal(fecha, result.FechaPrimeraCuota);
        }
    }

    [Fact]
    public async Task CreditoInexistente_LanzaInvalidOperation()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var svc = VentaServiceFactory.Create(ctx);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CalcularCreditoPersonallAsync(
                    creditoId: 999, montoAFinanciar: 1_000m, cuotas: 6,
                    fechaPrimeraCuota: DateTime.Today.AddMonths(1)));
        }
    }

    [Fact]
    public async Task MontoSuperaSaldo_LanzaInvalidOperation()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            ctx.Creditos.Add(CreditoActivo(1, clienteId: 1, saldo: 5_000m));
            await ctx.SaveChangesAsync();

            var svc = VentaServiceFactory.Create(ctx);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CalcularCreditoPersonallAsync(
                    creditoId: 1, montoAFinanciar: 10_000m, cuotas: 6,
                    fechaPrimeraCuota: DateTime.Today.AddMonths(1)));
        }
    }

    [Fact]
    public async Task CreditoCancelado_LanzaInvalidOperation()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            var credito = CreditoActivo(1, clienteId: 1, saldo: 50_000m);
            credito.Estado = EstadoCredito.Cancelado;
            ctx.Creditos.Add(credito);
            await ctx.SaveChangesAsync();

            var svc = VentaServiceFactory.Create(ctx);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CalcularCreditoPersonallAsync(
                    creditoId: 1, montoAFinanciar: 1_000m, cuotas: 6,
                    fechaPrimeraCuota: DateTime.Today.AddMonths(1)));
        }
    }

    [Fact]
    public async Task TasaCero_DevuelveCuotaIgualAMontoSinInteres()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            ctx.Clientes.Add(BaseCliente(1));
            ctx.Creditos.Add(CreditoActivo(1, clienteId: 1, saldo: 12_000m, tasa: 0m));
            await ctx.SaveChangesAsync();

            var svc = VentaServiceFactory.Create(ctx);

            var result = await svc.CalcularCreditoPersonallAsync(
                creditoId: 1, montoAFinanciar: 12_000m, cuotas: 12,
                fechaPrimeraCuota: DateTime.Today.AddMonths(1));

            // Con tasa 0%, cuota = monto / cuotas = 1000, total = monto
            Assert.Equal(1_000m, result.MontoCuota);
            Assert.Equal(12_000m, result.TotalAPagar);
        }
    }
}

// ---------------------------------------------------------------------------
// C. GetTotalVentaAsync — resolución de monto efectivo
// ---------------------------------------------------------------------------

public class VentaService_GetTotalVentaAsync
{
    private const decimal IVA_RATE = 0.21m;

    private static (AppDbContext ctx, SqliteConnection conn) CreateDb()
        => VentaServiceFactory.CreateDb();

    private static async Task<(int clienteId, int productoId)> SeedAsync(AppDbContext ctx)
    {
        var cat = new Categoria { Codigo = "C1", Nombre = "Cat", IsDeleted = false, RowVersion = new byte[8] };
        var marca = new Marca { Codigo = "M1", Nombre = "Marca", IsDeleted = false, RowVersion = new byte[8] };
        ctx.Categorias.Add(cat);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Nombre = "Prod Test", Codigo = "P01", PrecioVenta = 100m,
            StockActual = 10, CategoriaId = cat.Id, MarcaId = marca.Id,
            IsDeleted = false, RowVersion = new byte[8]
        };
        ctx.Productos.Add(producto);

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Cliente", TipoDocumento = "DNI",
            NumeroDocumento = "30000001", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false, RowVersion = new byte[8]
        };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();
        return (cliente.Id, producto.Id);
    }

    private static Venta VentaBase(int clienteId, decimal total = 0m) => new()
    {
        Numero = "V-001",
        ClienteId = clienteId,
        Estado = EstadoVenta.Cotizacion,
        Total = total,
        Descuento = 0m,
        IVA = 0m,
        IsDeleted = false,
        RowVersion = new byte[8]
    };

    private static VentaDetalle Detalle(int ventaId, int productoId, decimal precio, int cantidad = 1) => new()
    {
        VentaId = ventaId,
        ProductoId = productoId,
        Cantidad = cantidad,
        PrecioUnitario = precio,
        Subtotal = precio * cantidad,
        Descuento = 0m,
        IsDeleted = false,
        RowVersion = new byte[8]
    };

    [Fact]
    public async Task VentaInexistente_DevuelveNull()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var svc = VentaServiceFactory.Create(ctx);
            var result = await svc.GetTotalVentaAsync(999);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task TotalGuardadoValido_DevuelveTotalDirectamente()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (clienteId, _) = await SeedAsync(ctx);
            ctx.Ventas.Add(VentaBase(clienteId, total: 12_100m));
            await ctx.SaveChangesAsync();

            var svc = VentaServiceFactory.Create(ctx);
            var ventaId = (await ctx.Ventas.FirstAsync()).Id;
            var result = await svc.GetTotalVentaAsync(ventaId);
            Assert.Equal(12_100m, result);
        }
    }

    [Fact]
    public async Task TotalCero_RecalculaDesdeDetalles()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (clienteId, productoId) = await SeedAsync(ctx);
            ctx.Ventas.Add(VentaBase(clienteId, total: 0m));
            await ctx.SaveChangesAsync();
            var ventaId = (await ctx.Ventas.FirstAsync()).Id;
            ctx.VentaDetalles.Add(Detalle(ventaId, productoId, precio: 1_000m, cantidad: 2));
            await ctx.SaveChangesAsync();

            var svc = VentaServiceFactory.Create(ctx);
            var result = await svc.GetTotalVentaAsync(ventaId);

            // subtotal = 2000, descuento = 0, iva = 2000 * 0.21 = 420, total = 2420
            Assert.Equal(2_000m + 2_000m * IVA_RATE, result);
        }
    }

    [Fact]
    public async Task TotalCeroSinDetalles_DevuelveCero()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (clienteId, _) = await SeedAsync(ctx);
            ctx.Ventas.Add(VentaBase(clienteId, total: 0m));
            await ctx.SaveChangesAsync();
            var ventaId = (await ctx.Ventas.FirstAsync()).Id;

            var svc = VentaServiceFactory.Create(ctx);
            var result = await svc.GetTotalVentaAsync(ventaId);
            Assert.Equal(0m, result);
        }
    }

    [Fact]
    public async Task VentaEliminada_DevuelveNull()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (clienteId, _) = await SeedAsync(ctx);
            var venta = VentaBase(clienteId, total: 5_000m);
            venta.IsDeleted = true;
            ctx.Ventas.Add(venta);
            await ctx.SaveChangesAsync();
            var ventaId = (await ctx.Ventas.IgnoreQueryFilters().FirstAsync()).Id;

            var svc = VentaServiceFactory.Create(ctx);
            var result = await svc.GetTotalVentaAsync(ventaId);
            Assert.Null(result);
        }
    }
}
