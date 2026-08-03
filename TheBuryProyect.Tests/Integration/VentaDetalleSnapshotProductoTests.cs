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
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs mínimos (Micro-lote 5). ICajaService / ICurrentUserService son file-scoped
// en otros archivos; se replican acá acotados a lo que usa CreateAsync/UpdateAsync.
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceSnap : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaServiceSnap(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);
    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);

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

file sealed class StubCurrentUserServiceSnap : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

/// <summary>
/// Micro-lote 5 — snapshot histórico de la identidad del producto en <see cref="VentaDetalle"/>.
///
/// Cubre: captura server-side (Create/Update), inmutabilidad ante rename/recode/reprice,
/// conservación al editar, recaptura al reemplazar el producto, protección contra payload
/// manipulado, lectura centralizada con fallback (snapshot → relación viva legacy → Producto #id)
/// y render sin NullReferenceException cuando el producto ya no está disponible.
/// </summary>
public class VentaDetalleSnapshotProductoTests
{
    private const string TestUser = "testuser";

    // =======================================================================
    // Grupo 1 — Helper (regla pura de captura y lectura)
    // =======================================================================

    [Fact]
    public void Helper_Capturar_TomaNombreYCodigoDelProducto()
    {
        var detalle = new VentaDetalle { ProductoId = 7 };
        var producto = new Producto { Nombre = "Original", Codigo = "P-ORIG" };

        VentaDetalleProductoSnapshot.Capturar(detalle, producto, "fallbackNombre", "fallbackCodigo");

        Assert.Equal("Original", detalle.ProductoNombreAlMomento);
        Assert.Equal("P-ORIG", detalle.ProductoCodigoAlMomento);
    }

    [Fact]
    public void Helper_Capturar_SinProducto_UsaFallbackYLuegoProductoNumeroId()
    {
        var conFallback = new VentaDetalle { ProductoId = 9 };
        VentaDetalleProductoSnapshot.Capturar(conFallback, producto: null, "NombreCotizacion", "COT-9");
        Assert.Equal("NombreCotizacion", conFallback.ProductoNombreAlMomento);
        Assert.Equal("COT-9", conFallback.ProductoCodigoAlMomento);

        var sinNada = new VentaDetalle { ProductoId = 42 };
        VentaDetalleProductoSnapshot.Capturar(sinNada, producto: null);
        Assert.Equal("Producto #42", sinNada.ProductoNombreAlMomento);
        Assert.Null(sinNada.ProductoCodigoAlMomento);
    }

    [Fact]
    public void Helper_ResolverNombre_PrioridadSnapshotLuegoVivoLuegoId()
    {
        var conSnapshot = new VentaDetalle { ProductoId = 1, ProductoNombreAlMomento = "Snap", Producto = new Producto { Nombre = "Vivo" } };
        Assert.Equal("Snap", VentaDetalleProductoSnapshot.ResolverNombre(conSnapshot));

        var legacy = new VentaDetalle { ProductoId = 2, ProductoNombreAlMomento = null, Producto = new Producto { Nombre = "Vivo" } };
        Assert.Equal("Vivo", VentaDetalleProductoSnapshot.ResolverNombre(legacy));

        var huerfano = new VentaDetalle { ProductoId = 3, ProductoNombreAlMomento = null, Producto = null! };
        Assert.Equal("Producto #3", VentaDetalleProductoSnapshot.ResolverNombre(huerfano));
    }

    [Fact]
    public void Helper_ResolverCodigo_PrioridadSnapshotLuegoVivoLuegoNull()
    {
        var conSnapshot = new VentaDetalle { ProductoCodigoAlMomento = "SNP", Producto = new Producto { Codigo = "VIV" } };
        Assert.Equal("SNP", VentaDetalleProductoSnapshot.ResolverCodigo(conSnapshot));

        var legacy = new VentaDetalle { ProductoCodigoAlMomento = null, Producto = new Producto { Codigo = "VIV" } };
        Assert.Equal("VIV", VentaDetalleProductoSnapshot.ResolverCodigo(legacy));

        var huerfano = new VentaDetalle { ProductoCodigoAlMomento = null, Producto = null! };
        Assert.Null(VentaDetalleProductoSnapshot.ResolverCodigo(huerfano));
    }

    // =======================================================================
    // Grupo 2 — Lectura centralizada (mapper VentaDetalle → VentaDetalleViewModel)
    // =======================================================================

    [Fact]
    public void Mapper_ConSnapshot_UsaSnapshotNoProductoVivo()
    {
        var vm = MapDetalle(new VentaDetalle
        {
            ProductoId = 5,
            ProductoNombreAlMomento = "Nombre Histórico",
            ProductoCodigoAlMomento = "HIST-5",
            Producto = new Producto { Nombre = "Nombre Actual", Codigo = "ACT-5" }
        });

        Assert.Equal("Nombre Histórico", vm.ProductoNombre);
        Assert.Equal("HIST-5", vm.ProductoCodigo);
    }

    [Fact]
    public void Mapper_FilaLegacySinSnapshot_UsaProductoVivo()
    {
        var vm = MapDetalle(new VentaDetalle
        {
            ProductoId = 6,
            ProductoNombreAlMomento = null,
            ProductoCodigoAlMomento = null,
            Producto = new Producto { Nombre = "Nombre Vivo", Codigo = "VIV-6" }
        });

        Assert.Equal("Nombre Vivo", vm.ProductoNombre);
        Assert.Equal("VIV-6", vm.ProductoCodigo);
    }

    [Fact]
    public void Mapper_SinSnapshotNiProducto_RenderizaProductoNumeroIdSinExcepcion()
    {
        var vm = MapDetalle(new VentaDetalle
        {
            ProductoId = 99,
            ProductoNombreAlMomento = null,
            ProductoCodigoAlMomento = null,
            Producto = null!
        });

        Assert.Equal("Producto #99", vm.ProductoNombre);
        Assert.True(string.IsNullOrEmpty(vm.ProductoCodigo));
    }

    // =======================================================================
    // Grupo 3 — Captura server-side en CreateAsync + protección de payload
    // =======================================================================

    [Fact]
    public async Task CreateAsync_CapturaNombreYCodigoDesdeElProductoDeBd()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx, "FIX-RV-SNAPSHOT-ORIGINAL", "FIX-COD-ORIG");
            var svc = BuildService(ctx, new StubCajaServiceSnap(apertura));

            await svc.CreateAsync(BuildCreateVm(cliente.Id, producto.Id));

            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);
            Assert.Equal("FIX-RV-SNAPSHOT-ORIGINAL", detalle.ProductoNombreAlMomento);
            Assert.Equal("FIX-COD-ORIG", detalle.ProductoCodigoAlMomento);
        }
    }

    [Fact]
    public async Task CreateAsync_IgnoraNombreYCodigoEnviadosPorElCliente()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx, "Nombre Real", "COD-REAL");
            var svc = BuildService(ctx, new StubCajaServiceSnap(apertura));

            var vm = BuildCreateVm(cliente.Id, producto.Id);
            vm.Detalles[0].ProductoNombre = "NOMBRE FALSO DEL CLIENTE";
            vm.Detalles[0].ProductoCodigo = "COD-FALSO";

            await svc.CreateAsync(vm);

            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);
            Assert.Equal("Nombre Real", detalle.ProductoNombreAlMomento);
            Assert.Equal("COD-REAL", detalle.ProductoCodigoAlMomento);
        }
    }

    // =======================================================================
    // Grupo 4 — Inmutabilidad histórica (defecto reproducido)
    // =======================================================================

    [Fact]
    public async Task RenombrarYReprecarProducto_NoAlteraSnapshotNiPrecioHistorico()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx, "FIX-RV-SNAPSHOT-ORIGINAL", "FIX-COD-ORIG", precioVenta: 100m);
            var svc = BuildService(ctx, new StubCajaServiceSnap(apertura));

            await svc.CreateAsync(BuildCreateVm(cliente.Id, producto.Id));

            var precioHistorico = (await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted)).PrecioUnitario;

            // Mutar el catálogo DESPUÉS de la venta
            var productoTracked = await ctx.Productos.SingleAsync(p => p.Id == producto.Id);
            productoTracked.Nombre = "FIX-RV-SNAPSHOT-RENOMBRADO";
            productoTracked.Codigo = "FIX-COD-NUEVO";
            productoTracked.PrecioVenta = 999m;
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);
            Assert.Equal("FIX-RV-SNAPSHOT-ORIGINAL", detalle.ProductoNombreAlMomento);
            Assert.Equal("FIX-COD-ORIG", detalle.ProductoCodigoAlMomento);
            Assert.Equal(precioHistorico, detalle.PrecioUnitario);

            // El catálogo sí refleja los valores nuevos
            var actual = await ctx.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
            Assert.Equal("FIX-RV-SNAPSHOT-RENOMBRADO", actual.Nombre);
            Assert.Equal(999m, actual.PrecioVenta);
        }
    }

    [Fact]
    public async Task ProductoDesactivado_LaVentaHistoricaSigueMostrandoElSnapshot()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx, "Producto Vigente", "PV-1");
            var svc = BuildService(ctx, new StubCajaServiceSnap(apertura));
            await svc.CreateAsync(BuildCreateVm(cliente.Id, producto.Id));

            var productoTracked = await ctx.Productos.SingleAsync(p => p.Id == producto.Id);
            productoTracked.Activo = false;
            productoTracked.Nombre = "Producto Vigente (renombrado inactivo)";
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            var detalle = await ctx.VentaDetalles.AsNoTracking().Include(d => d.Producto).SingleAsync(d => !d.IsDeleted);
            var vm = MapDetalle(detalle);
            Assert.Equal("Producto Vigente", vm.ProductoNombre);
            Assert.Equal("PV-1", vm.ProductoCodigo);
        }
    }

    // =======================================================================
    // Grupo 5 — Edición: conserva / reemplaza / no manipulable
    // =======================================================================

    [Fact]
    public async Task UpdateAsync_EditarCantidadTrasRenombrar_ConservaSnapshotOriginal()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx, "Original", "ORIG-1");
            var svc = BuildService(ctx, new StubCajaServiceSnap(apertura));
            await svc.CreateAsync(BuildCreateVm(cliente.Id, producto.Id, cantidad: 1));

            var (ventaId, detalleId, rowVersion) = await PrepararEdicionAsync(ctx);

            // Renombrar el producto ENTRE el alta y la edición
            var productoTracked = await ctx.Productos.SingleAsync(p => p.Id == producto.Id);
            productoTracked.Nombre = "Renombrado";
            productoTracked.Codigo = "REN-1";
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            var updateVm = BuildUpdateVm(ventaId, cliente.Id, rowVersion,
                DetalleVm(producto.Id, cantidad: 5, id: detalleId));
            await svc.UpdateAsync(ventaId, updateVm);

            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);
            Assert.Equal(5, detalle.Cantidad);                       // el cambio de cantidad se aplicó
            Assert.Equal("Original", detalle.ProductoNombreAlMomento); // identidad conservada
            Assert.Equal("ORIG-1", detalle.ProductoCodigoAlMomento);
        }
    }

    [Fact]
    public async Task UpdateAsync_ReemplazarProducto_CapturaSnapshotDelNuevo()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, productoA, cliente) = await SeedBaseAsync(ctx, "Producto A", "A-1");
            var productoB = await CrearProductoAsync(ctx, productoA, "Producto B", "B-1");
            var svc = BuildService(ctx, new StubCajaServiceSnap(apertura));
            await svc.CreateAsync(BuildCreateVm(cliente.Id, productoA.Id));

            var (ventaId, detalleId, rowVersion) = await PrepararEdicionAsync(ctx);

            // Reemplazar el producto de la línea (línea nueva, sin Id conservado)
            var updateVm = BuildUpdateVm(ventaId, cliente.Id, rowVersion,
                DetalleVm(productoB.Id, cantidad: 1, id: 0));
            await svc.UpdateAsync(ventaId, updateVm);

            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);
            Assert.Equal(productoB.Id, detalle.ProductoId);
            Assert.Equal("Producto B", detalle.ProductoNombreAlMomento);
            Assert.Equal("B-1", detalle.ProductoCodigoAlMomento);
        }
    }

    [Fact]
    public async Task UpdateAsync_PayloadConNombreFalso_SeIgnora()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var (apertura, producto, cliente) = await SeedBaseAsync(ctx, "Real", "R-1");
            var svc = BuildService(ctx, new StubCajaServiceSnap(apertura));
            await svc.CreateAsync(BuildCreateVm(cliente.Id, producto.Id));

            var (ventaId, detalleId, rowVersion) = await PrepararEdicionAsync(ctx);

            var detalleVm = DetalleVm(producto.Id, cantidad: 2, id: detalleId);
            detalleVm.ProductoNombre = "HACKEADO";
            detalleVm.ProductoCodigo = "HACK";
            var updateVm = BuildUpdateVm(ventaId, cliente.Id, rowVersion, detalleVm);
            await svc.UpdateAsync(ventaId, updateVm);

            var detalle = await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted);
            Assert.Equal("Real", detalle.ProductoNombreAlMomento);
            Assert.Equal("R-1", detalle.ProductoCodigoAlMomento);
        }
    }

    // =======================================================================
    // Infraestructura
    // =======================================================================

    private static (AppDbContext ctx, SqliteConnection conn) CreateDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

    private static VentaDetalleViewModel MapDetalle(VentaDetalle detalle) =>
        CreateMapper().Map<VentaDetalleViewModel>(detalle);

    private static async Task<(AperturaCaja apertura, Producto producto, Cliente cliente)> SeedBaseAsync(
        AppDbContext ctx, string productoNombre, string productoCodigo, decimal precioVenta = 100m)
    {
        var caja = new Caja { Codigo = "C01", Nombre = "Caja Test", IsDeleted = false, RowVersion = new byte[8] };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();

        var categoria = new Categoria { Codigo = "CAT01", Nombre = "Categoria", IsDeleted = false, RowVersion = new byte[8] };
        var marca = new Marca { Codigo = "MRC01", Nombre = "Marca", IsDeleted = false, RowVersion = new byte[8] };
        ctx.Categorias.Add(categoria);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

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
            Nombre = productoNombre,
            Codigo = productoCodigo,
            PrecioCompra = 60m,
            PrecioVenta = precioVenta,
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
        AppDbContext ctx, Producto baseProd, string nombre, string codigo, decimal precioVenta = 100m)
    {
        var producto = new Producto
        {
            Nombre = nombre,
            Codigo = codigo,
            PrecioCompra = 60m,
            PrecioVenta = precioVenta,
            ComisionPorcentaje = 0m,
            StockActual = 100,
            CategoriaId = baseProd.CategoriaId,
            MarcaId = baseProd.MarcaId,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto;
    }

    /// <summary>Asigna una RowVersion estable y devuelve (ventaId, detalleId, rowVersion) para editar.</summary>
    private static async Task<(int ventaId, int detalleId, byte[] rowVersion)> PrepararEdicionAsync(AppDbContext ctx)
    {
        var venta = await ctx.Ventas.SingleAsync();
        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await ctx.SaveChangesAsync();
        var detalleId = (await ctx.VentaDetalles.AsNoTracking().SingleAsync(d => !d.IsDeleted)).Id;
        var rowVersion = venta.RowVersion;
        ctx.ChangeTracker.Clear();
        return (venta.Id, detalleId, rowVersion);
    }

    private static VentaService BuildService(AppDbContext ctx, ICajaService cajaService) =>
        new(
            ctx,
            CreateMapper(),
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(ctx, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(ctx),
            new StubCurrentUserServiceSnap(),
            null!,
            cajaService,
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());

    private static VentaViewModel BuildCreateVm(int clienteId, int productoId, int cantidad = 1) => new()
    {
        ClienteId = clienteId,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Descuento = 0,
        Detalles = new List<VentaDetalleViewModel> { DetalleVm(productoId, cantidad, id: 0) }
    };

    private static VentaViewModel BuildUpdateVm(int ventaId, int clienteId, byte[] rowVersion, VentaDetalleViewModel detalle) => new()
    {
        Id = ventaId,
        ClienteId = clienteId,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Descuento = 0,
        RowVersion = rowVersion,
        Detalles = new List<VentaDetalleViewModel> { detalle }
    };

    private static VentaDetalleViewModel DetalleVm(int productoId, int cantidad, int id) => new()
    {
        Id = id,
        ProductoId = productoId,
        Cantidad = cantidad,
        PrecioUnitario = 0m,
        Descuento = 0m
    };
}
