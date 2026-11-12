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

file sealed class StubNotificacionP02 : INotificacionService
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

file sealed class StubUsuarioP02 : ICurrentUserService
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
file sealed class CriterioFiscalPruebaV2 : ICriterioFiscal
{
    private readonly CriterioFiscalV1 _v1 = new();
    public int Version => 2;
    public decimal AlicuotaServicios => 10.5m;
    public string NombreAlicuotaServicios => "IVA 10.5%";

    public (decimal Neto, decimal Iva) SepararServicios(decimal bruto)
    {
        if (bruto <= 0m) return (0m, 0m);
        var neto = Math.Round(bruto / 1.105m, 2, MidpointRounding.AwayFromZero);
        return (neto, Math.Round(bruto - neto, 2, MidpointRounding.AwayFromZero));
    }

    public decimal TruncarAPesos(decimal valor) => Math.Round(valor, 0, MidpointRounding.AwayFromZero);
    public decimal CalcularRecargo(decimal totalCobrado, decimal brutoItems) => _v1.CalcularRecargo(totalCobrado, brutoItems);
    public decimal[] Prorratear(IReadOnlyList<decimal> pesos, decimal importe) => _v1.Prorratear(pesos, importe);
    public List<RecargoMedioPagoIva.ParteAlicuota> Repartir(IEnumerable<(decimal Porcentaje, decimal Bruto)> bases, decimal recargo) => _v1.Repartir(bases, recargo);
    public RecargoMedioPagoIva.Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => _v1.Discriminar(total, lineas);
    public List<RecargoMedioPagoIva.ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas) => _v1.ResumenPorAlicuota(total, lineas);
    public (decimal Neto, decimal Iva) AbsorberTruncado(decimal neto, decimal iva, decimal truncado) => _v1.AbsorberTruncado(neto, iva, truncado);
}

/// <summary>
/// P-02 / P5 (versionado fiscal por operación). Demuestra con los servicios REALES sobre SQLite en memoria que:
/// una operación histórica (versión 1) conserva importes de reporte de IVA, comprobante y devoluciones aunque el criterio
/// «vigente» cambie; una operación nueva nace con el criterio vigente; y el backfill/migración deja todo en versión 1 sin tocar importes.
/// La versión 2 de estos tests es solo de prueba: no existe en producción.
/// </summary>
public class FiscalVersionadoP02Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly VentaService _ventas;
    private readonly ReporteService _reportes;
    private static int _n = 52_000;

    public FiscalVersionadoP02Tests()
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

        var cajaService = new CajaService(_context, _mapper, NullLogger<CajaService>.Instance, new StubNotificacionP02());
        _ventas = new VentaService(
            _context, _mapper, NullLogger<VentaService>.Instance, null!, null!,
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context), new StubUsuarioP02(),
            null!, cajaService, null!, new TheBuryProject.Tests.Helpers.StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta());
        _reportes = new ReporteService(_context, NullLogger<ReporteService>.Instance);
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

    /// <summary>
    /// Venta facturada sembrada con importes fijos: 1 × 1.000 al 21 % (neto 826,45 + IVA 173,55) + armado de 121 (servicios) = 1.121.
    /// Con el criterio v1 los servicios son neto 100,00 + IVA 21,00 (21 %); con el de prueba v2 serían 109,50 + 11,50 (10,5 %).
    /// </summary>
    private async Task<(int VentaId, int FacturaId)> SeedVentaFacturadaAsync(Cliente cliente, Producto producto, int? version, DateTime fecha)
    {
        var venta = new Venta
        {
            Numero = $"VP02-{Interlocked.Increment(ref _n)}", ClienteId = cliente.Id, Estado = EstadoVenta.Facturada,
            TipoPago = TipoPago.Efectivo, FechaVenta = fecha, FechaFacturacion = fecha,
            Subtotal = 926.45m, IVA = 194.55m, Total = 1121m,
            AperturaCajaId = (await _context.AperturasCaja.FirstAsync()).Id,
            Detalles = new List<VentaDetalle>
            {
                new()
                {
                    ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 1000m, Subtotal = 1000m, SubtotalFinal = 1000m,
                    PorcentajeIVA = 21m, SubtotalNeto = 826.45m, SubtotalIVA = 173.55m,
                    SubtotalFinalNeto = 826.45m, SubtotalFinalIVA = 173.55m,
                    CostoUnitarioAlMomento = 400m, CostoTotalAlMomento = 400m,
                    TipoArmado = TipoServicioVenta.Armado1, ArmadoPrecioUnitario = 121m, ArmadoSubtotal = 121m
                }
            }
        };
        if (version.HasValue)
            venta.CriterioFiscalVersion = version.Value;
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        var factura = new Factura
        {
            VentaId = venta.Id, Numero = $"FP02-{Interlocked.Increment(ref _n)}", Tipo = TipoFactura.B, FechaEmision = fecha,
            Subtotal = 926.45m, IVA = 194.55m, Total = 1121m
        };
        _context.Facturas.Add(factura);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return (venta.Id, factura.Id);
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

    // ───────────────────────── (a) la operación histórica no varía ─────────────────────────

    [Fact]
    public async Task OperacionHistoricaV1_ConservaReporteYComprobante_AunqueCambieElCriterioVigente()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        var (ventaId, facturaId) = await SeedVentaFacturadaAsync(cliente, producto, version: null, dia); // default = v1

        // Valores esperados calculados a mano (criterio v1: servicios 21 %).
        var antesReporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
        var antesComprobante = await ComprobanteAsync(facturaId);
        Assert.Equal(1, antesReporte.VentasCantidad);
        Assert.Equal(926.45m, antesReporte.VentasNeto);
        Assert.Equal(194.55m, antesReporte.VentasIva);
        Assert.Equal(1121m, antesReporte.VentasTotal);
        var a21 = Assert.Single(antesReporte.VentasPorAlicuota);
        Assert.Equal((21m, 926.45m, 194.55m, 1121m), (a21.Porcentaje, a21.Neto, a21.Iva, a21.Total));

        using (CriterioFiscalRegistry.UsarParaPruebas(vigente: 2, new CriterioFiscalPruebaV2()))
        {
            // Cambió el criterio «vigente»: la venta histórica (versión 1) se relee con SUS reglas.
            Assert.Equal(2, CriterioFiscalRegistry.VersionVigente);
            var despuesReporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
            var despuesComprobante = await ComprobanteAsync(facturaId);

            Assert.Equal(antesReporte.VentasNeto, despuesReporte.VentasNeto);
            Assert.Equal(antesReporte.VentasIva, despuesReporte.VentasIva);
            Assert.Equal(antesReporte.VentasTotal, despuesReporte.VentasTotal);
            Assert.Equal(
                antesReporte.VentasPorAlicuota.Select(x => (x.Porcentaje, x.Neto, x.Iva, x.Total)),
                despuesReporte.VentasPorAlicuota.Select(x => (x.Porcentaje, x.Neto, x.Iva, x.Total)));

            Assert.Equal(antesComprobante.Totales.Total, despuesComprobante.Totales.Total);
            Assert.Equal(
                antesComprobante.ResumenAlicuotas.Select(x => (x.PorcentajeIVA, x.BaseImponible, x.IVA, x.Total)),
                despuesComprobante.ResumenAlicuotas.Select(x => (x.PorcentajeIVA, x.BaseImponible, x.IVA, x.Total)));
            var armado = despuesComprobante.Lineas.Single(l => l.ProductoCodigo == "ARMADO");
            Assert.Equal((21m, 100.00m, 21.00m), (armado.PorcentajeIVA, armado.SubtotalNeto, armado.IVA));
        }

        // Y tras volver al estado de producción sigue igual.
        var final = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
        Assert.Equal(antesReporte.VentasIva, final.VentasIva);
        Assert.Equal(1, (await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == ventaId)).CriterioFiscalVersion);
    }

    [Fact]
    public async Task OperacionNuevaConVersionVigente_UsaElCriterioVigente_YLaHistoricaNo()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var diaHistorico = DateTime.UtcNow.Date.AddDays(-2);
        var diaNuevo = DateTime.UtcNow.Date.AddDays(-1);
        var (_, facturaHistorica) = await SeedVentaFacturadaAsync(cliente, producto, version: 1, diaHistorico);
        int facturaNueva;

        using (CriterioFiscalRegistry.UsarParaPruebas(vigente: 2, new CriterioFiscalPruebaV2()))
        {
            // La operación nueva nace con la versión vigente (2).
            (_, facturaNueva) = await SeedVentaFacturadaAsync(cliente, producto, CriterioFiscalRegistry.VersionVigente, diaNuevo);

            var historica = await _reportes.GenerarReporteIvaAsync(FiltroDia(diaHistorico));
            var nueva = await _reportes.GenerarReporteIvaAsync(FiltroDia(diaNuevo));

            // Histórica: servicios al 21 % -> un solo bucket, IVA 194,55.
            Assert.Equal(194.55m, historica.VentasIva);
            Assert.Single(historica.VentasPorAlicuota);

            // Nueva: servicios al 10,5 % (121 / 1,105 = 109,50 + 11,50) -> dos buckets; el total sigue siendo 1.121.
            var b21 = nueva.VentasPorAlicuota.Single(x => x.Porcentaje == 21m);
            var b105 = nueva.VentasPorAlicuota.Single(x => x.Porcentaje == 10.5m);
            Assert.Equal((826.45m, 173.55m, 1000m), (b21.Neto, b21.Iva, b21.Total));
            Assert.Equal((109.50m, 11.50m, 121m), (b105.Neto, b105.Iva, b105.Total));
            Assert.Equal(1121m, nueva.VentasTotal);
            Assert.NotEqual(historica.VentasIva, nueva.VentasIva);

            // Comprobantes: cada uno con su criterio.
            var cHist = await ComprobanteAsync(facturaHistorica);
            var cNueva = await ComprobanteAsync(facturaNueva);
            Assert.Equal((21m, 100.00m, 21.00m), Servicios(cHist));
            Assert.Equal((10.5m, 109.50m, 11.50m), Servicios(cNueva));
        }

        static (decimal, decimal, decimal) Servicios(FacturaComprobanteViewModel c)
        {
            var l = c.Lineas.Single(x => x.ProductoCodigo == "ARMADO");
            return (l.PorcentajeIVA, l.SubtotalNeto, l.IVA);
        }
    }

    [Fact]
    public async Task CreateAsync_EstampaLaVersionVigente_YElRedondeoDeLaOperacionNuevaUsaEseCriterio()
    {
        var (cliente, producto) = await SeedBaseAsync(precio: 999.99m);
        VentaViewModel Vm() => new()
        {
            ClienteId = cliente.Id, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.Efectivo,
            Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 999.99m } }
        };

        // Producción (solo v1): total truncado a pesos (999,99 -> 999).
        var v1 = await _ventas.CreateAsync(Vm());
        var persistidaV1 = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == v1.Id);
        Assert.Equal(1, persistidaV1.CriterioFiscalVersion);
        Assert.Equal(999m, persistidaV1.Total);

        // Con una versión vigente distinta (solo de prueba): la operación NUEVA usa su redondeo (999,99 -> 1.000) y la estampa.
        using (CriterioFiscalRegistry.UsarParaPruebas(vigente: 2, new CriterioFiscalPruebaV2()))
        {
            var v2 = await _ventas.CreateAsync(Vm());
            var persistidaV2 = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == v2.Id);
            Assert.Equal(2, persistidaV2.CriterioFiscalVersion);
            Assert.Equal(1000m, persistidaV2.Total);

            // La venta histórica NO se recalcula: sigue en 999 / versión 1.
            var historica = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == v1.Id);
            Assert.Equal((1, 999m), (historica.CriterioFiscalVersion, historica.Total));
        }
    }

    [Fact]
    public async Task OperacionConVersionNoRegistrada_FallaEnVezDeCalcularConOtrasReglas()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        await SeedVentaFacturadaAsync(cliente, producto, version: 7, dia);

        // Con una versión desconocida se prefiere fallar a recalcular un importe histórico con otro criterio.
        Assert.Throws<InvalidOperationException>(() => CriterioFiscalRegistry.Resolver(7));
        await Assert.ThrowsAnyAsync<Exception>(() => _reportes.GenerarReporteIvaAsync(FiltroDia(dia)));
    }

    // ───────────────────────── (b) backfill / migración ─────────────────────────

    [Fact]
    public async Task Backfill_VentaExistenteSinVersion_QuedaEnV1_YNoCambiaNingunImporte()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        var (ventaId, facturaId) = await SeedVentaFacturadaAsync(cliente, producto, version: null, dia);
        var reporteAntes = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
        var comprobanteAntes = await ComprobanteAsync(facturaId);

        // La columna se agrega NOT NULL con DEFAULT 1: toda fila existente (anterior a P-02) queda en v1 sin que ningún
        // UPDATE toque sus importes. Se verifica el DEFAULT del esquema y que la fila queda en v1 con los mismos importes.
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT dflt_value, \"notnull\" FROM pragma_table_info('Ventas') WHERE name = 'CriterioFiscalVersion'";
            await using var rd = await cmd.ExecuteReaderAsync();
            Assert.True(await rd.ReadAsync());
            Assert.Equal("1", rd.GetString(0));
            Assert.Equal(1, rd.GetInt32(1));
        }

        _context.ChangeTracker.Clear();
        var venta = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == ventaId);
        Assert.Equal(CriterioFiscalRegistry.VersionInicial, venta.CriterioFiscalVersion);
        Assert.Equal((926.45m, 194.55m, 1121m), (venta.Subtotal, venta.IVA, venta.Total));
        var reporteDespues = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
        Assert.Equal((reporteAntes.VentasNeto, reporteAntes.VentasIva, reporteAntes.VentasTotal),
                     (reporteDespues.VentasNeto, reporteDespues.VentasIva, reporteDespues.VentasTotal));
        var comprobanteDespues = await ComprobanteAsync(facturaId);
        Assert.Equal(comprobanteAntes.Totales.Total, comprobanteDespues.Totales.Total);
    }

    [Fact]
    public void Migracion_AgregaSoloLaColumna_ConDefault1_SinUpdateNiTocarImportes()
    {
        var migracion = new TheBuryProyect.Migrations.P02CriterioFiscalVersion();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(TheBuryProyect.Migrations.P02CriterioFiscalVersion)
            .GetMethod("Up", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(migracion, new object[] { builder });

        // Única operación: AddColumn Ventas.CriterioFiscalVersion int NOT NULL DEFAULT 1 (backfill = default de la columna).
        var op = Assert.IsType<AddColumnOperation>(Assert.Single(builder.Operations));
        Assert.Equal("Ventas", op.Table);
        Assert.Equal("CriterioFiscalVersion", op.Name);
        Assert.False(op.IsNullable);
        Assert.Equal(1, op.DefaultValue);
        Assert.Equal(CriterioFiscalRegistry.VersionInicial, (int)op.DefaultValue!);
    }

    // ───────────────────────── v1 == reglas actuales (byte a byte) ─────────────────────────

    [Fact]
    public void V1_DelegaExactamenteEnLasReglasActuales()
    {
        var v1 = CriterioFiscalRegistry.Resolver(1);
        Assert.Same(v1, CriterioFiscalRegistry.Vigente);
        Assert.Equal(1, CriterioFiscalRegistry.VersionVigente);

        foreach (var valor in new[] { 0m, 0.99m, 999.99m, 1000m, 1234.50m, 9999.995m })
            Assert.Equal(Math.Floor(valor), v1.TruncarAPesos(valor));

        foreach (var bruto in new[] { 0m, -5m, 1m, 121m, 1000.01m })
            Assert.Equal(ServiciosVentaIva.Separar(bruto), v1.SepararServicios(bruto));
        Assert.Equal(ServiciosVentaIva.Porcentaje, v1.AlicuotaServicios);
        Assert.Equal(ServiciosVentaIva.NombreAlicuota, v1.NombreAlicuotaServicios);

        var lineas = new List<(decimal, decimal, decimal, decimal)> { (21m, 1000m, 826.45m, 173.55m), (10.5m, 500m, 452.51m, 47.49m) };
        foreach (var total in new[] { 1500m, 1545m, 1425m, 1499m })
        {
            var esperadoD = RecargoMedioPagoIva.Discriminar(total, lineas);
            var actualD = v1.Discriminar(total, lineas);
            Assert.Equal((esperadoD.Truncado, esperadoD.Neto, esperadoD.Iva), (actualD.Truncado, actualD.Neto, actualD.Iva));
            Assert.Equal(esperadoD.Partes, actualD.Partes);
            Assert.Equal(RecargoMedioPagoIva.ResumenPorAlicuota(total, lineas), v1.ResumenPorAlicuota(total, lineas)); // List<record>: compara elemento a elemento
        }

        Assert.Equal(RecargoMedioPagoIva.Prorratear(new[] { 1000m, 500m }, 45m), v1.Prorratear(new[] { 1000m, 500m }, 45m));
        Assert.Equal(RecargoMedioPagoIva.CalcularRecargo(1545m, 1500m), v1.CalcularRecargo(1545m, 1500m));
        Assert.Equal(RecargoMedioPagoIva.AbsorberTruncado(100m, 21m, 0.99m), v1.AbsorberTruncado(100m, 21m, 0.99m));
    }
}
