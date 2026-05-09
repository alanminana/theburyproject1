using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para AplicarAjustesPorItemAsync (Fase 16.4).
///
/// Cubre:
/// - Venta sin pagos por ítem → comportamiento anterior preservado
/// - Detalle con TipoPago propio → se persiste
/// - Plan activo positivo → aumenta Total
/// - Plan activo negativo → reduce Total
/// - Dos ítems mismo medio/plan → ajustes individuales correctos
/// - Ítems con distinto plan → ajustes independientes acumulados
/// - Plan inactivo → rechazado (InvalidOperationException)
/// - Plan de otro TipoPago → rechazado
/// - Plan de otro producto → rechazado
/// - Fallback TipoPago global cuando detalle.TipoPago es null
/// - Snapshots PorcentajeAjustePlanAplicado y MontoAjustePlanAplicado persistidos
/// - Venta.Total final sale del backend, no del frontend
/// </summary>
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

    // ── Infraestructura ──────────────────────────────────────────────

    private VentaService BuildService(AperturaCaja apertura) =>
        new VentaService(
            _context,
            _mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserServiceAjustePorItem(),
            null!,
            new StubCajaServiceAjustePorItem(apertura),
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

        // Producto con precio vigente fijo 100 (sin ProductoPrecioLista — usa fallback PrecioVenta)
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

    private async Task<Producto> AgregarProductoAsync(int categoriaId, int marcaId, decimal precioVenta = 100m)
    {
        var n = Interlocked.Increment(ref _counter).ToString();
        var producto = new Producto
        {
            Nombre = $"Prod Extra {n}",
            Codigo = $"PX{n}",
            PrecioCompra = 50m,
            PrecioVenta = precioVenta,
            PorcentajeIVA = 0m,
            ComisionPorcentaje = 0m,
            StockActual = 100,
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<(int condicionId, int planId)> SeedCondicionPlanAsync(
        int productoId,
        TipoPago tipoPago,
        decimal ajustePorcentaje,
        int cantidadCuotas = 1,
        bool planActivo = true)
    {
        var condicion = new ProductoCondicionPago
        {
            ProductoId = productoId,
            TipoPago = tipoPago,
            Permitido = true,
            Activo = true,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.ProductoCondicionesPago.Add(condicion);
        await _context.SaveChangesAsync();

        var plan = await SeedPlanParaCondicionAsync(condicion.Id, ajustePorcentaje, cantidadCuotas, planActivo);
        return (condicion.Id, plan);
    }

    private async Task<int> SeedPlanParaCondicionAsync(
        int condicionId,
        decimal ajustePorcentaje,
        int cantidadCuotas,
        bool activo = true)
    {
        var plan = new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicionId,
            ProductoCondicionPagoTarjetaId = null,
            CantidadCuotas = cantidadCuotas,
            AjustePorcentaje = ajustePorcentaje,
            TipoAjuste = TipoAjustePagoPlan.Porcentaje,
            Activo = activo,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.ProductoCondicionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();
        return plan.Id;
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

    // ── Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task SinPagosPorItem_ConservaComportamientoLegacy()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();
        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.Efectivo,
            new[] { DetalleVm(producto.Id, cantidad: 2) }));

        // 2 * 100 = 200; sin ajuste
        Assert.Equal(200m, resultado.Total);

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Null(detalle.ProductoCondicionPagoPlanId);
        Assert.Null(detalle.PorcentajeAjustePlanAplicado);
        Assert.Null(detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task DetalleTieneTipoPagoPersistido()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaDebito, ajustePorcentaje: 0m);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.Efectivo,
            new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.TarjetaDebito, planId: planId) }));

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Equal(TipoPago.TarjetaDebito, detalle.TipoPago);
        Assert.Equal(planId, detalle.ProductoCondicionPagoPlanId);
    }

    [Fact]
    public async Task PlanPositivo_AumentaTotal()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        // 10% recargo sobre SubtotalFinal=100 → ajuste=10 → Total=110
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaCredito, ajustePorcentaje: 10m);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.TarjetaCredito,
            new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId) }));

        Assert.Equal(110m, resultado.Total);

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Equal(10m, detalle.PorcentajeAjustePlanAplicado);
        Assert.Equal(10m, detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task PlanNegativo_ReduceTotal()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        // -5% descuento → ajuste=-5 → Total=95
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaDebito, ajustePorcentaje: -5m);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.TarjetaDebito,
            new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.TarjetaDebito, planId: planId) }));

        Assert.Equal(95m, resultado.Total);

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Equal(-5m, detalle.PorcentajeAjustePlanAplicado);
        Assert.Equal(-5m, detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task DosItemsMismoMedioPlan_CadaUnoTieneSuAjusteIndividual()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        // 10% sobre SubtotalFinal 100 cada línea → ajuste 10 cada una → Total 220
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaCredito, ajustePorcentaje: 10m);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.TarjetaCredito, new[]
            {
                DetalleVm(producto.Id, cantidad: 1, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId),
                DetalleVm(producto.Id, cantidad: 1, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId)
            }));

        Assert.Equal(220m, resultado.Total);

        var detalles = await _context.VentaDetalles
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.VentaId == resultado.Id)
            .ToListAsync();

        Assert.Equal(2, detalles.Count);
        Assert.All(detalles, d =>
        {
            Assert.Equal(planId, d.ProductoCondicionPagoPlanId);
            Assert.Equal(10m, d.PorcentajeAjustePlanAplicado);
            Assert.Equal(10m, d.MontoAjustePlanAplicado);
        });
    }

    [Fact]
    public async Task ItemsConDistintoPlan_AjustesAcumuladosIndependientemente()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        // Una sola condicion para TarjetaCredito; dos planes con distinto ajuste y cuotas
        // Ítem 1 (plan A): 100*10%=10 → 110; Ítem 2 (plan B): 100*20%=20 → 120; Total=230
        var (condicionId, planId10) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaCredito, ajustePorcentaje: 10m, cantidadCuotas: 3);
        var planId20 = await SeedPlanParaCondicionAsync(condicionId, ajustePorcentaje: 20m, cantidadCuotas: 6);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.TarjetaCredito, new[]
            {
                DetalleVm(producto.Id, cantidad: 1, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId10),
                DetalleVm(producto.Id, cantidad: 1, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId20)
            }));

        Assert.Equal(230m, resultado.Total);

        var detalles = await _context.VentaDetalles
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.VentaId == resultado.Id)
            .ToListAsync();

        Assert.Equal(2, detalles.Count);
        Assert.Contains(detalles, d => d.PorcentajeAjustePlanAplicado == 10m && d.MontoAjustePlanAplicado == 10m);
        Assert.Contains(detalles, d => d.PorcentajeAjustePlanAplicado == 20m && d.MontoAjustePlanAplicado == 20m);
    }

    [Fact]
    public async Task PlanInactivo_Rechazado()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaCredito, ajustePorcentaje: 10m, planActivo: false);

        var svc = BuildService(apertura);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(VentaVm(
                cliente.Id, TipoPago.TarjetaCredito,
                new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId) })));
    }

    [Fact]
    public async Task PlanDeOtroTipoPago_Rechazado()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        // Plan es para TarjetaDebito, pero el ítem usa TarjetaCredito
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaDebito, ajustePorcentaje: 5m);

        var svc = BuildService(apertura);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(VentaVm(
                cliente.Id, TipoPago.TarjetaCredito,
                new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId) })));
    }

    [Fact]
    public async Task PlanDeOtroProducto_Rechazado()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        var cat = await _context.Categorias.AsNoTracking().FirstAsync();
        var marca = await _context.Marcas.AsNoTracking().FirstAsync();
        var otroProducto = await AgregarProductoAsync(cat.Id, marca.Id);

        // Plan pertenece a otroProducto, no al producto del ítem
        var (_, planIdOtro) = await SeedCondicionPlanAsync(
            otroProducto.Id, TipoPago.TarjetaCredito, ajustePorcentaje: 5m);

        var svc = BuildService(apertura);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(VentaVm(
                cliente.Id, TipoPago.TarjetaCredito,
                new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.TarjetaCredito, planId: planIdOtro) })));
    }

    [Fact]
    public async Task FallbackTipoPagoGlobal_AplicaAjusteCuandoItemSinTipoPago()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        // detalle.TipoPago = null → fallback a TipoPago global (TarjetaDebito)
        // Plan para TarjetaDebito 10% → Total = 110
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaDebito, ajustePorcentaje: 10m);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.TarjetaDebito,
            new[] { DetalleVm(producto.Id, tipoPagoItem: null, planId: planId) }));

        Assert.Equal(110m, resultado.Total);

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Equal(10m, detalle.PorcentajeAjustePlanAplicado);
        Assert.Equal(10m, detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task Snapshots_PorcentajeYMontoGuardadosEnVentaDetalle()
    {
        var (apertura, cliente, producto) = await SeedBaseAsync();

        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.MercadoPago, ajustePorcentaje: 8m);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.MercadoPago,
            new[] { DetalleVm(producto.Id, tipoPagoItem: TipoPago.MercadoPago, planId: planId) }));

        var detalle = await _context.VentaDetalles
            .AsNoTracking()
            .SingleAsync(d => !d.IsDeleted && d.VentaId == resultado.Id);

        Assert.Equal(planId, detalle.ProductoCondicionPagoPlanId);
        Assert.Equal(8m, detalle.PorcentajeAjustePlanAplicado);
        Assert.Equal(8m, detalle.MontoAjustePlanAplicado);
    }

    [Fact]
    public async Task TotalFinalEsDelBackend_PrecioFrontendIgnorado()
    {
        // Frontend envía PrecioUnitario 999 — backend lo sobrescribe con precio vigente (100)
        var (apertura, cliente, producto) = await SeedBaseAsync();

        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaCredito, ajustePorcentaje: 10m);

        var svc = BuildService(apertura);

        var vm = VentaVm(cliente.Id, TipoPago.TarjetaCredito, new[]
        {
            new VentaDetalleViewModel
            {
                ProductoId = producto.Id,
                Cantidad = 1,
                PrecioUnitario = 999m,   // valor incorrecto del "frontend"
                Descuento = 0m,
                TipoPago = TipoPago.TarjetaCredito,
                ProductoCondicionPagoPlanId = planId
            }
        });

        var resultado = await svc.CreateAsync(vm);

        // Backend usa precio vigente 100, aplica ajuste 10% → Total = 110
        Assert.Equal(110m, resultado.Total);

        var venta = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.Id);
        Assert.Equal(110m, venta.Total);
    }

    [Fact]
    public async Task ItemSinPlanEnMezclado_NoPropagaAjuste()
    {
        // Un ítem tiene plan, otro no. El sin-plan no debe tener snapshot de ajuste.
        var (apertura, cliente, producto) = await SeedBaseAsync();
        var cat = await _context.Categorias.AsNoTracking().FirstAsync();
        var marca = await _context.Marcas.AsNoTracking().FirstAsync();
        var producto2 = await AgregarProductoAsync(cat.Id, marca.Id, precioVenta: 200m);

        // Solo producto tiene plan
        var (_, planId) = await SeedCondicionPlanAsync(
            producto.Id, TipoPago.TarjetaCredito, ajustePorcentaje: 10m);

        var svc = BuildService(apertura);

        var resultado = await svc.CreateAsync(VentaVm(
            cliente.Id, TipoPago.TarjetaCredito, new[]
            {
                DetalleVm(producto.Id, cantidad: 1, tipoPagoItem: TipoPago.TarjetaCredito, planId: planId),
                DetalleVm(producto2.Id, cantidad: 1, tipoPagoItem: TipoPago.TarjetaCredito)
            }));

        // producto: 100+10=110; producto2: 200 sin ajuste → Total=310
        Assert.Equal(310m, resultado.Total);

        var detalles = await _context.VentaDetalles
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.VentaId == resultado.Id)
            .ToListAsync();

        var conPlan = detalles.Single(d => d.ProductoCondicionPagoPlanId == planId);
        var sinPlan = detalles.Single(d => d.ProductoCondicionPagoPlanId == null);

        Assert.Equal(10m, conPlan.PorcentajeAjustePlanAplicado);
        Assert.Equal(10m, conPlan.MontoAjustePlanAplicado);
        Assert.Null(sinPlan.PorcentajeAjustePlanAplicado);
        Assert.Null(sinPlan.MontoAjustePlanAplicado);
    }
}

// ── Stubs file-scoped ────────────────────────────────────────────

file sealed class StubCurrentUserServiceAjustePorItem : TheBuryProject.Services.Interfaces.ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubCajaServiceAjustePorItem : TheBuryProject.Services.Interfaces.ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaServiceAjustePorItem(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    public Task<List<TheBuryProject.Models.Entities.Caja>> ObtenerTodasCajasAsync() => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.Caja> CrearCajaAsync(TheBuryProject.ViewModels.CajaViewModel model) => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.Caja> ActualizarCajaAsync(int id, TheBuryProject.ViewModels.CajaViewModel model) => throw new NotImplementedException();
    public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotImplementedException();
    public Task<AperturaCaja> AbrirCajaAsync(TheBuryProject.ViewModels.AbrirCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotImplementedException();
    public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotImplementedException();
    public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.MovimientoCaja> RegistrarMovimientoAsync(TheBuryProject.ViewModels.MovimientoCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<List<TheBuryProject.Models.Entities.MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.CierreCaja> CerrarCajaAsync(TheBuryProject.ViewModels.CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<TheBuryProject.Models.Entities.CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<TheBuryProject.Models.Entities.CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<TheBuryProject.ViewModels.DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<TheBuryProject.ViewModels.ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<TheBuryProject.ViewModels.HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}
