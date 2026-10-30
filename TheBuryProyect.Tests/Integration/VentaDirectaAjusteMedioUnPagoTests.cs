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
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

file sealed class StubNotificacionAjusteDirecta : INotificacionService
{
    public Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model) => Task.FromResult(new Notificacion());
    public Task CrearNotificacionParaUsuarioAsync(string u, TipoNotificacion t, string ti, string m, string? url = null, PrioridadNotificacion p = PrioridadNotificacion.Media) => Task.CompletedTask;
    public Task CrearNotificacionParaRolAsync(string r, TipoNotificacion t, string ti, string m, string? url = null, PrioridadNotificacion p = PrioridadNotificacion.Media) => Task.CompletedTask;
    public Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(string u, bool s = false, int l = 50) => Task.FromResult(new List<NotificacionViewModel>());
    public Task<int> ObtenerCantidadNoLeidasAsync(string u) => Task.FromResult(0);
    public Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id) => Task.FromResult<Notificacion?>(null);
    public Task MarcarComoLeidaAsync(int id, string u, byte[]? rv = null) => Task.CompletedTask;
    public Task MarcarTodasComoLeidasAsync(string u) => Task.CompletedTask;
    public Task EliminarNotificacionAsync(int id, string u, byte[]? rv = null) => Task.CompletedTask;
    public Task LimpiarNotificacionesAntiguasAsync(int d = 30) => Task.CompletedTask;
    public Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string u) => Task.FromResult(new ListaNotificacionesViewModel());
}

file sealed class StubCurrentUserAjusteDirecta : ICurrentUserService
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
/// H10 (D4 de docs/iva-recargos-verificacion.md): la venta DIRECTA con Efectivo / Transferencia aplica el mismo plan
/// general de 1 pago que ya aplicaba la venta nacida de la cotización (<c>AjusteMedioUnPago</c>): el backend es
/// autoritativo, el preview muestra el mismo total, y Subtotal + IVA == Total para que la factura cierre.
/// </summary>
public class VentaDirectaAjusteMedioUnPagoTests : IDisposable
{
    private const decimal Precio = 1_000m;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly VentaService _service;
    private static int _counter = 9100;

    public VentaDirectaAjusteMedioUnPagoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        var cajaEntidad = new Caja { Codigo = "CJAJ", Nombre = "Caja ajuste", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(cajaEntidad);
        _context.SaveChanges();
        _context.AperturasCaja.Add(new AperturaCaja { CajaId = cajaEntidad.Id, UsuarioApertura = "testuser", MontoInicial = 0m, Cerrada = false });
        _context.SaveChanges();

        var caja = new CajaService(_context, _mapper, NullLogger<CajaService>.Instance, new StubNotificacionAjusteDirecta());
        _service = new VentaService(
            _context, _mapper, NullLogger<VentaService>.Instance, null!, null!,
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context), new StubCurrentUserAjusteDirecta(),
            null!, caja, null!, new StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<(Cliente Cliente, Producto Producto)> SeedBaseAsync(decimal precio = Precio)
    {
        var n = Interlocked.Increment(ref _counter);
        var categoria = new Categoria { Codigo = $"CAT-AJ-{n}", Nombre = $"Cat AJ {n}" };
        var marca = new Marca { Codigo = $"MAR-AJ-{n}", Nombre = $"Marca AJ {n}" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Ajuste", TipoDocumento = "DNI",
            NumeroDocumento = n.ToString("D8"), Email = $"aj{n}@test.com",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        var producto = new Producto
        {
            Codigo = $"PROD-AJ-{n}", Nombre = $"Producto AJ {n}",
            CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = precio / 2m, PrecioVenta = precio, PorcentajeIVA = 21m, StockActual = 100
        };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return (cliente, producto);
    }

    private async Task SeedPlanAsync(TipoPago tipoPago, decimal ajustePorcentaje)
    {
        var medio = new ConfiguracionPago
        {
            TipoPago = tipoPago, Nombre = $"Medio {tipoPago} {Interlocked.Increment(ref _counter)}", Activo = true
        };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();
        _context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id, TipoPago = tipoPago, CantidadCuotas = 1,
            AjustePorcentaje = ajustePorcentaje, Etiqueta = "1 pago", Activo = true
        });
        await _context.SaveChangesAsync();
    }

    private static VentaViewModel Vm(int clienteId, int productoId, TipoPago tipoPago, decimal precio = Precio, int cantidad = 1)
        => new()
        {
            ClienteId = clienteId,
            FechaVenta = DateTime.UtcNow,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = tipoPago,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = productoId, Cantidad = cantidad, PrecioUnitario = precio }
            }
        };

    private async Task<Venta> CargarAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Ventas.Include(v => v.Detalles).AsNoTracking().FirstAsync(v => v.Id == id);
    }

    [Theory]
    [InlineData(TipoPago.Transferencia, 3, 1_030, 30)]
    [InlineData(TipoPago.Efectivo, -5, 950, -50)]
    public async Task Create_AplicaElPlanDeUnPagoDelMedio_YPersisteElAjuste(
        TipoPago pago, int porcentaje, int totalEsperado, int ajusteEsperado)
    {
        var (cliente, producto) = await SeedBaseAsync();
        await SeedPlanAsync(pago, porcentaje);

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, pago));

        var venta = await CargarAsync(creada.Id);
        Assert.Equal(totalEsperado, venta.Total);
        Assert.Equal(porcentaje, venta.AjusteMedioPagoPorcentaje);
        Assert.Equal(ajusteEsperado, venta.AjusteMedioPagoMonto);
        Assert.Equal(venta.Total, venta.TotalACobrar);
        Assert.Equal(venta.Total, creada.Total);
    }

    [Theory]
    [InlineData(TipoPago.Transferencia, 3)]
    [InlineData(TipoPago.Efectivo, -5)]
    public async Task Create_DiscriminaElAjusteEnNetoEIva_SubtotalMasIvaIgualTotal(TipoPago pago, int porcentaje)
    {
        var (cliente, producto) = await SeedBaseAsync(precio: 1_234.57m);
        await SeedPlanAsync(pago, porcentaje);

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, pago, precio: 1_234.57m, cantidad: 3));

        var venta = await CargarAsync(creada.Id);
        Assert.Equal(venta.Total, venta.Subtotal + venta.IVA);
        Assert.True(venta.IVA > 0m);
    }

    [Fact]
    public async Task Create_SinPlanConfigurado_NoAplicaAjuste()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, TipoPago.Transferencia));

        var venta = await CargarAsync(creada.Id);
        Assert.Equal(Precio, venta.Total);
        Assert.Null(venta.AjusteMedioPagoPorcentaje);
        Assert.Null(venta.AjusteMedioPagoMonto);
    }

    [Fact]
    public async Task Create_PlanConAjusteCero_NoAplicaAjuste()
    {
        var (cliente, producto) = await SeedBaseAsync();
        await SeedPlanAsync(TipoPago.Transferencia, 0m);

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, TipoPago.Transferencia));

        var venta = await CargarAsync(creada.Id);
        Assert.Equal(Precio, venta.Total);
        Assert.Null(venta.AjusteMedioPagoMonto);
    }

    [Fact]
    public async Task Preview_MuestraElMismoTotalQueGuardaCreate()
    {
        var (cliente, producto) = await SeedBaseAsync();
        await SeedPlanAsync(TipoPago.Transferencia, 3m);

        var preview = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            Detalles = new List<DetalleCalculoVentaRequest>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = Precio }
            },
            TipoPago = TipoPago.Transferencia
        });
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, TipoPago.Transferencia));

        Assert.Equal(1_030m, preview.Total);
        Assert.Equal(1_030m, preview.TotalConAjustePagoGlobal);
        Assert.Equal(30m, preview.AjustePagoGlobalAplicado);
        Assert.Equal(3m, preview.PorcentajeAjustePagoGlobalAplicado);
        Assert.Equal(preview.Total, creada.Total);
        var guardada = await CargarAsync(creada.Id);
        Assert.Equal(guardada.Subtotal, preview.Subtotal);
        Assert.Equal(guardada.IVA, preview.IVA);
        Assert.Equal(preview.Total, preview.Subtotal + preview.IVA);
    }

    [Fact]
    public async Task Preview_RecargoDiscriminadoEnNetoEIva_IgualQueCreate()
    {
        var (cliente, producto) = await SeedBaseAsync(precio: 1_210m);
        await SeedPlanAsync(TipoPago.Transferencia, 10m);

        var preview = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            Detalles = new List<DetalleCalculoVentaRequest>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 1_210m }
            },
            TipoPago = TipoPago.Transferencia
        });
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, TipoPago.Transferencia, precio: 1_210m));

        var guardada = await CargarAsync(creada.Id);
        Assert.Equal(1_331m, preview.Total);
        Assert.Equal(1_100m, guardada.Subtotal);
        Assert.Equal(231m, guardada.IVA);
        Assert.Equal(guardada.Subtotal, preview.Subtotal);
        Assert.Equal(guardada.IVA, preview.IVA);
    }

    [Fact]
    public async Task Preview_MedioSinAjusteDeUnPago_NoAplicaAjuste()
    {
        var (_, producto) = await SeedBaseAsync();
        await SeedPlanAsync(TipoPago.Transferencia, 3m);

        var preview = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            Detalles = new List<DetalleCalculoVentaRequest>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = Precio }
            },
            TipoPago = TipoPago.CuentaCorriente
        });

        Assert.Equal(Precio, preview.Total);
        Assert.Null(preview.TotalConAjustePagoGlobal);
    }

    [Theory]
    [InlineData(TipoPago.Transferencia, 3, 1_030)]
    [InlineData(TipoPago.Efectivo, -5, 950)]
    public async Task Update_VentaDirecta_ReaplicaElAjusteUnaSolaVez(TipoPago pago, int porcentaje, int totalEsperado)
    {
        var (cliente, producto) = await SeedBaseAsync();
        await SeedPlanAsync(pago, porcentaje);
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, pago));
        var venta = await _context.Ventas.FirstAsync(v => v.Id == creada.Id);
        venta.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        for (var i = 0; i < 2; i++)
        {
            var original = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == creada.Id);
            var vm = Vm(cliente.Id, producto.Id, pago);
            vm.Id = original.Id;
            vm.RowVersion = original.RowVersion;
            vm.Estado = original.Estado;
            var resultado = await _service.UpdateAsync(original.Id, vm);
            Assert.Equal(totalEsperado, resultado!.Total);
        }

        var guardada = await CargarAsync(creada.Id);
        Assert.Equal(totalEsperado, guardada.Total);
        Assert.Equal(porcentaje, guardada.AjusteMedioPagoPorcentaje);
        Assert.Equal(guardada.Total, guardada.Subtotal + guardada.IVA);
    }

    [Theory]
    [InlineData(TipoPago.Transferencia, 3)]
    [InlineData(TipoPago.Efectivo, -5)]
    public async Task Facturar_VentaDirecta_ElComprobanteCierreYGuardaElAjuste(TipoPago pago, int porcentaje)
    {
        var (cliente, producto) = await SeedBaseAsync(precio: 1_234.57m);
        await SeedPlanAsync(pago, porcentaje);
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, pago, precio: 1_234.57m, cantidad: 3));
        var venta = await _context.Ventas.FirstAsync(v => v.Id == creada.Id);
        venta.Estado = EstadoVenta.Confirmada;
        await _context.SaveChangesAsync();

        var ok = await _service.FacturarVentaAsync(venta.Id, new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow });

        Assert.True(ok);
        _context.ChangeTracker.Clear();
        var factura = await _context.Facturas.AsNoTracking().SingleAsync(f => f.VentaId == venta.Id);
        var guardada = await CargarAsync(venta.Id);
        Assert.Equal(guardada.Total, factura.Total);
        Assert.Equal(factura.Total, factura.Subtotal + factura.IVA);
        Assert.Equal(porcentaje, guardada.AjusteMedioPagoPorcentaje);
    }
}
