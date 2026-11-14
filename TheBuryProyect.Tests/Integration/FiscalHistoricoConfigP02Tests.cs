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

/// <summary>
/// P-02 / P5, auditoría de valores de configuración que podrían alterar retroactivamente una operación ya emitida.
/// Hallazgo confirmado: la alícuota de los servicios (armados/envío) salía de <c>Iva:PorcentajeDefault</c> (configuración global
/// modificable) tanto al calcular como al RELEER (reporte de IVA, comprobante). Corrección: cada venta nueva congela su alícuota
/// (<see cref="Venta.AlicuotaServiciosSnapshot"/>) y se relee con ella. Las ventas anteriores al snapshot (null) conservan el
/// comportamiento previo: su tratamiento es una decisión pendiente (docs/p02-versionado-fiscal-diseno.md §8).
/// Los demás insumos de las lecturas (alícuota por línea, importes, recargo) ya están persistidos por operación: se verifica.
/// </summary>
public class FiscalHistoricoConfigP02Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ReporteService _reportes;
    private readonly VentaService _ventas;
    private static int _n = 61_000;

    public FiscalHistoricoConfigP02Tests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new TheBuryProject.Tests.Infrastructure.SqliteRowVersionRotationInterceptor())
            .Options);
        _context.Database.EnsureCreated();
        _context.Cajas.Add(new Caja { Codigo = "CJP02B", Nombre = "Caja P02b", Activa = true, Estado = EstadoCaja.Abierta });
        _context.SaveChanges();
        _context.AperturasCaja.Add(new AperturaCaja { CajaId = _context.Cajas.First().Id, UsuarioApertura = "testuser", MontoInicial = 0m, Cerrada = false });
        _context.SaveChanges();
        _reportes = new ReporteService(_context, NullLogger<ReporteService>.Instance);
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var cajaService = new CajaService(_context, mapper, NullLogger<CajaService>.Instance, new StubNotificacionP02b());
        _ventas = new VentaService(
            _context, mapper, NullLogger<VentaService>.Instance, null!, null!,
            new FinancialCalculationService(), new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context), new StubUsuarioP02b(),
            null!, cajaService, null!, new TheBuryProject.Tests.Helpers.StubContratoVentaCreditoService(), new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<(Cliente Cliente, Producto Producto)> SeedBaseAsync()
    {
        var n = Interlocked.Increment(ref _n);
        var cat = new Categoria { Codigo = $"CQ{n}", Nombre = $"Cat P02b {n}" };
        var marca = new Marca { Codigo = $"MQ{n}", Nombre = $"Marca P02b {n}" };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "P02b", TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"),
            Email = $"p02b{n}@test.com", NivelRiesgo = NivelRiesgoCredito.AprobadoTotal
        };
        var producto = new Producto
        {
            Codigo = $"PQ{n}", Nombre = $"Prod {n}", CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 400m, PrecioVenta = 1000m, PorcentajeIVA = 21m, StockActual = 100
        };
        _context.Clientes.Add(cliente);
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return (cliente, producto);
    }

    /// <summary>1 × 1.000 al 21 % (826,45 + 173,55) + armado de 121 (servicios) = 1.121; con servicios al 21 %: 100,00 + 21,00.</summary>
    private async Task<(int VentaId, int FacturaId)> SeedVentaAsync(Cliente cliente, Producto producto, DateTime fecha,
        int version = 1, decimal? alicuotaServicios = null)
    {
        var venta = new Venta
        {
            Numero = $"VQ02-{Interlocked.Increment(ref _n)}", ClienteId = cliente.Id, Estado = EstadoVenta.Facturada,
            TipoPago = TipoPago.Efectivo, FechaVenta = fecha, FechaFacturacion = fecha,
            Subtotal = 926.45m, IVA = 194.55m, Total = 1121m,
            CriterioFiscalVersion = version, AlicuotaServiciosSnapshot = alicuotaServicios,
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
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        var factura = new Factura
        {
            VentaId = venta.Id, Numero = $"FQ02-{Interlocked.Increment(ref _n)}", Tipo = TipoFactura.B, FechaEmision = fecha,
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

    private static (decimal Pct, decimal Neto, decimal Iva) Armado(FacturaComprobanteViewModel c)
    {
        var l = c.Lineas.Single(x => x.ProductoCodigo == "ARMADO");
        return (l.PorcentajeIVA, l.SubtotalNeto, l.IVA);
    }

    // ───────────────────────── (1) el cambio de configuración NO altera operaciones con snapshot ─────────────────────────

    [Fact]
    public async Task CambiarAlicuotaConfigurada_NoAlteraReporteNiComprobante_DeUnaVentaConSnapshot()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        var (_, facturaId) = await SeedVentaAsync(cliente, producto, dia, alicuotaServicios: 21m);

        var antesReporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
        var antesComprobante = await ComprobanteAsync(facturaId);
        Assert.Equal((926.45m, 194.55m, 1121m), (antesReporte.VentasNeto, antesReporte.VentasIva, antesReporte.VentasTotal));
        Assert.Equal((21m, 100.00m, 21.00m), Armado(antesComprobante));

        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m))
        {
            Assert.Equal(10.5m, ProductoIvaResolver.PorcentajeDefault); // la configuración cambió
            var reporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
            var comprobante = await ComprobanteAsync(facturaId);

            Assert.Equal((antesReporte.VentasNeto, antesReporte.VentasIva, antesReporte.VentasTotal),
                         (reporte.VentasNeto, reporte.VentasIva, reporte.VentasTotal));
            Assert.Equal(antesReporte.VentasPorAlicuota.Select(x => (x.Porcentaje, x.Neto, x.Iva, x.Total)),
                         reporte.VentasPorAlicuota.Select(x => (x.Porcentaje, x.Neto, x.Iva, x.Total)));
            Assert.Equal(antesComprobante.ResumenAlicuotas.Select(x => (x.PorcentajeIVA, x.BaseImponible, x.IVA, x.Total)),
                         comprobante.ResumenAlicuotas.Select(x => (x.PorcentajeIVA, x.BaseImponible, x.IVA, x.Total)));
            Assert.Equal((21m, 100.00m, 21.00m), Armado(comprobante));
        }
    }

    [Fact]
    public async Task DosVentasConDistintaAlicuotaCongelada_ConviveEnElMismoReporte_SinMezclarse()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        await SeedVentaAsync(cliente, producto, dia, alicuotaServicios: 21m);
        await SeedVentaAsync(cliente, producto, dia, alicuotaServicios: 10.5m);

        // Cada venta se relee con su propia alícuota congelada, sin depender de la configuración actual (ni de la otra venta).
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(27m))
        {
            var reporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));
            Assert.Equal(2, reporte.VentasCantidad);
            var por = reporte.VentasPorAlicuota.ToDictionary(x => x.Porcentaje);
            Assert.False(por.ContainsKey(27m));
            // 21 %: línea 826,45/173,55 ×2 + servicios de la venta a 21 % (100,00/21,00); 10,5 %: servicios de la otra (109,50/11,50).
            Assert.Equal((2 * 826.45m + 100.00m, 2 * 173.55m + 21.00m), (por[21m].Neto, por[21m].Iva));
            Assert.Equal((109.50m, 11.50m), (por[10.5m].Neto, por[10.5m].Iva));
        }
    }

    [Fact]
    public void ReleerVentaConSnapshot_UsaSuAlicuota_YSuNombre()
    {
        var venta = new Venta { CriterioFiscalVersion = 1, AlicuotaServiciosSnapshot = 10.5m };
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(27m))
        {
            var c = CriterioFiscalRegistry.Resolver(venta);
            Assert.Equal(1, c.Version);
            Assert.Equal(10.5m, c.AlicuotaServicios);
            Assert.Equal(ServiciosVentaIva.NombreDe(10.5m), c.NombreAlicuotaServicios);
            Assert.Equal((109.50m, 11.50m), c.SepararServicios(121m));
            // El resto de las reglas (redondeo) sigue siendo el de la versión.
            Assert.Equal(999m, c.TruncarAPesos(999.99m));
        }
    }

    // ───────────────────────── (2) operaciones nuevas congelan la alícuota ─────────────────────────

    [Fact]
    public void Estampar_CongelaLaAlicuotaVigenteDeLaConfiguracionEnEseMomento()
    {
        var v = new Venta();
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m))
            CriterioFiscalRegistry.Estampar(v);

        Assert.Equal(1, v.CriterioFiscalVersion);
        Assert.Equal(10.5m, v.AlicuotaServiciosSnapshot);

        // Cambios posteriores de configuración no afectan lo ya estampado.
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(21m))
            Assert.Equal(10.5m, CriterioFiscalRegistry.Resolver(v).AlicuotaServicios);
    }

    [Fact]
    public async Task CreateAsync_CongelaLaAlicuotaDeServicios_YUnCambioPosteriorDeConfiguracionNoAlteraLaVenta()
    {
        var (cliente, producto) = await SeedBaseAsync();
        decimal totalAntes;
        int ventaId;
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m))
        {
            var vm = new VentaViewModel
            {
                ClienteId = cliente.Id, FechaVenta = DateTime.UtcNow, Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.Efectivo,
                Detalles = new List<VentaDetalleViewModel> { new() { ProductoId = producto.Id, Cantidad = 1, PrecioUnitario = 1000m } }
            };
            var creada = await _ventas.CreateAsync(vm);
            ventaId = creada.Id;
        }

        var persistida = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == ventaId);
        Assert.Equal(10.5m, persistida.AlicuotaServiciosSnapshot); // estampada al crear con la configuración de ese momento
        totalAntes = persistida.Total;

        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(21m))
            Assert.Equal(10.5m, CriterioFiscalRegistry.Resolver(persistida).AlicuotaServicios); // y no la sigue
        Assert.Equal(totalAntes, persistida.Total);
    }

    // ───────────────────────── (3) RIESGO RESIDUAL documentado: ventas anteriores al snapshot ─────────────────────────

    [Fact]
    public async Task VentaAnteriorAlSnapshot_SinAlicuotaCongelada_SigueDependiendoDeLaConfiguracion_DecisionPendiente()
    {
        // Caracterización del estado ACTUAL, no un comportamiento deseado: una venta sin snapshot (null) se relee con la
        // alícuota de la versión, que lee la configuración. Cómo tratar estas ventas (backfill 21 % vs configuración vigente
        // al momento de la migración vs dejarlas) es una decisión sensible pendiente de aprobación: ver §8 del diseño.
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        var (_, facturaId) = await SeedVentaAsync(cliente, producto, dia, alicuotaServicios: null);

        Assert.Equal((21m, 100.00m, 21.00m), Armado(await ComprobanteAsync(facturaId)));
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m))
            Assert.Equal((10.5m, 109.50m, 11.50m), Armado(await ComprobanteAsync(facturaId)));
    }

    // ───────────────────────── (4) otras lecturas no dependen de la configuración ─────────────────────────

    [Fact]
    public async Task LineasProductoDelReporte_NoDependenDeLaAlicuotaConfigurada()
    {
        // La alícuota por línea es el snapshot VentaDetalle.PorcentajeIVA: ni el reporte ni el comprobante la toman de la configuración.
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        var (_, facturaId) = await SeedVentaAsync(cliente, producto, dia, alicuotaServicios: 21m);
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m))
        {
            var comprobante = await ComprobanteAsync(facturaId);
            var linea = comprobante.Lineas.Single(l => l.ProductoCodigo != "ARMADO");
            Assert.Equal(21m, linea.PorcentajeIVA);
            Assert.Equal(826.45m, linea.SubtotalNeto);
        }
    }

    [Fact]
    public void RecargoMedioPago_YDevolucion_NoDependenDeLaAlicuotaDeServicios()
    {
        // El reparto del recargo por línea (devolución) y el neto del recargo de débito usan solo alícuotas por línea y
        // montos persistidos: la configuración no interviene.
        var criterio = CriterioFiscalRegistry.Resolver(1);
        var antes = criterio.Prorratear(new[] { 1000m, 500m }, 45m);
        var repartoAntes = criterio.Repartir(new[] { (21m, 1000m), (10.5m, 500m) }, 45m);
        using (ProductoIvaResolver.UsarPorcentajeDefaultParaPruebas(10.5m))
        {
            Assert.Equal(antes, criterio.Prorratear(new[] { 1000m, 500m }, 45m));
            Assert.Equal(repartoAntes, criterio.Repartir(new[] { (21m, 1000m), (10.5m, 500m) }, 45m));
        }
    }

    // ───────────────────────── (5) reporte de IVA endurecido ante versiones inválidas ─────────────────────────

    [Theory]
    // 0 no se prueba por inserción: EF lo toma como «sin valor» (HasDefaultValue(1)) y guarda 1.
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task ReporteIva_VersionInexistenteOInvalida_NoFalla_ExcluyeLaVenta_YLaInformaAparte(int version)
    {
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        await SeedVentaAsync(cliente, producto, dia, version: 1, alicuotaServicios: 21m);
        await SeedVentaAsync(cliente, producto, dia, version: version, alicuotaServicios: 21m);

        var reporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));

        // Solo la venta válida entra en los totales; la inválida NO se recalcula con otra regla y queda informada.
        Assert.Equal(1, reporte.VentasCantidad);
        Assert.Equal((926.45m, 194.55m, 1121m), (reporte.VentasNeto, reporte.VentasIva, reporte.VentasTotal));
        Assert.Equal((1, 1121m), (reporte.VentasVersionFiscalInvalida, reporte.VentasVersionFiscalInvalidaTotal));
    }

    [Fact]
    public async Task ReporteIva_VersionInvalida_NoExponeDetalleInternoEnElViewModel()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var dia = DateTime.UtcNow.Date.AddDays(-1);
        await SeedVentaAsync(cliente, producto, dia, version: 4242);

        var reporte = await _reportes.GenerarReporteIvaAsync(FiltroDia(dia));

        // Ninguna propiedad de texto del resultado contiene el número de versión ni el mensaje de la excepción.
        var json = System.Text.Json.JsonSerializer.Serialize(reporte);
        Assert.DoesNotContain("4242", json);
        Assert.DoesNotContain("no registrada", json);
        Assert.Equal(1, reporte.VentasVersionFiscalInvalida);
    }

    [Fact]
    public async Task Comprobante_VersionInvalida_FallaConExcepcionTipada_SinCalcularConOtraRegla()
    {
        var (cliente, producto) = await SeedBaseAsync();
        var (_, facturaId) = await SeedVentaAsync(cliente, producto, DateTime.UtcNow.Date.AddDays(-1), version: 77);
        var ex = await Assert.ThrowsAsync<CriterioFiscalDesconocidoException>(() => ComprobanteAsync(facturaId));
        Assert.Equal(77, ex.Version);
    }

    // ───────────────────────── (6) migración aditiva ─────────────────────────

    [Fact]
    public void Migracion_AgregaSoloUnaColumnaNullable_SinBackfillNiTocarImportes()
    {
        var migracion = new TheBuryProyect.Migrations.P02AlicuotaServiciosSnapshot();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(TheBuryProyect.Migrations.P02AlicuotaServiciosSnapshot)
            .GetMethod("Up", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(migracion, new object[] { builder });

        // Única operación: AddColumn Ventas.AlicuotaServiciosSnapshot decimal(5,2) NULL. Sin UPDATE: las ventas existentes
        // quedan en null (comportamiento previo) hasta que se apruebe una política de backfill.
        var op = Assert.IsType<AddColumnOperation>(Assert.Single(builder.Operations));
        Assert.Equal(("Ventas", "AlicuotaServiciosSnapshot"), (op.Table, op.Name));
        Assert.True(op.IsNullable);
        Assert.Null(op.DefaultValue);
        Assert.Equal("decimal(5,2)", op.ColumnType);
    }

    [Fact]
    public async Task VentaViewModel_NoMapeaDeVueltaLaAlicuotaCongelada()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        var venta = new Venta { CriterioFiscalVersion = 1, AlicuotaServiciosSnapshot = 21m };
        var vm = mapper.Map<VentaViewModel>(venta);
        Assert.Equal(21m, vm.AlicuotaServiciosSnapshot); // entidad → VM: se informa (solo lectura)

        vm.AlicuotaServiciosSnapshot = 99m; // lo que un POST malicioso no puede inyectar (BindNever) ni el mapeo inverso aplicar
        var destino = new Venta { AlicuotaServiciosSnapshot = 21m };
        mapper.Map(vm, destino);
        Assert.Equal(21m, destino.AlicuotaServiciosSnapshot);
        await Task.CompletedTask;
    }
}

file sealed class StubNotificacionP02b : INotificacionService
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

file sealed class StubUsuarioP02b : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string? GetIpAddress() => "127.0.0.1";
}
