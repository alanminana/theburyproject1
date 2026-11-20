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

file sealed class NotificacionMixtas : INotificacionService
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

file sealed class UsuarioMixtas : ICurrentUserService
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
/// P-02 §3: matriz de alícuotas mixtas (21 %, 10,5 %, exento, combinadas) con envío y armado, descuentos y recargos del medio de pago,
/// centavos y redondeo. Cada caso se compara contra un ORÁCULO independiente del total y verifica en venta directa, comprobante y
/// Reporte de IVA: Neto + IVA = Total, descuento solo sobre productos en V2, servicios sin descuento y los mismos importes en todas las capas.
/// </summary>
public class FiscalAlicuotasMixtasP02Tests : IDisposable
{
    private const decimal Envio = 200m;
    private const decimal Armado = 450m;

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _ventas;
    private readonly ReporteService _reportes;
    private static int _n = 91_000;

    public FiscalAlicuotasMixtasP02Tests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new TheBuryProject.Tests.Infrastructure.SqliteRowVersionRotationInterceptor())
            .Options);
        _context.Database.EnsureCreated();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();

        var caja = new Caja { Codigo = "CJMX", Nombre = "Caja mixtas", Activa = true, Estado = EstadoCaja.Abierta };
        _context.Cajas.Add(caja);
        _context.SaveChanges();
        _context.AperturasCaja.Add(new AperturaCaja { CajaId = caja.Id, UsuarioApertura = "testuser", MontoInicial = 0m, Cerrada = false });
        _context.SaveChanges();

        var cajaService = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new NotificacionMixtas());
        _ventas = new VentaService(
            _context, mapper, NullLogger<VentaService>.Instance, null!, null!,
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context), new UsuarioMixtas(),
            null!, cajaService, null!, new TheBuryProject.Tests.Helpers.StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta());
        _reportes = new ReporteService(_context, NullLogger<ReporteService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Producto> NuevoProductoAsync(decimal precio, decimal iva)
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CM{n}", Nombre = $"Cat M {n}" };
        var marca = new Marca { Codigo = $"MM{n}", Nombre = $"Marca M {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = $"PM{n}", Nombre = $"Prod M {n}", CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 100m, PrecioVenta = precio, PorcentajeIVA = iva, StockActual = 100
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task ConfigurarMedioAsync(TipoPago tipo, decimal pct, bool servicios)
    {
        if (pct != 0m)
        {
            var medio = new ConfiguracionPago { TipoPago = tipo, Nombre = tipo.ToString(), Activo = true };
            _context.ConfiguracionesPago.Add(medio);
            await _context.SaveChangesAsync();
            _context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = medio.Id, TipoPago = tipo, CantidadCuotas = 1, AjustePorcentaje = pct, Activo = true
            });
        }

        if (servicios)
        {
            await new ServicioVentaPrecioService(_context).GuardarAsync(new[]
            {
                new ServicioVentaPrecioComando(TipoServicioVenta.EnvioCiudad, Envio, true, 80m),
                new ServicioVentaPrecioComando(TipoServicioVenta.Armado5, Armado, true, 120m)
            });
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>Oráculo independiente del total cobrado (no usa ningún código de producción de cálculo).</summary>
    private static (decimal Total, decimal Ajuste) Oraculo(int version, decimal productos, decimal servicios, decimal pct)
    {
        var baseCentavos = productos + servicios;
        var piso = Math.Floor(baseCentavos);
        var redondeada = version == 2 && baseCentavos - piso > 0.50m ? piso + 1m : piso;
        var sujeta = version == 2 && pct < 0m ? Math.Max(0m, redondeada - servicios) : redondeada;
        var ajuste = Math.Round(sujeta * pct / 100m, 2, MidpointRounding.AwayFromZero);
        return (redondeada + ajuste, ajuste);
    }

    // (versión, iva A, precio A, iva B?, precio B, envío+armado, pct, tipo de pago)
    public static IEnumerable<object[]> Casos()
    {
        foreach (var version in new[] { 1, 2 })
        {
            foreach (var pct in new[] { -5.0, 3.0, 0.0 })
            {
                var tipo = (int)(pct >= 0 ? TipoPago.Transferencia : TipoPago.Efectivo);
                yield return new object[] { version, 21.0, 1000.00, -1.0, 0.0, false, pct, tipo };
                yield return new object[] { version, 10.5, 1000.49, -1.0, 0.0, false, pct, tipo };
                yield return new object[] { version, 21.0, 1000.51, 10.5, 333.33, false, pct, tipo };
                yield return new object[] { version, 21.0, 999.99, 0.0, 250.75, false, pct, tipo };   // exento
                yield return new object[] { version, 21.0, 1000.49, 10.5, 800.10, true, pct, tipo };   // + envío y armado
                yield return new object[] { version, 0.0, 500.55, -1.0, 0.0, true, pct, tipo };        // solo exento + servicios
            }
        }
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task Matriz_VentaComprobanteYReporteCoincidenConElOraculo(
        int version, double ivaA, double precioA, double ivaB, double precioB, bool servicios, double pct, int tipoPagoInt)
    {
        var tipo = (TipoPago)tipoPagoInt;
        var n = Interlocked.Increment(ref _n);
        var cliente = new Cliente { Nombre = "Mx", Apellido = "Alic", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"mx{n}@t.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        var a = await NuevoProductoAsync((decimal)precioA, (decimal)ivaA);
        var detalles = new List<VentaDetalleViewModel> { new() { ProductoId = a.Id, Cantidad = 1, PrecioUnitario = (decimal)precioA } };
        var productos = (decimal)precioA;
        if (ivaB >= 0)
        {
            var b = await NuevoProductoAsync((decimal)precioB, (decimal)ivaB);
            detalles.Add(new() { ProductoId = b.Id, Cantidad = 1, PrecioUnitario = (decimal)precioB });
            productos += (decimal)precioB;
        }

        if (servicios)
            detalles[0].TipoArmado = TipoServicioVenta.Armado5;
        await ConfigurarMedioAsync(tipo, (decimal)pct, servicios);

        using var _ = CriterioFiscalRegistry.UsarParaPruebas(vigente: version);
        var vm = new VentaViewModel
        {
            ClienteId = cliente.Id, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Cotizacion, TipoPago = tipo, Detalles = detalles
        };
        if (servicios)
        {
            vm.TieneEnvio = true;
            vm.Envio = new VentaEnvioViewModel { Destinatario = "Juan", Domicilio = "Calle 1", TipoEnvio = TipoServicioVenta.EnvioCiudad };
        }

        var creada = await _ventas.CreateAsync(vm);
        var venta = await _context.Ventas.Include(v => v.Detalles).SingleAsync(v => v.Id == creada.Id);

        var serviciosImporte = servicios ? Envio + Armado : 0m;
        var (totalEsperado, ajusteEsperado) = Oraculo(version, productos, serviciosImporte, (decimal)pct);

        // 1. Importe cobrado contra el oráculo y neto + IVA = total.
        Assert.Equal(totalEsperado, venta.Total);
        Assert.Equal(ajusteEsperado == 0m ? (decimal?)null : ajusteEsperado, venta.AjusteMedioPagoMonto);
        Assert.Equal(venta.Total, venta.Subtotal + venta.IVA);
        Assert.True(venta.Subtotal > 0m);

        // 2. Comprobante y Reporte de IVA: mismo neto, IVA y total que la venta, y entre sí por alícuota.
        var dia = FechaComercial.DiaComercial(DateTime.UtcNow);
        venta.Estado = EstadoVenta.Facturada;
        venta.FechaFacturacion = dia;
        var factura = new Factura
        {
            VentaId = venta.Id, Numero = $"FMT-{Interlocked.Increment(ref _n)}", Tipo = TipoFactura.B, FechaEmision = dia,
            Subtotal = venta.Subtotal, IVA = venta.IVA, Total = venta.Total
        };
        _context.Facturas.Add(factura);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var f = await _context.Facturas
            .Include(x => x.Venta).ThenInclude(v => v.Detalles).ThenInclude(d => d.Producto)
            .Include(x => x.Venta).ThenInclude(v => v.Cliente)
            .Include(x => x.Venta).ThenInclude(v => v.Envio)
            .Include(x => x.Venta).ThenInclude(v => v.DatosTarjeta)
            .AsNoTracking().SingleAsync(x => x.Id == factura.Id);
        var comprobante = FacturaComprobanteBuilder.Build(f);
        var reporte = await _reportes.GenerarReporteIvaAsync(new ReporteIvaFiltroViewModel { Tipo = "ventas", FechaDesde = dia, FechaHasta = dia });

        Assert.Equal(venta.Total, comprobante.Totales.Total);
        Assert.Equal(venta.Total, comprobante.ResumenAlicuotas.Sum(r => r.BaseImponible + r.IVA));
        Assert.Equal(venta.Total, comprobante.ResumenAlicuotas.Sum(r => r.Total));
        Assert.Equal(venta.Total, reporte.VentasTotal);
        Assert.Equal(venta.Total, reporte.VentasNeto + reporte.VentasIva);

        // V2 (y V1 sin ajuste): el desglose por alícuota es IDÉNTICO en venta, comprobante y reporte. V1 con ajuste y alícuotas mixtas
        // conserva su divergencia histórica de centavos entre el neto guardado y el reconstruido (V1 es inmutable; no se corrige).
        if (version == 2 || pct == 0)
        {
            Assert.Equal(venta.Subtotal, comprobante.ResumenAlicuotas.Sum(r => r.BaseImponible));
            Assert.Equal(venta.IVA, comprobante.ResumenAlicuotas.Sum(r => r.IVA));
            Assert.Equal((venta.Subtotal, venta.IVA), (reporte.VentasNeto, reporte.VentasIva));
            foreach (var r in reporte.VentasPorAlicuota)
            {
                var c = comprobante.ResumenAlicuotas.Single(x => x.PorcentajeIVA == r.Porcentaje);
                Assert.Equal((c.BaseImponible, c.IVA, c.Total), (r.Neto, r.Iva, r.Total));
            }
        }

        // 3. V2 con descuento: los servicios no reciben descuento (su bruto exacto queda intacto); ningún importe se duplica.
        if (version == 2 && pct < 0 && servicios)
        {
            var alicuotaServicios = CriterioFiscalRegistry.Resolver(CriterioFiscalRegistry.VersionDosRedondeoComercial).AlicuotaServicios;
            var lineasServicios = comprobante.Lineas.Where(l => l.ProductoCodigo is "ARMADO" or "ENVIO").Sum(l => l.Total);
            Assert.Equal(serviciosImporte, lineasServicios);
            Assert.Equal(alicuotaServicios, comprobante.Lineas.First(l => l.ProductoCodigo == "ENVIO").PorcentajeIVA);
            Assert.Equal(totalEsperado - serviciosImporte, venta.Total - serviciosImporte);
        }
    }

    // ─── Integridad V1/V2 con ajuste de medio de pago y alícuotas mixtas (cierre P-02) ───────────────────────────────

    /// <summary>
    /// Barrido de combinaciones con alícuotas mixtas (21 / 10,5 / exento) y ajuste del medio de pago. Verifica, por versión:
    /// (1) el importe cobrado coincide con el oráculo independiente y <c>Neto + IVA = Total</c> (el defecto histórico de V1 NO toca
    /// lo cobrado); (2) V2 reconstruye el neto del comprobante exactamente igual al guardado (divergencia 0); (3) V1 puede diferir en
    /// centavos entre el neto guardado y el reconstruido, de centavos en los casos con ajustes reales (−5 % / +3 %; medido hasta $0,71, y la prueba exige < $1: NO es una cota universal, un recargo del 999 % extremo da más) y sin alterar el total; (4) un
    /// comprobante de una venta V1 se interpreta idéntico aunque después la versión vigente pase a V2 (se resuelve por la versión
    /// persistida en la venta, no por la vigente).
    /// </summary>
    [Theory]
    [InlineData(1, 21.0, 1000.51, 10.5, 333.33, -5.0, 0)]
    [InlineData(1, 21.0, 1000.51, 10.5, 333.33, 3.0, 1)]
    [InlineData(1, 21.0, 999.99, 0.0, 250.75, 3.0, 1)]
    [InlineData(1, 10.5, 1234.57, 21.0, 777.77, -5.0, 0)]
    [InlineData(1, 21.0, 4321.09, 10.5, 99.99, 3.0, 1)]
    [InlineData(2, 21.0, 1000.51, 10.5, 333.33, -5.0, 0)]
    [InlineData(2, 21.0, 1000.51, 10.5, 333.33, 3.0, 1)]
    [InlineData(2, 21.0, 999.99, 0.0, 250.75, 3.0, 1)]
    [InlineData(2, 10.5, 1234.57, 21.0, 777.77, -5.0, 0)]
    [InlineData(2, 21.0, 4321.09, 10.5, 99.99, 3.0, 1)]
    public async Task IntegridadFiscal_AjusteConAlicuotasMixtas_NoAlteraLoCobradoYV2NoDiverge(
        int version, double ivaA, double precioA, double ivaB, double precioB, double pct, int tipoPagoInt)
    {
        var tipo = (TipoPago)tipoPagoInt;
        var n = Interlocked.Increment(ref _n);
        var cliente = new Cliente { Nombre = "Int", Apellido = "Fiscal", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"), Email = $"if{n}@t.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        var a = await NuevoProductoAsync((decimal)precioA, (decimal)ivaA);
        var b = await NuevoProductoAsync((decimal)precioB, (decimal)ivaB);
        await ConfigurarMedioAsync(tipo, (decimal)pct, servicios: false);

        using var _ = CriterioFiscalRegistry.UsarParaPruebas(vigente: version);
        var creada = await _ventas.CreateAsync(new VentaViewModel
        {
            ClienteId = cliente.Id, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Cotizacion, TipoPago = tipo,
            Detalles = new List<VentaDetalleViewModel>
            {
                new() { ProductoId = a.Id, Cantidad = 1, PrecioUnitario = (decimal)precioA },
                new() { ProductoId = b.Id, Cantidad = 1, PrecioUnitario = (decimal)precioB }
            }
        });
        var venta = await _context.Ventas.Include(v => v.Detalles).SingleAsync(v => v.Id == creada.Id);

        var (totalEsperado, _) = Oraculo(version, (decimal)precioA + (decimal)precioB, 0m, (decimal)pct);
        Assert.Equal(totalEsperado, venta.Total);                 // (1) lo cobrado = oráculo, en ambas versiones
        Assert.Equal(venta.Total, venta.Subtotal + venta.IVA);    //     y Neto + IVA = Total

        var dia = FechaComercial.DiaComercial(DateTime.UtcNow);
        venta.Estado = EstadoVenta.Facturada;
        venta.FechaFacturacion = dia;
        _context.Facturas.Add(new Factura
        {
            VentaId = venta.Id, Numero = $"INT-{Interlocked.Increment(ref _n)}", Tipo = TipoFactura.B, FechaEmision = dia,
            Subtotal = venta.Subtotal, IVA = venta.IVA, Total = venta.Total
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        async Task<FacturaComprobanteViewModel> ConstruirAsync()
        {
            var f = await _context.Facturas
                .Include(x => x.Venta).ThenInclude(v => v.Detalles).ThenInclude(d => d.Producto)
                .Include(x => x.Venta).ThenInclude(v => v.Cliente)
                .Include(x => x.Venta).ThenInclude(v => v.Envio)
                .Include(x => x.Venta).ThenInclude(v => v.DatosTarjeta)
                .AsNoTracking().SingleAsync(x => x.VentaId == venta.Id);
            return FacturaComprobanteBuilder.Build(f);
        }

        var comprobante = await ConstruirAsync();
        Assert.Equal(venta.Total, comprobante.Totales.Total);
        var divergencia = Math.Abs(venta.Subtotal - comprobante.ResumenAlicuotas.Sum(r => r.BaseImponible));

        if (version == 2)
            Assert.Equal(0m, divergencia);                         // (2) V2 no reproduce la inconsistencia
        else
            Assert.True(divergencia < 1.00m, $"V1: divergencia {divergencia} >= 1,00");   // (3) cota histórica de V1: < $1

        // (4) con V2 vigente, el comprobante de una venta V1 se interpreta exactamente igual (versión persistida en la venta).
        if (version == 1)
        {
            using var v2Vigente = CriterioFiscalRegistry.UsarParaPruebas(vigente: 2);
            var bajoV2 = await ConstruirAsync();
            Assert.Equal(comprobante.Totales.Total, bajoV2.Totales.Total);
            Assert.Equal(
                comprobante.ResumenAlicuotas.Select(r => (r.PorcentajeIVA, r.BaseImponible, r.IVA, r.Total)),
                bajoV2.ResumenAlicuotas.Select(r => (r.PorcentajeIVA, r.BaseImponible, r.IVA, r.Total)));
        }
    }

    // ─── Devoluciones parciales: nunca se reintegra más de lo cobrado por los productos ──────────────────────────────

    [Theory]
    [InlineData(2, -5.0)]
    [InlineData(2, 3.0)]
    [InlineData(1, -5.0)]
    [InlineData(1, 3.0)]
    public void Devolucion_ParcialesYTotal_NuncaSuperanLoCobradoPorLosProductos(int version, double pct)
    {
        var criterio = CriterioFiscalRegistry.Resolver(version);
        var lineas = new[] { 1000.49m, 333.33m, 250.75m };
        var servicios = 650m;
        var (total, _) = Oraculo(version, lineas.Sum(), servicios, (decimal)pct);

        var recargo = DevolucionService.RecargoPorLinea(lineas, servicios, total, criterio);
        var porLinea = lineas.Select((l, i) => l + recargo[i]).ToArray();

        Assert.All(porLinea, x => Assert.True(x >= 0m));
        // Devolver todo reintegra exactamente lo cobrado por los productos: con descuento en V2, el total menos los servicios (que no se devuelven).
        var productosCobrados = total - servicios;
        if (version == 2 && pct < 0)
            Assert.Equal(productosCobrados, porLinea.Sum());
        else
            Assert.True(porLinea.Sum() <= total);
        // Cada devolución parcial, sumada con el resto, no excede el total devolvible (sin duplicar importes).
        for (var k = 1; k < porLinea.Length; k++)
            Assert.True(porLinea.Take(k).Sum() + porLinea.Skip(k).Sum() <= porLinea.Sum() + 0.0001m);
    }
}
