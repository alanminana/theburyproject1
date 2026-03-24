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
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs de ICajaService — solo el método usado por CreateAsync
// ---------------------------------------------------------------------------

file sealed class StubCajaService : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaService(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    // Resto de la interfaz — no usado en estos tests
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
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Stub de IPrecioService — configurable por test
// ---------------------------------------------------------------------------

file sealed class StubPrecioServiceP : IPrecioService
{
    private readonly ListaPrecio? _lista;
    public StubPrecioServiceP(ListaPrecio? lista) => _lista = lista;
    public Task<ListaPrecio?> GetListaPredeterminadaAsync() => Task.FromResult(_lista);
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

// ---------------------------------------------------------------------------
// Stub de ICurrentUserService
// ---------------------------------------------------------------------------

file sealed class StubCurrentUserServiceP : ICurrentUserService
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
// Tests
// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para la lógica de aplicación de precio vigente en CreateAsync.
///
/// Ruta al método: CreateAsync → AgregarDetalles → AplicarPrecioVigenteADetallesAsync
///
/// Dependencias resueltas:
///   - IMapper real (MappingProfile)
///   - ICajaService stub (ObtenerAperturaActivaParaUsuarioAsync devuelve AperturaCaja { Id=1 })
///   - IPrecioService stub (GetListaPredeterminadaAsync configurable por test)
///   - ICurrentUserService stub (GetUsername = "testuser", IsAuthenticated = true)
///   - SQLite :memory: con EnsureCreated()
///   - TipoPago = Efectivo (evita bloque CreditoPersonal y CapturarSnapshotLimiteCreditoAsync)
///
/// Assert sobre VentaDetalle persistido en DB, no sobre el ViewModel de retorno.
/// </summary>
public class VentaServicePrecioTests
{
    private const string TestUser = "testuser";
    private const decimal PrecioFallback = 100m;
    private const decimal PrecioLista = 150m;

    // ---------------------------------------------------------------------------
    // Infraestructura
    // ---------------------------------------------------------------------------

    private static (AppDbContext ctx, SqliteConnection conn) CreateDb()
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

    private static IMapper CreateMapper() =>
        new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

    private static ICurrentUserService CreateCurrentUserService() => new StubCurrentUserServiceP();

    /// <summary>
    /// Siembra la infraestructura base: Caja, AperturaCaja, Categoria, Marca, Cliente, Producto.
    /// Devuelve la AperturaCaja (con Id asignado) y el Producto (con Id asignado).
    /// </summary>
    private static async Task<(AperturaCaja apertura, Producto producto, Cliente cliente)> SeedBaseAsync(AppDbContext ctx)
    {
        // Caja — necesaria para AperturaCaja FK
        var caja = new Caja
        {
            Codigo = "C01",
            Nombre = "Caja Test",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();

        // Categoria y Marca — FKs requeridas por Producto
        var categoria = new Categoria
        {
            Codigo = "CAT01",
            Nombre = "Categoria Test",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        var marca = new Marca
        {
            Codigo = "MRC01",
            Nombre = "Marca Test",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.Categorias.Add(categoria);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        // AperturaCaja, Cliente, Producto — guardados juntos
        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.AperturasCaja.Add(apertura);

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            TipoDocumento = "DNI",
            NumeroDocumento = "30000001",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.Clientes.Add(cliente);

        var producto = new Producto
        {
            Nombre = "Producto Test",
            Codigo = "P001",
            PrecioVenta = PrecioFallback,
            StockActual = 100,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.Productos.Add(producto);

        await ctx.SaveChangesAsync();
        return (apertura, producto, cliente);
    }

    private static VentaService BuildService(
        AppDbContext ctx,
        IMapper mapper,
        ICajaService cajaService,
        IPrecioService precioService)
    {
        var logger = NullLogger<VentaService>.Instance;
        var validator = new VentaValidator();
        var numberGenerator = new VentaNumberGenerator(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.VentaNumberGenerator>.Instance);
        var financialService = new FinancialCalculationService();

        return new VentaService(
            ctx,
            mapper,
            logger,
            null!,                  // IAlertaStockService — no usado en CreateAsync pre-confirmación
            null!,                  // IMovimientoStockService — no usado en CreateAsync
            financialService,
            validator,
            numberGenerator,
            precioService,
            CreateCurrentUserService(),
            null!,                  // IValidacionVentaService — solo para CreditoPersonal
            cajaService,
            null!);                 // ICreditoDisponibleService — solo para CreditoPersonal
    }

    private static VentaViewModel BuildViewModel(int clienteId, int productoId) => new()
    {
        ClienteId = clienteId,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Descuento = 0,
        Detalles = new List<VentaDetalleViewModel>
        {
            new()
            {
                ProductoId = productoId,
                Cantidad = 1,
                PrecioUnitario = 0m,  // sobreescrito por AplicarPrecioVigenteADetallesAsync
                Descuento = 0
            }
        }
    };

    // ---------------------------------------------------------------------------
    // Caso 1 — Lista vigente: usa precio de lista, no fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ConListaVigente_UsaPrecioDeListaNoFallback()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);

            // Lista predeterminada activa
            var lista = new ListaPrecio
            {
                Nombre = "Lista Principal",
                EsPredeterminada = true,
                Activa = true,
                IsDeleted = false,
                RowVersion = new byte[8]
            };
            ctx.ListasPrecios.Add(lista);
            await ctx.SaveChangesAsync();

            // Precio vigente para el producto
            ctx.ProductosPrecios.Add(new ProductoPrecioLista
            {
                ProductoId = producto.Id,
                ListaId = lista.Id,
                Precio = PrecioLista,
                VigenciaDesde = DateTime.UtcNow.AddDays(-1),
                VigenciaHasta = null,       // sin fecha de vencimiento
                EsVigente = true,
                IsDeleted = false,
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var mapper = CreateMapper();
            var cajaStub = new StubCajaService(apertura);
            var precioStub = new StubPrecioServiceP(new ListaPrecio { Id = lista.Id });

            var svc = BuildService(ctx, mapper, cajaStub, precioStub);

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            // Assert sobre el detalle persistido
            var detalles = await ctx.VentaDetalles
                .AsNoTracking()
                .Where(d => !d.IsDeleted)
                .ToListAsync();

            Assert.Single(detalles);
            Assert.Equal(PrecioLista, detalles[0].PrecioUnitario);
        }
    }

    // ---------------------------------------------------------------------------
    // Caso 2 — Lista vencida: fallback a Producto.PrecioVenta
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ConListaVencida_UsaFallbackPrecioVenta()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);

            var lista = new ListaPrecio
            {
                Nombre = "Lista Principal",
                EsPredeterminada = true,
                Activa = true,
                IsDeleted = false,
                RowVersion = new byte[8]
            };
            ctx.ListasPrecios.Add(lista);
            await ctx.SaveChangesAsync();

            // Precio con VigenciaHasta en el pasado
            ctx.ProductosPrecios.Add(new ProductoPrecioLista
            {
                ProductoId = producto.Id,
                ListaId = lista.Id,
                Precio = PrecioLista,
                VigenciaDesde = DateTime.UtcNow.AddDays(-30),
                VigenciaHasta = DateTime.UtcNow.AddDays(-1),   // vencido ayer
                EsVigente = true,
                IsDeleted = false,
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var mapper = CreateMapper();
            var cajaStub = new StubCajaService(apertura);
            var precioStub = new StubPrecioServiceP(new ListaPrecio { Id = lista.Id });

            var svc = BuildService(ctx, mapper, cajaStub, precioStub);

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var detalles = await ctx.VentaDetalles
                .AsNoTracking()
                .Where(d => !d.IsDeleted)
                .ToListAsync();

            Assert.Single(detalles);
            Assert.Equal(PrecioFallback, detalles[0].PrecioUnitario);
        }
    }

    // ---------------------------------------------------------------------------
    // Caso 3 — Sin lista predeterminada: fallback a Producto.PrecioVenta
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_SinListaPredeterminada_UsaFallbackPrecioVenta()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);

            // No se siembra ninguna ListaPrecio ni ProductoPrecioLista

            var mapper = CreateMapper();
            var cajaStub = new StubCajaService(apertura);
            var precioStub = new StubPrecioServiceP(null); // sin lista

            var svc = BuildService(ctx, mapper, cajaStub, precioStub);

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var detalles = await ctx.VentaDetalles
                .AsNoTracking()
                .Where(d => !d.IsDeleted)
                .ToListAsync();

            Assert.Single(detalles);
            Assert.Equal(PrecioFallback, detalles[0].PrecioUnitario);
        }
    }
}
