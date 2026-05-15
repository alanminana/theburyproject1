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
    public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotImplementedException();
    public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotImplementedException();
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
///   - IPrecioVigenteResolver real sobre SQLite :memory:
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
    private const decimal CostoFallback = 60m;

    [Fact]
    public async Task CreateAsync_GuardaCostoHistoricoEnDetalle()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);

            var mapper = CreateMapper();
            var cajaStub = new StubCajaService(apertura);
            var svc = BuildService(ctx, mapper, cajaStub);

            await svc.CreateAsync(BuildViewModel(
                cliente.Id,
                new[] { Detalle(producto.Id, cantidad: 2, descuento: 0m) }));

            var detalle = await ctx.VentaDetalles
                .AsNoTracking()
                .SingleAsync(d => !d.IsDeleted);

            Assert.Equal(CostoFallback, detalle.CostoUnitarioAlMomento);
            Assert.Equal(CostoFallback * 2, detalle.CostoTotalAlMomento);
        }
    }

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
            PrecioCompra = CostoFallback,
            PrecioVenta = PrecioFallback,
            ComisionPorcentaje = 0m,
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

    private static async Task<Producto> CrearProductoAsync(
        AppDbContext ctx,
        Producto productoBase,
        string codigo,
        decimal precioVenta,
        decimal comisionPorcentaje,
        decimal porcentajeIVA = 21m,
        int? alicuotaIVAId = null)
    {
        var producto = new Producto
        {
            Nombre = $"Producto {codigo}",
            Codigo = codigo,
            PrecioCompra = CostoFallback,
            PrecioVenta = precioVenta,
            PorcentajeIVA = porcentajeIVA,
            AlicuotaIVAId = alicuotaIVAId,
            ComisionPorcentaje = comisionPorcentaje,
            StockActual = 100,
            CategoriaId = productoBase.CategoriaId,
            MarcaId = productoBase.MarcaId,
            IsDeleted = false,
            RowVersion = new byte[8]
        };

        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto;
    }

    private static async Task<AlicuotaIVA> CrearAlicuotaAsync(
        AppDbContext ctx,
        string codigo,
        string nombre,
        decimal porcentaje)
    {
        var alicuota = new AlicuotaIVA
        {
            Codigo = codigo,
            Nombre = nombre,
            Porcentaje = porcentaje,
            Activa = true,
            IsDeleted = false,
            RowVersion = new byte[8]
        };

        ctx.AlicuotasIVA.Add(alicuota);
        await ctx.SaveChangesAsync();
        return alicuota;
    }

    private static VentaService BuildService(
        AppDbContext ctx,
        IMapper mapper,
        ICajaService cajaService)
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
            new PrecioVigenteResolver(ctx),
            CreateCurrentUserService(),
            null!,                  // IValidacionVentaService — solo para CreditoPersonal
            cajaService,
            null!,                  // ICreditoDisponibleService — solo para CreditoPersonal
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
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
            Detalle(productoId, cantidad: 1, descuento: 0m)
        }
    };

    private static VentaViewModel BuildViewModel(int clienteId, IEnumerable<VentaDetalleViewModel> detalles) => new()
    {
        ClienteId = clienteId,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Descuento = 0,
        Detalles = detalles.ToList()
    };

    private static VentaDetalleViewModel Detalle(int productoId, int cantidad, decimal descuento) => new()
    {
        ProductoId = productoId,
        Cantidad = cantidad,
        PrecioUnitario = 0m,  // sobreescrito por AplicarPrecioVigenteADetallesAsync
        Descuento = descuento
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
            var svc = BuildService(ctx, mapper, cajaStub);

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

    [Fact]
    public async Task CreateAsync_ConListaVigente_TrataPrecioListaComoFinalConIvaIncluido()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 999m;
            producto.PorcentajeIVA = 21m;

            var lista = new ListaPrecio
            {
                Nombre = "Lista Default",
                Codigo = "LPDEFIVA",
                Tipo = TipoListaPrecio.Contado,
                Activa = true,
                EsPredeterminada = true,
                Orden = 1,
                IsDeleted = false,
                RowVersion = new byte[8]
            };
            ctx.ListasPrecios.Add(lista);
            await ctx.SaveChangesAsync();

            ctx.ProductosPrecios.Add(new ProductoPrecioLista
            {
                ProductoId = producto.Id,
                ListaId = lista.Id,
                Precio = 1210m,
                Costo = CostoFallback,
                VigenciaDesde = DateTime.UtcNow.AddDays(-1),
                EsVigente = true,
                IsDeleted = false,
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(1210m, detalle.PrecioUnitario);
            Assert.Equal(1000m, detalle.PrecioUnitarioNeto);
            Assert.Equal(210m, detalle.IVAUnitario);
            Assert.Equal(1000m, venta.Subtotal);
            Assert.Equal(210m, venta.IVA);
            Assert.Equal(1210m, venta.Total);
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
            var svc = BuildService(ctx, mapper, cajaStub);

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

            var svc = BuildService(ctx, mapper, cajaStub);

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var detalles = await ctx.VentaDetalles
                .AsNoTracking()
                .Where(d => !d.IsDeleted)
                .ToListAsync();

            Assert.Single(detalles);
            Assert.Equal(PrecioFallback, detalles[0].PrecioUnitario);
        }
    }

    [Fact]
    public async Task CreateAsync_ProductoConComision_GuardaSnapshotDeComision()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.ComisionPorcentaje = 8m;
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var detalle = await ctx.VentaDetalles
                .AsNoTracking()
                .SingleAsync(d => !d.IsDeleted);

            Assert.Equal(8m, detalle.ComisionPorcentajeAplicada);
            Assert.Equal(8m, detalle.ComisionMonto);
        }
    }

    [Fact]
    public async Task CreateAsync_DosProductosConComisionesDistintas_GuardaSnapshotPorDetalle()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, productoA, cliente) = await SeedBaseAsync(ctx);
            productoA.ComisionPorcentaje = 8m;
            var productoB = await CrearProductoAsync(ctx, productoA, "P002", precioVenta: 400m, comisionPorcentaje: 3.5m);
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, new[]
            {
                Detalle(productoA.Id, cantidad: 1, descuento: 0m),
                Detalle(productoB.Id, cantidad: 1, descuento: 0m)
            }));

            var detalles = await ctx.VentaDetalles
                .AsNoTracking()
                .Where(d => !d.IsDeleted)
                .OrderBy(d => d.ProductoId)
                .ToListAsync();

            Assert.Equal(2, detalles.Count);
            Assert.Equal(8m, detalles[0].ComisionPorcentajeAplicada);
            Assert.Equal(8m, detalles[0].ComisionMonto);
            Assert.Equal(3.5m, detalles[1].ComisionPorcentajeAplicada);
            Assert.Equal(14m, detalles[1].ComisionMonto);
        }
    }

    [Fact]
    public async Task CreateAsync_ProductoSinComision_GuardaComisionCero()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var detalle = await ctx.VentaDetalles
                .AsNoTracking()
                .SingleAsync(d => !d.IsDeleted);

            Assert.Equal(0m, detalle.ComisionPorcentajeAplicada);
            Assert.Equal(0m, detalle.ComisionMonto);
        }
    }

    [Fact]
    public async Task CreateAsync_ConDescuentoDeDetalle_CalculaComisionSobreSubtotalFinal()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 100m;
            producto.ComisionPorcentaje = 10m;
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, new[]
            {
                Detalle(producto.Id, cantidad: 2, descuento: 50m)
            }));

            var detalle = await ctx.VentaDetalles
                .AsNoTracking()
                .SingleAsync(d => !d.IsDeleted);

            Assert.Equal(150m, detalle.Subtotal);
            Assert.Equal(10m, detalle.ComisionPorcentajeAplicada);
            Assert.Equal(15m, detalle.ComisionMonto);
        }
    }

    [Fact]
    public async Task CreateAsync_ConDescuentoGeneral_CalculaComisionSobreSubtotalFinal()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 100m;
            producto.PorcentajeIVA = 0m;
            producto.ComisionPorcentaje = 10m;
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, producto.Id);
            model.Descuento = 20m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var detalle = await ctx.VentaDetalles
                .AsNoTracking()
                .SingleAsync(d => !d.IsDeleted);

            Assert.Equal(100m, detalle.Subtotal);
            Assert.Equal(20m, detalle.DescuentoGeneralProrrateado);
            Assert.Equal(80m, detalle.SubtotalFinal);
            Assert.Equal(10m, detalle.ComisionPorcentajeAplicada);
            Assert.Equal(8m, detalle.ComisionMonto);
        }
    }

    [Fact]
    public async Task CreateAsync_CambiarComisionDelProducto_NoAlteraSnapshotDelDetalle()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.ComisionPorcentaje = 8m;
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            producto.ComisionPorcentaje = 20m;
            await ctx.SaveChangesAsync();

            var detalle = await ctx.VentaDetalles
                .AsNoTracking()
                .SingleAsync(d => !d.IsDeleted);

            Assert.Equal(8m, detalle.ComisionPorcentajeAplicada);
            Assert.Equal(8m, detalle.ComisionMonto);
        }
    }

    [Fact]
    public async Task CreateAsync_LineaIva21_PersisteSnapshotNetoIvaYTotal()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 1210m;
            producto.PorcentajeIVA = 21m;
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(1210m, detalle.PrecioUnitario);
            Assert.Equal(1210m, detalle.Subtotal);
            Assert.Equal(21m, detalle.PorcentajeIVA);
            Assert.Equal(1000m, detalle.PrecioUnitarioNeto);
            Assert.Equal(210m, detalle.IVAUnitario);
            Assert.Equal(1000m, detalle.SubtotalNeto);
            Assert.Equal(210m, detalle.SubtotalIVA);
            Assert.Equal(0m, detalle.DescuentoGeneralProrrateado);
            Assert.Equal(detalle.SubtotalNeto, detalle.SubtotalFinalNeto);
            Assert.Equal(detalle.SubtotalIVA, detalle.SubtotalFinalIVA);
            Assert.Equal(detalle.Subtotal, detalle.SubtotalFinal);
            Assert.Equal(1000m, venta.Subtotal);
            Assert.Equal(210m, venta.IVA);
            Assert.Equal(1210m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_LineaIva105DesdeAlicuotaProducto_PersisteSnapshot()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            var alicuota = await CrearAlicuotaAsync(ctx, "IVA105", "IVA 10.5", 10.5m);
            producto.PrecioVenta = 110.50m;
            producto.AlicuotaIVAId = alicuota.Id;
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(10.5m, detalle.PorcentajeIVA);
            Assert.Equal(alicuota.Id, detalle.AlicuotaIVAId);
            Assert.Equal("IVA 10.5", detalle.AlicuotaIVANombre);
            Assert.Equal(100m, detalle.SubtotalNeto);
            Assert.Equal(10.50m, detalle.SubtotalIVA);
            Assert.Equal(100m, venta.Subtotal);
            Assert.Equal(10.50m, venta.IVA);
            Assert.Equal(110.50m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_DescuentoGeneralLineaIva21_ProrrateaSnapshotsFinales()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 1210m;
            producto.PorcentajeIVA = 21m;
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, producto.Id);
            model.Descuento = 121m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(121m, detalle.DescuentoGeneralProrrateado);
            Assert.Equal(1089m, detalle.SubtotalFinal);
            Assert.Equal(900m, detalle.SubtotalFinalNeto);
            Assert.Equal(189m, detalle.SubtotalFinalIVA);
            Assert.Equal(900m, venta.Subtotal);
            Assert.Equal(189m, venta.IVA);
            Assert.Equal(1089m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_DescuentoGeneralLineaIva105_ProrrateaSnapshotsFinales()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            var alicuota = await CrearAlicuotaAsync(ctx, "IVA105D", "IVA 10.5", 10.5m);
            producto.PrecioVenta = 110.50m;
            producto.AlicuotaIVAId = alicuota.Id;
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, producto.Id);
            model.Descuento = 10.50m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(10.50m, detalle.DescuentoGeneralProrrateado);
            Assert.Equal(100m, detalle.SubtotalFinal);
            Assert.Equal(90.50m, detalle.SubtotalFinalNeto);
            Assert.Equal(9.50m, detalle.SubtotalFinalIVA);
            Assert.Equal(90.50m, venta.Subtotal);
            Assert.Equal(9.50m, venta.IVA);
            Assert.Equal(100m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_DescuentoGeneralLineaIva0_ProrrateaSnapshotsFinales()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 100m;
            producto.PorcentajeIVA = 0m;
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, producto.Id);
            model.Descuento = 20m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(20m, detalle.DescuentoGeneralProrrateado);
            Assert.Equal(80m, detalle.SubtotalFinal);
            Assert.Equal(80m, detalle.SubtotalFinalNeto);
            Assert.Equal(0m, detalle.SubtotalFinalIVA);
            Assert.Equal(80m, venta.Subtotal);
            Assert.Equal(0m, venta.IVA);
            Assert.Equal(80m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_LineasMixtasConDescuentoGeneral_CierraTotalesFinales()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto21, cliente) = await SeedBaseAsync(ctx);
            producto21.PrecioVenta = 1210m;
            producto21.PorcentajeIVA = 21m;
            var alicuota105 = await CrearAlicuotaAsync(ctx, "IVA105MIXD", "IVA 10.5", 10.5m);
            var producto105 = await CrearProductoAsync(ctx, producto21, "P105D", 110.50m, 0m, alicuotaIVAId: alicuota105.Id);
            var producto0 = await CrearProductoAsync(ctx, producto21, "P000D", 100m, 0m, porcentajeIVA: 0m);
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, new[]
            {
                Detalle(producto21.Id, cantidad: 1, descuento: 0m),
                Detalle(producto105.Id, cantidad: 1, descuento: 0m),
                Detalle(producto0.Id, cantidad: 1, descuento: 0m)
            });
            model.Descuento = 142.05m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalles = await ctx.VentaDetalles.AsNoTracking().Where(d => !d.IsDeleted).ToListAsync();

            Assert.Equal(1080m, venta.Subtotal);
            Assert.Equal(198.45m, venta.IVA);
            Assert.Equal(1278.45m, venta.Total);
            Assert.Equal(venta.Subtotal, detalles.Sum(d => d.SubtotalFinalNeto));
            Assert.Equal(venta.IVA, detalles.Sum(d => d.SubtotalFinalIVA));
            Assert.Equal(venta.Total, detalles.Sum(d => d.SubtotalFinal));
        }
    }

    [Fact]
    public async Task CreateAsync_DescuentoLineaYGeneral_ProrrateaSobreSubtotalConDescuentoLinea()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 121m;
            producto.PorcentajeIVA = 21m;
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, new[] { Detalle(producto.Id, cantidad: 2, descuento: 21m) });
            model.Descuento = 100m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(221m, detalle.Subtotal);
            Assert.Equal(100m, detalle.DescuentoGeneralProrrateado);
            Assert.Equal(121m, detalle.SubtotalFinal);
            Assert.Equal(100m, detalle.SubtotalFinalNeto);
            Assert.Equal(21m, detalle.SubtotalFinalIVA);
            Assert.Equal(121m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_DescuentoGeneralMayorAlTotal_LimitaTotalACero()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 100m;
            producto.PorcentajeIVA = 0m;
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, producto.Id);
            model.Descuento = 200m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(100m, detalle.DescuentoGeneralProrrateado);
            Assert.Equal(0m, detalle.SubtotalFinal);
            Assert.Equal(0m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_ProrrateoConCentavos_AjustaDiferenciaYCierraContraVenta()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, productoA, cliente) = await SeedBaseAsync(ctx);
            productoA.PrecioVenta = 0.05m;
            productoA.PorcentajeIVA = 0m;
            var productoB = await CrearProductoAsync(ctx, productoA, "P005B", 0.05m, 0m, porcentajeIVA: 0m);
            var productoC = await CrearProductoAsync(ctx, productoA, "P005C", 0.05m, 0m, porcentajeIVA: 0m);
            await ctx.SaveChangesAsync();

            var model = BuildViewModel(cliente.Id, new[]
            {
                Detalle(productoA.Id, cantidad: 1, descuento: 0m),
                Detalle(productoB.Id, cantidad: 1, descuento: 0m),
                Detalle(productoC.Id, cantidad: 1, descuento: 0m)
            });
            model.Descuento = 0.01m;
            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(model);

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalles = await ctx.VentaDetalles.AsNoTracking().Where(d => !d.IsDeleted).ToListAsync();

            Assert.Equal(0.14m, venta.Total);
            Assert.Equal(0.01m, detalles.Sum(d => d.DescuentoGeneralProrrateado));
            Assert.Equal(venta.Total, detalles.Sum(d => d.SubtotalFinal));
        }
    }

    [Fact]
    public async Task CreateAsync_LineaIva0_DejaNetoIgualFinal()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx);
            producto.PrecioVenta = 100m;
            producto.PorcentajeIVA = 0m;
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto.Id));

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();
            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);

            Assert.Equal(0m, detalle.PorcentajeIVA);
            Assert.Equal(100m, detalle.SubtotalNeto);
            Assert.Equal(0m, detalle.SubtotalIVA);
            Assert.Equal(100m, venta.Subtotal);
            Assert.Equal(0m, venta.IVA);
            Assert.Equal(100m, venta.Total);
        }
    }

    [Fact]
    public async Task CreateAsync_LineasMixtas_SumaSubtotalIvaYTotalDesdeSnapshots()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto21, cliente) = await SeedBaseAsync(ctx);
            producto21.PrecioVenta = 1210m;
            producto21.PorcentajeIVA = 21m;
            var alicuota105 = await CrearAlicuotaAsync(ctx, "IVA105", "IVA 10.5", 10.5m);
            var producto105 = await CrearProductoAsync(ctx, producto21, "P105", 110.50m, 0m, alicuotaIVAId: alicuota105.Id);
            var producto0 = await CrearProductoAsync(ctx, producto21, "P000", 100m, 0m, porcentajeIVA: 0m);
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, new[]
            {
                Detalle(producto21.Id, cantidad: 1, descuento: 0m),
                Detalle(producto105.Id, cantidad: 1, descuento: 0m),
                Detalle(producto0.Id, cantidad: 1, descuento: 0m)
            }));

            var venta = await ctx.Ventas.AsNoTracking().SingleAsync();

            Assert.Equal(1200m, venta.Subtotal);
            Assert.Equal(220.50m, venta.IVA);
            Assert.Equal(1420.50m, venta.Total);
        }
    }

    [Fact]
    public async Task UpdateAsync_RecreaDetalleConSnapshotIvaVigente()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto21, cliente) = await SeedBaseAsync(ctx);
            producto21.PrecioVenta = 121m;
            producto21.PorcentajeIVA = 21m;

            var alicuota105 = await CrearAlicuotaAsync(ctx, "IVA105", "IVA 10.5", 10.5m);
            var producto105 = await CrearProductoAsync(ctx, producto21, "P105U", 110.50m, 0m, alicuotaIVAId: alicuota105.Id);
            await ctx.SaveChangesAsync();

            var svc = BuildService(ctx, CreateMapper(), new StubCajaService(apertura));

            await svc.CreateAsync(BuildViewModel(cliente.Id, producto21.Id));

            var ventaPersistida = await ctx.Ventas.SingleAsync();
            ventaPersistida.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            var ventaOriginal = await ctx.Ventas
                .AsNoTracking()
                .SingleAsync();

            var updateModel = BuildViewModel(cliente.Id, producto105.Id);
            updateModel.Id = ventaOriginal.Id;
            updateModel.RowVersion = ventaOriginal.RowVersion;
            updateModel.FechaVenta = ventaOriginal.FechaVenta;
            updateModel.Estado = ventaOriginal.Estado;

            await svc.UpdateAsync(ventaOriginal.Id, updateModel);

            var detalleActivo = await ctx.VentaDetalles
                .AsNoTracking()
                .SingleAsync(d => !d.IsDeleted);

            Assert.Equal(producto105.Id, detalleActivo.ProductoId);
            Assert.Equal(110.50m, detalleActivo.PrecioUnitario);
            Assert.Equal(10.5m, detalleActivo.PorcentajeIVA);
            Assert.Equal(alicuota105.Id, detalleActivo.AlicuotaIVAId);
            Assert.Equal("IVA 10.5", detalleActivo.AlicuotaIVANombre);
            Assert.Equal(100m, detalleActivo.SubtotalNeto);
            Assert.Equal(10.50m, detalleActivo.SubtotalIVA);

            var ventaActualizada = await ctx.Ventas
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(100m, ventaActualizada.Subtotal);
            Assert.Equal(10.50m, ventaActualizada.IVA);
            Assert.Equal(110.50m, ventaActualizada.Total);
        }
    }
}




