using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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

file sealed class StubNotificacionTarjetaV2 : INotificacionService
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

file sealed class StubUsuarioTarjetaV2 : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string? GetIpAddress() => "127.0.0.1";
}

/// <summary>
/// Versión de PRUEBA (solo existe en estos tests; NO está registrada en producción) de un criterio fiscal distinto al v1:
/// servicios (armados/envío) a otra alícuota (10,5 %) y el total se redondea comercialmente a pesos en lugar de truncarse.
/// Todo lo demás delega en v1.
/// </summary>
/// <summary>P-02 / Agente 3: tarjeta + servicios + centavos con los servicios reales (SQLite en memoria).</summary>
public class FiscalTarjetaServiciosV2P02Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly VentaService _ventas;
    private readonly ReporteService _reportes;
    private readonly DevolucionService _devoluciones;
    private static int _n = 53_000;

    public FiscalTarjetaServiciosV2P02Tests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new TheBuryProject.Tests.Infrastructure.SqliteRowVersionRotationInterceptor())
            .Options);
        _context.Database.EnsureCreated();
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        var caja = new Caja { Codigo = "CJP02", Nombre = "Caja P02", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();
        _context.AperturasCaja.Add(new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m, Cerrada = false });
        _context.SaveChanges();

        var cajaService = new CajaService(_context, _mapper, NullLogger<CajaService>.Instance, new StubNotificacionTarjetaV2());
        _ventas = new VentaService(
            _context, _mapper, NullLogger<VentaService>.Instance, null!, null!,
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context), new StubUsuarioTarjetaV2(),
            null!, cajaService, null!, new TheBuryProject.Tests.Helpers.StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta());
        _reportes = new ReporteService(_context, NullLogger<ReporteService>.Instance);
        _devoluciones = new DevolucionService(
            _context, new MovimientoStockService(_context, NullLogger<MovimientoStockService>.Instance),
            new StubUsuarioTarjetaV2(), NullLogger<DevolucionService>.Instance, cajaService);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ───────────────────────── siembra ─────────────────────────

    private async Task<(Cliente Cliente, Producto Producto)> SeedBaseAsync(decimal precio = 1000m)
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CP{n}", Nombre = $"Cat P02 {n}" };
        var marca = new Marca { Codigo = $"MP{n}", Nombre = $"Marca P02 {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "P02", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"),
            Email = $"p02{n}@test.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        var producto = new Producto
        {
            Codigo = $"PP{n}", Nombre = $"Prod {n}", CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 400m, PrecioVenta = precio, PorcentajeIVA = 21m, StockActual = 100
        };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return (cliente, producto);
    }

    private static ReporteIvaFiltroViewModel FiltroDia(DateTime dia) =>
        new() { Tipo = "ventas", FechaDesde = dia.Date, FechaHasta = dia.Date };

    private async Task<FacturaComprobanteViewModel> ComprobanteAsync(int facturaId)
    {
        var factura = await _context.Facturas
            .Include(f => f.Venta).ThenInclude(v => v.Detalles).ThenInclude(d => d.Producto)
            .Include(f => f.Venta).ThenInclude(v => v.Cliente)
            .Include(f => f.Venta).ThenInclude(v => v.Envio)
            .Include(f => f.Venta).ThenInclude(v => v.DatosTarjeta)
            .AsNoTracking()
            .SingleAsync(f => f.Id == facturaId);
        return FacturaComprobanteBuilder.Build(factura);
    }


    /// <summary>
    /// P-02 / Agente 3: contado con TARJETA (recargo del plan) + servicios (armado y envío) + centavos. El recargo de tarjeta se
    /// aplica en GuardarDatosTarjetaAsync, que discriminaba el ajuste sin los servicios exactos: con V2 la base ya está redondeada a
    /// pesos y la inferencia reparte distinto que el comprobante y el Reporte de IVA (que usan el monto del ajuste persistido).
    /// Invariante: lo persistido en la venta (neto/IVA) == comprobante == Reporte de IVA, y Neto + IVA == Total.
    /// </summary>
    [Theory]
    [InlineData(2, 1000.51, 21.0, 10.0, 3)]
    [InlineData(2, 999.99, 21.0, 15.0, 6)]
    [InlineData(2, 4321.09, 21.0, 10.0, 3)]
    [InlineData(2, 1234.57, 10.5, 10.0, 3)]
    [InlineData(2, 999.99, 10.5, 15.0, 6)]
    [InlineData(2, 4321.09, 10.5, 10.0, 3)]
    [InlineData(1, 1000.51, 21.0, 10.0, 3)]
    [InlineData(1, 999.99, 21.0, 15.0, 6)]
    public async Task Tarjeta_ConServiciosYCentavos_VentaComprobanteYReporteDiscriminanLoMismo(int version, double precio, double iva, double pct, int cuotas)
    {
        var (cliente, producto) = await SeedBaseAsync((decimal)precio);
        producto.PorcentajeIVA = (decimal)iva;
        var dia = TheBuryProject.Services.FechaComercial.DiaComercial(DateTime.UtcNow);
        var medio = new ConfiguracionPago { TipoPago = TipoPago.MercadoPago, Nombre = "MP", Activo = true };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();
        var plan = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id, TipoPago = TipoPago.MercadoPago, CantidadCuotas = cuotas, AjustePorcentaje = (decimal)pct, Etiqueta = "plan", Activo = true
        };
        _context.ConfiguracionPagoPlanes.Add(plan);
        await new ServicioVentaPrecioService(_context).GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, 200m, true, 80m),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado5, 450m, true, 120m)
        });
        await _context.SaveChangesAsync();

        using var _ = CriterioFiscalRegistry.UsarParaPruebas(vigente: version);
        var creada = await _ventas.CreateAsync(new VentaViewModel
        {
            ClienteId = cliente.Id, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.MercadoPago,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = (decimal)precio, TipoArmado = TipoServicioVenta.Armado5 }
            },
            TieneEnvio = true,
            Envio = new VentaEnvioViewModel { Destinatario = "Juan", Domicilio = "Calle 1", TipoEnvio = TipoServicioVenta.EnvioCiudad },
            DatosTarjeta = new DatosTarjetaViewModel { NombreTarjeta = "MP", TipoTarjeta = TipoTarjeta.Credito, ConfiguracionPagoPlanId = plan.Id }
        });

        var venta = await _context.Ventas.Include(v => v.Detalles).SingleAsync(v => v.Id == creada.Id);
        Assert.Equal(version, venta.CriterioFiscalVersion);
        // El recargo de tarjeta vive en DatosTarjeta.MontoAjustePagoAplicado (no en AjusteMedioPagoMonto) y debe ser el del plan.
        var datos = await _context.DatosTarjeta.AsNoTracking().SingleAsync(d => d.VentaId == venta.Id);
        var criterio = TheBuryProject.Helpers.CriterioFiscalRegistry.Resolver(version);
        var baseCobrada = criterio.TruncarAPesos((decimal)precio + 650m);
        Assert.True(datos.MontoAjustePagoAplicado > 0m);
        Assert.Equal(venta.Total, criterio.TruncarAPesos(baseCobrada + datos.MontoAjustePagoAplicado.GetValueOrDefault()));
        Assert.Equal(venta.Total, venta.Subtotal + venta.IVA);

        venta.Estado = EstadoVenta.Facturada;
        venta.FechaFacturacion = dia;
        var factura = new Factura
        {
            VentaId = venta.Id, Numero = $"FTV-{Interlocked.Increment(ref _n)}", Tipo = TipoFactura.B, FechaEmision = dia,
            Subtotal = venta.Subtotal, IVA = venta.IVA, Total = venta.Total
        };
        _context.Facturas.Add(factura);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var comprobante = await ComprobanteAsync(factura.Id);
        Assert.Equal(venta.Total, comprobante.Totales.Total);
        Assert.Equal(venta.Subtotal, comprobante.ResumenAlicuotas.Sum(r => r.BaseImponible));
        Assert.Equal(venta.IVA, comprobante.ResumenAlicuotas.Sum(r => r.IVA));
        var reporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
        Assert.Equal(venta.Total, reporte.VentasTotal);
        Assert.Equal(venta.Subtotal, reporte.VentasNeto);
        Assert.Equal(venta.IVA, reporte.VentasIva);
    }

    /// <summary>
    /// P-02 / Agente 3: EMISIÓN real de la factura de contado (FacturarVentaAsync, que vuelve a discriminar y persiste Factura.Subtotal/IVA/Total).
    /// Las demás pruebas V2 arman la Factura a mano. Con V2, Efectivo −5 % / Transferencia +3 %, producto con centavos (21 % y 10,5 %), armado y
    /// envío: la factura emitida debe cerrar (Neto + IVA = Total), coincidir con lo persistido en la venta ANTES de facturar, con el
    /// comprobante, con el Reporte de IVA y con el ingreso de caja.
    /// </summary>
    [Theory]
    [InlineData(2, 1000.51, 21.0, TipoPago.Efectivo, -5.0)]
    [InlineData(2, 1234.57, 10.5, TipoPago.Efectivo, -5.0)]
    [InlineData(2, 999.99, 21.0, TipoPago.Transferencia, 3.0)]
    [InlineData(2, 4321.09, 10.5, TipoPago.Transferencia, 3.0)]
    [InlineData(1, 1000.51, 21.0, TipoPago.Efectivo, -5.0)]
    [InlineData(1, 999.99, 21.0, TipoPago.Transferencia, 3.0)]
    public async Task EmisionDeFactura_ContadoConServiciosYCentavos_FacturaVentaComprobanteReporteYCajaCoinciden(
        int version, double precio, double iva, TipoPago tipoPago, double pct)
    {
        var (cliente, producto) = await SeedBaseAsync((decimal)precio);
        producto.PorcentajeIVA = (decimal)iva;
        var medio = new ConfiguracionPago { TipoPago = tipoPago, Nombre = tipoPago.ToString(), Activo = true };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();
        _context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id, TipoPago = tipoPago, CantidadCuotas = 1, AjustePorcentaje = (decimal)pct, Activo = true
        });
        await new ServicioVentaPrecioService(_context).GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, 200m, true, 80m),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado5, 450m, true, 120m)
        });
        await _context.SaveChangesAsync();

        using var _ = CriterioFiscalRegistry.UsarParaPruebas(vigente: version);
        var creada = await _ventas.CreateAsync(new VentaViewModel
        {
            ClienteId = cliente.Id, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Cotizacion, TipoPago = tipoPago,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = (decimal)precio, TipoArmado = TipoServicioVenta.Armado5 }
            },
            TieneEnvio = true,
            Envio = new VentaEnvioViewModel { Destinatario = "Juan", Domicilio = "Calle 1", TipoEnvio = TipoServicioVenta.EnvioCiudad }
        });

        var antes = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == creada.Id);
        Assert.Equal(version, antes.CriterioFiscalVersion);
        Assert.Equal(antes.Total, antes.Subtotal + antes.IVA);

        var venta = await _context.Ventas.SingleAsync(v => v.Id == creada.Id);
        venta.Estado = EstadoVenta.Confirmada;
        await _context.SaveChangesAsync();
        Assert.True(await _ventas.FacturarVentaAsync(venta.Id, new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow }));
        _context.ChangeTracker.Clear();

        var despues = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == creada.Id);
        var factura = await _context.Facturas.AsNoTracking().SingleAsync(f => f.VentaId == creada.Id);
        Assert.Equal((antes.Subtotal, antes.IVA, antes.Total), (despues.Subtotal, despues.IVA, despues.Total)); // facturar no re-escribe importes
        Assert.Equal((antes.Subtotal, antes.IVA, antes.Total), (factura.Subtotal, factura.IVA, factura.Total));
        Assert.Equal(factura.Total, factura.Subtotal + factura.IVA);

        var comprobante = await ComprobanteAsync(factura.Id);
        Assert.Equal(factura.Total, comprobante.Totales.Total);
        // V2: el desglose coincide al centavo. V1 es inmutable y conserva su diferencia de desglose neto/IVA ya documentada
        // (docs/p02-procedimiento-activacion.md §5; medida acá hasta $0,81 con envío y armado): nunca afecta al total.
        var tolerancia = version == 1 ? 1m : 0m;
        Assert.InRange(comprobante.ResumenAlicuotas.Sum(r => r.BaseImponible) - factura.Subtotal, -tolerancia, tolerancia);
        Assert.InRange(comprobante.ResumenAlicuotas.Sum(r => r.IVA) - factura.IVA, -tolerancia, tolerancia);
        Assert.Equal(factura.Total, comprobante.ResumenAlicuotas.Sum(r => r.BaseImponible) + comprobante.ResumenAlicuotas.Sum(r => r.IVA));

        var dia = TheBuryProject.Services.FechaComercial.DiaComercial(DateTime.UtcNow);
        var reporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
        Assert.Equal(factura.Total, reporte.VentasTotal);
        Assert.Equal(factura.Total, reporte.VentasNeto + reporte.VentasIva);
        Assert.InRange(reporte.VentasNeto - factura.Subtotal, -tolerancia, tolerancia);
        Assert.InRange(reporte.VentasIva - factura.IVA, -tolerancia, tolerancia);

        var ingresoCaja = await _context.MovimientosCaja.AsNoTracking()
            .Where(m => m.ReferenciaId == creada.Id && m.Tipo == TipoMovimientoCaja.Ingreso).SumAsync(m => m.Monto);
        Assert.Equal(despues.TotalACobrar, ingresoCaja);
    }

    /// <summary>
    /// P-02 / Agente 3: devolución (nota de crédito interna) de una venta de contado V2 con descuento de Efectivo −5 % y servicios.
    /// El descuento alcanzó solo a los productos y envío/armado no se devuelven: devolver TODOS los productos reintegra exactamente
    /// <c>Total − servicios</c> (lo cobrado por los productos), y la devolución parcial nunca supera lo proporcional ni deja un
    /// resto negativo. El egreso de Caja coincide con el total de la devolución.
    /// </summary>
    [Theory]
    [InlineData(1000.51, 21.0)]
    [InlineData(1234.57, 10.5)]
    public async Task Devolucion_V2EfectivoConServicios_ReintegraExactamenteLoCobradoPorLosProductos(double precio, double iva)
    {
        var (cliente, producto) = await SeedBaseAsync((decimal)precio);
        producto.PorcentajeIVA = (decimal)iva;
        var medio = new ConfiguracionPago { TipoPago = TipoPago.Efectivo, Nombre = "Efectivo", Activo = true };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();
        _context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id, TipoPago = TipoPago.Efectivo, CantidadCuotas = 1, AjustePorcentaje = -5m, Activo = true
        });
        await new ServicioVentaPrecioService(_context).GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, 200m, true, 80m),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado5, 450m, true, 120m)
        });
        await _context.SaveChangesAsync();

        using var _ = CriterioFiscalRegistry.UsarParaPruebas(vigente: 2);
        var creada = await _ventas.CreateAsync(new VentaViewModel
        {
            ClienteId = cliente.Id, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.Efectivo,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = (decimal)precio, TipoArmado = TipoServicioVenta.Armado5 }
            },
            TieneEnvio = true,
            Envio = new VentaEnvioViewModel { Destinatario = "Juan", Domicilio = "Calle 1", TipoEnvio = TipoServicioVenta.EnvioCiudad }
        });
        var venta = await _context.Ventas.SingleAsync(v => v.Id == creada.Id);
        venta.Estado = EstadoVenta.Confirmada;
        await _context.SaveChangesAsync();
        Assert.True(await _ventas.FacturarVentaAsync(venta.Id, new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow }));
        _context.ChangeTracker.Clear();

        var facturada = await _context.Ventas.AsNoTracking().Include(v => v.Detalles).SingleAsync(v => v.Id == creada.Id);
        var servicios = facturada.Detalles.Sum(d => d.ArmadoSubtotal) + 200m;
        var cobradoPorProductos = facturada.Total - servicios;

        var dev = await _devoluciones.CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = facturada.Id, ClienteId = cliente.Id, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "P02 V2",
                TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero, RegistrarEgresoCaja = true
            },
            new List<DevolucionDetalle> { new() { ProductoId = producto.Id, Cantidad = 2 } });
        _context.ChangeTracker.Clear();
        dev = await _devoluciones.AprobarDevolucionAsync(dev.Id, "testuser", (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion);
        _context.ChangeTracker.Clear();
        dev = await _devoluciones.CompletarDevolucionAsync(dev.Id, (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion);
        _context.ChangeTracker.Clear();

        Assert.Equal(cobradoPorProductos, dev.TotalDevolucion);
        var egreso = await _context.MovimientosCaja.AsNoTracking()
            .Where(m => m.ReferenciaId == dev.Id && m.Tipo == TipoMovimientoCaja.Egreso).SumAsync(m => m.Monto);
        Assert.Equal(dev.TotalDevolucion, egreso);
    }
}
