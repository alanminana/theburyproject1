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
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs mínimos para ConfirmarVentaCreditoAsync
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceConfirmar : ICajaService
{
    private readonly AperturaCaja _apertura;

    public StubCajaServiceConfirmar(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(
        int creditoId, string creditoNumero, decimal montoAnticipo, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());

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
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

/// <summary>
/// Stub de IMovimientoStockService que acepta salidas sin hacer nada.
/// ConfirmarVentaCreditoAsync llama RegistrarSalidasAsync; no necesitamos
/// verificar stock real en estos tests (los datos de producto ya están en DB).
/// </summary>
file sealed class StubMovimientoStockService : IMovimientoStockService
{
    public Task<List<MovimientoStock>> RegistrarSalidasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> salidas,
        string motivo,
        string? usuarioActual = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
        => Task.FromResult(new List<MovimientoStock>());

    public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
    public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null, string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo, decimal cantidad, string? referencia, string motivo, string? usuarioActual = null, int? ordenCompraId = null) => throw new NotImplementedException();
    public Task<List<MovimientoStock>> RegistrarEntradasAsync(List<(int productoId, decimal cantidad, string? referencia)> entradas, string motivo, string? usuarioActual = null, int? ordenCompraId = null, IReadOnlyList<MovimientoStockCostoLinea>? costos = null) => throw new NotImplementedException();
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
}

file sealed class StubAlertaStockService : IAlertaStockService
{
    public Task<int> VerificarYGenerarAlertasAsync(IEnumerable<int> productoIds) => Task.FromResult(0);
    public Task<int> GenerarAlertasStockBajoAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<List<AlertaStock>> GetAlertasPendientesAsync() => throw new NotImplementedException();
    public Task<PaginatedResult<AlertaStockViewModel>> BuscarAsync(AlertaStockFiltroViewModel filtro) => throw new NotImplementedException();
    public Task<AlertaStockViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<bool> ResolverAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<bool> IgnorarAlertaAsync(int id, string usuarioResolucion, string? observaciones = null, byte[]? rowVersion = null) => throw new NotImplementedException();
    public Task<AlertaStockEstadisticasViewModel> GetEstadisticasAsync() => throw new NotImplementedException();
    public Task<List<AlertaStock>> GetAlertasByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<AlertaStock?> VerificarYGenerarAlertaAsync(int productoId) => throw new NotImplementedException();
    public Task<int> LimpiarAlertasAntiguasAsync(int diasAntiguedad = 30, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<ProductoCriticoViewModel>> GetProductosCriticosAsync() => throw new NotImplementedException();
}

/// <summary>
/// Stub de ICreditoDisponibleService que siempre devuelve cupo ilimitado.
/// Permite pasar ValidarCupoDisponibleEnConfirmacionAsync sin seedear límites.
/// </summary>
file sealed class StubCreditoDisponibleServiceConfirmar : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default)
        => Task.FromResult(999_999m);

    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);

    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CreditoDisponibleResultado
        {
            Limite = 0m,          // 0 → ValidarCupo hace early return sin bloquear
            Disponible = 999_999m
        });

    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(
        IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items,
        string usuario)
        => throw new NotImplementedException();

    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync()
        => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceConfirmar : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubValidacionVentaServiceConfirmar : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
        int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });

    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para VentaService.ConfirmarVentaCreditoAsync.
///
/// Cada test siembra el estado mínimo necesario y verifica las postcondiciones:
/// - Crédito transiciona a Generado
/// - Venta transiciona a Confirmada
/// - Cuotas generadas con cantidad correcta
/// - Anticipo registrado en caja cuando corresponde
/// - Guards: venta inexistente, crédito no encontrado, TipoPago incorrecto,
///   crédito en estado incorrecto (sin FechaConfiguracionCredito)
/// </summary>
public class VentaServiceConfirmarCreditoTests : IDisposable
{
    private const string TestUser = "testuser";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private readonly AperturaCaja _apertura;

    public VentaServiceConfirmarCreditoTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        // Seed sincrónico de caja: necesitamos _apertura antes de construir el service
        _apertura = SeedCajaSinc();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = CreateService(existeContratoGenerado: true);
    }

    private VentaService CreateService(bool existeContratoGenerado)
    {
        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        return new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockService(),
            new StubMovimientoStockService(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserServiceConfirmar(),
            new StubValidacionVentaServiceConfirmar(),
            new StubCajaServiceConfirmar(_apertura),
            new StubCreditoDisponibleServiceConfirmar(),
            new StubContratoVentaCreditoService(existeContratoGenerado),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Seed helpers
    // -------------------------------------------------------------------------

    private AperturaCaja SeedCajaSinc()
    {
        var caja = new Caja
        {
            Codigo = "C01",
            Nombre = "Caja Test",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Cajas.Add(caja);
        _context.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.AperturasCaja.Add(apertura);
        _context.SaveChanges();
        return apertura;
    }

    private static int _counter = 100; // base para evitar colisión con IDs del seed de caja (1,2)

    /// <summary>
    /// Siembra un escenario completo con IDs autoincrement (sin forzar IDs para evitar
    /// colisiones entre tests paralelos). Retorna las entidades con sus IDs reales.
    /// El crédito queda en EstadoCredito.Configurado, listo para confirmar.
    /// </summary>
    private async Task<(Venta venta, Credito credito)> SeedVentaConfirmable(
        decimal total = 10_000m,
        decimal montoAprobado = 8_000m,
        int cantidadCuotas = 12,
        decimal tasaInteres = 3m,
        bool sinCreditoId = false)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var categoria = new Categoria { Nombre = $"Cat{suffix}" };
        var marca = new Marca { Nombre = $"Marca{suffix}" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Cliente",
            NumeroDocumento = $"9{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PROD{suffix}",
            Nombre = $"Producto {suffix}",
            PrecioVenta = total,
            StockActual = 10,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            IsDeleted = false
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRED{suffix}",
            Estado = EstadoCredito.Configurado,
            TasaInteres = tasaInteres,
            MontoSolicitado = montoAprobado,
            MontoAprobado = montoAprobado,
            SaldoPendiente = montoAprobado,
            CantidadCuotas = cantidadCuotas,
            MontoCuota = 0m,
            TotalAPagar = 0m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = sinCreditoId ? null : (int?)credito.Id,
            Numero = $"VTA{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.CreditoPersonal,
            Total = total,
            AperturaCajaId = null,
            FechaConfiguracionCredito = DateTime.UtcNow,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioUnitario = total,
            Subtotal = total,
            IsDeleted = false
        };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        return (venta, credito);
    }

    // -------------------------------------------------------------------------
    // Tests — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVentaCredito_HappyPath_RetornaTrue()
    {
        // Arrange
        var (venta, _) = await SeedVentaConfirmable();

        // Act
        var result = await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_HappyPath_VentaTransicionaAConfirmada()
    {
        // Arrange
        var (venta, _) = await SeedVentaConfirmable();

        // Act
        await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.NotNull(ventaActualizada);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
        Assert.NotNull(ventaActualizada.FechaConfirmacion);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_HappyPath_CreditoTransicionaAGenerado()
    {
        // Arrange
        var (venta, credito) = await SeedVentaConfirmable();

        // Act
        await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        var creditoActualizado = await _context.Creditos.FindAsync(credito.Id);
        Assert.NotNull(creditoActualizado);
        Assert.Equal(EstadoCredito.Generado, creditoActualizado!.Estado);
        Assert.NotNull(creditoActualizado.FechaAprobacion);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_HappyPath_GeneraCuotasCorrectas()
    {
        // Arrange
        const int cantidadCuotas = 12;
        var (venta, credito) = await SeedVentaConfirmable(
            cantidadCuotas: cantidadCuotas);

        // Act
        await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        var cuotas = await _context.Cuotas
            .Where(c => c.CreditoId == credito.Id)
            .ToListAsync();

        Assert.Equal(cantidadCuotas, cuotas.Count);
        Assert.All(cuotas, c => Assert.Equal(EstadoCuota.Pendiente, c.Estado));
        Assert.All(cuotas, c => Assert.True(c.MontoTotal > 0));
    }

    [Fact]
    public async Task ConfirmarVentaCredito_HappyPath_CuotasNumeroSecuencial()
    {
        // Arrange
        const int cantidadCuotas = 6;
        var (venta, credito) = await SeedVentaConfirmable(
            cantidadCuotas: cantidadCuotas);

        // Act
        await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        var numeros = await _context.Cuotas
            .Where(c => c.CreditoId == credito.Id)
            .OrderBy(c => c.NumeroCuota)
            .Select(c => c.NumeroCuota)
            .ToListAsync();

        Assert.Equal(Enumerable.Range(1, cantidadCuotas).ToList(), numeros);
    }

    // -------------------------------------------------------------------------
    // Tests — guards de estado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVentaCredito_VentaInexistente_RetornaFalse()
    {
        // Arrange: no hay venta con id 999
        // Act
        var result = await _service.ConfirmarVentaCreditoAsync(999);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_TipoPagoNoEsCreditoPersonal_LanzaInvalidOperation()
    {
        // Arrange: venta con TipoPago = Efectivo — sin IDs forzados
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "C",
            NumeroDocumento = $"8{suffix}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VTA{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.Efectivo,
            Total = 1_000m,
            FechaConfiguracionCredito = DateTime.UtcNow,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaCreditoAsync(venta.Id));

        Assert.Contains("Crédito Personal", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_SinCreditoId_LanzaInvalidOperation()
    {
        // Arrange: venta CreditoPersonal pero sin CreditoId
        var (venta, _) = await SeedVentaConfirmable(sinCreditoId: true);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaCreditoAsync(venta.Id));

        Assert.Contains("crédito asociado", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_CreditoEnEstadoIncorrecto_SinFechaConfiguracion_LanzaInvalidOperation()
    {
        // Arrange: crédito en estado PendienteConfiguracion, venta sin FechaConfiguracionCredito
        var suffix = Interlocked.Increment(ref _counter).ToString();
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "C",
            NumeroDocumento = $"7{suffix}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRED{suffix}",
            Estado = EstadoCredito.PendienteConfiguracion,
            TasaInteres = 3m, MontoSolicitado = 8_000m, MontoAprobado = 8_000m,
            SaldoPendiente = 8_000m, CantidadCuotas = 12, MontoCuota = 0m, TotalAPagar = 0m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1), IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Numero = $"VTA{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.CreditoPersonal,
            Total = 10_000m,
            FechaConfiguracionCredito = null,  // sin fecha → guard activo
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfirmarVentaCreditoAsync(venta.Id));

        Assert.Contains("Configurado", ex.Message);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_SinContratoGenerado_LanzaInvalidOperation()
    {
        // Arrange
        var (venta, _) = await SeedVentaConfirmable();
        var serviceSinContrato = CreateService(existeContratoGenerado: false);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => serviceSinContrato.ConfirmarVentaCreditoAsync(venta.Id));

        Assert.Equal("Debe generar e imprimir el Contrato de Venta antes de continuar.", ex.Message);

        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Presupuesto, ventaActualizada!.Estado);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_MaxCuotasCreditoPorProducto_LanzaCondicionesPagoVentaException()
    {
        var (venta, credito) = await SeedVentaConfirmable(cantidadCuotas: 12);
        var productoId = await _context.VentaDetalles
            .Where(d => d.VentaId == venta.Id)
            .Select(d => d.ProductoId)
            .SingleAsync();
        _context.ProductoCreditoRestricciones.Add(new ProductoCreditoRestriccion
        {
            ProductoId = productoId,
            Permitido = true,
            MaxCuotasCredito = 6,
            Activo = true,
            IsDeleted = false
        });
        await _context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<TheBuryProject.Services.Exceptions.CondicionesPagoVentaException>(
            () => _service.ConfirmarVentaCreditoAsync(venta.Id));

        Assert.Contains("entre", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("6", ex.Message);

        var creditoActualizado = await _context.Creditos.FindAsync(credito.Id);
        Assert.Equal(EstadoCredito.Configurado, creditoActualizado!.Estado);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_ConExcepcionDocumental_SinContratoGenerado_NoPermiteContinuar()
    {
        // Arrange: una excepción documental/autorización no debe saltear el contrato.
        var (venta, _) = await SeedVentaConfirmable();
        venta.RequiereAutorizacion = true;
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        venta.MotivoAutorizacion = "EXCEPCION_DOC|testuser|Documento exceptuado";
        await _context.SaveChangesAsync();

        var serviceSinContrato = CreateService(existeContratoGenerado: false);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => serviceSinContrato.ConfirmarVentaCreditoAsync(venta.Id));

        Assert.Equal("Debe generar e imprimir el Contrato de Venta antes de continuar.", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Test — anticipo en caja
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVentaCredito_ConAnticipo_VentaQuedsConfirmada()
    {
        // Arrange: total 10.000, montoAprobado 8.000 → anticipo = 2.000
        var (venta, _) = await SeedVentaConfirmable(
            total: 10_000m,
            montoAprobado: 8_000m);

        // Act
        var result = await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert: se completó sin excepción y la venta quedó confirmada
        Assert.True(result);
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_SinAnticipo_TotalIgualMontoAprobado_VentaConfirmada()
    {
        // Arrange: total == montoAprobado → anticipo = 0 → no se llama RegistrarMovimientoAnticipo
        var (venta, _) = await SeedVentaConfirmable(
            total: 8_000m,
            montoAprobado: 8_000m);

        // Act
        var result = await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        Assert.True(result);
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);
    }

    // -------------------------------------------------------------------------
    // Tests — crédito personal con descuento general prorrateado
    // Documentan que GenerarCuotasCreditoAsync usa credito.MontoAprobado
    // (derivado de venta.Total final) y nunca VentaDetalle.Subtotal bruto.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Siembra una venta donde VentaDetalle.Subtotal (bruto pre-descuento) difiere de
    /// VentaDetalle.SubtotalFinal (post-descuento general prorrateado).
    /// Venta.Total refleja el importe final, que es la base del crédito.
    /// </summary>
    private async Task<(Venta venta, Credito credito)> SeedVentaConfirmableConDescuentoGeneral(
        decimal subtotalBruto,
        decimal totalFinal,
        decimal montoAprobado,
        int cantidadCuotas,
        decimal tasaInteres = 3m)  // > 0 requerido por el guard de ConfirmarVentaCreditoAsync
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var categoria = new Categoria { Nombre = $"CatDesc{suffix}" };
        var marca = new Marca { Nombre = $"MarcaDesc{suffix}" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Descuento",
            NumeroDocumento = $"5{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PRODD{suffix}",
            Nombre = $"ProductoDesc {suffix}",
            PrecioVenta = subtotalBruto,
            StockActual = 10,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            IsDeleted = false
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CREDD{suffix}",
            Estado = EstadoCredito.Configurado,
            TasaInteres = tasaInteres,
            MontoSolicitado = montoAprobado,
            MontoAprobado = montoAprobado,
            SaldoPendiente = montoAprobado,
            CantidadCuotas = cantidadCuotas,
            MontoCuota = 0m,
            TotalAPagar = 0m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        // Venta.Total = totalFinal (ya post-prorrateo: Sum(SubtotalFinal))
        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Numero = $"VTAD{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.CreditoPersonal,
            Total = totalFinal,
            AperturaCajaId = null,
            FechaConfiguracionCredito = DateTime.UtcNow,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        // Detalle con Subtotal bruto (pre-descuento) ≠ SubtotalFinal (post-descuento).
        // GenerarCuotasCreditoAsync nunca lee estas columnas: usa credito.MontoAprobado.
        var detalle = new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioUnitario = subtotalBruto,
            Subtotal = subtotalBruto,        // importe bruto pre-descuento
            SubtotalFinal = totalFinal,      // importe final post-prorrateo
            IsDeleted = false
        };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        return (venta, credito);
    }

    [Fact]
    public async Task ConfirmarVentaCredito_ConDescuentoGeneral_CuotasCalculadasSobreTotalFinalNoSubtotalBruto()
    {
        // Arrange
        // VentaDetalle.Subtotal = 1.000 (bruto pre-descuento) ← valor incorrecto si hubiera bug
        // VentaDetalle.SubtotalFinal = Venta.Total = MontoAprobado = 900 (final post-descuento)
        // GenerarCuotasCreditoAsync usa credito.MontoAprobado, nunca VentaDetalle.Subtotal.
        // Si hubiera regresión usando el bruto, SaldoPendiente sería 1.000 en lugar de 900.
        const decimal subtotalBruto = 1_000m;
        const decimal totalFinal = 900m;
        const int cuotas = 3;

        var (venta, credito) = await SeedVentaConfirmableConDescuentoGeneral(
            subtotalBruto: subtotalBruto,
            totalFinal: totalFinal,
            montoAprobado: totalFinal,   // sin anticipo: financia el total final completo
            cantidadCuotas: cuotas);

        // Act
        var result = await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        Assert.True(result);

        var creditoActualizado = await _context.Creditos.FindAsync(credito.Id);
        Assert.NotNull(creditoActualizado);

        // SaldoPendiente = montoFinanciado = credito.MontoAprobado (asignación directa, sin FP)
        // Debe ser 900 (totalFinal), NO 1.000 (subtotalBruto)
        Assert.Equal(totalFinal, creditoActualizado!.SaldoPendiente);
        Assert.NotEqual(subtotalBruto, creditoActualizado.SaldoPendiente);

        // Las cuotas generadas suman TotalAPagar (calculado sobre 900 + interés, no sobre 1.000)
        // SumAsync sobre decimal no es compatible con el proveedor SQLite de tests;
        // se materializa primero y se suma en memoria.
        var montosCuotas = await _context.Cuotas
            .Where(c => c.CreditoId == credito.Id)
            .Select(c => c.MontoTotal)
            .ToListAsync();
        var totalCuotas = montosCuotas.Sum();
        Assert.Equal(creditoActualizado.TotalAPagar, totalCuotas);
        Assert.True(totalCuotas > totalFinal,     "Las cuotas incluyen interés sobre 900");
        Assert.True(totalCuotas < subtotalBruto,  "Las cuotas NO se calcularon sobre el subtotal bruto 1.000");
    }

    [Fact]
    public async Task ConfirmarVentaCredito_ConDescuentoGeneralYAnticipo_CuotasYAnticipoSobreTotalFinal()
    {
        // Arrange
        // VentaDetalle.Subtotal = 1.000 (bruto pre-descuento)
        // Venta.Total = 900 (final post-descuento)
        // MontoAprobado = 720 → anticipo = 900 - 720 = 180
        // Las cuotas deben calcularse sobre 720, no sobre 1.000 ni sobre 900
        const decimal subtotalBruto = 1_000m;
        const decimal totalFinal = 900m;
        const decimal montoAprobado = 720m;
        const int cuotas = 3;

        var (venta, credito) = await SeedVentaConfirmableConDescuentoGeneral(
            subtotalBruto: subtotalBruto,
            totalFinal: totalFinal,
            montoAprobado: montoAprobado,
            cantidadCuotas: cuotas);

        // Act
        var result = await _service.ConfirmarVentaCreditoAsync(venta.Id);

        // Assert
        Assert.True(result);

        var creditoActualizado = await _context.Creditos.FindAsync(credito.Id);
        Assert.NotNull(creditoActualizado);

        // SaldoPendiente = montoFinanciado = 720 (MontoAprobado)
        // No debe ser 1.000 (subtotalBruto) ni 900 (totalFinal sin descontar el anticipo)
        Assert.Equal(montoAprobado, creditoActualizado!.SaldoPendiente);
        Assert.NotEqual(subtotalBruto, creditoActualizado.SaldoPendiente);
        Assert.NotEqual(totalFinal, creditoActualizado.SaldoPendiente);

        // Anticipo = venta.Total - credito.MontoAprobado = 900 - 720 = 180
        // El stub de caja acepta el movimiento sin excepción; verificamos que la venta confirmó
        var ventaActualizada = await _context.Ventas.FindAsync(venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada!.Estado);

        // Cuotas generadas suman TotalAPagar (sobre 720 + interés, no sobre 1.000 ni 900)
        var montosCuotas = await _context.Cuotas
            .Where(c => c.CreditoId == credito.Id)
            .Select(c => c.MontoTotal)
            .ToListAsync();
        var totalCuotas = montosCuotas.Sum();
        Assert.Equal(creditoActualizado.TotalAPagar, totalCuotas);
        Assert.True(totalCuotas > montoAprobado,   "Las cuotas incluyen interés sobre 720");
        Assert.True(totalCuotas < subtotalBruto,   "Las cuotas NO se calcularon sobre el subtotal bruto 1.000");
    }
}
