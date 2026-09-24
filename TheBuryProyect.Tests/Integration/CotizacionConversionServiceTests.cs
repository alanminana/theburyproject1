using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// VENTA-COTIZACION-REWORK-03: stub de IValidacionVentaService configurable —
// mismo patrón que VentaServiceCreditoPersonalTests.StubValidacionVentaService
// (no reusable directamente porque ese es `file`-scoped a ese archivo).
// ---------------------------------------------------------------------------
file sealed class StubValidacionVentaServiceConversion : IValidacionVentaService
{
    private readonly ValidacionVentaResult _resultado;
    public StubValidacionVentaServiceConversion(ValidacionVentaResult resultado) => _resultado = resultado;

    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
        int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(_resultado);

    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// VENTA-COTIZACION-EXCEPCION-01: stub mínimo de ICurrentUserService — sólo lo
// necesita VentaService.AplicarExcepcionDocumentalSiCorresponde (permiso
// ventas.authorize + nombre de usuario para el log), las demás pruebas de este
// archivo siguen pasando null! porque ese método no hace nada cuando NoViable
// es false o cuando AplicarExcepcionDocumental viene en false (corta antes).
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserServiceConversion : ICurrentUserService
{
    private readonly bool _tienePermisoAutorizar;
    private readonly bool _tienePermisoActualizar;
    private readonly bool _tienePermisoFacturar;

    public StubCurrentUserServiceConversion(
        bool tienePermisoAutorizar,
        bool tienePermisoActualizar = false,
        bool tienePermisoFacturar = false)
    {
        _tienePermisoAutorizar = tienePermisoAutorizar;
        _tienePermisoActualizar = tienePermisoActualizar;
        _tienePermisoFacturar = tienePermisoFacturar;
    }

    public string GetUsername() => "carlos";
    public string GetUserId() => "1";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion)
    {
        if (modulo != "ventas") return false;
        return accion switch
        {
            "authorize" => _tienePermisoAutorizar,
            "update" => _tienePermisoActualizar,
            "invoice" => _tienePermisoFacturar,
            _ => false
        };
    }
    public string? GetIpAddress() => null;
}

// ---------------------------------------------------------------------------
// COTIZACION-MIVENTA-01: stub de IVentaService que sólo implementa ConfirmarVentaAsync/
// FacturarVentaAsync de forma configurable (el resto de esta interfaz, NotImplementedException
// — ninguno de los otros miembros lo llama CotizacionConversionService.
// ConfirmarYFacturarSiCorrespondeAsync, que es lo único que estos tests ejercitan). No re-testea
// la lógica interna de ConfirmarVentaAsync/FacturarVentaAsync — eso ya lo cubren
// VentaServiceConfirmarEfectivoTests.cs y sus ~90 casos; esto sólo verifica que
// CotizacionConversionService encadena, gatea por permiso y reporta correctamente.
// ---------------------------------------------------------------------------
file sealed class StubVentaServiceConfirmarFacturar : IVentaService
{
    private readonly Func<int, Task<bool>> _confirmar;
    private readonly Func<int, FacturaViewModel, Task<bool>> _facturar;
    public List<FacturaViewModel> FacturasSolicitadas { get; } = new();

    public StubVentaServiceConfirmarFacturar(
        Func<int, Task<bool>>? confirmar = null,
        Func<int, FacturaViewModel, Task<bool>>? facturar = null)
    {
        _confirmar = confirmar ?? (_ => Task.FromResult(true));
        _facturar = facturar ?? ((_, __) => Task.FromResult(true));
    }

    public Task<bool> ConfirmarVentaAsync(int id) => _confirmar(id);

    public Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel)
    {
        FacturasSolicitadas.Add(facturaViewModel);
        return _facturar(id, facturaViewModel);
    }

    public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
    public Task<VentaViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
    public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
    public Task<bool> ConfirmarVentaCreditoAsync(int id) => throw new NotImplementedException();
    public Task<bool> CancelarVentaAsync(int id, string motivo) => throw new NotImplementedException();
    public Task AsociarCreditoAVentaAsync(int ventaId, int creditoId) => throw new NotImplementedException();
    public Task<int?> AnularFacturaAsync(int facturaId, string motivo) => throw new NotImplementedException();
    public Task<bool> ValidarStockAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo) => throw new NotImplementedException();
    public Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
    public Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
    public Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
    public Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel) => throw new NotImplementedException();
    public Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta) => throw new NotImplementedException();
    public Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque) => throw new NotImplementedException();
    public Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas) => throw new NotImplementedException();
    public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
    public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
    public Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
    public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// COTIZACION-MIVENTA-02: stub mínimo de ICajaService — sólo lo necesita el preflight
// (ObtenerAperturaActivaParaUsuarioAsync), configurable con/sin apertura activa. Mismo patrón
// que StubCajaServiceCajaEnf de VentaServiceCajaEnforcementTests.cs (no reusable directamente,
// `file`-scoped a ese archivo).
// ---------------------------------------------------------------------------
file sealed class StubCajaServiceConversion : ICajaService
{
    private readonly AperturaCaja? _apertura;
    public StubCajaServiceConversion(AperturaCaja? apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario) => Task.FromResult(_apertura);

    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => throw new NotImplementedException();
    public Task<List<Caja>> ObtenerTodasCajasAsync() => throw new NotImplementedException();
    public Task<Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<Caja> CrearCajaAsync(CajaViewModel model) => throw new NotImplementedException();
    public Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model) => throw new NotImplementedException();
    public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotImplementedException();
    public Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotImplementedException();
    public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotImplementedException();
    public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotImplementedException();
    public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

// PROBLEMA 1 (contrato de Crédito personal, pedido 2026-09-17 §7): stub configurable para
// que PreflightConversionAsync pueda enriquecer (nunca bloquear más) el aviso de Crédito
// personal con los faltantes contractuales del cliente. Por defecto "todo completo" — sólo
// los tests de CASO D lo configuran con faltantes reales.
file sealed class StubContratoVentaCreditoServiceConversion : IContratoVentaCreditoService
{
    private readonly ContratoVentaCreditoValidacionResult _validacionCliente;
    public StubContratoVentaCreditoServiceConversion(ContratoVentaCreditoValidacionResult? validacionCliente = null)
        => _validacionCliente = validacionCliente ?? new ContratoVentaCreditoValidacionResult();

    public ContratoVentaCreditoValidacionResult ValidarDatosClienteParaContrato(Cliente? cliente) => _validacionCliente;

    public Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId) => throw new NotImplementedException();
    public Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario) => throw new NotImplementedException();
    public Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario) => throw new NotImplementedException();
    public Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ExisteContratoGeneradoAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ExistePlantillaActivaAsync() => throw new NotImplementedException();
    public Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId) => throw new NotImplementedException();
}

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

        // VENTA-COTIZACION-REWORK-03: aprobable por defecto — ninguno de los tests
        // preexistentes (no crediticios) toca esta rama; los tests de Crédito personal que sí
        // necesitan otro resultado (RequiereAutorizacion/NoViable) arman su propia instancia de
        // CotizacionConversionService vía BuildService(), no la comparten con _service.
        _service = BuildService(new ValidacionVentaResult { NoViable = false, RequiereAutorizacion = false });
    }

    /// <summary>
    /// VentaService "real" (no stub) para IVentaService.AplicarResultadoValidacionAsync/
    /// CrearCreditoPendienteParaVentaAsync: ambos métodos sólo tocan AppDbContext + logger (ver
    /// sus cuerpos), así que el resto de las dependencias del constructor de VentaService —
    /// irrelevantes para esas dos operaciones — se pasan en null!, mismo patrón que ya usa
    /// VentaServiceCreditoPersonalTests para escenarios equivalentes.
    /// </summary>
    private CotizacionConversionService BuildService(ValidacionVentaResult validacionCredito) =>
        BuildService(validacionCredito, currentUserService: null!);

    // VENTA-COTIZACION-EXCEPCION-01: overload con ICurrentUserService real (stub) — sólo lo
    // necesitan los tests que ejercitan AplicarExcepcionDocumentalSiCorresponde (NoViable=true +
    // AplicarExcepcionDocumental=true en el request). Los demás tests de este archivo siguen
    // usando el overload de arriba (null!), que no cambia.
    private CotizacionConversionService BuildService(ValidacionVentaResult validacionCredito, ICurrentUserService currentUserService)
    {
        var numberGenerator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);
        var ventaService = new VentaService(
            _context,
            null!,                    // IMapper
            NullLogger<VentaService>.Instance,
            null!,                    // IAlertaStockService
            null!,                    // IMovimientoStockService
            null!,                    // IFinancialCalculationService
            null!,                    // IVentaValidator
            numberGenerator,
            null!,                    // IPrecioVigenteResolver
            currentUserService,       // ICurrentUserService — usado por AplicarExcepcionDocumentalSiCorresponde
            null!,                    // IValidacionVentaService (no la usan los 3 métodos reutilizados)
            null!,                    // ICajaService
            null!,                    // ICreditoDisponibleService
            null!,                    // IContratoVentaCreditoService
            null!);                   // IConfiguracionPagoService

        return new CotizacionConversionService(
            _context,
            numberGenerator,
            _precioResolver,
            new StubValidacionVentaServiceConversion(validacionCredito),
            ventaService,
            currentUserService,       // ICurrentUserService — usado por ConfirmarYFacturarSiCorrespondeAsync (COTIZACION-MIVENTA-01)
            null!,                    // IVentaValidator — sólo lo usa PreflightConversionAsync, ver BuildServiceParaPreflight
            null!,                    // ICajaService — idem
            null!,                    // IContratoVentaCreditoService — idem
            NullLogger<CotizacionConversionService>.Instance);
    }

    // COTIZACION-MIVENTA-01: overload con StubVentaServiceConfirmarFacturar — sólo para los
    // tests de ConfirmarVenta/Facturar encadenados (medio Efectivo, sin crédito personal), que
    // no necesitan un VentaService real (ver comentario del stub).
    private CotizacionConversionService BuildServiceParaConfirmarFacturar(
        ICurrentUserService currentUserService,
        IVentaService ventaService)
    {
        var numberGenerator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);
        return new CotizacionConversionService(
            _context,
            numberGenerator,
            _precioResolver,
            new StubValidacionVentaServiceConversion(new ValidacionVentaResult { NoViable = false, RequiereAutorizacion = false }),
            ventaService,
            currentUserService,
            null!,
            null!,
            null!,                    // IContratoVentaCreditoService — no involucrado en este camino (Efectivo, sin crédito personal)
            NullLogger<CotizacionConversionService>.Instance);
    }

    // COTIZACION-MIVENTA-02: overload dedicado a PreflightConversionAsync/PreviewFacturaAsync —
    // usa un VentaValidator REAL (sin dependencias, ver VentaValidator.cs) para que ValidarStock
    // se ejerza de verdad, y el stub configurable de ICajaService de arriba.
    private CotizacionConversionService BuildServiceParaPreflight(
        ICurrentUserService currentUserService,
        AperturaCaja? aperturaActiva,
        ContratoVentaCreditoValidacionResult? validacionContratoCliente = null)
    {
        var numberGenerator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);
        return new CotizacionConversionService(
            _context,
            numberGenerator,
            _precioResolver,
            new StubValidacionVentaServiceConversion(new ValidacionVentaResult { NoViable = false, RequiereAutorizacion = false }),
            null!,
            currentUserService,
            new VentaValidator(),
            new StubCajaServiceConversion(aperturaActiva),
            new StubContratoVentaCreditoServiceConversion(validacionContratoCliente),
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
    public async Task Preview_CotizacionConAnticipo_ExponeAnticipoCotizadoComoIntencion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.Anticipo = 15m;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.Equal(15m, resultado.AnticipoCotizado);
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
    public async Task Preview_EfectivoSinCliente_EsConvertibleParaPermitirOverrideEnLaUi()
    {
        // Reproduce un bug real de staging (2026-09-23): el modal "Convertir a Venta" deja buscar y
        // seleccionar un cliente para resolver justo esta condición (ClienteFaltante), pero
        // Convertible quedaba en false para SIEMPRE porque el mensaje de cliente faltante se
        // agregaba a Errores sin importar el medio de pago — el botón de confirmar nunca se
        // habilitaba, ni siquiera después de elegir un cliente. Para medios distintos de Crédito
        // personal, ConvertirAsync ya acepta ClienteIdOverride (ver Convertir_SinClienteConOverride_Convierte),
        // así que el preview no debe bloquear la conversión sólo por esto.
        var cotizacion = CotizacionEmitida(conCliente: false);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.Efectivo;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.True(resultado.ClienteFaltante);
        Assert.True(resultado.Convertible);
        Assert.DoesNotContain(resultado.Errores, e => e.Contains("cliente", StringComparison.OrdinalIgnoreCase));
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

    // ENVIO-ML4: la intención de envío declarada en el simulador (Cotizacion.TieneEnvio)
    // debe sobrevivir a la conversión, precargada con la dirección real del cliente.
    [Fact]
    public async Task Convertir_CotizacionConTieneEnvio_CreaVentaConEnvioPendienteYDireccionDelCliente()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.TieneEnvio = true;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas
            .Include(v => v.Envio)
            .FirstAsync(v => v.Id == resultado.VentaId);

        Assert.NotNull(venta.Envio);
        Assert.Equal(EstadoEnvio.Pendiente, venta.Envio!.Estado);
        Assert.Equal(_cliente.Domicilio, venta.Envio.Domicilio);
        Assert.Contains(_cliente.Apellido, venta.Envio.Destinatario);
    }

    [Fact]
    public async Task Convertir_CotizacionSinTieneEnvio_NoCreaEnvio()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        Assert.False(cotizacion.TieneEnvio);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas
            .Include(v => v.Envio)
            .FirstAsync(v => v.Id == resultado.VentaId);

        Assert.Null(venta.Envio);
    }

    // COTIZACION-MIVENTA-01: el modal de envío del Cotizador puede mandar datos reales
    // distintos a los del Cliente (ej. entregar en otra dirección) — deben prevalecer.
    [Fact]
    public async Task Convertir_ConTieneEnvioYOverridesDelModal_UsaLosDatosDelModalNoLosDelCliente()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.TieneEnvio = true;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = true,
            EnvioDestinatario = "Otro Destinatario",
            EnvioDomicilio = "Otra Calle 999",
            EnvioTelefono = "555-1234",
            EnvioLocalidad = "Otra Localidad",
            EnvioTransportista = "Correo Test",
            EnvioCostoEnvio = 500m
        };

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.Include(v => v.Envio).FirstAsync(v => v.Id == resultado.VentaId);
        Assert.Equal("Otro Destinatario", venta.Envio!.Destinatario);
        Assert.Equal("Otra Calle 999", venta.Envio.Domicilio);
        Assert.Equal("555-1234", venta.Envio.Telefono);
        Assert.Equal("Otra Localidad", venta.Envio.Localidad);
        Assert.Equal("Correo Test", venta.Envio.Transportista);
        Assert.Equal(500m, venta.Envio.CostoEnvio);
    }

    // VENTA-ENVIO-TOTAL-01: el importe guardado en la Cotización sobrevive a la conversión aunque el
    // request no lo repita (caso "Pasar a venta" desde una cotización ya guardada), queda persistido
    // en VentaEnvio.CostoEnvio (fuente única) y se suma en TotalACobrar SIN tocar Venta.Total.
    [Fact]
    public async Task Convertir_CotizacionConCostoEnvioPersistido_LlevaElImporteAVentaSinTocarTotal()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.TieneEnvio = true;
        cotizacion.CostoEnvio = 1000m;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        _context.ChangeTracker.Clear(); // recarga real desde la base, no la instancia en memoria
        var venta = await _context.Ventas.Include(v => v.Envio).FirstAsync(v => v.Id == resultado.VentaId);

        Assert.Equal(1000m, venta.Envio!.CostoEnvio);
        Assert.Equal(1000m, venta.ImporteEnvio);
        Assert.Equal(venta.Subtotal, venta.Total);                 // el envío no entra en Venta.Total
        Assert.Equal(venta.Total + 1000m, venta.TotalACobrar);      // se suma una sola vez
        Assert.Equal(venta.Total, venta.TotalFacturable);          // el comprobante no lo incluye
    }

    [Fact]
    public async Task Convertir_CostoEnvioDelModalNegativo_NoRestaDelTotalACobrar()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.TieneEnvio = true;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = true,
            EnvioCostoEnvio = -250m
        };

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.Include(v => v.Envio).FirstAsync(v => v.Id == resultado.VentaId);
        Assert.Equal(0m, venta.ImporteEnvio);
        Assert.Equal(venta.Total, venta.TotalACobrar);
    }

    [Fact]
    public async Task Preview_CotizacionConEnvio_ExponeImporteYTotalACobrar()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.TieneEnvio = true;
        cotizacion.CostoEnvio = 1000m;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var preview = await _service.PreviewConversionAsync(cotizacion.Id);

        Assert.Equal(1000m, preview.ImporteEnvio);
        Assert.Equal(preview.TotalCotizado + 1000m, preview.TotalACobrar);
    }

    [Fact]
    public async Task Convertir_SinConfirmarVenta_NoConfirmaNiFactura()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Exitoso);
        Assert.False(resultado.VentaConfirmada);
        Assert.False(resultado.Facturada);
        Assert.Null(resultado.MensajeConfirmacion);
    }

    [Fact]
    public async Task Convertir_ConConfirmarVenta_YPermiso_ConfirmaLaVentaYNoFacturaSiNoSePidio()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var ventaServiceStub = new StubVentaServiceConfirmarFacturar();
        var currentUser = new StubCurrentUserServiceConversion(tienePermisoAutorizar: false, tienePermisoActualizar: true);
        var service = BuildServiceParaConfirmarFacturar(currentUser, ventaServiceStub);

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = true,
            ConfirmarVenta = true
        };

        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        Assert.True(resultado.VentaConfirmada);
        Assert.False(resultado.Facturada);
        Assert.Empty(ventaServiceStub.FacturasSolicitadas);
    }

    [Fact]
    public async Task Convertir_ConConfirmarVentaYFacturar_YAmbosPermisos_ConfirmaYFactura()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var ventaServiceStub = new StubVentaServiceConfirmarFacturar();
        var currentUser = new StubCurrentUserServiceConversion(
            tienePermisoAutorizar: false, tienePermisoActualizar: true, tienePermisoFacturar: true);
        var service = BuildServiceParaConfirmarFacturar(currentUser, ventaServiceStub);

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = true,
            ConfirmarVenta = true,
            Facturar = true,
            TipoFactura = TipoFactura.A
        };

        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        Assert.True(resultado.VentaConfirmada);
        Assert.True(resultado.Facturada);
        Assert.Null(resultado.MensajeConfirmacion);
        var factura = Assert.Single(ventaServiceStub.FacturasSolicitadas);
        Assert.Equal(TipoFactura.A, factura.Tipo);
        Assert.Equal(resultado.VentaId, factura.VentaId);
    }

    // §9 del pedido: nunca confiar sólo en el frontend — sin el permiso real
    // (ventas/update), la venta queda CREADA pero no confirmada, con el motivo explícito.
    [Fact]
    public async Task Convertir_ConConfirmarVenta_SinPermisoActualizar_CreaLaVentaPeroNoLaConfirma()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var ventaServiceStub = new StubVentaServiceConfirmarFacturar();
        var currentUser = new StubCurrentUserServiceConversion(tienePermisoAutorizar: false, tienePermisoActualizar: false);
        var service = BuildServiceParaConfirmarFacturar(currentUser, ventaServiceStub);

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = true,
            ConfirmarVenta = true,
            Facturar = true
        };

        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        Assert.NotNull(resultado.VentaId);
        Assert.False(resultado.VentaConfirmada);
        Assert.False(resultado.Facturada);
        Assert.Contains("permiso", resultado.MensajeConfirmacion, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ventaServiceStub.FacturasSolicitadas);
    }

    // Idem, pero con permiso para confirmar y sin permiso para facturar: la venta se
    // confirma igual (stock/caja), sólo se bloquea el paso de facturación.
    [Fact]
    public async Task Convertir_ConConfirmarVentaYFacturar_SinPermisoFacturar_ConfirmaPeroNoFactura()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var ventaServiceStub = new StubVentaServiceConfirmarFacturar();
        var currentUser = new StubCurrentUserServiceConversion(
            tienePermisoAutorizar: false, tienePermisoActualizar: true, tienePermisoFacturar: false);
        var service = BuildServiceParaConfirmarFacturar(currentUser, ventaServiceStub);

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = true,
            ConfirmarVenta = true,
            Facturar = true
        };

        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        Assert.True(resultado.VentaConfirmada);
        Assert.False(resultado.Facturada);
        Assert.Contains("facturar", resultado.MensajeConfirmacion, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ventaServiceStub.FacturasSolicitadas);
    }

    // Estado real (ej. crédito personal pendiente de configurar el plan, mismo camino que
    // Venta/Create): ConfirmarVentaAsync lanza InvalidOperationException — no se propaga, se
    // reporta como mensaje y la venta queda creada, nunca en un estado ambiguo.
    [Fact]
    public async Task Convertir_ConConfirmarVenta_SiConfirmarVentaAsyncRechaza_NoPropagaYReportaElMotivo()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var ventaServiceStub = new StubVentaServiceConfirmarFacturar(
            confirmar: _ => throw new InvalidOperationException("La venta requiere configurar el plan de financiamiento."));
        var currentUser = new StubCurrentUserServiceConversion(tienePermisoAutorizar: false, tienePermisoActualizar: true);
        var service = BuildServiceParaConfirmarFacturar(currentUser, ventaServiceStub);

        var resultado = await service.ConvertirAVentaAsync(
            cotizacion.Id,
            new CotizacionConversionRequest { UsarPrecioCotizado = true, ConfirmarAdvertencias = true, ConfirmarVenta = true },
            "carlos");

        Assert.True(resultado.Exitoso);
        Assert.NotNull(resultado.VentaId);
        Assert.False(resultado.VentaConfirmada);
        Assert.Contains("plan de financiamiento", resultado.MensajeConfirmacion);
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

    // VENTA-COTIZACION-REWORK-03 (auditoría en vivo del usuario, 2026-09-15): antes esta prueba
    // se llamaba Convertir_NoCreaCredito y afirmaba el bug — la conversión NUNCA creaba el
    // Credito real ni evaluaba autorización para Crédito personal (RequiereAutorizacion=false
    // hardcodeado). Corregido: ahora reutiliza la misma evaluación/creación de crédito que
    // Venta/Create (IValidacionVentaService + IVentaService), así que con un cliente aprobable
    // SÍ debe crear el Credito real, igual que si la venta se hubiera creado directo.
    [Fact]
    public async Task Convertir_CreditoPersonalAprobable_CreaCreditoPendienteConfiguracion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var creditosAntes = await _context.Creditos.CountAsync();
        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        var creditosDespues = await _context.Creditos.CountAsync();

        Assert.True(resultado.Exitoso);
        Assert.Equal(creditosAntes + 1, creditosDespues);

        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.NotNull(venta);
        Assert.True(venta!.CreditoId.HasValue);
        Assert.False(venta.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.NoRequiere, venta.EstadoAutorizacion);
        Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);

        var credito = await _context.Creditos.FindAsync(venta.CreditoId!.Value);
        Assert.NotNull(credito);
        Assert.Equal(EstadoCredito.PendienteConfiguracion, credito!.Estado);
        Assert.Equal(venta.Total, credito.MontoSolicitado);
    }

    [Fact]
    public async Task Convertir_CreditoPersonalRequiereAutorizacion_NoCreaCreditoYQuedaPendienteAutorizacion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var validacion = new ValidacionVentaResult
        {
            NoViable = false,
            RequiereAutorizacion = true,
            RazonesAutorizacion = { new RazonAutorizacion { Tipo = TipoRazonAutorizacion.MoraActiva, Descripcion = "Mora 15 días" } }
        };
        var service = BuildService(validacion);

        var creditosAntes = await _context.Creditos.CountAsync();
        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        var creditosDespues = await _context.Creditos.CountAsync();

        Assert.True(resultado.Exitoso);
        Assert.Equal(creditosAntes, creditosDespues);

        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.NotNull(venta);
        Assert.False(venta!.CreditoId.HasValue);
        Assert.True(venta.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.PendienteAutorizacion, venta.EstadoAutorizacion);
        Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);
        Assert.NotNull(venta.RazonesAutorizacionJson);
    }

    [Fact]
    public async Task Convertir_CreditoPersonalNoViable_RechazaConversionYNoConvierteLaCotizacion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var validacion = new ValidacionVentaResult { NoViable = true };
        var service = BuildService(validacion);

        var ventasAntes = await _context.Ventas.CountAsync();
        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        var ventasDespues = await _context.Ventas.CountAsync();

        Assert.False(resultado.Exitoso);
        Assert.NotEmpty(resultado.Errores);
        Assert.Equal(ventasAntes, ventasDespues);

        var cotizacionRecargada = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Emitida, cotizacionRecargada!.Estado);
    }

    // ─── VENTA-COTIZACION-EXCEPCION-01: excepción documental reutilizada desde el Cotizador ──
    // Misma regla que VentaServiceCreditoPersonalTests (CreateAsync): un NoViable por
    // documentación/cupo insuficiente puede exceptuarse con AplicarExcepcionDocumental=true +
    // motivo + permiso ventas.authorize; un NoViable por mora NUNCA se exceptúa, tenga o no el
    // request ese flag. Ver IVentaService.AplicarExcepcionDocumentalSiCorresponde — el mismo
    // método que usa CreateAsync, no una reimplementación para Cotización.

    [Fact]
    public async Task Convertir_CreditoPersonalNoViablePorDocumentacion_ConExcepcionYPermiso_AutorizaLaVentaYRegistraTraza()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var validacion = new ValidacionVentaResult
        {
            NoViable = true,
            PendienteRequisitos = true,
            RequisitosPendientes = new List<RequisitoPendiente>
            {
                new() { Tipo = TipoRequisitoPendiente.DocumentacionFaltante, Descripcion = "Falta DNI del cliente" }
            }
        };
        var service = BuildService(validacion, new StubCurrentUserServiceConversion(tienePermisoAutorizar: true));

        var request = new CotizacionConversionRequest
        {
            UsarPrecioCotizado = true,
            ConfirmarAdvertencias = false,
            AplicarExcepcionDocumental = true,
            MotivoExcepcionDocumental = "Cliente con legajo en trámite, aprobado por gerencia"
        };
        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");

        Assert.True(resultado.Exitoso);
        Assert.True(resultado.ExcepcionDocumentalAplicada);

        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.NotNull(venta);
        Assert.True(venta!.RequiereAutorizacion);
        Assert.Equal(EstadoAutorizacionVenta.Autorizada, venta.EstadoAutorizacion);
        Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);
        Assert.Equal("carlos", venta.UsuarioAutoriza);
        Assert.StartsWith("EXCEPCION_DOC|", venta.MotivoAutorizacion);
        Assert.Contains("Cliente con legajo en trámite", venta.MotivoAutorizacion);

        var cotizacionRecargada = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.ConvertidaAVenta, cotizacionRecargada!.Estado);
    }

    [Fact]
    public async Task Convertir_CreditoPersonalNoViablePorDocumentacion_ConExcepcionSinPermiso_RechazaConversionYNoConvierteLaCotizacion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var validacion = new ValidacionVentaResult
        {
            NoViable = true,
            PendienteRequisitos = true,
            RequisitosPendientes = new List<RequisitoPendiente>
            {
                new() { Tipo = TipoRequisitoPendiente.DocumentacionFaltante, Descripcion = "Falta DNI del cliente" }
            }
        };
        // Mismo criterio que Venta/Create: ventas.create (o cotizaciones.convert, el permiso real
        // de este endpoint) no alcanza para autorizar una excepción — se requiere ventas.authorize.
        var service = BuildService(validacion, new StubCurrentUserServiceConversion(tienePermisoAutorizar: false));

        var request = new CotizacionConversionRequest
        {
            AplicarExcepcionDocumental = true,
            MotivoExcepcionDocumental = "Motivo igual, usuario sin permiso"
        };
        var ventasAntes = await _context.Ventas.CountAsync();
        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");
        var ventasDespues = await _context.Ventas.CountAsync();

        Assert.False(resultado.Exitoso);
        Assert.NotEmpty(resultado.Errores);
        Assert.Equal(ventasAntes, ventasDespues);

        var cotizacionRecargada = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Emitida, cotizacionRecargada!.Estado);
    }

    [Fact]
    public async Task Convertir_CreditoPersonalNoViablePorMora_ConExcepcionYPermiso_NuncaEsExceptuable_RechazaConversion()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var validacion = new ValidacionVentaResult
        {
            NoViable = true,
            PendienteRequisitos = true,
            RequisitosPendientes = new List<RequisitoPendiente>
            {
                new() { Tipo = TipoRequisitoPendiente.ClienteNoApto, Descripcion = "Mora activa de 45 días" }
            }
        };
        var service = BuildService(validacion, new StubCurrentUserServiceConversion(tienePermisoAutorizar: true));

        var request = new CotizacionConversionRequest
        {
            AplicarExcepcionDocumental = true,
            MotivoExcepcionDocumental = "Se solicita excepción igual"
        };
        var ventasAntes = await _context.Ventas.CountAsync();
        var resultado = await service.ConvertirAVentaAsync(cotizacion.Id, request, "carlos");
        var ventasDespues = await _context.Ventas.CountAsync();

        Assert.False(resultado.Exitoso);
        Assert.NotEmpty(resultado.Errores);
        Assert.Equal(ventasAntes, ventasDespues);

        var cotizacionRecargada = await _context.Cotizaciones.FindAsync(cotizacion.Id);
        Assert.Equal(EstadoCotizacion.Emitida, cotizacionRecargada!.Estado);
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

    // ─── COTIZ-QA-2: conversión con descuentos ───────────────────────────────

    [Fact]
    public async Task Convertir_ConDescuentoImporteSnapshot_AplicaDescuentoEnVentaDetalle()
    {
        // precio 100, cantidad 2, descuentoImporte 10 → subtotal = 100*2 - 10 = 190.
        // VENTA-CREDITO-ELEGIBILIDAD-DESCUENTO-FIX: VentaDetalle.Descuento es porcentaje en toda
        // la autoridad de VentaService, así que el importe ($10 sobre un bruto de $200) se
        // normaliza a su equivalente: 10/200*100 = 5%.
        var cotizacion = CotizacionEmitidaConDescuento(conCliente: true, precioSnapshot: 100m, descuentoImporte: 10m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "kira");

        Assert.True(resultado.Exitoso);
        var detalle = await _context.VentaDetalles.FirstAsync(d => d.VentaId == resultado.VentaId);
        Assert.Equal(5m, detalle.Descuento);
        Assert.Equal(190m, detalle.Subtotal);
    }

    [Fact]
    public async Task Convertir_ConSoloDescuentoPorcentajeSnapshot_DescuentoDetalleEsCero()
    {
        // Por diseño: la conversión usa solo DescuentoImporteSnapshot.
        // DescuentoPorcentajeSnapshot es solo snapshot/auditoría; no se recalcula al convertir.
        var cotizacion = CotizacionEmitidaConDescuento(conCliente: true, precioSnapshot: 100m, descuentoPorcentaje: 10m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "kira");

        Assert.True(resultado.Exitoso);
        var detalle = await _context.VentaDetalles.FirstAsync(d => d.VentaId == resultado.VentaId);
        Assert.Equal(0m, detalle.Descuento);
        Assert.Equal(200m, detalle.Subtotal); // 100 * 2 sin descuento aplicado
    }

    [Fact]
    public async Task Convertir_ConAmbosDescuentosSnapshot_UsaImporte()
    {
        // Cuando hay porcentaje e importe, la conversión usa solo importe (el subtotal resta el
        // importe crudo: 100*2 - 15 = 185). VentaDetalle.Descuento se normaliza al equivalente
        // porcentual de ese importe sobre el bruto: 15/200*100 = 7.5%.
        var cotizacion = CotizacionEmitidaConDescuento(
            conCliente: true, precioSnapshot: 100m,
            descuentoPorcentaje: 10m, descuentoImporte: 15m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "kira");

        Assert.True(resultado.Exitoso);
        var detalle = await _context.VentaDetalles.FirstAsync(d => d.VentaId == resultado.VentaId);
        Assert.Equal(7.5m, detalle.Descuento);
        Assert.Equal(185m, detalle.Subtotal); // 100 * 2 - 15
    }

    [Fact]
    public async Task Convertir_DescuentoGeneralNoPropagaAVentaDescuento()
    {
        // venta.Descuento = 0 siempre; el descuento general de la cotizacion no se propaga
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.DescuentoTotal = 20m; // simula descuento general aplicado
        cotizacion.TotalBase = 80m;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var resultado = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "kira");

        Assert.True(resultado.Exitoso);
        var venta = await _context.Ventas.FindAsync(resultado.VentaId);
        Assert.Equal(0m, venta!.Descuento); // descuento general no propagado
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

    private Cotizacion CotizacionEmitidaConDescuento(
        bool conCliente,
        decimal precioSnapshot = 100m,
        decimal? descuentoPorcentaje = null,
        decimal? descuentoImporte = null)
    {
        var descuento = descuentoImporte ?? 0m;
        return new Cotizacion
        {
            Numero = $"COT-TEST-{Guid.NewGuid():N}",
            Fecha = DateTime.UtcNow,
            Estado = EstadoCotizacion.Emitida,
            ClienteId = conCliente ? _cliente.Id : null,
            Subtotal = precioSnapshot * 2,
            DescuentoTotal = descuento,
            TotalBase = precioSnapshot * 2 - descuento,
            Detalles =
            {
                new CotizacionDetalle
                {
                    ProductoId = _producto.Id,
                    CodigoProductoSnapshot = _producto.Codigo,
                    NombreProductoSnapshot = _producto.Nombre,
                    Cantidad = 2,
                    PrecioUnitarioSnapshot = precioSnapshot,
                    DescuentoPorcentajeSnapshot = descuentoPorcentaje,
                    DescuentoImporteSnapshot = descuentoImporte,
                    Subtotal = precioSnapshot * 2 - descuento
                }
            }
        };
    }

    private static CotizacionConversionRequest RequestDefault() =>
        new() { UsarPrecioCotizado = true, ConfirmarAdvertencias = false };

    // ─── PREFLIGHT TESTS (COTIZACION-MIVENTA-02) ────────────────────────────

    private static AperturaCaja AperturaAbierta() => new()
    {
        CajaId = 1,
        UsuarioApertura = "carlos",
        MontoInicial = 0m,
        Cerrada = false
    };

    private static readonly ICurrentUserService PermisosCompletos =
        new StubCurrentUserServiceConversion(tienePermisoAutorizar: false, tienePermisoActualizar: true, tienePermisoFacturar: true);

    [Fact]
    public async Task Preflight_TodoEnOrden_Listo()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var service = BuildServiceParaPreflight(PermisosCompletos, AperturaAbierta());
        var resultado = await service.PreflightConversionAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Listo);
        Assert.Empty(resultado.Bloqueos);
        Assert.False(resultado.EsCreditoPersonal);
    }

    [Fact]
    public async Task Preflight_SinCajaAbierta_BloqueaConAccionAbrirCaja()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var service = BuildServiceParaPreflight(PermisosCompletos, aperturaActiva: null);
        var resultado = await service.PreflightConversionAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.False(resultado.Listo);
        Assert.Contains(resultado.Bloqueos, b => b.Codigo == "sin_caja" && b.AccionSugerida == "Abrir caja");
    }

    [Fact]
    public async Task Preflight_StockInsuficiente_Bloquea()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        _producto.StockActual = 1m; // la cotización pide 2 (ver CotizacionEmitida)
        await _context.SaveChangesAsync();

        var service = BuildServiceParaPreflight(PermisosCompletos, AperturaAbierta());
        var resultado = await service.PreflightConversionAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.False(resultado.Listo);
        Assert.Contains(resultado.Bloqueos, b => b.Codigo == "stock");
    }

    // Regla real (VentaValidator.ValidarEstadoParaConfirmacion + AplicarResultadoValidacionAsync):
    // toda venta de Crédito personal queda PendienteFinanciacion, un estado que
    // ValidarEstadoParaConfirmacion nunca acepta — confirmar en un solo paso es
    // estructuralmente imposible hasta configurar el plan en el wizard. El preflight debe
    // reportar esto SIEMPRE para Crédito personal, nunca dejarlo pasar como "Listo".
    [Fact]
    public async Task Preflight_CreditoPersonal_SiempreBloqueaConAccionContinuarConWizard()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        cotizacion.MedioPagoSeleccionado = CotizacionMedioPagoTipo.CreditoPersonal;
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var service = BuildServiceParaPreflight(PermisosCompletos, AperturaAbierta());
        var resultado = await service.PreflightConversionAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.False(resultado.Listo);
        Assert.True(resultado.EsCreditoPersonal);
        Assert.Contains(resultado.Bloqueos, b => b.Codigo == "credito_personal_requiere_wizard" && b.AccionSugerida == "Continuar con wizard");
    }

    [Fact]
    public async Task Preflight_SinPermisoActualizar_Bloquea()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var sinPermiso = new StubCurrentUserServiceConversion(tienePermisoAutorizar: false, tienePermisoActualizar: false);
        var service = BuildServiceParaPreflight(sinPermiso, AperturaAbierta());
        var resultado = await service.PreflightConversionAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.False(resultado.Listo);
        Assert.Contains(resultado.Bloqueos, b => b.Codigo == "sin_permiso_confirmar");
    }

    [Fact]
    public async Task Preflight_FacturarSinPermisoFacturar_Bloquea()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var soloConfirmar = new StubCurrentUserServiceConversion(tienePermisoAutorizar: false, tienePermisoActualizar: true, tienePermisoFacturar: false);
        var service = BuildServiceParaPreflight(soloConfirmar, AperturaAbierta());
        var request = new CotizacionConversionRequest { UsarPrecioCotizado = true, ConfirmarAdvertencias = false, Facturar = true };
        var resultado = await service.PreflightConversionAsync(cotizacion.Id, request, "carlos");

        Assert.False(resultado.Listo);
        Assert.Contains(resultado.Bloqueos, b => b.Codigo == "sin_permiso_facturar");
    }

    [Fact]
    public async Task Preflight_FacturarFalse_NoExigePermisoFacturar()
    {
        var cotizacion = CotizacionEmitida(conCliente: true);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var soloConfirmar = new StubCurrentUserServiceConversion(tienePermisoAutorizar: false, tienePermisoActualizar: true, tienePermisoFacturar: false);
        var service = BuildServiceParaPreflight(soloConfirmar, AperturaAbierta());
        var resultado = await service.PreflightConversionAsync(cotizacion.Id, RequestDefault(), "carlos");

        Assert.True(resultado.Listo);
    }

    // ─── FACTURA PREVIEW TESTS (COTIZACION-MIVENTA-02) ──────────────────────

    [Fact]
    public async Task PreviewFactura_CotizacionInexistente_NoExitoso()
    {
        var resultado = await _service.PreviewFacturaAsync(999_999);

        Assert.False(resultado.Exitoso);
        Assert.NotEmpty(resultado.Errores);
    }

    // Paridad con Venta/Details: Subtotal/IVA deben salir del MISMO cálculo que
    // ConvertirAVentaAsync usa para los VentaDetalle reales (ConstruirDetalles), envuelto en el
    // MISMO builder que ya usa VentaController.Facturar GET (FacturaAlicuotaResumenBuilder). El
    // producto de este fixture no tiene AlicuotaIVA asignada, así que porcentaje = default.
    [Fact]
    public async Task PreviewFactura_CalculaSubtotalIvaYAlicuotas_ConsistenteConConvertir()
    {
        var cotizacion = CotizacionEmitida(conCliente: true, precioSnapshot: 121m);
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();

        var previewFactura = await _service.PreviewFacturaAsync(cotizacion.Id);
        Assert.True(previewFactura.Exitoso);

        var resultadoConversion = await _service.ConvertirAVentaAsync(cotizacion.Id, RequestDefault(), "carlos");
        Assert.True(resultadoConversion.Exitoso);

        var venta = await _context.Ventas.FirstAsync(v => v.Id == resultadoConversion.VentaId);

        Assert.Equal(venta.Subtotal, previewFactura.Subtotal);
        Assert.Equal(venta.IVA, previewFactura.IVA);
        Assert.Equal(venta.Total, previewFactura.Total);
        Assert.NotEmpty(previewFactura.ResumenAlicuotas);
    }

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
