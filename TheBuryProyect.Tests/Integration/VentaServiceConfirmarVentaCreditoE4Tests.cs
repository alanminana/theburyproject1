using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
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

// ---------------------------------------------------------------------------
// Stubs file-scoped para la suite E4 (ConfirmarVentaAsync + CreditoPersonal)
// ---------------------------------------------------------------------------

file sealed class StubValidacionVentaE4 : IValidacionVentaService
{
    private readonly bool _requiereAutorizacion;

    public StubValidacionVentaE4(bool requiereAutorizacion = false)
        => _requiereAutorizacion = requiereAutorizacion;

    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
        int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });

    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId)
        => Task.FromResult(new ValidacionVentaResult
        {
            NoViable = false,
            PendienteRequisitos = false,
            RequiereAutorizacion = _requiereAutorizacion
        });

    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto)
        => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado)
        => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId)
        => throw new NotImplementedException();
}

file sealed class StubCajaServiceE4 : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaServiceE4(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(
        int creditoId, string creditoNumero, decimal montoAnticipo, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());

    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(
        int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario)
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
    public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotImplementedException();
    public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubMovimientoStockE4 : IMovimientoStockService
{
    public Task<List<MovimientoStock>> RegistrarSalidasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> salidas,
        string motivo,
        string? usuarioActual = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
        => Task.FromResult(new List<MovimientoStock>());

    public Task<List<MovimientoStock>> RegistrarEntradasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> entradas,
        string motivo,
        string? usuarioActual = null,
        int? ordenCompraId = null,
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
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
}

file sealed class StubAlertaStockE4 : IAlertaStockService
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

file sealed class StubCreditoDisponibleE4 : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(0m);
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CreditoDisponibleResultado { Limite = 0m, Disponible = 999_999m });
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(
        IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario)
        => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync()
        => throw new NotImplementedException();
}

file sealed class StubCurrentUserE4 : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para el flujo E4 canónico:
/// VentaService.ConfirmarVentaAsync con TipoPago.CreditoPersonal y DatosCreditoPersonallJson.
///
/// El flujo E4 es distinto de ConfirmarVentaCreditoAsync (legacy):
/// - No requiere que el Credito esté en estado Configurado
/// - Lee el plan desde DatosCreditoPersonallJson
/// - Crea VentaCreditoCuota[] (no Cuota[])
/// - Descuenta Credito.SaldoPendiente
/// - Limpia el JSON tras confirmar
/// - Asigna CreditoId a la venta desde el JSON (no desde la venta original)
/// </summary>
public class VentaServiceConfirmarVentaCreditoE4Tests : IDisposable
{
    private const string TestUser = "testuser";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AperturaCaja _apertura;

    private static int _counter = 300;

    public VentaServiceConfirmarVentaCreditoE4Tests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _apertura = SeedCajaSinc();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Factory del servicio
    // -------------------------------------------------------------------------

    private VentaService CreateService(
        bool existeContratoGenerado = true,
        bool validacionRequiereAutorizacion = false)
    {
        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        return new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockE4(),
            new StubMovimientoStockE4(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserE4(),
            new StubValidacionVentaE4(validacionRequiereAutorizacion),
            new StubCajaServiceE4(_apertura),
            new StubCreditoDisponibleE4(),
            new StubContratoVentaCreditoService(existeContratoGenerado),
            new StubConfiguracionPagoServiceVenta());
    }

    // -------------------------------------------------------------------------
    // Seed helpers
    // -------------------------------------------------------------------------

    private AperturaCaja SeedCajaSinc()
    {
        var caja = new Caja
        {
            Codigo = "CE4", Nombre = "Caja E4 Test",
            IsDeleted = false, RowVersion = new byte[8]
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

    /// <summary>
    /// Siembra el escenario E4 canónico:
    /// - Credito activo con SaldoPendiente suficiente
    /// - Venta con TipoPago=CreditoPersonal, DatosCreditoPersonallJson válido, CreditoId=null
    /// - LimiteAplicado ya seteado para evitar CapturarSnapshot (no relevante en E4)
    /// </summary>
    private async Task<(Venta venta, Credito credito)> SeedVentaE4(
        decimal total = 5_000m,
        decimal montoAFinanciar = 5_000m,
        decimal saldoCredito = 20_000m,
        int cantidadCuotas = 6,
        decimal montoCuota = 900m,
        bool requiereAutorizacion = false,
        EstadoAutorizacionVenta estadoAutorizacion = EstadoAutorizacionVenta.NoRequiere)
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var categoria = new Categoria { Nombre = $"Cat{suffix}" };
        var marca = new Marca { Nombre = $"Marca{suffix}" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "E4Test", Apellido = $"Cliente{suffix}",
            NumeroDocumento = $"4{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRE-E4-{suffix}",
            Estado = EstadoCredito.Activo,
            TasaInteres = 3m,
            MontoSolicitado = saldoCredito,
            MontoAprobado = saldoCredito,
            SaldoPendiente = saldoCredito,
            CantidadCuotas = cantidadCuotas,
            MontoCuota = 0m,
            TotalAPagar = 0m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();

        var planJson = JsonSerializer.Serialize(new
        {
            CreditoId = credito.Id,
            MontoAFinanciar = montoAFinanciar,
            CantidadCuotas = cantidadCuotas,
            MontoCuota = montoCuota,
            TotalAPagar = montoCuota * cantidadCuotas,
            TasaInteresMensual = 3m,
            FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
            InteresTotal = (montoCuota * cantidadCuotas) - montoAFinanciar
        });

        var producto = new Producto
        {
            Codigo = $"PE4{suffix}",
            Nombre = $"Prod{suffix}",
            PrecioVenta = total,
            StockActual = 10,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            IsDeleted = false
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = null,
            Numero = $"VTAE4-{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.CreditoPersonal,
            Total = total,
            DatosCreditoPersonallJson = planJson,
            LimiteAplicado = 1m,
            RequiereAutorizacion = requiereAutorizacion,
            EstadoAutorizacion = estadoAutorizacion,
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
    // Test 1 — VentaCreditoCuotas creadas correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVentaAsync_CreditoPersonalConJson_CreaVentaCreditoCuotas()
    {
        // Arrange
        const int cantidadCuotas = 6;
        const decimal montoCuota = 900m;
        var (venta, credito) = await SeedVentaE4(
            cantidadCuotas: cantidadCuotas,
            montoCuota: montoCuota);

        var service = CreateService();

        // Act
        var result = await service.ConfirmarVentaAsync(venta.Id);

        // Assert
        Assert.True(result);

        var cuotas = await _context.VentaCreditoCuotas
            .AsNoTracking()
            .Where(c => c.VentaId == venta.Id)
            .OrderBy(c => c.NumeroCuota)
            .ToListAsync();

        Assert.Equal(cantidadCuotas, cuotas.Count);
        Assert.All(cuotas, c => Assert.Equal(montoCuota, c.Monto));
        Assert.All(cuotas, c => Assert.Equal(credito.Id, c.CreditoId));
        Assert.All(cuotas, c => Assert.False(c.Pagada));

        var numeros = cuotas.Select(c => c.NumeroCuota).ToList();
        Assert.Equal(Enumerable.Range(1, cantidadCuotas).ToList(), numeros);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Credito.SaldoPendiente decrementado por montoAFinanciar
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVentaAsync_CreditoPersonalConJson_DecrementaSaldoCredito()
    {
        // Arrange
        const decimal saldoInicial = 20_000m;
        const decimal montoAFinanciar = 5_000m;
        var (venta, credito) = await SeedVentaE4(
            montoAFinanciar: montoAFinanciar,
            saldoCredito: saldoInicial);

        var service = CreateService();

        // Act
        await service.ConfirmarVentaAsync(venta.Id);

        // Assert
        var creditoActualizado = await _context.Creditos
            .AsNoTracking()
            .FirstAsync(c => c.Id == credito.Id);

        Assert.Equal(saldoInicial - montoAFinanciar, creditoActualizado.SaldoPendiente);
    }

    // -------------------------------------------------------------------------
    // Test 3 — DatosCreditoPersonallJson limpiado y venta Confirmada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVentaAsync_CreditoPersonalConJson_LimpiaJsonYConfirmaVenta()
    {
        // Arrange
        var (venta, _) = await SeedVentaE4();
        var service = CreateService();

        // Act
        await service.ConfirmarVentaAsync(venta.Id);

        // Assert
        var ventaActualizada = await _context.Ventas
            .AsNoTracking()
            .FirstAsync(v => v.Id == venta.Id);

        Assert.Null(ventaActualizada.DatosCreditoPersonallJson);
        Assert.Equal(EstadoVenta.Confirmada, ventaActualizada.Estado);
        Assert.NotNull(ventaActualizada.FechaConfirmacion);
    }

    // -------------------------------------------------------------------------
    // Test 4 — Guard E4: venta requiere autorización no otorgada → lanza
    // -------------------------------------------------------------------------

    /// <summary>
    /// Caracterización: el guard E4 en ConfirmarVentaAsync (línea ~717) verifica
    /// venta.RequiereAutorizacion independientemente de la validación del servicio.
    /// Si la venta tiene RequiereAutorizacion=true y EstadoAutorizacion!=Autorizada,
    /// el bloque E4 lanza antes de crear el crédito, incluso si ValidarConfirmacionVentaAsync
    /// devuelve RequiereAutorizacion=false (ya que la venta tiene el flag directo).
    /// </summary>
    [Fact]
    public async Task ConfirmarVentaAsync_CreditoPersonalConJson_VentaSinAutorizacion_LanzaAntesDeCrearCredito()
    {
        // Arrange: venta.RequiereAutorizacion=true pero NO autorizada
        var (venta, credito) = await SeedVentaE4(
            requiereAutorizacion: true,
            estadoAutorizacion: EstadoAutorizacionVenta.PendienteAutorizacion);

        // El stub de validación devuelve RequiereAutorizacion=false (no bloquea en la validación de servicio).
        // El bloque E4 sí evalúa venta.RequiereAutorizacion directamente.
        var service = CreateService(validacionRequiereAutorizacion: false);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmarVentaAsync(venta.Id));

        Assert.Contains("autorización", ex.Message, StringComparison.OrdinalIgnoreCase);

        // El crédito no debe haber sido modificado (rollback)
        var creditoSinCambios = await _context.Creditos
            .AsNoTracking()
            .FirstAsync(c => c.Id == credito.Id);
        Assert.Equal(20_000m, creditoSinCambios.SaldoPendiente);

        // No se crearon cuotas
        var cuotas = await _context.VentaCreditoCuotas
            .AsNoTracking()
            .Where(c => c.VentaId == venta.Id)
            .ToListAsync();
        Assert.Empty(cuotas);
    }

    // -------------------------------------------------------------------------
    // Test 5 — Segunda confirmación bloqueada por estado Confirmada
    // -------------------------------------------------------------------------

    /// <summary>
    /// Caracterización del comportamiento idempotente del estado:
    /// Tras ConfirmarVentaAsync exitosa, venta.Estado == Confirmada.
    /// Una segunda llamada es bloqueada por ValidarEstadoParaConfirmacion antes
    /// de llegar al bloque E4, evitando así duplicación de cuotas.
    /// El DatosCreditoPersonallJson ya está null, por lo que el bloque E4 tampoco
    /// se ejecutaría aunque el guard de estado no existiera.
    /// </summary>
    [Fact]
    public async Task ConfirmarVentaAsync_CreditoPersonalConJson_SegundaLlamada_BloqueadaPorEstado()
    {
        // Arrange
        var (venta, _) = await SeedVentaE4(cantidadCuotas: 3);
        var service = CreateService();

        // Primera confirmación — debe ser exitosa
        var primeraConfirmacion = await service.ConfirmarVentaAsync(venta.Id);
        Assert.True(primeraConfirmacion);

        var cuotasTrasPrimera = await _context.VentaCreditoCuotas
            .AsNoTracking()
            .Where(c => c.VentaId == venta.Id)
            .CountAsync();
        Assert.Equal(3, cuotasTrasPrimera);

        // Act — segunda llamada sobre una venta ya Confirmada
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConfirmarVentaAsync(venta.Id));

        // Assert — el guard de estado bloquea antes de cualquier efecto
        Assert.Contains("Presupuesto", ex.Message);

        var cuotasTrasSegunda = await _context.VentaCreditoCuotas
            .AsNoTracking()
            .Where(c => c.VentaId == venta.Id)
            .CountAsync();

        // No se duplicaron cuotas
        Assert.Equal(3, cuotasTrasSegunda);
    }

    // -------------------------------------------------------------------------
    // Test adicional — CreditoId asignado a la venta desde el JSON
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmarVentaAsync_CreditoPersonalConJson_AsignaCreditoIdDesdeJson()
    {
        // Arrange: venta empieza SIN CreditoId (E4: se asigna en la confirmación)
        var (venta, credito) = await SeedVentaE4();
        Assert.Null(venta.CreditoId);

        var service = CreateService();

        // Act
        await service.ConfirmarVentaAsync(venta.Id);

        // Assert
        var ventaActualizada = await _context.Ventas
            .AsNoTracking()
            .FirstAsync(v => v.Id == venta.Id);

        Assert.Equal(credito.Id, ventaActualizada.CreditoId);
    }
}
