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

file sealed class StubNotifEnvioTotal : INotificacionService
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

/// <summary>
/// VENTA-ENVIO-TOTAL-01: el envío es un concepto separado del total de productos que SÍ se cobra.
/// TotalACobrar = Venta.Total + VentaEnvio.CostoEnvio (backend, fórmula única en VentaMontos); Caja
/// registra y concilia lo realmente cobrado; el total facturable (comprobante) no lo incluye y queda
/// explícito. Estos tests cubren la cadena completa sobre SQLite real: fórmula → persistencia →
/// movimiento de caja → conciliación → reversión.
/// </summary>
public class VentaEnvioTotalACobrarTests : IDisposable
{
    private const decimal TotalProductos = 180_758.90m;
    private const decimal Envio = 1_000m;
    private const decimal TotalACobrar = 181_758.90m;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CajaService _caja;
    private readonly AperturaCaja _apertura;
    private static int _counter = 9000;

    public VentaEnvioTotalACobrarTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _caja = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new StubNotifEnvioTotal());
        _apertura = SeedApertura();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private AperturaCaja SeedApertura()
    {
        var caja = new Caja { Codigo = "CJENV", Nombre = "Caja envío", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "cajero-envio",
            MontoInicial = 0m,
            Cerrada = false
        };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();
        return apertura;
    }

    /// <summary>Venta Confirmada (Total = productos) con envío opcional; devuelve la venta recargada CON Envio.</summary>
    private async Task<Venta> SeedVentaAsync(
        decimal? costoEnvio,
        TipoPago tipoPago = TipoPago.Efectivo,
        EstadoVenta estado = EstadoVenta.Confirmada)
    {
        var n = Interlocked.Increment(ref _counter);
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Envio",
            TipoDocumento = "DNI",
            NumeroDocumento = n.ToString("D8"),
            Email = $"envio-total{n}@test.com"
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            Numero = $"VTA-ENV-{n}",
            ClienteId = cliente.Id,
            AperturaCajaId = _apertura.Id,
            Estado = estado,
            TipoPago = tipoPago,
            Subtotal = 149_387.52m,
            IVA = 31_371.38m,
            Total = TotalProductos
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        if (costoEnvio.HasValue)
        {
            _context.VentaEnvios.Add(new VentaEnvio
            {
                VentaId = venta.Id,
                Destinatario = "Juan Pérez",
                Domicilio = "Calle Falsa 123",
                CostoEnvio = costoEnvio
            });
            await _context.SaveChangesAsync();
        }

        _context.ChangeTracker.Clear();
        return await _context.Ventas.Include(v => v.Envio).AsNoTracking().FirstAsync(v => v.Id == venta.Id);
    }

    private Task<MovimientoCaja?> CobrarAsync(Venta venta, decimal monto) =>
        _caja.RegistrarMovimientoVentaAsync(venta.Id, venta.Numero, monto, venta.TipoPago, "cajero-envio");

    private async Task<CajaConciliacionViewModel> ConciliarAsync()
    {
        _context.ChangeTracker.Clear();
        var detalle = await _caja.ObtenerDetallesAperturaAsync(_apertura.Id);
        return CajaConciliacionBuilder.Build(detalle, cierre: null, puedeOperar: true);
    }

    // ── fórmula (backend) ───────────────────────────────────────────────────

    // Strings + decimal.Parse: los literales double de InlineData no representan bien los importes monetarios.
    [Theory]
    [InlineData("180758.90", "1000", "181758.90")]   // caso base
    [InlineData("180758.90", "0", "180758.90")]      // envío con costo cero: se muestra, no suma
    [InlineData("180758.90", "", "180758.90")]       // sin costo cargado
    [InlineData("180758.90", "-500", "180758.90")]   // un envío nunca resta
    [InlineData("100", "10.005", "110.01")]          // redondeo monetario (away from zero)
    public void CalcularTotalACobrar_SumaProductosMasEnvioUnaSolaVez(string productos, string envio, string esperado)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        decimal? costo = envio.Length == 0 ? null : decimal.Parse(envio, inv);

        var total = VentaMontos.CalcularTotalACobrar(decimal.Parse(productos, inv), costo);

        Assert.Equal(decimal.Parse(esperado, inv), total);
    }

    [Fact]
    public void TotalFacturable_NoIncluyeElEnvio_YQuedaSeparadoDelTotalACobrar()
    {
        var venta = new Venta { Total = TotalProductos, Envio = new VentaEnvio { CostoEnvio = Envio } };

        Assert.Equal(TotalProductos, venta.Total);
        Assert.Equal(Envio, venta.ImporteEnvio);
        Assert.Equal(TotalACobrar, venta.TotalACobrar);
        Assert.Equal(TotalProductos, venta.TotalFacturable);            // el comprobante cubre sólo productos
        Assert.NotEqual(venta.TotalACobrar, venta.TotalFacturable);      // la diferencia es explícita (= envío)
        Assert.Equal(Envio, venta.TotalACobrar - venta.TotalFacturable);
    }

    [Fact]
    public void VentaViewModel_ExponeLosMismosMontosDerivadosEnBackend()
    {
        var vm = new VentaViewModel
        {
            Total = TotalProductos,
            Envio = new VentaEnvioViewModel { CostoEnvio = Envio }
        };

        Assert.Equal(Envio, vm.ImporteEnvio);
        Assert.Equal(TotalACobrar, vm.TotalACobrar);
        Assert.Equal(TotalProductos, vm.TotalFacturable);
        Assert.Equal(TotalProductos, new VentaViewModel { Total = TotalProductos }.TotalACobrar);
    }

    // ── persistencia ────────────────────────────────────────────────────────

    [Fact]
    public async Task Persistencia_GuardarEnvio_RecargarVenta_MismoImporte()
    {
        var venta = await SeedVentaAsync(Envio);

        // Contexto nuevo sobre la misma base: nada queda en memoria del contexto que escribió.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var otroContexto = new AppDbContext(options);
        var recargada = await otroContexto.Ventas.Include(v => v.Envio).AsNoTracking().FirstAsync(v => v.Id == venta.Id);

        Assert.Equal(Envio, recargada.Envio!.CostoEnvio);
        Assert.Equal(TotalProductos, recargada.Total);            // el envío no se mezcló en Venta.Total
        Assert.Equal(TotalACobrar, recargada.TotalACobrar);
    }

    // ── Caja: caso base (Efectivo) ──────────────────────────────────────────

    [Fact]
    public async Task Caja_EfectivoConEnvio_RegistraTotalACobrarYConciliaVendidoIgualCobrado()
    {
        var venta = await SeedVentaAsync(Envio);

        var mov = await CobrarAsync(venta, venta.TotalACobrar);

        Assert.NotNull(mov);
        Assert.Equal(TotalACobrar, mov!.Monto);
        Assert.Contains("incluye envío", mov.Observaciones);
        Assert.Equal(venta.Id, mov.VentaId);

        var vm = await ConciliarAsync();
        Assert.Equal(TotalACobrar, vm.CajaFisicaEsperada);        // caja esperada +181.758,90
        Assert.Equal(TotalACobrar, vm.TotalVendido);
        Assert.Equal(TotalACobrar, vm.TotalCobrado);
        Assert.Equal(0m, vm.TotalPendiente);

        var linea = Assert.Single(vm.Ventas);
        Assert.Equal(TotalProductos, linea.TotalProductos);
        Assert.Equal(Envio, linea.ImporteEnvio);
        Assert.Equal(TotalACobrar, linea.TotalVenta);
        Assert.Equal(TotalACobrar, linea.CobradoAhora);
        Assert.Equal(0m, linea.Pendiente);

        var efectivo = Assert.Single(vm.ResumenPorMedio, r => r.MedioKey == "efectivo");
        Assert.Equal(TotalACobrar, efectivo.TotalVendido);
        Assert.Equal(TotalACobrar, efectivo.TotalCobrado);
        Assert.Equal(0m, efectivo.TotalPendiente);
    }

    [Fact]
    public async Task Caja_PagoParcial_SeCobraSoloLosProductos_EnvioQuedaPendiente()
    {
        var venta = await SeedVentaAsync(Envio);

        await CobrarAsync(venta, TotalProductos);   // falta cobrar el envío

        var vm = await ConciliarAsync();
        var linea = Assert.Single(vm.Ventas);
        Assert.Equal(TotalACobrar, linea.TotalVenta);
        Assert.Equal(TotalProductos, linea.CobradoAhora);
        Assert.Equal(Envio, linea.Pendiente);                 // no se marca totalmente cobrada
        Assert.Equal(Envio, vm.TotalPendiente);
        Assert.Equal(TotalProductos, vm.CajaFisicaEsperada);  // la caja refleja sólo lo que entró
    }

    [Fact]
    public async Task Caja_SinEnvio_NoCambiaNada()
    {
        var venta = await SeedVentaAsync(costoEnvio: null);

        var mov = await CobrarAsync(venta, venta.TotalACobrar);

        Assert.Equal(TotalProductos, mov!.Monto);
        Assert.DoesNotContain("envío", mov.Observaciones ?? string.Empty);
        var vm = await ConciliarAsync();
        Assert.Equal(TotalProductos, vm.TotalVendido);
        Assert.Equal(0m, vm.TotalPendiente);
        Assert.Equal(0m, Assert.Single(vm.Ventas).ImporteEnvio);
    }

    [Fact]
    public async Task Caja_EnvioConCostoCero_SeMuestraSinSumar()
    {
        var venta = await SeedVentaAsync(costoEnvio: 0m);
        Assert.NotNull(venta.Envio);

        await CobrarAsync(venta, venta.TotalACobrar);

        var vm = await ConciliarAsync();
        var linea = Assert.Single(vm.Ventas);
        Assert.Equal(0m, linea.ImporteEnvio);
        Assert.Equal(TotalProductos, linea.TotalVenta);
        Assert.Equal(0m, linea.Pendiente);
    }

    [Fact]
    public async Task Caja_CancelarVentaConEnvio_RevierteTambienElEnvio()
    {
        var venta = await SeedVentaAsync(Envio);
        await CobrarAsync(venta, venta.TotalACobrar);

        var contramov = await _caja.RegistrarContramovimientoVentaAsync(venta.Id, venta.Numero, "prueba", "cajero-envio");

        Assert.Equal(TotalACobrar, contramov!.Monto);            // espeja el ingreso único (incluye envío)
        var detalle = await _caja.ObtenerDetallesAperturaAsync(_apertura.Id);
        Assert.Equal(0m, detalle.CajaFisicaEsperada);
    }

    // ── otros medios de pago ────────────────────────────────────────────────

    [Theory]
    [InlineData(TipoPago.Transferencia, "transferencia")]
    [InlineData(TipoPago.TarjetaDebito, "tarjeta")]
    [InlineData(TipoPago.TarjetaCredito, "tarjeta")]
    [InlineData(TipoPago.MercadoPago, "mercadopago")]
    public async Task Caja_MediosDigitalesConEnvio_CobranTotalACobrarSinPendiente(TipoPago medio, string medioKey)
    {
        var venta = await SeedVentaAsync(Envio, tipoPago: medio);

        await CobrarAsync(venta, venta.TotalACobrar);

        var vm = await ConciliarAsync();
        var linea = Assert.Single(vm.Ventas);
        Assert.Equal(TotalACobrar, linea.TotalVenta);
        Assert.Equal(TotalACobrar, linea.CobradoAhora);
        Assert.Equal(0m, linea.Pendiente);
        var resumen = Assert.Single(vm.ResumenPorMedio, r => r.MedioKey == medioKey);
        Assert.Equal(TotalACobrar, resumen.TotalCobrado);
        Assert.Equal(0m, resumen.TotalPendiente);
    }

    [Fact]
    public async Task Caja_CreditoPersonalConEnvio_NoGeneraIngresoNiTocaElCredito_EnvioQuedaPendienteExplicito()
    {
        // Regla existente (no se modifica): Crédito personal no genera ingreso inmediato y el crédito
        // se calcula sobre Venta.Total. El envío no se financia: se muestra como parte de lo que
        // falta cobrar hasta que exista una decisión de negocio sobre cómo cobrarlo.
        var venta = await SeedVentaAsync(Envio, tipoPago: TipoPago.CreditoPersonal);

        var mov = await CobrarAsync(venta, venta.TotalACobrar);

        Assert.Null(mov);
        Assert.Equal(TotalProductos, venta.Total);               // base del crédito/cupo: intacta
        var vm = await ConciliarAsync();
        var linea = Assert.Single(vm.Ventas);
        Assert.Equal(Envio, linea.ImporteEnvio);
        Assert.Equal(0m, linea.CobradoAhora);
        Assert.Equal(TotalACobrar, linea.Pendiente);
    }

    // ── edición posterior ───────────────────────────────────────────────────

    [Theory]
    [InlineData(EstadoVenta.Confirmada)]
    [InlineData(EstadoVenta.Facturada)]
    [InlineData(EstadoVenta.Entregada)]
    [InlineData(EstadoVenta.Cancelada)]
    public void EdicionPosterior_VentaYaCobrada_NoPermiteReeditarElImporteDelEnvio(EstadoVenta estado)
    {
        // El importe sólo se escribe vía VentaService.UpdateAsync, que exige este guard: una vez
        // confirmada la venta (cobro registrado) el importe no puede cambiar en silencio.
        var venta = new Venta { Estado = estado, Total = TotalProductos, Envio = new VentaEnvio { CostoEnvio = Envio } };

        Assert.Throws<InvalidOperationException>(() => new VentaValidator().ValidarEstadoParaEdicion(venta));
    }

    [Fact]
    public async Task CambiarEstadoDelEnvio_NoAlteraElImporteNiElTotalACobrar()
    {
        var venta = await SeedVentaAsync(Envio);
        var servicio = new VentaEnvioService(_context, NullLogger<VentaEnvioService>.Instance);

        foreach (var siguiente in new[] { EstadoEnvio.Preparando, EstadoEnvio.Despachado, EstadoEnvio.Entregado })
        {
            var r = await servicio.CambiarEstadoAsync(venta.Id, siguiente, null, "tester");
            Assert.True(r.Exitoso);
        }

        _context.ChangeTracker.Clear();
        var recargada = await _context.Ventas.Include(v => v.Envio).AsNoTracking().FirstAsync(v => v.Id == venta.Id);
        Assert.Equal(Envio, recargada.Envio!.CostoEnvio);
        Assert.Equal(TotalACobrar, recargada.TotalACobrar);
        Assert.Equal(TotalProductos, recargada.Total);
    }
}
