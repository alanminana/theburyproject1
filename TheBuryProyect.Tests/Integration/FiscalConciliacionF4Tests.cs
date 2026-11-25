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
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

file sealed class StubNotificacionF4 : INotificacionService
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

file sealed class StubUsuarioF4 : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubCreditoDisponibleF4 : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(int puntaje, CancellationToken cancellationToken = default) => Task.FromResult(999_999m);
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CreditoDisponibleResultado { Limite = 0m, Disponible = 999_999m });
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(int Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

/// <summary>
/// P-02 / F4.2: CONCILIACIÓN entre Caja (MovimientoCaja), Ventas, Facturas, Devoluciones, cobro de cuotas y cada reporte
/// (<see cref="ReporteService"/>) sobre UN escenario sintético completo, con los servicios REALES (VentaService, CajaService,
/// DevolucionService, CreditoService, ReporteService) sobre SQLite en memoria. Los valores esperados están calculados a mano
/// y escritos en cada test (no derivados del código bajo prueba) y se leen de lo PERSISTIDO.
///
/// Escenario (producto A $1.000 con IVA 21 %, costo 400; producto B $500 con IVA 10,5 %, costo 200):
///  V1 contado Efectivo −5 %, 3 × A                       → ítems 3.000, total 2.850, facturada
///  V2 Transferencia +3 %, A + B                          → ítems 1.500, total 1.545, facturada
///  V3 tarjeta (plan global MercadoPago 3 cuotas +10 %), A → ítems 1.000, total 1.100, facturada
///  V4 crédito personal 2 × A, anticipo 500, 3 cuotas de 600 (capital 500 + interés 100):
///     cuota 1 pagada por Transferencia (+3 % = 18), cuota 2 pagada en Efectivo, cuota 3 vencida → total 2.000, facturada
///  V5 contado Efectivo sin plan, 2 × B                   → total 1.000, facturada y luego factura ANULADA (vuelve a Confirmada)
///  Devolución parcial de 1 de las 3 unidades de V1 (reembolso en efectivo).
///
/// Nada de esto cambia ningún criterio fiscal: fija lo que HOY hace el sistema. Las diferencias entre módulos que son
/// CRITERIO (no defecto matemático) se marcan «Caracteriza_» y están listadas en docs/iva-matriz-diferencias.md.
/// </summary>
public class FiscalConciliacionF4Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly CajaService _caja;
    private readonly VentaService _ventas;
    private readonly DevolucionService _devoluciones;
    private readonly CreditoService _creditos;
    private readonly ReporteService _reportes;
    private readonly StubConfiguracionPagoAjuste _configPagoCuotas = new() { AjustePorcentaje = 3m };
    private readonly RelojComercialFake _reloj = new(DateOnly.FromDateTime(DateTime.UtcNow));
    private static int _n = 41_000;

    public FiscalConciliacionF4Tests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new TheBuryProject.Tests.Infrastructure.SqliteRowVersionRotationInterceptor())
            .Options);
        _context.Database.EnsureCreated();
        _mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        var cajaEntidad = new Caja { Codigo = "CJF4", Nombre = "Caja F4", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(cajaEntidad);
        _context.SaveChanges();
        _context.AperturasCaja.Add(new AperturaCaja { CajaId = cajaEntidad.Id, UsuarioApertura = "testuser", MontoInicial = 0m, Cerrada = false });
        _context.SaveChanges();

        _caja = new CajaService(_context, _mapper, NullLogger<CajaService>.Instance, new StubNotificacionF4());
        _ventas = new VentaService(
            _context, _mapper, NullLogger<VentaService>.Instance, null!, null!,
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context), new StubUsuarioF4(),
            null!, _caja, null!, new StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta());
        _devoluciones = new DevolucionService(
            _context, new MovimientoStockService(_context, NullLogger<MovimientoStockService>.Instance),
            new StubUsuarioF4(), NullLogger<DevolucionService>.Instance, _caja);
        _creditos = new CreditoService(
            _context, _mapper, NullLogger<CreditoService>.Instance, new FinancialCalculationService(), _caja,
            new StubCreditoDisponibleF4(), new StubUsuarioF4(), configuracionPagoService: _configPagoCuotas, reloj: _reloj);
        _reportes = new ReporteService(_context, NullLogger<ReporteService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ───────────────────────── siembra ─────────────────────────

    private sealed record Escenario(
        Cliente Cliente, Producto A, Producto B,
        int V1, int V2, int V3, int V4, int V5, int DevolucionId, int Cuota1Id, int Cuota2Id, int Cuota3Id);

    private async Task SeedPlanAsync(TipoPago tipoPago, decimal ajuste, int cuotas = 1)
    {
        var medio = new ConfiguracionPago { TipoPago = tipoPago, Nombre = $"Medio {tipoPago} {Interlocked.Increment(ref _n)}", Activo = true };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();
        _context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id, TipoPago = tipoPago, CantidadCuotas = cuotas,
            AjustePorcentaje = ajuste, Etiqueta = $"{cuotas} pago(s) {ajuste:+0;-0}", Activo = true
        });
        await _context.SaveChangesAsync();
    }

    private static VentaViewModel Vm(int clienteId, TipoPago pago, params (int ProductoId, int Cantidad, decimal Precio)[] lineas) => new()
    {
        ClienteId = clienteId,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = pago,
        Detalles = lineas.Select(l => new VentaDetalleViewModel { ProductoId = l.ProductoId, Cantidad = l.Cantidad, PrecioUnitario = l.Precio }).ToList()
    };

    private async Task<int> CrearConfirmarYFacturarAsync(VentaViewModel vm, Func<int, Task>? antesDeConfirmar = null)
    {
        var creada = await _ventas.CreateAsync(vm);
        if (antesDeConfirmar != null)
            await antesDeConfirmar(creada.Id);
        var venta = await _context.Ventas.FirstAsync(v => v.Id == creada.Id);
        venta.Estado = EstadoVenta.Confirmada;
        await _context.SaveChangesAsync();
        Assert.True(await _ventas.FacturarVentaAsync(venta.Id, new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow }));
        _context.ChangeTracker.Clear();
        return venta.Id;
    }

    private async Task<Escenario> SeedEscenarioAsync()
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CF4{n}", Nombre = $"Cat F4 {n}" };
        var marca = new Marca { Codigo = $"MF4{n}", Nombre = $"Marca F4 {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "F4", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"),
            Email = $"f4{n}@test.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        var a = new Producto { Codigo = $"PA{n}", Nombre = $"A {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 400m, PrecioVenta = 1000m, PorcentajeIVA = 21m, StockActual = 100 };
        var b = new Producto { Codigo = $"PB{n}", Nombre = $"B {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 200m, PrecioVenta = 500m, PorcentajeIVA = 10.5m, StockActual = 100 };
        _context.Clientes.Add(cliente);
        _context.Productos.AddRange(a, b);
        await _context.SaveChangesAsync();

        await SeedPlanAsync(TipoPago.Efectivo, -5m);
        await SeedPlanAsync(TipoPago.Transferencia, 3m);

        // V1: Efectivo -5 % sobre 3 × 1.000
        var v1 = await CrearConfirmarYFacturarAsync(Vm(cliente.Id, TipoPago.Efectivo, (a.Id, 3, 1000m)));
        // V2: Transferencia +3 % sobre A + B
        var v2 = await CrearConfirmarYFacturarAsync(Vm(cliente.Id, TipoPago.Transferencia, (a.Id, 1, 1000m), (b.Id, 1, 500m)));

        // V3: tarjeta con plan global (MercadoPago 3 cuotas +10 %): el plan se aplica con GuardarDatosTarjetaAsync, como en el wizard.
        var medioMp = new ConfiguracionPago { TipoPago = TipoPago.MercadoPago, Nombre = $"MP F4 {n}", Activo = true };
        _context.ConfiguracionesPago.Add(medioMp);
        await _context.SaveChangesAsync();
        var planMp = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medioMp.Id, TipoPago = TipoPago.MercadoPago, CantidadCuotas = 3,
            AjustePorcentaje = 10m, Etiqueta = "MP 3 cuotas +10", Activo = true
        };
        _context.ConfiguracionPagoPlanes.Add(planMp);
        await _context.SaveChangesAsync();
        var v3 = await CrearConfirmarYFacturarAsync(
            Vm(cliente.Id, TipoPago.MercadoPago, (a.Id, 1, 1000m)),
            async id => Assert.True(await _ventas.GuardarDatosTarjetaAsync(id, new DatosTarjetaViewModel
            {
                NombreTarjeta = "Mercado Pago", TipoTarjeta = TipoTarjeta.Credito, ConfiguracionPagoPlanId = planMp.Id
            })));

        // V5: Efectivo sin ajuste (se elimina el plan -5 % para este caso) y factura anulada.
        var planEfectivo = await _context.ConfiguracionPagoPlanes.FirstAsync(p => p.TipoPago == TipoPago.Efectivo);
        planEfectivo.AjustePorcentaje = 0m;
        await _context.SaveChangesAsync();
        var v5 = await CrearConfirmarYFacturarAsync(Vm(cliente.Id, TipoPago.Efectivo, (b.Id, 2, 500m)));
        var factura5 = await _context.Facturas.AsNoTracking().SingleAsync(f => f.VentaId == v5);
        Assert.Equal(v5, await _ventas.AnularFacturaAsync(factura5.Id, "Error de carga (QA F4)"));

        // V4: crédito personal con anticipo (sembrado: la generación del crédito es otro módulo; aquí se concilia el dinero).
        var venta4 = new Venta
        {
            Numero = $"VF4-{n}", ClienteId = cliente.Id, Estado = EstadoVenta.Facturada, TipoPago = TipoPago.CreditoPersonal,
            FechaVenta = DateTime.UtcNow, FechaFacturacion = DateTime.UtcNow,
            Subtotal = 1652.89m, IVA = 347.11m, Total = 2000m,
            AperturaCajaId = (await _context.AperturasCaja.FirstAsync()).Id,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = a.Id, Cantidad = 2, PrecioUnitario = 1000m, Subtotal = 2000m, SubtotalFinal = 2000m,
                    PorcentajeIVA = 21m, SubtotalNeto = 1652.89m, SubtotalIVA = 347.11m,
                    SubtotalFinalNeto = 1652.89m, SubtotalFinalIVA = 347.11m,
                    CostoUnitarioAlMomento = 400m, CostoTotalAlMomento = 800m
                }
            }
        };
        _context.Ventas.Add(venta4);
        await _context.SaveChangesAsync();
        _context.Facturas.Add(new Factura
        {
            VentaId = venta4.Id, Numero = $"FB-F4-{n}", Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow,
            Subtotal = 1652.89m, IVA = 347.11m, Total = 2000m
        });
        var credito = new Credito
        {
            Numero = $"CRF4-{n}", ClienteId = cliente.Id, Estado = EstadoCredito.Activo,
            MontoSolicitado = 1500m, MontoAprobado = 1500m, TasaInteres = 0m, CantidadCuotas = 3,
            MontoCuota = 600m, TotalAPagar = 1800m, SaldoPendiente = 1800m, FechaSolicitud = DateTime.UtcNow
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        venta4.CreditoId = credito.Id;
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var cuotas = new[]
        {
            // Las dos primeras vencen hoy (sin punitorio); la tercera venció hace 40 días (mora).
            NuevaCuota(credito.Id, 1, hoy),
            NuevaCuota(credito.Id, 2, hoy),
            NuevaCuota(credito.Id, 3, hoy.AddDays(-40))
        };
        _context.Cuotas.AddRange(cuotas);
        await _context.SaveChangesAsync();

        // Anticipo de 500 por Caja real
        await _caja.RegistrarMovimientoAnticipoAsync(credito.Id, credito.Numero, 500m, "testuser", venta4.Id);
        // Cuota 1 por Transferencia (+3 % lo resuelve el servidor) y cuota 2 en Efectivo (sin ajuste)
        _configPagoCuotas.AjustePorcentaje = 3m;
        Assert.True(await _creditos.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id, CuotaId = cuotas[0].Id, MontoPagado = 600m, FechaPago = DateTime.UtcNow, MedioPago = "Transferencia"
        }));
        _configPagoCuotas.AjustePorcentaje = 0m;
        Assert.True(await _creditos.PagarCuotaAsync(new PagarCuotaViewModel
        {
            CreditoId = credito.Id, CuotaId = cuotas[1].Id, MontoPagado = 600m, FechaPago = DateTime.UtcNow, MedioPago = "Efectivo"
        }));

        // Devolución parcial de 1 de las 3 unidades de V1 (reembolso en efectivo)
        var venta1 = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == v1);
        var dev = await _devoluciones.CrearDevolucionAsync(
            new Devolucion
            {
                VentaId = v1, ClienteId = cliente.Id, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA F4",
                TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero, RegistrarEgresoCaja = true
            },
            new List<DevolucionDetalle> { new() { ProductoId = a.Id, Cantidad = 1 } });
        _context.ChangeTracker.Clear();
        dev = await _devoluciones.AprobarDevolucionAsync(dev.Id, "testuser", (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion);
        _context.ChangeTracker.Clear();
        dev = await _devoluciones.CompletarDevolucionAsync(dev.Id, (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion);
        _context.ChangeTracker.Clear();

        return new Escenario(cliente, a, b, v1, v2, v3, venta4.Id, v5, dev.Id, cuotas[0].Id, cuotas[1].Id, cuotas[2].Id);
    }

    private static Cuota NuevaCuota(int creditoId, int numero, DateTime vence) => new()
    {
        CreditoId = creditoId, NumeroCuota = numero, MontoCapital = 500m, MontoInteres = 100m, MontoTotal = 600m,
        FechaVencimiento = vence, Estado = EstadoCuota.Pendiente
    };

    private static ReporteVentasFiltroViewModel FiltroVentas() => new()
    {
        FechaDesde = DateTime.UtcNow.Date.AddDays(-2),
        FechaHasta = DateTime.UtcNow.Date.AddDays(2)
    };

    // ───────────────────────── 1. importes persistidos de cada venta ─────────────────────────

    [Theory]
    // venta, total, neto, IVA (a mano; ver la cabecera de la clase)
    [InlineData(1, 2850.00, 2355.37, 494.63)]
    [InlineData(2, 1545.00, 1317.30, 227.70)]
    [InlineData(3, 1100.00, 909.09, 190.91)]
    public async Task Persistido_VentaFacturada_NetoMasIvaIgualTotalYCoincideConElCalculoManual(int cual, double total, double neto, double iva)
    {
        var e = await SeedEscenarioAsync();
        var id = cual switch { 1 => e.V1, 2 => e.V2, _ => e.V3 };

        var venta = await _context.Ventas.AsNoTracking().Include(v => v.Detalles).FirstAsync(v => v.Id == id);
        var factura = await _context.Facturas.AsNoTracking().SingleAsync(f => f.VentaId == id);

        Assert.Equal((decimal)total, venta.Total);
        Assert.Equal((decimal)neto, venta.Subtotal);
        Assert.Equal((decimal)iva, venta.IVA);
        Assert.Equal(venta.Total, venta.Subtotal + venta.IVA);
        // Factura congelada = venta, y el total facturable es el total de la venta
        Assert.Equal((venta.Subtotal, venta.IVA, venta.Total), (factura.Subtotal, factura.IVA, factura.Total));
        Assert.Equal(venta.TotalFacturable, factura.Total);
        // La suma de las líneas con IVA (sin el ajuste del medio de pago) nunca supera el total en más que el descuento
        Assert.All(venta.Detalles, d => Assert.Equal(d.SubtotalFinal, d.SubtotalFinalNeto + d.SubtotalFinalIVA));
    }

    // ───────────────────────── 2. Reporte de IVA ─────────────────────────

    [Fact]
    public async Task ReporteIva_SoloFacturadas_CoincideConLaSumaDeLasFacturasVigentesYDesglosaPorAlicuota()
    {
        var e = await SeedEscenarioAsync();

        var r = await _reportes.GenerarReporteIvaAsync(new ReporteIvaFiltroViewModel
        {
            Tipo = "ventas", FechaDesde = DateTime.UtcNow.Date.AddDays(-2), FechaHasta = DateTime.UtcNow.Date.AddDays(2)
        });

        // V1+V2+V3+V4 (V5 tiene la factura anulada y vuelve a Confirmada: no declara IVA)
        Assert.Equal(4, r.VentasCantidad);
        Assert.Equal(6234.65m, r.VentasNeto);
        Assert.Equal(1260.35m, r.VentasIva);
        Assert.Equal(7495.00m, r.VentasTotal);
        Assert.Equal(r.VentasTotal, r.VentasNeto + r.VentasIva);

        var a21 = r.VentasPorAlicuota.Single(x => x.Porcentaje == 21m);
        Assert.Equal((5768.59m, 1211.41m, 6980.00m), (a21.Neto, a21.Iva, a21.Total));
        var a105 = r.VentasPorAlicuota.Single(x => x.Porcentaje == 10.5m);
        Assert.Equal((466.06m, 48.94m, 515.00m), (a105.Neto, a105.Iva, a105.Total));

        // Conciliación contra lo persistido en Factura (no anuladas)
        var facturas = await _context.Facturas.AsNoTracking().Where(f => !f.Anulada).ToListAsync();
        Assert.Equal(r.VentasIva, facturas.Sum(f => f.IVA));
        Assert.Equal(r.VentasNeto, facturas.Sum(f => f.Subtotal));
        Assert.Equal(r.VentasTotal, facturas.Sum(f => f.Total));
        Assert.Equal(0, r.VentasSinDesglose);
    }

    [Fact]
    public async Task ReporteIva_DevolucionSinNcFiscalVinculada_NoNetea_ElIvaDebitoSigueEnElReporte()
    {
        // Criterio interno del propietario (2026-11-25): el Reporte de IVA netea SOLO con una NC fiscal externa válida vinculada; una devolución sin ese vínculo no resta IVA.
        // V1: 3 × 1.000 con −5 % → se cobró 2.850 (IVA 494,63). Se devolvió 1 unidad = 950,00 cobrados.
        // IVA contenido en lo devuelto (950 / 1,21 = 785,12 neto → IVA 164,88): NO se resta del Reporte de IVA.
        var e = await SeedEscenarioAsync();
        var filtro = new ReporteIvaFiltroViewModel
        {
            Tipo = "ventas", FechaDesde = DateTime.UtcNow.Date.AddDays(-2), FechaHasta = DateTime.UtcNow.Date.AddDays(2)
        };

        var iva = await _reportes.GenerarReporteIvaAsync(filtro);
        var ventas = await _reportes.GenerarReporteVentasAsync(FiltroVentas());

        // El reporte de ventas SÍ netea (950,00)...
        Assert.Equal(950.00m, ventas.TotalDevoluciones);
        Assert.Equal(785.12m, ventas.TotalDevolucionesNeto);
        // ...y el de IVA no: sigue informando el IVA íntegro de V1 dentro de 1.260,35.
        Assert.Equal(1260.35m, iva.VentasIva);
        Assert.Equal(0, iva.NotasCreditoCantidad);
        Assert.Equal(0m, iva.NotasCreditoIva);
        // IVA contenido en lo devuelto (solo ilustrativo; se netearía únicamente con una NC fiscal vinculada): 164,88 → 1.095,47.
        Assert.Equal(164.88m, ventas.TotalDevoluciones - ventas.TotalDevolucionesNeto);
        Assert.Equal(1095.47m, iva.VentasIva - (ventas.TotalDevoluciones - ventas.TotalDevolucionesNeto));
    }

    // ───────────────────────── 3. Reporte de Ventas y ganancia ─────────────────────────

    [Fact]
    public async Task ReporteVentas_TotalesGananciaYDevolucion_CoincidenConElCalculoManual()
    {
        var e = await SeedEscenarioAsync();

        var r = await _reportes.GenerarReporteVentasAsync(FiltroVentas());

        // Comerciales: V1, V2, V3, V4 y V5 (la factura anulada deja la venta Confirmada: sigue siendo venta comercial)
        Assert.Equal(5, r.CantidadVentas);
        Assert.Equal(8495.00m, r.TotalVentas);                       // 2850 + 1545 + 1100 + 2000 + 1000
        Assert.Equal(950.00m, r.TotalDevoluciones);
        Assert.Equal(7545.00m, r.TotalVentasNetas);
        // Ingreso neto sin IVA: 2355,37 + 1317,30 + 909,09 + 1652,89 + 904,98
        Assert.Equal(7139.63m, r.TotalIngresoNeto);
        // Costo: V1 3×400 + V2 400+200 + V3 400 + V4 2×400 + V5 2×200 = 3.400
        Assert.Equal(3400.00m, r.TotalCosto);
        Assert.Equal(3739.63m, r.TotalGananciaVentas);               // 7139,63 − 3400
        // Devolución: neto 785,12, costo revertido 400 → ganancia neta 3739,63 − (785,12 − 400)
        Assert.Equal(785.12m, r.TotalDevolucionesNeto);
        Assert.Equal(400.00m, r.TotalCostoDevuelto);
        Assert.Equal(3354.51m, r.TotalGanancia);
        // El total por tipo de pago suma el total de ventas
        Assert.Equal(r.TotalVentas, r.VentasPorTipoPago.Values.Sum());
    }

    // ───────────────────────── 4. Caja ─────────────────────────

    [Fact]
    public async Task Caja_MovimientosPersistidos_SumanLoCobradoYElEgresoDeLaDevolucion()
    {
        var e = await SeedEscenarioAsync();

        var movs = await _context.MovimientosCaja.AsNoTracking().Where(m => !m.IsDeleted).ToListAsync();
        var ingresos = movs.Where(m => m.Tipo == TipoMovimientoCaja.Ingreso).Sum(m => m.Monto);
        var egresos = movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto);

        // Ventas no crédito: 2850 + 1545 + 1100 + 1000; anticipo 500; cuota 1 = 600 + 18; cuota 2 = 600
        Assert.Equal(2850m + 1545m + 1100m + 1000m, movs.Where(m => m.VentaId.HasValue && m.Tipo == TipoMovimientoCaja.Ingreso && m.VentaId != e.V4).Sum(m => m.Monto));
        Assert.Equal(8213.00m, ingresos);
        Assert.Equal(950.00m, egresos);
        // Cada venta se cobró UNA sola vez por su TotalACobrar
        foreach (var id in new[] { e.V1, e.V2, e.V3, e.V5 })
        {
            var v = await _context.Ventas.AsNoTracking().FirstAsync(x => x.Id == id);
            Assert.Equal(v.TotalACobrar, Assert.Single(movs, m => m.VentaId == id && m.Tipo == TipoMovimientoCaja.Ingreso).Monto);
        }
    }

    [Fact]
    public async Task Caja_ConciliaContraVentasYCobrosDeCuotas_IdentidadExplicitaEntreModulos()
    {
        // Identidad que hoy se cumple entre Caja y el Reporte de Ventas (criterio actual, sin ajustes):
        //   Ingresos de caja = Σ Venta.Total (reporte) − capital de la venta a crédito aún no cobrado
        //                      + interés cobrado en cuotas + recargo del medio de pago en cuotas
        //   8.213 = 8.495 − 500 + 200 + 18
        var e = await SeedEscenarioAsync();
        var ventas = await _reportes.GenerarReporteVentasAsync(FiltroVentas());
        var ingresos = await _context.MovimientosCaja.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Tipo == TipoMovimientoCaja.Ingreso).SumAsync(m => m.Monto);

        var cuotas = await _context.Cuotas.AsNoTracking().Where(c => c.CreditoId == (_context.Ventas.First(v => v.Id == e.V4).CreditoId)).ToListAsync();
        var interesCobrado = cuotas.Where(c => c.Estado == EstadoCuota.Pagada).Sum(c => c.MontoInteres);
        var recargoCuotas = cuotas.Sum(c => c.RecargoMedioPago);
        var capitalCuotasPendiente = cuotas.Where(c => c.Estado != EstadoCuota.Pagada).Sum(c => c.MontoCapital);

        Assert.Equal(200m, interesCobrado);
        Assert.Equal(18m, recargoCuotas);
        Assert.Equal(500m, capitalCuotasPendiente);
        Assert.Equal(ventas.TotalVentas - capitalCuotasPendiente + interesCobrado + recargoCuotas, ingresos);
    }

    [Fact]
    public async Task Caja_ConciliacionBuilder_TotalIvaFacturadoCoincideConElReporteDeIva_ExcluyendoLaFacturaAnulada()
    {
        var e = await SeedEscenarioAsync();
        var apertura = await _context.AperturasCaja.AsNoTracking().FirstAsync();

        var detalle = await _caja.ObtenerDetallesAperturaAsync(apertura.Id);
        var conciliacion = CajaConciliacionBuilder.Build(detalle, null, true);

        var iva = await _reportes.GenerarReporteIvaAsync(new ReporteIvaFiltroViewModel
        {
            Tipo = "ventas", FechaDesde = DateTime.UtcNow.Date.AddDays(-2), FechaHasta = DateTime.UtcNow.Date.AddDays(2)
        });

        // Caja y Reporte de IVA aplican el mismo criterio (solo Facturada/Entregada discriminan IVA)
        Assert.Equal(iva.VentasIva, conciliacion.TotalIvaFacturado);
        Assert.False(conciliacion.Ventas.Single(v => v.VentaId == e.V5).DiscriminaIva);
    }

    // ───────────────────────── 5. cobro de cuotas, recibo y recargo fuera de IVA ─────────────────────────

    [Fact]
    public async Task Cuotas_Recargo3PorCiento_EntraACajaPeroNoALaVentaNiAlIva_YElReciboCierraConElMovimiento()
    {
        var e = await SeedEscenarioAsync();

        var cuota1 = await _context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == e.Cuota1Id);
        Assert.Equal(EstadoCuota.Pagada, cuota1.Estado);
        Assert.Equal(600m, cuota1.MontoPagado);              // el recargo no salda capital
        Assert.Equal(18m, cuota1.RecargoMedioPago);          // 3 % de 600

        var pago = await _context.PagosCuota.AsNoTracking().SingleAsync(p => p.CuotaId == e.Cuota1Id);
        var mov = await _context.MovimientosCaja.AsNoTracking().SingleAsync(m => m.Id == pago.MovimientoCajaId);
        Assert.Equal(600m, mov.ImporteBase);
        Assert.Equal(18m, mov.RecargoMedioPago);
        Assert.Equal(618m, mov.Monto);
        // «Recibo»: total cobrado = importe del pago + (recargo − descuento) del movimiento (DocumentoContextoBuilder) = Monto de caja
        var totalCobradoRecibo = pago.ImporteTotal + (mov.RecargoMedioPago ?? 0m) - (mov.DescuentoMedioPago ?? 0m);
        Assert.Equal(mov.Monto, totalCobradoRecibo);

        // Cuota 2 en efectivo: sin ajuste
        var cuota2 = await _context.Cuotas.AsNoTracking().FirstAsync(c => c.Id == e.Cuota2Id);
        Assert.Equal(0m, cuota2.RecargoMedioPago);

        // La venta a crédito y el Reporte de IVA no incluyen ni intereses ni recargo: IVA de V4 = solo sus ítems (347,11)
        var venta4 = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == e.V4);
        Assert.Equal(2000m, venta4.Total);
        Assert.Equal(347.11m, venta4.IVA);
        var credito = await _context.Creditos.AsNoTracking().FirstAsync(c => c.Id == venta4.CreditoId);
        Assert.Equal(1800m, credito.TotalAPagar);            // capital financiado 1.500 + interés 300
        Assert.Equal(2300m, 500m + credito.TotalAPagar);     // el cliente paga 300 más que Venta.Total; esos 300 no pasan por IVA
    }

    // ───────────────────────── 6. mora ─────────────────────────

    [Fact]
    public async Task ReporteMorosidad_SoloLaCuotaVencida_YNoMezclaIvaNiRecargos()
    {
        var e = await SeedEscenarioAsync();

        var m = await new ReporteService(_context, NullLogger<ReporteService>.Instance, reloj: _reloj).GenerarReporteMorosidadAsync();

        // Cuota 3: 600 vencida hace 40 días (capital 500 + interés 100); las cuotas 1 y 2 están pagadas.
        Assert.Equal(600m, m.TotalDeudaVencida);
        Assert.Equal(1, m.CantidadClientesMorosos);
    }

    // ───────────────────────── 7. factura anulada ─────────────────────────

    [Fact]
    public async Task FacturaAnulada_LaVentaVuelveAConfirmada_SigueEnVentasYCaja_PeroNoEnElIva()
    {
        var e = await SeedEscenarioAsync();

        var venta5 = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == e.V5);
        var factura = await _context.Facturas.AsNoTracking().SingleAsync(f => f.VentaId == e.V5);
        Assert.Equal(EstadoVenta.Confirmada, venta5.Estado);
        Assert.True(factura.Anulada);
        Assert.Equal(1000m, venta5.Total);
        Assert.Equal(venta5.Total, venta5.Subtotal + venta5.IVA);
        Assert.Equal(1000m, factura.Total);   // la factura anulada conserva sus importes (histórico)
        // Caja: el cobro de V5 sigue vigente (no hay contramovimiento)
        Assert.Single(await _context.MovimientosCaja.AsNoTracking().Where(mv => mv.VentaId == e.V5).ToListAsync());
    }

    // ───────────────────────── 7b. devolución sobre un total truncado a pesos ─────────────────────────

    [Fact]
    public async Task Devolucion_VentaConCentavosTruncados_DevolverTodoReintegraExactamenteLoCobrado()
    {
        // 3 × 333,33 = 999,99 → el total cobrado se trunca a 999 (criterio vigente). Devolver las 3 unidades debe reintegrar
        // 999,00 (lo cobrado), nunca 999,99: un reembolso mayor que el ingreso no cierra contra Caja.
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CT{n}", Nombre = $"Cat T {n}" };
        var marca = new Marca { Codigo = $"MT{n}", Nombre = $"Marca T {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente { Nombre = "T", Apellido = "Trunc", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"t{n}@t.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal };
        var prod = new Producto { Codigo = $"PT{n}", Nombre = $"P {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 100m, PrecioVenta = 333.33m, PorcentajeIVA = 21m, StockActual = 10 };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();

        var ventaId = await CrearConfirmarYFacturarAsync(Vm(cliente.Id, TipoPago.Efectivo, (prod.Id, 3, 333.33m)));
        var venta = await _context.Ventas.AsNoTracking().FirstAsync(v => v.Id == ventaId);
        Assert.Equal(999m, venta.Total);
        Assert.Equal(venta.Total, venta.Subtotal + venta.IVA);

        var dev = await _devoluciones.CrearDevolucionAsync(
            new Devolucion { VentaId = ventaId, ClienteId = cliente.Id, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA F4", TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero },
            new List<DevolucionDetalle> { new() { ProductoId = prod.Id, Cantidad = 3 } });

        Assert.Equal(venta.Total, dev.TotalDevolucion);
    }

    [Fact]
    public async Task Devolucion_VentaConCentavosTruncados_TresDevolucionesParcialesNoSuperanLoCobrado()
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CU{n}", Nombre = $"Cat U {n}" };
        var marca = new Marca { Codigo = $"MU{n}", Nombre = $"Marca U {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente { Nombre = "U", Apellido = "Trunc", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"u{n}@t.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal };
        var prod = new Producto { Codigo = $"PU{n}", Nombre = $"P {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 100m, PrecioVenta = 333.33m, PorcentajeIVA = 21m, StockActual = 10 };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        var ventaId = await CrearConfirmarYFacturarAsync(Vm(cliente.Id, TipoPago.Efectivo, (prod.Id, 3, 333.33m)));

        decimal reintegrado = 0m;
        for (var i = 0; i < 3; i++)
        {
            var dev = await _devoluciones.CrearDevolucionAsync(
                new Devolucion { VentaId = ventaId, ClienteId = cliente.Id, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA F4", TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero },
                new List<DevolucionDetalle> { new() { ProductoId = prod.Id, Cantidad = 1 } });
            reintegrado += dev.TotalDevolucion;
        }

        Assert.Equal(999m, reintegrado);
    }

    // ───────────────────────── 7c. nota de crédito interna ─────────────────────────

    [Fact]
    public async Task NotaCreditoInterna_AprobarGeneraLaNCPorElTotalDevuelto_SinEgresoDeCaja_YElReporteLaResta()
    {
        var e = await SeedEscenarioAsync();   // ya incluye una devolución de 950 reembolsada en efectivo

        var dev = await _devoluciones.CrearDevolucionAsync(
            new Devolucion { VentaId = e.V1, ClienteId = e.Cliente.Id, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA F4 NC", TipoResolucion = TipoResolucionDevolucion.NotaCredito },
            new List<DevolucionDetalle> { new() { ProductoId = e.A.Id, Cantidad = 1 } });
        _context.ChangeTracker.Clear();
        var rv = (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion;
        await _devoluciones.AprobarDevolucionAsync(dev.Id, "testuser", rv);
        _context.ChangeTracker.Clear();

        // 2.ª unidad de V1: 2.850 cobrados / 3 = 950,00
        var nc = await _context.NotasCredito.AsNoTracking().SingleAsync(n => n.DevolucionId == dev.Id);
        Assert.Equal(950.00m, nc.MontoTotal);
        Assert.Equal(EstadoNotaCredito.Vigente, nc.Estado);
        // Una NC interna NO mueve Caja: el egreso sigue siendo solo el de la devolución en efectivo
        var egresos = await _context.MovimientosCaja.AsNoTracking().Where(m => m.Tipo == TipoMovimientoCaja.Egreso).SumAsync(m => m.Monto);
        Assert.Equal(950.00m, egresos);
        // El Reporte de Ventas resta las dos devoluciones (Aprobada y Completada); el Reporte de IVA no netea ninguna
        var ventas = await _reportes.GenerarReporteVentasAsync(FiltroVentas());
        Assert.Equal(1900.00m, ventas.TotalDevoluciones);
        Assert.Equal(2, ventas.CantidadDevoluciones);
        // y la NC no es un comprobante fiscal: Factura no registra ninguna Nota de crédito (TipoFactura.NotaCredito)
        Assert.Empty(await _context.Facturas.AsNoTracking().Where(f => f.Tipo == TipoFactura.NotaCredito).ToListAsync());
    }

    // ───────────────────────── 7c-bis. neteo informativo del IVA por NC fiscal externa vinculada ─────────────────────────
    // Criterio interno del propietario (2026-11-25; no es certificación del contador): el IVA de una devolución se resta en el período de la
    // FECHA DE LA NC FISCAL externa vinculada; sin vínculo válido no se resta; la venta original no cambia.

    private async Task<NotaCredito> DevolverConNcAsync(int ventaId, int clienteId, params (int ProductoId, int Cantidad)[] items)
    {
        var dev = await _devoluciones.CrearDevolucionAsync(
            new Devolucion { VentaId = ventaId, ClienteId = clienteId, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA NC fiscal", TipoResolucion = TipoResolucionDevolucion.NotaCredito },
            items.Select(i => new DevolucionDetalle { ProductoId = i.ProductoId, Cantidad = i.Cantidad }).ToList());
        _context.ChangeTracker.Clear();
        var rv = (await _context.Devoluciones.AsNoTracking().FirstAsync(d => d.Id == dev.Id)).RowVersion;
        await _devoluciones.AprobarDevolucionAsync(dev.Id, "testuser", rv);
        _context.ChangeTracker.Clear();
        return await _context.NotasCredito.AsNoTracking().SingleAsync(n => n.DevolucionId == dev.Id);
    }

    private async Task VincularFiscalAsync(int ncId, decimal importe, DateTime fecha, string numero = "00000001")
    {
        var nc = await _context.NotasCredito.FirstAsync(n => n.Id == ncId);
        nc.FiscalExternaPuntoVenta = "00001"; nc.FiscalExternaTipo = "NCB"; nc.FiscalExternaNumero = numero;
        nc.FiscalExternaFecha = fecha.Date; nc.FiscalExternaImporte = importe;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private ReporteIvaFiltroViewModel FiltroIva(DateTime desde, DateTime hasta) =>
        new() { Tipo = "ventas", FechaDesde = desde, FechaHasta = hasta };

    [Fact]
    public async Task NcFiscal_AlicuotasMixtas_NetaElIvaPorAlicuota_Y_NoTocaLaVentaOriginal()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var filtro = FiltroIva(hoy.AddDays(-2), hoy.AddDays(2));
        var antes = await _reportes.GenerarReporteIvaAsync(filtro);

        // V2 = A (21 %) 1.000 + B (10,5 %) 500 con +3 % de transferencia = 1.545 (1.030 + 515). Se devuelve todo.
        var nc = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.A.Id, 1), (e.B.Id, 1));
        Assert.Equal(1545.00m, nc.MontoTotal);
        await VincularFiscalAsync(nc.Id, 1545.00m, hoy);

        var r = await _reportes.GenerarReporteIvaAsync(filtro);
        // La venta original NO cambia (ni importes ni IVA ni operaciones)
        Assert.Equal(antes.VentasIva, r.VentasIva);
        Assert.Equal(antes.VentasNeto, r.VentasNeto);
        Assert.Equal(antes.VentasTotal, r.VentasTotal);
        Assert.Equal(antes.VentasCantidad, r.VentasCantidad);
        // NC fiscal: 21 % → 1.030,00 = 851,24 + 178,76 ; 10,5 % → 515,00 = 466,06 + 48,94
        Assert.Equal(1, r.NotasCreditoCantidad);
        var a21 = r.NotasCreditoPorAlicuota.Single(x => x.Porcentaje == 21m);
        var a105 = r.NotasCreditoPorAlicuota.Single(x => x.Porcentaje == 10.5m);
        Assert.Equal((851.24m, 178.76m, 1030.00m), (a21.Neto, a21.Iva, a21.Total));
        Assert.Equal((466.06m, 48.94m, 515.00m), (a105.Neto, a105.Iva, a105.Total));
        Assert.Equal(227.70m, r.NotasCreditoIva);
        Assert.Equal(1545.00m, r.NotasCreditoTotal);
        Assert.Equal(r.NotasCreditoTotal, r.NotasCreditoNeto + r.NotasCreditoIva);
        Assert.Equal(antes.VentasIva - 227.70m, r.VentasIva - r.NotasCreditoIva);
    }

    [Fact]
    public async Task NcFiscal_DevolucionParcial_SoloNetaLaAlicuotaDeLoDevuelto()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var nc = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.B.Id, 1));   // solo el producto al 10,5 %: 500 × 1,03 = 515
        await VincularFiscalAsync(nc.Id, nc.MontoTotal, hoy);

        var r = await _reportes.GenerarReporteIvaAsync(FiltroIva(hoy.AddDays(-2), hoy.AddDays(2)));

        var unica = Assert.Single(r.NotasCreditoPorAlicuota);
        Assert.Equal(10.5m, unica.Porcentaje);
        Assert.Equal(nc.MontoTotal, unica.Total);
        Assert.Equal(unica.Total, unica.Neto + unica.Iva);
        Assert.Equal(unica.Iva, r.NotasCreditoIva);
    }

    [Fact]
    public async Task NcFiscal_UnaNcParcialSobreLoDevuelto_UsaElImporteDeLaNcFiscal()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var nc = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.A.Id, 1), (e.B.Id, 1));
        await VincularFiscalAsync(nc.Id, 772.50m, hoy);   // la NC fiscal acredita la mitad de la interna

        var r = await _reportes.GenerarReporteIvaAsync(FiltroIva(hoy.AddDays(-2), hoy.AddDays(2)));

        Assert.Equal(772.50m, r.NotasCreditoTotal);
        // mitad de cada alícuota: 515,00 (21 %) y 257,50 (10,5 %)
        Assert.Equal(515.00m, r.NotasCreditoPorAlicuota.Single(x => x.Porcentaje == 21m).Total);
        Assert.Equal(257.50m, r.NotasCreditoPorAlicuota.Single(x => x.Porcentaje == 10.5m).Total);
    }

    [Fact]
    public async Task NcFiscal_SeImputaAlPeriodoDeLaFechaFiscal_NoAlDeLaVentaNiAlDeLaDevolucion()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var nc = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.A.Id, 1));
        var fechaFiscal = hoy.AddMonths(1);   // otro mes (cambio de mes)
        await VincularFiscalAsync(nc.Id, nc.MontoTotal, fechaFiscal);

        var mesDeLaVenta = await _reportes.GenerarReporteIvaAsync(FiltroIva(hoy.AddDays(-2), hoy.AddDays(2)));
        var mesDeLaNc = await _reportes.GenerarReporteIvaAsync(FiltroIva(fechaFiscal, fechaFiscal));
        var diaAnterior = await _reportes.GenerarReporteIvaAsync(FiltroIva(fechaFiscal.AddDays(-1), fechaFiscal.AddDays(-1)));

        Assert.Equal(0, mesDeLaVenta.NotasCreditoCantidad);
        Assert.Equal(0m, mesDeLaVenta.NotasCreditoIva);
        Assert.Equal(1, mesDeLaNc.NotasCreditoCantidad);
        Assert.True(mesDeLaNc.NotasCreditoIva > 0m);
        Assert.Equal(0, mesDeLaNc.VentasCantidad);          // la venta sigue en su mes; el mes de la NC queda con IVA neto negativo
        Assert.True(mesDeLaNc.DiferenciaIva < 0m);
        Assert.Equal(0, diaAnterior.NotasCreditoCantidad);
    }

    [Fact]
    public async Task NcFiscal_SinVinculo_NoNeta_Y_NcInternaNoAlcanza()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.A.Id, 1));   // NC interna aprobada, SIN comprobante fiscal externo

        var r = await _reportes.GenerarReporteIvaAsync(FiltroIva(hoy.AddDays(-2), hoy.AddDays(2)));

        Assert.Equal(0, r.NotasCreditoCantidad);
        Assert.Equal(0m, r.NotasCreditoIva);
        Assert.Empty(r.NotasCreditoPorAlicuota);
    }

    [Fact]
    public async Task NcFiscal_VinculoInvalido_NoNeta()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var filtro = FiltroIva(hoy.AddDays(-2), hoy.AddDays(2));

        // importe de la NC fiscal mayor que el de la NC interna
        var nc1 = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.A.Id, 1));
        await VincularFiscalAsync(nc1.Id, nc1.MontoTotal + 100m, hoy, "00000001");
        Assert.Equal(0, (await _reportes.GenerarReporteIvaAsync(filtro)).NotasCreditoCantidad);

        // importe cero
        await VincularFiscalSobrescribirAsync(nc1.Id, 0m, hoy, "00000002");
        Assert.Equal(0, (await _reportes.GenerarReporteIvaAsync(filtro)).NotasCreditoCantidad);

        // vínculo válido pero NC cancelada
        await VincularFiscalSobrescribirAsync(nc1.Id, nc1.MontoTotal, hoy, "00000003");
        Assert.Equal(1, (await _reportes.GenerarReporteIvaAsync(filtro)).NotasCreditoCantidad);
        var nc = await _context.NotasCredito.FirstAsync(n => n.Id == nc1.Id);
        nc.Estado = EstadoNotaCredito.Cancelada;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        Assert.Equal(0, (await _reportes.GenerarReporteIvaAsync(filtro)).NotasCreditoCantidad);
    }

    private async Task VincularFiscalSobrescribirAsync(int ncId, decimal importe, DateTime fecha, string numero)
    {
        var nc = await _context.NotasCredito.FirstAsync(n => n.Id == ncId);
        nc.FiscalExternaPuntoVenta = "00001"; nc.FiscalExternaTipo = "NCB"; nc.FiscalExternaNumero = numero;
        nc.FiscalExternaFecha = fecha.Date; nc.FiscalExternaImporte = importe;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task NcFiscal_MultiplesDevolucionesDeLaMismaVenta_CadaNcCuentaUnaVez_YSumanSinDuplicar()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var ncA = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.A.Id, 1));
        var ncB = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.B.Id, 1));
        await VincularFiscalAsync(ncA.Id, ncA.MontoTotal, hoy, "00000010");
        await VincularFiscalAsync(ncB.Id, ncB.MontoTotal, hoy.AddDays(1), "00000011");

        var filtro = FiltroIva(hoy.AddDays(-2), hoy.AddDays(3));
        var todo = await _reportes.GenerarReporteIvaAsync(filtro);
        var soloPrimera = await _reportes.GenerarReporteIvaAsync(FiltroIva(hoy, hoy));
        var soloSegunda = await _reportes.GenerarReporteIvaAsync(FiltroIva(hoy.AddDays(1), hoy.AddDays(1)));

        Assert.Equal(2, todo.NotasCreditoCantidad);
        Assert.Equal(ncA.MontoTotal + ncB.MontoTotal, todo.NotasCreditoTotal);
        Assert.Equal(soloPrimera.NotasCreditoIva + soloSegunda.NotasCreditoIva, todo.NotasCreditoIva);
        Assert.Equal(1, soloPrimera.NotasCreditoCantidad);
        Assert.Equal(1, soloSegunda.NotasCreditoCantidad);
        // Cada NC es igual a la devolución completa de la venta: 227,70 de IVA en total, como la devolución total en un solo comprobante
        Assert.Equal(227.70m, todo.NotasCreditoIva);
    }

    [Fact]
    public async Task NcFiscal_FiltroPorAlicuota_RestringeTambienElNeteo()
    {
        var e = await SeedEscenarioAsync();
        var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        var nc = await DevolverConNcAsync(e.V2, e.Cliente.Id, (e.A.Id, 1), (e.B.Id, 1));
        await VincularFiscalAsync(nc.Id, nc.MontoTotal, hoy);

        var f = FiltroIva(hoy.AddDays(-2), hoy.AddDays(2));
        f.Alicuota = 10.5m;
        var r = await _reportes.GenerarReporteIvaAsync(f);

        Assert.Equal(48.94m, r.NotasCreditoIva);
        Assert.Equal(48.94m, Assert.Single(r.NotasCreditoPorAlicuota).Iva);
    }

    // ───────────────────────── 7d. envío y armado ─────────────────────────

    [Fact]
    public async Task EnvioYArmado_VentaNueva_DentroDelTotalConIvaAl21_FacturaReporteCajaYGananciaCierran()
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CE{n}", Nombre = $"Cat E {n}" };
        var marca = new Marca { Codigo = $"ME{n}", Nombre = $"Marca E {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente { Nombre = "E", Apellido = "Envio", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"e{n}@t.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal };
        var prod = new Producto { Codigo = $"PE{n}", Nombre = $"P {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 400m, PrecioVenta = 1000m, PorcentajeIVA = 21m, StockActual = 10 };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        await new ServicioVentaPrecioService(_context).GuardarAsync(new[]
        {
            new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, 200m, true, 80m),
            new ServicioVentaPrecioComando(TipoServicioVenta.Armado5, 450m, true, 120m)
        });

        var vm = Vm(cliente.Id, TipoPago.Efectivo, (prod.Id, 1, 1000m));
        vm.Detalles[0].TipoArmado = TipoServicioVenta.Armado5;
        vm.TieneEnvio = true;
        vm.Envio = new VentaEnvioViewModel { Destinatario = "Juan", Domicilio = "Calle 1", TipoEnvio = TipoServicioVenta.EnvioCiudad };
        var ventaId = await CrearConfirmarYFacturarAsync(vm);

        var venta = await _context.Ventas.AsNoTracking().Include(v => v.Envio).FirstAsync(v => v.Id == ventaId);
        var factura = await _context.Facturas.AsNoTracking().SingleAsync(f => f.VentaId == ventaId);
        // Producto 1.000 (826,45 + 173,55) + servicios 650 al 21 % (537,19 + 112,81) = 1.650 (1.363,64 + 286,36)
        Assert.Equal(1650m, venta.Total);
        Assert.Equal((1363.64m, 286.36m), (venta.Subtotal, venta.IVA));
        Assert.Equal((venta.Subtotal, venta.IVA, venta.Total), (factura.Subtotal, factura.IVA, factura.Total));
        // El envío nuevo ya está en Total: nada se cobra aparte
        Assert.True(venta.Envio!.IncluidoEnTotal);
        Assert.Equal(0m, venta.ImporteEnvioFueraDelTotal);
        Assert.Equal(venta.Total, venta.TotalACobrar);
        Assert.Equal(venta.TotalFacturable, venta.TotalACobrar);
        // Caja cobró TotalACobrar
        Assert.Equal(1650m, (await _context.MovimientosCaja.AsNoTracking().SingleAsync(m => m.VentaId == ventaId)).Monto);

        var iva = await _reportes.GenerarReporteIvaAsync(new ReporteIvaFiltroViewModel
        { Tipo = "ventas", FechaDesde = DateTime.UtcNow.Date.AddDays(-2), FechaHasta = DateTime.UtcNow.Date.AddDays(2) });
        Assert.Equal((1363.64m, 286.36m, 1650m), (iva.VentasNeto, iva.VentasIva, iva.VentasTotal));

        // Ganancia sobre neto sin IVA, descontando costo de producto (400) + armado (120) + envío (80)
        var ventas = await _reportes.GenerarReporteVentasAsync(FiltroVentas());
        Assert.Equal(600m, ventas.TotalCosto);
        Assert.Equal(763.64m, ventas.TotalGananciaVentas);

        // Devolver el producto NO devuelve armado ni envío: reintegra 1.000, no 1.650
        var dev = await _devoluciones.CrearDevolucionAsync(
            new Devolucion { VentaId = ventaId, ClienteId = cliente.Id, Motivo = MotivoDevolucion.DefectoFabrica, Descripcion = "QA F4", TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero },
            new List<DevolucionDetalle> { new() { ProductoId = prod.Id, Cantidad = 1 } });
        Assert.Equal(1000m, dev.TotalDevolucion);
    }

    [Fact]
    public async Task EnvioLegacyFueraDelTotal_CajaCobraTotalMasEnvio_PeroFacturaYReportesSoloElTotal()
    {
        // Ventas anteriores al modelo nuevo (VentaEnvio.IncluidoEnTotal = false): TotalACobrar = Total + envío; TotalFacturable = Total.
        // Hoy: Caja 1.200, Factura 1.000, Reporte de Ventas 1.000 → 200 de envío que ingresaron a Caja sin IVA ni factura.
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CL{n}", Nombre = $"Cat L {n}" };
        var marca = new Marca { Codigo = $"ML{n}", Nombre = $"Marca L {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente { Nombre = "L", Apellido = "Legacy", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"l{n}@t.com" };
        var prod = new Producto { Codigo = $"PL{n}", Nombre = $"P {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 100m, PrecioVenta = 1000m, PorcentajeIVA = 21m, StockActual = 10 };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        var venta = new Venta
        {
            Numero = $"VL{n}", ClienteId = cliente.Id, Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow, Subtotal = 826.45m, IVA = 173.55m, Total = 1000m,
            Envio = new VentaEnvio { Destinatario = "X", Domicilio = "Y", CostoEnvio = 200m, IncluidoEnTotal = false },
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = prod.Id, Cantidad = 1, PrecioUnitario = 1000m, Subtotal = 1000m, SubtotalFinal = 1000m,
                    PorcentajeIVA = 21m, SubtotalNeto = 826.45m, SubtotalIVA = 173.55m, SubtotalFinalNeto = 826.45m, SubtotalFinalIVA = 173.55m,
                    CostoUnitarioAlMomento = 100m, CostoTotalAlMomento = 100m
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        Assert.True(await _ventas.FacturarVentaAsync(venta.Id, new FacturaViewModel { Tipo = TipoFactura.B, FechaEmision = DateTime.UtcNow }));
        _context.ChangeTracker.Clear();

        var persistida = await _context.Ventas.AsNoTracking().Include(v => v.Envio).FirstAsync(v => v.Id == venta.Id);
        var factura = await _context.Facturas.AsNoTracking().SingleAsync(f => f.VentaId == venta.Id);
        var mov = await _context.MovimientosCaja.AsNoTracking().SingleAsync(m => m.VentaId == venta.Id);

        Assert.Equal(1200m, persistida.TotalACobrar);
        Assert.Equal(1200m, mov.Monto);
        Assert.Equal(1000m, factura.Total);
        Assert.Equal(1000m, persistida.TotalFacturable);
        Assert.Equal(persistida.Total, persistida.Subtotal + persistida.IVA);
        var ventas = await _reportes.GenerarReporteVentasAsync(FiltroVentas());
        Assert.Equal(1000m, ventas.TotalVentas);
        Assert.Equal(200m, mov.Monto - ventas.TotalVentas);   // diferencia Caja − Ventas = envío legacy
    }

    // ───────────────────────── 8. fecha de imputación: día comercial (UTC−3) vs UTC ─────────────────────────

    private async Task<Venta> SeedVentaEnInstanteAsync(DateTime fechaUtc, decimal total = 1000m)
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CD{n}", Nombre = $"Cat D {n}" };
        var marca = new Marca { Codigo = $"MD{n}", Nombre = $"Marca D {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente { Nombre = "F", Apellido = "Fecha", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"d{n}@t.com" };
        var prod = new Producto { Codigo = $"PD{n}", Nombre = $"P {n}", CategoriaId = cat.Id, MarcaId = marca.Id, PrecioCompra = 100m, PrecioVenta = total, PorcentajeIVA = 21m, StockActual = 10 };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        var neto = Math.Round(total / 1.21m, 2, MidpointRounding.AwayFromZero);
        var venta = new Venta
        {
            Numero = $"VD{n}", ClienteId = cliente.Id, Estado = EstadoVenta.Facturada, TipoPago = TipoPago.Efectivo,
            FechaVenta = fechaUtc, Subtotal = neto, IVA = total - neto, Total = total,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = prod.Id, Cantidad = 1, PrecioUnitario = total, Subtotal = total, SubtotalFinal = total,
                    PorcentajeIVA = 21m, SubtotalFinalNeto = neto, SubtotalFinalIVA = total - neto,
                    CostoUnitarioAlMomento = 100m, CostoTotalAlMomento = 100m
                }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    [Fact]
    public async Task C15_VentaDeLas2230ArgentinaDel31DeOctubre_LosReportesLaImputanAOctubre()
    {
        // P-02 / C15 (contador, 2026-11-19): los informes usan el día comercial argentino. 31/10 22:30 ART (UTC−3) = 01/11 01:30 UTC:
        // se guarda como instante UTC y los reportes de Ventas / IVA la imputan a OCTUBRE (antes la imputaban a noviembre UTC).
        var instante = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Utc);
        await SeedVentaEnInstanteAsync(instante, total: 1210m);

        var comercial = TimeZoneInfo.ConvertTimeFromUtc(instante, new RelojComercial(TimeProvider.System).ZonaComercial);
        Assert.Equal(new DateTime(2026, 10, 31, 22, 30, 0), comercial);   // día comercial = 31/10

        var octubre = await _reportes.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel
        { FechaDesde = new DateTime(2026, 10, 1), FechaHasta = new DateTime(2026, 10, 31) });
        var noviembre = await _reportes.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel
        { FechaDesde = new DateTime(2026, 11, 1), FechaHasta = new DateTime(2026, 11, 30) });
        var ivaOctubre = await _reportes.GenerarReporteIvaAsync(new ReporteIvaFiltroViewModel
        { Tipo = "ventas", FechaDesde = new DateTime(2026, 10, 1), FechaHasta = new DateTime(2026, 10, 31) });
        var ivaNoviembre = await _reportes.GenerarReporteIvaAsync(new ReporteIvaFiltroViewModel
        { Tipo = "ventas", FechaDesde = new DateTime(2026, 11, 1), FechaHasta = new DateTime(2026, 11, 30) });

        Assert.Equal(1, octubre.CantidadVentas);
        Assert.Equal(1210m, octubre.TotalVentas);
        Assert.Equal(0, noviembre.CantidadVentas);
        Assert.Equal(210.00m, ivaOctubre.VentasIva);   // 1210 − 1000,00 (neto)
        Assert.Equal(0m, ivaNoviembre.VentasIva);
        // La pantalla de reportes muestra la hora comercial (31/10 22:30), no la UTC.
        Assert.Equal(comercial, Assert.Single(octubre.Ventas).FechaVenta);
    }

    [Theory]
    // instante UTC de la venta → ¿entra en el rango de octubre (día completo comercial, 00:00 ART 01/10 a 23:59:59 ART 31/10)?
    [InlineData("2026-11-01T02:59:59", true)]    // 23:59:59 ART del 31/10: último segundo de octubre
    [InlineData("2026-11-01T03:00:00", false)]   // 00:00 ART del 01/11: primer instante de noviembre
    [InlineData("2026-10-01T03:00:00", true)]    // 00:00 ART del 01/10: primer instante de octubre (borde inferior inclusivo)
    [InlineData("2026-10-01T02:59:59", false)]   // 23:59:59 ART del 30/09
    public async Task ReporteVentas_BordesDeMes_SeComparanPorDiaComercialArgentino(string instanteUtc, bool entraEnOctubre)
    {
        await SeedVentaEnInstanteAsync(DateTime.SpecifyKind(DateTime.Parse(instanteUtc), DateTimeKind.Utc));

        var r = await _reportes.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel
        { FechaDesde = new DateTime(2026, 10, 1), FechaHasta = new DateTime(2026, 10, 31) });

        Assert.Equal(entraEnOctubre ? 1 : 0, r.CantidadVentas);
    }

    // P-02 / C15: cambio de día, de mes y de AÑO. Instante UTC de la venta -> (período mensual al que pertenece, etiqueta del día comercial).
    [Theory]
    [InlineData("2026-01-01T02:59:59", 2025, 12, "31/12/2025")]   // 23:59:59 ART del 31/12: diciembre del año ANTERIOR
    [InlineData("2026-01-01T03:00:00", 2026, 1, "01/01/2026")]    // 00:00 ART del 01/01: enero
    [InlineData("2026-03-01T02:59:59", 2026, 2, "28/02/2026")]    // fin de febrero (no bisiesto)
    [InlineData("2028-03-01T02:59:59", 2028, 2, "29/02/2028")]    // fin de febrero bisiesto
    [InlineData("2026-11-01T01:30:00", 2026, 10, "31/10/2026")]   // el caso del contador: 22:30 ART
    public async Task C15_CambioDeDiaMesYAnio_ElPeriodoMensualYElDiaSonLosComercialesArgentinos(string instanteUtc, int anio, int mes, string diaEsperado)
    {
        await SeedVentaEnInstanteAsync(DateTime.SpecifyKind(DateTime.Parse(instanteUtc), DateTimeKind.Utc), total: 1210m);

        var delMes = await _reportes.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel
        { FechaDesde = new DateTime(anio, mes, 1), FechaHasta = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes)) });
        var iva = await _reportes.GenerarReporteIvaAsync(new ReporteIvaFiltroViewModel
        { Tipo = "ventas", FechaDesde = new DateTime(anio, mes, 1), FechaHasta = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes)) });
        var delAnio = await _reportes.GenerarReporteVentasAsync(new ReporteVentasFiltroViewModel
        { FechaDesde = new DateTime(anio, 1, 1), FechaHasta = new DateTime(anio, 12, 31) });
        var porDia = await _reportes.ObtenerVentasAgrupadasAsync(new DateTime(anio, mes, 1), new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes)), "dia");

        Assert.Equal(1, delMes.CantidadVentas);
        Assert.Equal(210.00m, iva.VentasIva);
        Assert.Equal(1, delAnio.CantidadVentas);   // el año comercial también coincide (31/12 22:30 es del año que termina)
        Assert.Equal(diaEsperado, Assert.Single(porDia).Etiqueta);
    }

    // Conciliación de Caja: la hora de cada venta del turno es la comercial (antes ToLocalTime() dependía de la zona del servidor).
    [Fact]
    public async Task C15_ConciliacionDeCaja_MuestraLaHoraComercialDeLaVenta()
    {
        var e = await SeedEscenarioAsync();
        var venta = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == e.V1);
        var apertura = await _context.AperturasCaja.AsNoTracking().FirstAsync();
        var detalle = await _caja.ObtenerDetallesAperturaAsync(apertura.Id);

        var conciliacion = CajaConciliacionBuilder.Build(detalle, null, true);

        var linea = conciliacion.Ventas.Single(v => v.VentaId == e.V1);
        Assert.Equal(FechaComercial.AComercial(venta.FechaVenta), linea.Fecha);
    }

    [Fact]
    public async Task ReporteVentas_AgrupadasPorDia_UsaElDiaComercialArgentinoDeLaVenta()
    {
        // Misma venta 31/10 22:30 ART: el reporte «por día» la agrupa en el 31/10, no en el 01/11 (día UTC).
        await SeedVentaEnInstanteAsync(new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Utc));

        var grupos = await _reportes.ObtenerVentasAgrupadasAsync(new DateTime(2026, 10, 1), new DateTime(2026, 11, 30), "dia");

        var grupo = Assert.Single(grupos);
        Assert.Equal("31/10/2026", grupo.Etiqueta);
        var meses = await _reportes.ObtenerVentasAgrupadasAsync(new DateTime(2026, 10, 1), new DateTime(2026, 11, 30), "mes");
        Assert.Equal("10/2026", Assert.Single(meses).Etiqueta);
    }
}
