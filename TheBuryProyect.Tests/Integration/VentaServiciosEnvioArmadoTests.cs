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

file sealed class StubNotificacionServicios : INotificacionService
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

file sealed class StubCurrentUserServicios : ICurrentUserService
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
/// Envíos (Ciudad/Rural) y armados (N.º 1..6) de la venta: precios fijos globales, opcionales, dentro de
/// Venta.Total (y por lo tanto alcanzados por el recargo del medio de pago), con detalle separado.
/// SQLite real, VentaService real (Create/Update/preview) y el servicio de precios globales.
/// </summary>
public class VentaServiciosEnvioArmadoTests : IDisposable
{
    private const decimal PrecioProducto = 1_000m;
    private const decimal PrecioArmado3 = 300m;
    private const decimal PrecioArmado5 = 450m;
    private const decimal PrecioEnvioCiudad = 200m;
    private const decimal PrecioEnvioRural = 500m;
    private const decimal CostoEnvioCiudad = 80m;
    private const decimal CostoEnvioRural = 150m;
    private const decimal CostoArmado3 = 100m;
    private const decimal CostoArmado5 = 120m;
    private readonly VentaCostoServiciosService _costos;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly VentaService _service;
    private readonly ServicioVentaPrecioService _precios;
    private static int _counter = 7000;

    public VentaServiciosEnvioArmadoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance)
            .CreateMapper();

        var caja = new CajaService(_context, _mapper, NullLogger<CajaService>.Instance, new StubNotificacionServicios());
        SeedApertura();

        _service = new VentaService(
            _context,
            _mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserServicios(),
            null!,
            caja,
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());

        _precios = new ServicioVentaPrecioService(_context);
        _precios.GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, PrecioEnvioCiudad, true, CostoEnvioCiudad),
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioRural, PrecioEnvioRural, true, CostoEnvioRural),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado3, PrecioArmado3, true, CostoArmado3),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado5, PrecioArmado5, true, CostoArmado5)
        }).GetAwaiter().GetResult();
        _costos = new VentaCostoServiciosService(_context, NullLogger<VentaCostoServiciosService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private void SeedApertura()
    {
        var caja = new Caja { Codigo = "CJSRV", Nombre = "Caja servicios", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        _context.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "testuser",
            MontoInicial = 0m,
            Cerrada = false
        });
        _context.SaveChanges();
    }

    private async Task<(Cliente Cliente, Producto Producto)> SeedBaseAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        var categoria = new Categoria { Codigo = $"CAT-SV-{n}", Nombre = $"Cat SV {n}" };
        var marca = new Marca { Codigo = $"MAR-SV-{n}", Nombre = $"Marca SV {n}" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Servicios", TipoDocumento = "DNI",
            NumeroDocumento = n.ToString("D8"), Email = $"sv{n}@test.com",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        var producto = new Producto
        {
            Codigo = $"PROD-SV-{n}", Nombre = $"Producto SV {n}",
            CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = PrecioProducto / 2m, PrecioVenta = PrecioProducto,
            PorcentajeIVA = 21m, StockActual = 100
        };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return (cliente, producto);
    }

    private static VentaViewModel Vm(int clienteId, int productoId, int cantidad = 1,
        TipoServicioVenta? armado = null, bool cajaCerrada = false,
        TipoServicioVenta? tipoEnvio = null, TipoPago tipoPago = TipoPago.Efectivo)
    {
        var vm = new VentaViewModel
        {
            ClienteId = clienteId,
            FechaVenta = DateTime.UtcNow,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = tipoPago,
            Detalles = new List<VentaDetalleViewModel>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = cantidad,
                    PrecioUnitario = PrecioProducto,
                    TipoArmado = armado,
                    EntregaCajaCerrada = cajaCerrada
                }
            }
        };

        if (tipoEnvio.HasValue)
        {
            vm.TieneEnvio = true;
            vm.Envio = new VentaEnvioViewModel
            {
                Destinatario = "Juan Pérez",
                Domicilio = "Calle Falsa 123",
                TipoEnvio = tipoEnvio
            };
        }

        return vm;
    }

    private async Task<Venta> CargarVentaAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Ventas
            .Include(v => v.Detalles)
            .Include(v => v.Envio)
            .AsNoTracking()
            .FirstAsync(v => v.Id == id);
    }

    // ── opcionales ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TipoPago.Efectivo)]
    [InlineData(TipoPago.Transferencia)]
    public async Task Caja_DesglosaServiciosGuardados_YConciliaUnSoloCobro(TipoPago pago)
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 2,
            armado: TipoServicioVenta.Armado5, tipoEnvio: TipoServicioVenta.EnvioCiudad, tipoPago: pago));
        var venta = await _context.Ventas.FirstAsync(v => v.Id == creada.Id);
        venta.Estado = EstadoVenta.Confirmada;
        await _context.SaveChangesAsync();
        var caja = new CajaService(_context, _mapper, NullLogger<CajaService>.Instance, new StubNotificacionServicios());
        await caja.RegistrarMovimientoVentaAsync(venta.Id, venta.Numero, venta.TotalACobrar, pago, "testuser");

        // La caja debe mostrar los snapshots de la venta, aunque cambien las tarifas.
        await _precios.GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, 999m, true),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado5, 999m, true)
        });
        _context.ChangeTracker.Clear();
        var detalle = await caja.ObtenerDetallesAperturaAsync(venta.AperturaCajaId!.Value);
        var conciliacion = CajaConciliacionBuilder.Build(detalle, null, true);
        var linea = Assert.Single(conciliacion.Ventas);

        Assert.Equal(2000m, linea.TotalProductos);
        Assert.Equal(900m, linea.TotalArmados);
        Assert.Equal(200m, linea.ImporteEnvio);
        Assert.True(linea.TieneEnvio);
        Assert.True(linea.TieneArmados);
        Assert.Equal(0m, linea.AjusteTotal);
        Assert.Equal(3100m, linea.TotalVenta);
        Assert.Equal(3100m, linea.CobradoAhora);
        Assert.Equal(0m, linea.Pendiente);
        Assert.Equal(3100m, Assert.Single(detalle.Movimientos).Monto);
        Assert.Equal(pago == TipoPago.Efectivo ? 3100m : 0m, conciliacion.CajaFisicaEsperada);
    }

    [Fact]
    public async Task SinEnvioNiArmado_NoAgregaNingunCosto()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 2));

        var venta = await CargarVentaAsync(creada.Id);
        Assert.Equal(2_000m, venta.Total);
        Assert.Null(venta.Envio);
        var detalle = Assert.Single(venta.Detalles);
        Assert.Null(detalle.TipoArmado);
        Assert.Equal(0m, detalle.ArmadoSubtotal);
        Assert.Equal(venta.Total, venta.TotalACobrar);
    }

    [Fact]
    public async Task SoloArmado_SeCobraPorCadaUnidad_YQuedaSeparadoEnLaLinea()
    {
        var (cliente, producto) = await SeedBaseAsync();

        // 2 unidades + Armado N.º 3 => 300 × 2 = 600
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 2, armado: TipoServicioVenta.Armado3));

        var venta = await CargarVentaAsync(creada.Id);
        var detalle = Assert.Single(venta.Detalles);
        Assert.Equal(TipoServicioVenta.Armado3, detalle.TipoArmado);
        Assert.Equal(PrecioArmado3, detalle.ArmadoPrecioUnitario);
        Assert.Equal(PrecioArmado3 * 2, detalle.ArmadoSubtotal);
        Assert.Equal(2_000m, detalle.SubtotalFinal);            // el producto no absorbe el armado
        Assert.Equal(2_000m + 600m, venta.Total);
        Assert.Null(venta.Envio);
    }

    [Fact]
    public async Task SoloEnvio_SePersisteConPrecioGlobalYDentroDelTotal()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, tipoEnvio: TipoServicioVenta.EnvioRural));

        var venta = await CargarVentaAsync(creada.Id);
        Assert.NotNull(venta.Envio);
        Assert.Equal(TipoServicioVenta.EnvioRural, venta.Envio!.TipoEnvio);
        Assert.Equal(PrecioEnvioRural, venta.Envio.CostoEnvio);
        Assert.True(venta.Envio.IncluidoEnTotal);
        Assert.Equal(PrecioProducto + PrecioEnvioRural, venta.Total);
        // Ya está dentro de Total: no se suma de nuevo al "a cobrar".
        Assert.Equal(venta.Total, venta.TotalACobrar);
        Assert.Equal(0m, venta.ImporteEnvioFueraDelTotal);
        Assert.Equal(PrecioEnvioRural, venta.ImporteEnvio);
    }

    [Fact]
    public async Task EnvioYArmadoSimultaneos_SumanAlSubtotal_YSuIvaCierraContraElTotal()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(Vm(
            cliente.Id, producto.Id, cantidad: 2,
            armado: TipoServicioVenta.Armado5, tipoEnvio: TipoServicioVenta.EnvioCiudad));

        var venta = await CargarVentaAsync(creada.Id);
        var esperado = (PrecioProducto * 2) + (PrecioArmado5 * 2) + PrecioEnvioCiudad; // 2000 + 900 + 200
        Assert.Equal(3_100m, esperado);
        Assert.Equal(esperado, venta.Total);
        Assert.Equal(esperado, venta.Subtotal + venta.IVA);
    }

    // ── caja cerrada / sin armado ───────────────────────────────────────────

    [Fact]
    public async Task CajaCerrada_NoAgregaCostoDeArmado()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 3, cajaCerrada: true));

        var venta = await CargarVentaAsync(creada.Id);
        var detalle = Assert.Single(venta.Detalles);
        Assert.True(detalle.EntregaCajaCerrada);
        Assert.Null(detalle.TipoArmado);
        Assert.Equal(0m, detalle.ArmadoSubtotal);
        Assert.Equal(3_000m, venta.Total);
    }

    [Fact]
    public async Task CajaCerradaConArmado_EsContradictorio_YSeRechaza()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, cajaCerrada: true)));

        Assert.Contains("caja cerrada", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── el servidor es la autoridad del precio ──────────────────────────────

    [Fact]
    public async Task ElPrecioEnviadoPorElCliente_SeIgnora_YSeUsaElPrecioGlobal()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var vm = Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad);
        vm.Detalles[0].ArmadoPrecioUnitario = 1m;
        vm.Detalles[0].ArmadoSubtotal = 1m;
        vm.Envio!.CostoEnvio = 1m;

        var creada = await _service.CreateAsync(vm);

        var venta = await CargarVentaAsync(creada.Id);
        Assert.Equal(PrecioArmado3, venta.Detalles.Single().ArmadoSubtotal);
        Assert.Equal(PrecioEnvioCiudad, venta.Envio!.CostoEnvio);
        Assert.Equal(PrecioProducto + PrecioArmado3 + PrecioEnvioCiudad, venta.Total);
    }

    // ── ticket #24: elegir qué productos viajan en el envío ─────────────────

    private static VentaViewModel VmDosLineas(int clienteId, int productoId, bool primeraEnvia, bool segundaEnvia, bool conEnvio = true)
    {
        var vm = Vm(clienteId, productoId, tipoEnvio: conEnvio ? TipoServicioVenta.EnvioCiudad : null);
        vm.Detalles[0].EnviarADomicilio = primeraEnvia;
        vm.Detalles.Add(new VentaDetalleViewModel
        {
            ProductoId = productoId,
            Cantidad = 1,
            PrecioUnitario = PrecioProducto,
            EnviarADomicilio = segundaEnvia
        });
        return vm;
    }

    [Fact]
    public async Task EnvioConUnSoloProducto_PersisteLaSeleccion_YNoAlteraElTotal()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(VmDosLineas(cliente.Id, producto.Id, primeraEnvia: false, segundaEnvia: true));

        var venta = await CargarVentaAsync(creada.Id);
        var lineas = venta.Detalles.Where(d => !d.IsDeleted).OrderBy(d => d.Id).ToList();
        Assert.False(lineas[0].EnviarADomicilio);
        Assert.True(lineas[1].EnviarADomicilio);
        // El precio global del envío no depende de cuántos productos viajan.
        Assert.Equal(2 * PrecioProducto + PrecioEnvioCiudad, venta.Total);
    }

    [Fact]
    public async Task EnvioSinNingunProductoElegido_SeRechaza()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(VmDosLineas(cliente.Id, producto.Id, primeraEnvia: false, segundaEnvia: false)));

        Assert.Contains("al menos un producto", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SinEnvio_LaSeleccionDeProductosSeIgnora()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(VmDosLineas(cliente.Id, producto.Id, primeraEnvia: false, segundaEnvia: false, conEnvio: false));

        var venta = await CargarVentaAsync(creada.Id);
        Assert.Null(venta.Envio);
        Assert.All(venta.Detalles.Where(d => !d.IsDeleted), d => Assert.True(d.EnviarADomicilio));
    }

    [Fact]
    public async Task Editar_CambiaLosProductosDelEnvio()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(VmDosLineas(cliente.Id, producto.Id, primeraEnvia: true, segundaEnvia: true));
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        var venta = await CargarVentaAsync(creada.Id);

        var edicion = VmDosLineas(cliente.Id, producto.Id, primeraEnvia: true, segundaEnvia: false);
        edicion.Id = venta.Id;
        edicion.RowVersion = venta.RowVersion;
        edicion.FechaVenta = venta.FechaVenta;
        await _service.UpdateAsync(venta.Id, edicion);

        venta = await CargarVentaAsync(creada.Id);
        var lineas = venta.Detalles.Where(d => !d.IsDeleted).OrderBy(d => d.Id).ToList();
        Assert.True(lineas[0].EnviarADomicilio);
        Assert.False(lineas[1].EnviarADomicilio);
    }

    [Fact]
    public async Task EnvioSinTipo_SeRechaza()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var vm = Vm(cliente.Id, producto.Id, tipoEnvio: TipoServicioVenta.EnvioCiudad);
        vm.Envio!.TipoEnvio = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(vm));

        Assert.Contains("tipo de envío", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnEnvioNoSePuedeUsarComoArmado_NiAlReves()
    {
        var (cliente, producto) = await SeedBaseAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.EnvioCiudad)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Vm(cliente.Id, producto.Id, tipoEnvio: TipoServicioVenta.Armado3)));
    }

    [Fact]
    public async Task ServicioDesactivado_NoSePuedeVender()
    {
        var (cliente, producto) = await SeedBaseAsync();
        await _precios.GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado3, PrecioArmado3, Activo: false)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3)));
    }

    [Fact]
    public async Task CambiarElPrecioGlobal_NoAlteraLaVentaYaRegistrada()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad));

        await _precios.GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado3, 999m, true),
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, 999m, true)
        });

        var venta = await CargarVentaAsync(creada.Id);
        Assert.Equal(PrecioArmado3, venta.Detalles.Single().ArmadoPrecioUnitario);
        Assert.Equal(PrecioEnvioCiudad, venta.Envio!.CostoEnvio);
        Assert.Equal(PrecioProducto + PrecioArmado3 + PrecioEnvioCiudad, venta.Total);
    }

    // ── edición ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Editar_AgregarYQuitarServicios_RecalculaElTotal()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id));
        // SQLite no genera rowversion: se fija uno, como hacen los demás tests de Update.
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        var venta = await CargarVentaAsync(creada.Id);
        Assert.Equal(PrecioProducto, venta.Total);

        var conServicios = Vm(cliente.Id, producto.Id, cantidad: 2,
            armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioRural);
        conServicios.Id = venta.Id;
        conServicios.RowVersion = venta.RowVersion;
        conServicios.FechaVenta = venta.FechaVenta;
        await _service.UpdateAsync(venta.Id, conServicios);

        venta = await CargarVentaAsync(creada.Id);
        Assert.Equal(2_000m + 600m + 500m, venta.Total);
        Assert.Equal(TipoServicioVenta.EnvioRural, venta.Envio!.TipoEnvio);
        Assert.True(venta.Envio.IncluidoEnTotal);

        var sinServicios = Vm(cliente.Id, producto.Id, cantidad: 2);
        sinServicios.Id = venta.Id;
        sinServicios.RowVersion = venta.RowVersion;
        sinServicios.FechaVenta = venta.FechaVenta;
        await _service.UpdateAsync(venta.Id, sinServicios);

        venta = await CargarVentaAsync(creada.Id);
        Assert.Equal(2_000m, venta.Total);
        Assert.Null(venta.Envio);
        Assert.Equal(0m, venta.Detalles.Single(d => !d.IsDeleted).ArmadoSubtotal);
    }

    // ── recargo del medio de pago sobre productos + armados + envío ─────────

    [Fact]
    public async Task Preview_ElRecargoDelPlanAlcanzaAProductosArmadosYEnvio()
    {
        var (_, producto) = await SeedBaseAsync();
        var medio = new ConfiguracionPago { TipoPago = TipoPago.MercadoPago, Nombre = "MP servicios", Activo = true };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();
        var plan = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id,
            TipoPago = TipoPago.MercadoPago,
            CantidadCuotas = 3,
            AjustePorcentaje = 10m,
            Etiqueta = "MP 3 +10",
            Activo = true
        };
        _context.ConfiguracionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();

        var resultado = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            Detalles = new List<DetalleCalculoVentaRequest>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = PrecioProducto,
                    TipoArmado = TipoServicioVenta.Armado3
                }
            },
            TipoPago = TipoPago.MercadoPago,
            ConfiguracionPagoPlanId = plan.Id,
            TipoEnvio = TipoServicioVenta.EnvioRural
        });

        // productos 2000 + armados 600 + envío 500 = 3100 → +10% = 3410
        Assert.Equal(2_000m, resultado.TotalProductos);
        Assert.Equal(600m, resultado.TotalArmados);
        Assert.Equal(500m, resultado.ImporteEnvio);
        Assert.Equal(310m, resultado.AjustePagoGlobalAplicado);
        Assert.Equal(3_410m, resultado.Total);
        Assert.Equal(PrecioArmado3, resultado.Detalles.Single().ArmadoPrecioUnitario);
        Assert.Equal(600m, resultado.Detalles.Single().ArmadoSubtotal);
    }

    [Fact]
    public async Task Preview_SinServicios_NoCambiaElResultadoHistorico()
    {
        var (_, producto) = await SeedBaseAsync();

        var resultado = await _service.CalcularTotalesPreviewConServiciosAsync(new CalcularTotalesVentaRequest
        {
            Detalles = new List<DetalleCalculoVentaRequest>
            {
                new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = PrecioProducto }
            }
        });

        Assert.Equal(PrecioProducto, resultado.Total);
        Assert.Equal(0m, resultado.TotalArmados);
        Assert.Equal(0m, resultado.ImporteEnvio);
    }

    // ── administración de precios globales ──────────────────────────────────

    [Fact]
    public async Task ListarPrecios_SiempreDevuelveLosOchoServicios()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        await using var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();

        var lista = await new ServicioVentaPrecioService(ctx).ListarAsync();

        Assert.Equal(8, lista.Count);
        Assert.All(lista, p => { Assert.Equal(0m, p.Precio); Assert.True(p.Activo); });
        Assert.Equal(2, lista.Count(p => p.Tipo.EsEnvio()));
        Assert.Equal(6, lista.Count(p => p.Tipo.EsArmado()));
        Assert.Equal(
            new[] { TipoServicioVenta.Armado5, TipoServicioVenta.Armado6 },
            lista.Where(p => p.Tipo.EsDomiciliario()).Select(p => p.Tipo).ToArray());
    }

    [Fact]
    public async Task GuardarPrecios_HaceUpsert_YRechazaNegativos()
    {
        await _precios.GuardarAsync(new[] { new ServicioVentaPrecioComando(TipoServicioVenta.Armado1, 123.456m, true) });
        var lista = await _precios.ListarAsync();
        Assert.Equal(123.46m, lista.Single(p => p.Tipo == TipoServicioVenta.Armado1).Precio);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _precios.GuardarAsync(new[] { new ServicioVentaPrecioComando(TipoServicioVenta.Armado1, -1m, true) }));
        Assert.Equal(1, await _context.ServiciosVentaPrecios.CountAsync(s => s.Tipo == TipoServicioVenta.Armado1));
    }

    // ── comprobante: los servicios se facturan como líneas propias ──────────

    [Fact]
    public async Task ResumenDeAlicuotas_IncluyeArmadosYEnvioParaCerrarContraElTotal()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 2,
            armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioRural));

        var vm = await _service.GetByIdAsync(creada.Id);
        Assert.NotNull(vm);

        var resumen = FacturaAlicuotaResumenBuilder.Build(vm!.Detalles, vm.ImporteEnvioIncluido);

        Assert.Equal(vm.Total, resumen.Sum(r => r.Total));
        Assert.Equal(600m, vm.TotalArmados);
        Assert.Equal(500m, vm.ImporteEnvioIncluido);
    }

    // ── costo estándar / real de envío y armado (H12, alternativa A+B) ───────

    /// <summary>Arma el comando como lo haría el formulario: con la versión de cada fila tal como se mostró.</summary>
    private async Task<VentaCostoServiciosComando> Cmd(int ventaId, decimal? envio, Dictionary<int, decimal?> armados)
    {
        var info = await _costos.ObtenerAsync(ventaId);
        var versiones = info!.Armados.Where(a => a.DetalleId.HasValue).ToDictionary(a => a.DetalleId!.Value, a => a.RowVersionBase64);
        return new VentaCostoServiciosComando(envio, armados, info.Envio?.RowVersionBase64, versiones);
    }

    [Fact]
    public async Task CostoReal_FormularioObsoleto_SeRechazaSinPisarElCambioDeOtroUsuario()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad));
        var detalleId = (await CargarConProductosAsync(creada.Id)).Detalles.Single().Id;

        // Dos personas abren el formulario con la misma versión.
        var formularioA = await Cmd(creada.Id, 90m, new Dictionary<int, decimal?> { [detalleId] = 200m });
        var formularioB = await Cmd(creada.Id, null, new Dictionary<int, decimal?> { [detalleId] = null });

        await _costos.GuardarAsync(creada.Id, formularioA, "A");
        // SQL Server cambia el rowversion en cada UPDATE; SQLite no, así que se emula (igual que otros tests de esta clase).
        await _context.Database.ExecuteSqlRawAsync("UPDATE VentaEnvio SET RowVersion = X'0A0B0C0D0E0F1011' WHERE VentaId = {0}", creada.Id);
        await _context.Database.ExecuteSqlRawAsync("UPDATE VentaDetalles SET RowVersion = X'1A1B1C1D1E1F2021' WHERE VentaId = {0}", creada.Id);
        _context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _costos.GuardarAsync(creada.Id, formularioB, "B"));

        Assert.Equal(90m + 200m, CostoServiciosVenta.Total(await CargarConProductosAsync(creada.Id)));   // el cambio de A sigue
    }

    [Fact]
    public async Task CostoReal_SinVersionesEnElFormulario_SeRechaza()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, tipoEnvio: TipoServicioVenta.EnvioCiudad));

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _costos.GuardarAsync(creada.Id,
            new VentaCostoServiciosComando(50m, new Dictionary<int, decimal?>()), "tester"));
        Assert.Null((await CargarConProductosAsync(creada.Id)).Envio!.CostoRealServicio);
    }

    [Fact]
    public async Task Editar_ConservaElCostoRealDeLaLineaEquivalente_YLoDescartaSiCambiaLaCantidad()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 2, armado: TipoServicioVenta.Armado3));
        var detalleId = (await CargarConProductosAsync(creada.Id)).Detalles.Single().Id;
        await _costos.GuardarAsync(creada.Id, await Cmd(creada.Id, null, new Dictionary<int, decimal?> { [detalleId] = 175m }), "tester");

        // Editar solo datos generales (misma línea): el costo real se conserva.
        await _context.Database.ExecuteSqlRawAsync("UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        _context.ChangeTracker.Clear();
        var vmIgual = Vm(cliente.Id, producto.Id, cantidad: 2, armado: TipoServicioVenta.Armado3);
        vmIgual.Id = creada.Id;
        vmIgual.RowVersion = (await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == creada.Id)).RowVersion;
        await _service.UpdateAsync(creada.Id, vmIgual);
        var conservado = (await CargarConProductosAsync(creada.Id)).Detalles.Where(d => !d.IsDeleted).Single();
        Assert.Equal(175m, conservado.ArmadoCostoReal);
        Assert.Equal(CostoArmado3, conservado.ArmadoCostoUnitarioEstandar);

        // Cambia la cantidad: ya no es el mismo trabajo, vuelve al estándar.
        await _context.Database.ExecuteSqlRawAsync("UPDATE Ventas SET RowVersion = X'0807060504030201' WHERE Id = {0}", creada.Id);
        _context.ChangeTracker.Clear();
        var vmOtraCantidad = Vm(cliente.Id, producto.Id, cantidad: 3, armado: TipoServicioVenta.Armado3);
        vmOtraCantidad.Id = creada.Id;
        vmOtraCantidad.RowVersion = (await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == creada.Id)).RowVersion;
        await _service.UpdateAsync(creada.Id, vmOtraCantidad);
        var descartado = (await CargarConProductosAsync(creada.Id)).Detalles.Where(d => !d.IsDeleted).Single();
        Assert.Null(descartado.ArmadoCostoReal);
        Assert.Equal(CostoArmado3 * 3, CostoServiciosVenta.Armados(new[] { descartado }));
    }

    private async Task<Venta> CargarConProductosAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.Ventas
            .Include(v => v.Detalles).ThenInclude(d => d.Producto)
            .Include(v => v.Envio)
            .AsNoTracking()
            .FirstAsync(v => v.Id == id);
    }

    [Fact]
    public async Task Crear_CongelaElCostoEstandarDeEntregaYArmado()
    {
        var (cliente, producto) = await SeedBaseAsync();

        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 3,
            armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioRural));

        var venta = await CargarConProductosAsync(creada.Id);
        Assert.Equal(CostoEnvioRural, venta.Envio!.CostoEstandarServicio);
        Assert.Null(venta.Envio.CostoRealServicio);
        var linea = venta.Detalles.Single();
        Assert.Equal(CostoArmado3, linea.ArmadoCostoUnitarioEstandar);
        Assert.Null(linea.ArmadoCostoReal);
        // Estándar por unidad × cantidad + entrega.
        Assert.Equal(CostoEnvioRural + CostoArmado3 * 3, CostoServiciosVenta.Total(venta));
    }

    [Fact]
    public async Task CambiarElCostoEstandarGlobal_NoAlteraLaVentaYaRegistrada()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad));
        var antes = CostoServiciosVenta.Total(await CargarConProductosAsync(creada.Id));

        await _precios.GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, PrecioEnvioCiudad, true, 999m),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado3, PrecioArmado3, true, 888m)
        });

        Assert.Equal(antes, CostoServiciosVenta.Total(await CargarConProductosAsync(creada.Id)));
        Assert.Equal(CostoEnvioCiudad + CostoArmado3, antes);
    }

    [Fact]
    public async Task CostoReal_ReemplazaAlEstandar_NuncaSeDescuentanAmbos_YVacioVuelveAlEstandar()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, cantidad: 2,
            armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad));
        var detalleId = (await CargarConProductosAsync(creada.Id)).Detalles.Single().Id;

        await _costos.GuardarAsync(creada.Id,
            await Cmd(creada.Id, 95m, new Dictionary<int, decimal?> { [detalleId] = 260m }), "tester");

        var conReal = await CargarConProductosAsync(creada.Id);
        Assert.Equal(95m + 260m, CostoServiciosVenta.Total(conReal));            // solo el real
        Assert.NotEqual(CostoEnvioCiudad + CostoArmado3 * 2 + 95m + 260m, CostoServiciosVenta.Total(conReal));
        Assert.Equal(CostoEnvioCiudad, conReal.Envio!.CostoEstandarServicio);      // el estándar sigue guardado

        var info = await _costos.ObtenerAsync(creada.Id);
        Assert.Equal(95m, info!.Envio!.CostoAplicado);
        Assert.True(info.Envio.UsaCostoReal);
        Assert.Equal(260m, info.Armados.Single().CostoAplicado);

        // Vacío = vuelve al estándar.
        await _costos.GuardarAsync(creada.Id,
            await Cmd(creada.Id, null, new Dictionary<int, decimal?> { [detalleId] = null }), "tester");
        Assert.Equal(CostoEnvioCiudad + CostoArmado3 * 2, CostoServiciosVenta.Total(await CargarConProductosAsync(creada.Id)));
    }

    [Fact]
    public async Task CostoReal_CeroEsUnValorValido_YNoSeConfundeConElEstandar()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, tipoEnvio: TipoServicioVenta.EnvioRural));

        await _costos.GuardarAsync(creada.Id, await Cmd(creada.Id, 0m, new Dictionary<int, decimal?>()), "tester");

        Assert.Equal(0m, CostoServiciosVenta.Total(await CargarConProductosAsync(creada.Id)));
    }

    [Fact]
    public async Task CostoReal_ValorInvalido_NoDejaCambiosParciales()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad));
        var detalleId = (await CargarConProductosAsync(creada.Id)).Detalles.Single().Id;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _costos.GuardarAsync(creada.Id,
            await Cmd(creada.Id, 70m, new Dictionary<int, decimal?> { [detalleId] = -1m }), "tester"));

        var venta = await CargarConProductosAsync(creada.Id);
        Assert.Null(venta.Envio!.CostoRealServicio);
        Assert.Null(venta.Detalles.Single().ArmadoCostoReal);
    }

    [Fact]
    public async Task CostoReal_LineaSinArmadoOAjena_SeRechaza()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var sinArmado = await _service.CreateAsync(Vm(cliente.Id, producto.Id));
        var otra = await _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3));
        var detalleDeOtra = (await CargarConProductosAsync(otra.Id)).Detalles.Single().Id;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _costos.GuardarAsync(sinArmado.Id,
            await Cmd(sinArmado.Id, null, new Dictionary<int, decimal?> { [detalleDeOtra] = 10m }), "tester"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _costos.GuardarAsync(sinArmado.Id,
            await Cmd(sinArmado.Id, 10m, new Dictionary<int, decimal?>()), "tester"));   // sin envío
    }

    [Fact]
    public async Task CostoReal_VentaCancelada_SeRechaza()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, tipoEnvio: TipoServicioVenta.EnvioCiudad));
        var v = await _context.Ventas.FirstAsync(x => x.Id == creada.Id);
        v.Estado = EstadoVenta.Cancelada;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await _costos.GuardarAsync(creada.Id,
            await Cmd(creada.Id, 10m, new Dictionary<int, decimal?>()), "tester"));
    }

    [Fact]
    public async Task VentaAnteriorSinSnapshot_NoSumaCostoDeServicios()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad));
        // Simula una venta registrada antes de esta función: sin snapshot ni costo real.
        var envio = await _context.VentaEnvios.FirstAsync(e => e.VentaId == creada.Id);
        envio.CostoEstandarServicio = null;
        var linea = await _context.VentaDetalles.FirstAsync(d => d.VentaId == creada.Id);
        linea.ArmadoCostoUnitarioEstandar = 0m;
        await _context.SaveChangesAsync();

        Assert.Equal(0m, CostoServiciosVenta.Total(await CargarConProductosAsync(creada.Id)));
    }

    [Fact]
    public async Task Ganancia_DescuentaElCostoDeServicios_ConElRealReemplazandoAlEstandar()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var creada = await _service.CreateAsync(Vm(cliente.Id, producto.Id, armado: TipoServicioVenta.Armado3, tipoEnvio: TipoServicioVenta.EnvioCiudad));
        var detalleId = (await CargarConProductosAsync(creada.Id)).Detalles.Single().Id;

        var conEstandar = ReporteService.GananciaNetaDeVenta(await CargarConProductosAsync(creada.Id));
        var sinCostos = conEstandar + CostoEnvioCiudad + CostoArmado3;

        await _costos.GuardarAsync(creada.Id,
            await Cmd(creada.Id, 30m, new Dictionary<int, decimal?> { [detalleId] = 40m }), "tester");

        var conReal = ReporteService.GananciaNetaDeVenta(await CargarConProductosAsync(creada.Id));
        Assert.Equal(sinCostos - 30m - 40m, conReal);   // el real reemplaza: ni el estándar ni la suma de ambos
    }

    [Fact]
    public async Task Editar_LineasDuplicadas_NoTrasladanElCostoRealEntreLineas()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var vm = Vm(cliente.Id, producto.Id, cantidad: 2, armado: TipoServicioVenta.Armado3);
        vm.Detalles.Add(new VentaDetalleViewModel
        {
            ProductoId = producto.Id, Cantidad = 2, PrecioUnitario = PrecioProducto, TipoArmado = TipoServicioVenta.Armado3
        });
        var creada = await _service.CreateAsync(vm);

        var lineas = (await CargarConProductosAsync(creada.Id)).Detalles.OrderBy(d => d.Id).ToList();
        Assert.Equal(2, lineas.Count);
        // Solo la segunda línea tiene costo real.
        await _costos.GuardarAsync(creada.Id, await Cmd(creada.Id, null,
            new Dictionary<int, decimal?> { [lineas[0].Id] = null, [lineas[1].Id] = 175m }), "tester");

        // Se elimina la línea con costo real y queda una sola idéntica a la otra: el costo real NO se traslada.
        await _context.Database.ExecuteSqlRawAsync("UPDATE Ventas SET RowVersion = X'0102030405060708' WHERE Id = {0}", creada.Id);
        _context.ChangeTracker.Clear();
        var soloUna = Vm(cliente.Id, producto.Id, cantidad: 2, armado: TipoServicioVenta.Armado3);
        soloUna.Id = creada.Id;
        soloUna.RowVersion = (await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == creada.Id)).RowVersion;
        await _service.UpdateAsync(creada.Id, soloUna);

        var activa = (await CargarConProductosAsync(creada.Id)).Detalles.Where(d => !d.IsDeleted).Single();
        Assert.Null(activa.ArmadoCostoReal);
        Assert.Equal(CostoArmado3 * 2, CostoServiciosVenta.Armado(activa));
    }

    [Fact]
    public async Task GuardarCostosEstandar_RechazaNegativosYPersiste()
    {
        await _precios.GuardarAsync(new[] { new ServicioVentaPrecioComando(TipoServicioVenta.Armado1, 50m, true, 12.345m) });
        var lista = await _precios.ListarAsync();
        Assert.Equal(12.35m, lista.Single(p => p.Tipo == TipoServicioVenta.Armado1).Costo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _precios.GuardarAsync(new[] { new ServicioVentaPrecioComando(TipoServicioVenta.Armado1, 50m, true, -1m) }));
        Assert.Equal(12.35m, (await _precios.ListarAsync()).Single(p => p.Tipo == TipoServicioVenta.Armado1).Costo);
    }
}
