using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

public sealed class CotizacionConversionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CotizacionConversionService _service;
    private readonly StubPrecioVigenteResolver _precioResolver;

    private readonly Producto _producto;
    private readonly Cliente _cliente;

    public CotizacionConversionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var categoria = new Categoria { Codigo = "CAT-CV", Nombre = "Categoria conversion" };
        var marca = new Marca { Codigo = "MAR-CV", Nombre = "Marca conversion" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        _context.SaveChanges();

        _producto = new Producto
        {
            Codigo = "P-CV",
            Nombre = "Producto conversion",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 50m,
            PrecioVenta = 100m,
            StockActual = 10m,
            StockMinimo = 1m,
            Activo = true,
            RequiereNumeroSerie = false
        };

        _cliente = new Cliente
        {
            TipoDocumento = "DNI",
            NumeroDocumento = "456789",
            Apellido = "Test",
            Nombre = "Conversion",
            Telefono = "123",
            Domicilio = "Calle 2"
        };

        _context.Productos.Add(_producto);
        _context.Clientes.Add(_cliente);
        _context.SaveChanges();

        _precioResolver = new StubPrecioVigenteResolver();
        var numberGenerator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);

        _service = new CotizacionConversionService(
            _context,
            numberGenerator,
            _precioResolver,
            NullLogger<CotizacionConversionService>.Instance);
    }

    // ─── PREVIEW TESTS ───────────────────────────────────────────────────

    [Fact]
    public async Task Preview_CotizacionExistente_DevuelveConvertible()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.True(resultado.Convertible);
        Assert.Empty(resultado.Errores);
        Assert.Equal(cotizacion.Id, resultado.CotizacionId);
        Assert.Equal(EstadoCotizacion.Emitida, resultado.EstadoCotizacion);
    }

    [Fact]
    public async Task Preview_CotizacionConvertida_DevuelveError()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.Estado = EstadoCotizacion.ConvertidaAVenta;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.False(resultado.Convertible);
        Assert.Contains(resultado.Errores, e => e.Contains("ya fue convertida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_CotizacionCancelada_DevuelveError()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.Estado = EstadoCotizacion.Cancelada;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.False(resultado.Convertible);
        Assert.Contains(resultado.Errores, e => e.Contains("cancelada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_CotizacionVencida_BloqueaConversion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.Estado = EstadoCotizacion.Vencida;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.False(resultado.Convertible);
        Assert.Contains(resultado.Errores, e => e.Contains("vencida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_CotizacionConFechaVencimientoPasada_BloqueaConversion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.FechaVencimiento = DateTime.UtcNow.AddDays(-1);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.False(resultado.Convertible);
        Assert.Contains(resultado.Errores, e => e.Contains("vencid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_ProductoConPrecioCambiado_AgregaAdvertencia()
    {
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        _precioResolver.SetPrecio(_producto.Id, precioActual: 150m);

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.True(resultado.HayCambiosDePrecios);
        Assert.Contains(resultado.Advertencias, a => a.Contains("precio", StringComparison.OrdinalIgnoreCase));
        var detallePreview = Assert.Single(resultado.Detalles);
        Assert.True(detallePreview.PrecioCambio);
        Assert.Equal(150m, detallePreview.PrecioActual);
    }

    [Fact]
    public async Task Preview_CreditoPersonalSinCliente_BloqueaConversion()
    {
        var cotizacion = CotizacionEmitida(conCliente: false);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.False(resultado.Convertible);
        Assert.True(resultado.ClienteFaltante);
        Assert.Contains(resultado.Errores, e => e.Contains("cliente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_NoCreaVenta()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var ventasAntes = await _context.Ventas.CountAsync();
        await _service.PreviewConversionAsync(cotizacion.Id);
        var ventasDespues = await _context.Ventas.CountAsync();

        Assert.Equal(ventasAntes, ventasDespues);
    }

    [Fact]
    public async Task Preview_ProductoTrazable_AgregaAdvertencia()
    {
        _producto.RequiereNumeroSerie = true;
        await _context.SaveChangesAsync();

        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.True(resultado.HayProductosTrazables);
        Assert.Contains(resultado.Advertencias, a => a.Contains("unidad", StringComparison.OrdinalIgnoreCase));
        var detallePreview = Assert.Single(resultado.Detalles);
        Assert.True(detallePreview.RequiereUnidadFisica);

        _producto.RequiereNumeroSerie = false;
        await _context.SaveChangesAsync();
    }

    // ─── V1.8: PREVIEW DIFERENCIAS DE PRECIO ────────────────────────────

    [Fact]
    public async Task Preview_ProductoSinCambioPrecio_DiferenciaEsCero()
    {
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        _precioResolver.SetPrecio(_producto.Id, precioActual: 100m);

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        var detalle = Assert.Single(resultado.Detalles);
        Assert.False(detalle.PrecioCambio);
        Assert.Equal(0m, detalle.DiferenciaUnitaria);
        Assert.Equal(0m, detalle.DiferenciaTotal);
    }

    [Fact]
    public async Task Preview_ProductoConCambioPrecio_IncluyeDiferenciaUnitaria()
    {
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        _precioResolver.SetPrecio(_producto.Id, precioActual: 150m);

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        var detalle = Assert.Single(resultado.Detalles);
        Assert.True(detalle.PrecioCambio);
        Assert.Equal(100m, detalle.PrecioCotizado);
        Assert.Equal(150m, detalle.PrecioActual);
        Assert.Equal(50m, detalle.DiferenciaUnitaria);
    }

    [Fact]
    public async Task Preview_CambioPrecio_DiferenciaTotal_EsDiferenciaUnitariaPorCantidad()
    {
        // Cantidad fija = 2 (ver CotizacionEmitida helper)
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        _precioResolver.SetPrecio(_producto.Id, precioActual: 150m);

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        var detalle = Assert.Single(resultado.Detalles);
        Assert.Equal(50m, detalle.DiferenciaUnitaria);
        Assert.Equal(2, detalle.Cantidad);
        Assert.Equal(100m, detalle.DiferenciaTotal); // 50 * 2
    }

    [Fact]
    public async Task Preview_SinPrecioActual_DiferenciaEsNull()
    {
        // El resolver no tiene precio para el producto → precioActual = null
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        // No llamar a _precioResolver.SetPrecio → sin precio configurado

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        var detalle = Assert.Single(resultado.Detalles);
        Assert.Null(detalle.PrecioActual);
        Assert.Null(detalle.DiferenciaUnitaria);
        Assert.Null(detalle.DiferenciaTotal);
    }

    // ─── CONVERSIÓN TESTS ────────────────────────────────────────────────

    [Fact]
    public async Task Convertir_CotizacionValida_CreaVentaEnEstadoCotizacion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        Assert.NotNull(resultado.VentaId);
        Assert.NotNull(resultado.NumeroVenta);
        Assert.Equal(EstadoVenta.Cotizacion, resultado.EstadoVenta);

        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.NotNull(venta);
        Assert.Equal(EstadoVenta.Cotizacion, venta.Estado);
        Assert.Equal(_cliente.Id, venta.ClienteId);
    }

    [Fact]
    public async Task Convertir_CopiaDetallesDesdeSnapshot()
    {
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 200m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var detalles = await _context.VentaDetalles
            .Where(d => d.VentaId == resultado.VentaId)
            .ToListAsync();

        Assert.Single(detalles);
        Assert.Equal(_producto.Id, detalles[0].ProductoId);
        Assert.Equal(2, detalles[0].Cantidad);
        Assert.Equal(200m, detalles[0].PrecioUnitario);
    }

    [Fact]
    public async Task Convertir_MarcaCotizacionComoConvertida()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        await _context.Entry(cotizacion).ReloadAsync();
        Assert.Equal(EstadoCotizacion.ConvertidaAVenta, cotizacion.Estado);
    }

    [Fact]
    public async Task Convertir_NoConfirmaVenta()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.NotNull(venta);
        Assert.NotEqual(EstadoVenta.Confirmada, venta.Estado);
        Assert.Equal(EstadoVenta.Cotizacion, venta.Estado);
    }

    [Fact]
    public async Task Convertir_NoDescuentaStock()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var stockAntes = _producto.StockActual;
        await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        await _context.Entry(_producto).ReloadAsync();

        Assert.Equal(stockAntes, _producto.StockActual);
    }

    [Fact]
    public async Task Convertir_NoMarcaProductoUnidad()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var unidadesMarcadas = await _context.ProductoUnidades
            .Where(u => u.Estado == EstadoUnidad.Vendida)
            .CountAsync();

        await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        var unidadesMarcadasDespues = await _context.ProductoUnidades
            .Where(u => u.Estado == EstadoUnidad.Vendida)
            .CountAsync();

        Assert.Equal(unidadesMarcadas, unidadesMarcadasDespues);
    }

    [Fact]
    public async Task Convertir_NoRegistraCaja()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var movimientosAntes = await _context.MovimientosCaja.CountAsync();
        await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        var movimientosDespues = await _context.MovimientosCaja.CountAsync();

        Assert.Equal(movimientosAntes, movimientosDespues);
    }

    [Fact]
    public async Task Convertir_NoGeneraFactura()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var facturasAntes = await _context.Facturas.CountAsync();
        await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        var facturasDespues = await _context.Facturas.CountAsync();

        Assert.Equal(facturasAntes, facturasDespues);
    }

    [Fact]
    public async Task Convertir_CotizacionYaConvertida_Falla()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.Estado = EstadoCotizacion.ConvertidaAVenta;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.False(resultado.Exitoso);
        Assert.NotEmpty(resultado.Errores);
    }

    [Fact]
    public async Task Convertir_ConAdvertenciasSinConfirmar_Falla()
    {
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        _precioResolver.SetPrecio(_producto.Id, precioActual: 150m);

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = false
        };

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("advertencias", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Convertir_ConAdvertenciasConfirmadas_Convierte()
    {
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        _precioResolver.SetPrecio(_producto.Id, precioActual: 150m);

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = true
        };

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        Assert.NotNull(resultado.VentaId);
    }

    [Fact]
    public async Task Convertir_SinClienteYSinOverride_Falla()
    {
        var cotizacion = CotizacionEmitida(conCliente: false);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.False(resultado.Exitoso);
        Assert.Contains(resultado.Errores, e => e.Contains("cliente", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Convertir_SinClienteConOverride_Convierte()
    {
        var cotizacion = CotizacionEmitida(conCliente: false);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var request = new CotizacionConversionRequest
        {
            ClienteIdOverride = _cliente.Id,
            ConfirmarAdvertencias = false,
            UsarPrecioCotizado = true
        };

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.Equal(_cliente.Id, venta!.ClienteId);
    }

    [Fact]
    public async Task Convertir_UsandoPrecioActual_UsaPrecioDelResolver()
    {
        _precioResolver.SetPrecio(_producto.Id, precioActual: 300m);

        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = false,
            ConfirmarAdvertencias = false
        };

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        var detalles = await _context.VentaDetalles
            .Where(d => d.VentaId == resultado.VentaId)
            .ToListAsync();

        Assert.Single(detalles);
        Assert.Equal(300m, detalles[0].PrecioUnitario);
    }

    [Fact]
    public async Task Convertir_NoCreaCredito()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var creditosAntes = await _context.Creditos.CountAsync();
        await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        var creditosDespues = await _context.Creditos.CountAsync();

        Assert.Equal(creditosAntes, creditosDespues);
    }

    [Fact]
    public async Task Convertir_MapeoMedioPago_CreditoPersonal()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.Equal(TipoPago.CreditoPersonal, venta!.TipoPago);
    }

    [Fact]
    public async Task Convertir_IncluyeNumeroCotizacionEnObservaciones()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.NotNull(venta!.Observaciones);
        Assert.Contains(cotizacion.Numero, venta.Observaciones, StringComparison.OrdinalIgnoreCase);
    }

    // ─── V1.5: TRAZABILIDAD ──────────────────────────────────────────────

    [Fact]
    public async Task Convertir_SetCotizacionOrigenIdEnVenta()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.NotNull(venta);
        Assert.Equal(cotizacion.Id, venta.CotizacionOrigenId);
    }

    [Fact]
    public async Task Convertir_PermiteEncontrarVentaPorCotizacionOrigen()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var ventaViaFK = await _context.Ventas
            .FirstOrDefaultAsync(v => v.CotizacionOrigenId == cotizacion.Id);
        Assert.NotNull(ventaViaFK);
        Assert.Equal(resultado.VentaId, ventaViaFK.Id);
    }

    [Fact]
    public async Task Convertir_NoPermiteDobleConversion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var primera = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        var segunda = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(primera.Exitoso);
        Assert.False(segunda.Exitoso);
        Assert.Contains(segunda.Errores, e => e.Contains("convertida", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Venta_CotizacionOrigenId_EsNullable_EnVentasNormales()
    {
        // Una venta creada sin conversión no tiene CotizacionOrigenId
        var venta = new Venta
        {
            Numero = $"V-TEST-{Guid.NewGuid():N}"[..20],
            ClienteId = _cliente.Id,
            FechaVenta = DateTime.UtcNow,
            Estado = EstadoVenta.Cotizacion,
            TipoPago = TipoPago.Efectivo,
            CotizacionOrigenId = null
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var ventaLeida = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaLeida);
        Assert.Null(ventaLeida.CotizacionOrigenId);
    }

    // ─── V1.5: IVA EN VentaDetalle ───────────────────────────────────────

    [Fact]
    public async Task Convertir_IVAUnitario_NoEsCeroSiProductoTieneIVA()
    {
        // Producto con IVA 21% directo
        _producto.PorcentajeIVA = 21m;
        await _context.SaveChangesAsync();

        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 121m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var detalle = await _context.VentaDetalles
            .FirstAsync(d => d.VentaId == resultado.VentaId);

        Assert.Equal(21m, detalle.PorcentajeIVA);
        Assert.True(detalle.IVAUnitario > 0m, "IVAUnitario debe ser mayor a cero para producto con IVA 21%");
        Assert.True(detalle.PrecioUnitarioNeto < detalle.PrecioUnitario, "PrecioUnitarioNeto debe ser menor al precio con IVA");
    }

    [Fact]
    public async Task Convertir_IVACero_SiProductoNoTieneIVA()
    {
        _producto.PorcentajeIVA = 0m;
        await _context.SaveChangesAsync();

        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 100m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var detalle = await _context.VentaDetalles
            .FirstAsync(d => d.VentaId == resultado.VentaId);

        Assert.Equal(0m, detalle.IVAUnitario);
        Assert.Equal(detalle.PrecioUnitario, detalle.PrecioUnitarioNeto);
    }

    [Fact]
    public async Task Convertir_SubtotalCoherente_ConIVADescompuesto()
    {
        // precio 121, cantidad 2, IVA 21% → subtotal 242, neto 200, iva 42
        _producto.PorcentajeIVA = 21m;
        await _context.SaveChangesAsync();

        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 121m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var detalle = await _context.VentaDetalles
            .FirstAsync(d => d.VentaId == resultado.VentaId);

        // precio 121 / 1.21 = 100 neto; 121 - 100 = 21 iva
        Assert.Equal(100m, detalle.PrecioUnitarioNeto);
        Assert.Equal(21m, detalle.IVAUnitario);
        Assert.Equal(200m, detalle.SubtotalNeto);    // 100 * 2
        Assert.Equal(42m, detalle.SubtotalIVA);      // 21 * 2
        Assert.Equal(242m, detalle.Subtotal);         // 121 * 2
    }

    [Fact]
    public async Task Convertir_UsaAlicuotaIVA_SiProductoLaTiene()
    {
        // Configurar AlicuotaIVA activa en el producto
        var alicuota = new AlicuotaIVA
        {
            Codigo = "IVA10.5",
            Nombre = "IVA 10.5%",
            Porcentaje = 10.5m,
            Activa = true
        };
        _context.AlicuotasIVA.Add(alicuota);
        await _context.SaveChangesAsync();

        _producto.AlicuotaIVAId = alicuota.Id;
        _producto.PorcentajeIVA = 21m; // debe ser ignorado si AlicuotaIVA activa
        await _context.SaveChangesAsync();

        // Precio 110.5 con IVA 10.5% → neto 100
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 110.5m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var detalle = await _context.VentaDetalles
            .FirstAsync(d => d.VentaId == resultado.VentaId);

        Assert.Equal(10.5m, detalle.PorcentajeIVA);
        Assert.Equal(alicuota.Id, detalle.AlicuotaIVAId);
        Assert.Equal("IVA 10.5%", detalle.AlicuotaIVANombre);

        // Limpiar
        _producto.AlicuotaIVAId = null;
        await _context.SaveChangesAsync();
    }

    // ─── HELPERS ─────────────────────────────────────────────────────────

    private Cotizacion CotizacionEmitida(bool conCliente, decimal precioSnapshot = 100m) =>
        new()
        {
            Numero = $"COT-TEST-{Guid.NewGuid():N}",
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida,
            ClienteId = conCliente ? _cliente.Id : null,
            Subtotal = precioSnapshot * 2,
            TotalBase = precioSnapshot * 2,
            Detalles =
            {
                new CotizacionDetalle
                {
                    ProductoId = _producto.Id,
                    CodigoProductoSnapshot = _producto.Codigo,
                    NombreProductoSnapshot = _producto.Nombre,
                    Cantidad = 2,
                    PrecioUnitarioSnapshot = precioSnapshot,
                    Subtotal = precioSnapshot * 2
                }
            }
        };

    private static CotizacionConversionRequest RequestDefault() =>
        new() { UsarPrecioCotizado = true, ConfirmarAdvertencias = false };

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class StubPrecioVigenteResolver : IPrecioVigenteResolver
    {
        private readonly Dictionary<int, decimal> _precios = new();

        public void SetPrecio(int productoId, decimal precioActual) =>
            _precios[productoId] = precioActual;

        public Task<PrecioVigenteResultado?> ResolverAsync(int productoId, int? listaId = null, DateTime? fecha = null) =>
            Task.FromResult(_precios.TryGetValue(productoId, out var p)
                ? (PrecioVigenteResultado?)new PrecioVigenteResultado { ProductoId = productoId, PrecioFinalConIva = p }
                : null);

        public Task<IReadOnlyDictionary<int, PrecioVigenteResultado>> ResolverBatchAsync(
            IEnumerable<int> productoIds,
            int? listaId = null,
            DateTime? fecha = null,
            CancellationToken cancellationToken = default)
        {
            var resultado = new Dictionary<int, PrecioVigenteResultado>();
            foreach (var id in productoIds)
            {
                if (_precios.TryGetValue(id, out var p))
                    resultado[id] = new PrecioVigenteResultado { ProductoId = id, PrecioFinalConIva = p };
            }
            return Task.FromResult<IReadOnlyDictionary<int, PrecioVigenteResultado>>(resultado);
        }
    }
}
