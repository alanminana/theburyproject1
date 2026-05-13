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

// ---------------------------------------------------------------------------
// Stubs file-scoped — no colisionan con otros archivos de test
// ---------------------------------------------------------------------------

file sealed class StubNotificacionFase61 : INotificacionService
{
    public Task<Notificacion> CrearNotificacionAsync(CrearNotificacionViewModel model)
        => throw new NotImplementedException();
    public Task CrearNotificacionParaUsuarioAsync(string usuario, TipoNotificacion tipo,
        string titulo, string mensaje, string? url = null,
        PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        => Task.CompletedTask;
    public Task CrearNotificacionParaRolAsync(string rol, TipoNotificacion tipo,
        string titulo, string mensaje, string? url = null,
        PrioridadNotificacion prioridad = PrioridadNotificacion.Media)
        => Task.CompletedTask;
    public Task<List<NotificacionViewModel>> ObtenerNotificacionesUsuarioAsync(
        string usuario, bool soloNoLeidas = false, int limite = 50)
        => throw new NotImplementedException();
    public Task<int> ObtenerCantidadNoLeidasAsync(string usuario)
        => throw new NotImplementedException();
    public Task<Notificacion?> ObtenerNotificacionPorIdAsync(int id)
        => throw new NotImplementedException();
    public Task MarcarComoLeidaAsync(int notificacionId, string usuario, byte[]? rowVersion = null)
        => throw new NotImplementedException();
    public Task MarcarTodasComoLeidasAsync(string usuario)
        => throw new NotImplementedException();
    public Task EliminarNotificacionAsync(int id, string usuario, byte[]? rowVersion = null)
        => throw new NotImplementedException();
    public Task LimpiarNotificacionesAntiguasAsync(int diasAntiguedad = 30)
        => throw new NotImplementedException();
    public Task<ListaNotificacionesViewModel> ObtenerResumenNotificacionesAsync(string usuario)
        => throw new NotImplementedException();
}

file sealed class StubValidacionVentaFase61 : IValidacionVentaService
{
    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
        int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(new ValidacionVentaResult { NoViable = false });
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId)
        => Task.FromResult(new ValidacionVentaResult
        {
            NoViable = false,
            PendienteRequisitos = false,
            RequiereAutorizacion = false
        });
    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto)
        => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado)
        => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId)
        => throw new NotImplementedException();
}

file sealed class StubMovimientoStockFase61 : IMovimientoStockService
{
    public Task<List<MovimientoStock>> RegistrarSalidasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> salidas,
        string motivo, string? usuarioActual = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
        => Task.FromResult(new List<MovimientoStock>());
    public Task<List<MovimientoStock>> RegistrarEntradasAsync(
        List<(int productoId, decimal cantidad, string? referencia)> entradas,
        string motivo, string? usuarioActual = null, int? ordenCompraId = null,
        IReadOnlyList<MovimientoStockCostoLinea>? costos = null)
        => Task.FromResult(new List<MovimientoStock>());
    public Task<IEnumerable<MovimientoStock>> GetAllAsync() => throw new NotImplementedException();
    public Task<MovimientoStock?> GetByIdAsync(int id) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByProductoIdAsync(int productoId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByOrdenCompraIdAsync(int ordenCompraId) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByTipoAsync(TipoMovimiento tipo) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> GetByFechaRangoAsync(DateTime fechaDesde, DateTime fechaHasta) => throw new NotImplementedException();
    public Task<IEnumerable<MovimientoStock>> SearchAsync(int? productoId = null, TipoMovimiento? tipo = null,
        DateTime? fechaDesde = null, DateTime? fechaHasta = null,
        string? orderBy = null, string? orderDirection = "desc") => throw new NotImplementedException();
    public Task<MovimientoStock> CreateAsync(MovimientoStock movimiento) => throw new NotImplementedException();
    public Task<MovimientoStock> RegistrarAjusteAsync(int productoId, TipoMovimiento tipo,
        decimal cantidad, string? referencia, string motivo,
        string? usuarioActual = null, int? ordenCompraId = null) => throw new NotImplementedException();
    public Task<bool> HayStockDisponibleAsync(int productoId, decimal cantidad) => throw new NotImplementedException();
    public Task<(bool Valido, string Mensaje)> ValidarCantidadAsync(decimal cantidad) => throw new NotImplementedException();
}

file sealed class StubAlertaStockFase61 : IAlertaStockService
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

file sealed class StubCreditoDisponibleFase61 : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default)
        => Task.FromResult(999_999m);
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

file sealed class StubCurrentUserFase61 : ICurrentUserService
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
/// Prueba integral Fase 6.1.
///
/// Cubre el flujo completo que une las piezas de Fase 5:
///   configuración global de pago → aplicar ajuste → confirmar venta
///   → registrar en caja → generar comprobante.
///
/// El gap que cierra: los tests previos verifican GuardarDatosTarjetaAsync y
/// el builder de forma aislada, pero ninguno verifica que ConfirmarVentaAsync
/// pasa el total AJUSTADO al CajaService real y que el MovimientoCaja persiste
/// ese total y no el original.
///
/// Usa CajaService real (no stub) para confirmar la persistencia.
/// Confirma que ProductoCondicionPagoPlanId no interviene (flujo canónico global,
/// sin legacy por producto).
/// </summary>
public class AjusteGlobalVentaCajaComprobanteTests : IDisposable
{
    private const string TestUser = "testuser";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _ventaService;
    private readonly AperturaCaja _apertura;

    private static int _counter = 5000;

    public AjusteGlobalVentaCajaComprobanteTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        _apertura = SeedCajaSinc();

        // CajaService real: persiste MovimientoCaja en el mismo AppDbContext.
        // Permite verificar que el monto registrado es el total ajustado, no el original.
        var cajaService = new CajaService(
            _context,
            mapper,
            NullLogger<CajaService>.Instance,
            new StubNotificacionFase61());

        _ventaService = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            new StubAlertaStockFase61(),
            new StubMovimientoStockFase61(),
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(_context),
            new StubCurrentUserFase61(),
            new StubValidacionVentaFase61(),
            cajaService,
            new StubCreditoDisponibleFase61(),
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ── Seed helpers ─────────────────────────────────────────────────

    private AperturaCaja SeedCajaSinc()
    {
        var caja = new Caja
        {
            Codigo = "CAJAF61",
            Nombre = "Caja Fase 6.1",
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

    /// <summary>
    /// Siembra ConfiguracionPago + ConfiguracionPagoPlan + Venta con VentaDetalle.
    /// TipoPago.MercadoPago está en TiposPagoConPlanes → el plan global aplica vía
    /// GuardarDatosTarjetaAsync / ValidarYObtenerPlanPagoGlobalAsync.
    /// </summary>
    private async Task<(Venta Venta, ConfiguracionPagoPlan Plan)> SeedVentaConPlanGlobal(
        decimal totalOriginal = 1_000m,
        decimal ajustePorcentaje = 10m,
        int cantidadCuotas = 3,
        string etiquetaPlan = "MP 3 cuotas +10")
    {
        var suffix = Interlocked.Increment(ref _counter).ToString();

        var medio = new ConfiguracionPago
        {
            TipoPago = TipoPago.MercadoPago,
            Nombre = $"MP F61-{suffix}",
            Activo = true,
            IsDeleted = false
        };
        _context.ConfiguracionesPago.Add(medio);
        await _context.SaveChangesAsync();

        var plan = new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = medio.Id,
            ConfiguracionTarjetaId = null,
            TipoPago = TipoPago.MercadoPago,
            CantidadCuotas = cantidadCuotas,
            AjustePorcentaje = ajustePorcentaje,
            Etiqueta = etiquetaPlan,
            Activo = true,
            IsDeleted = false
        };
        _context.ConfiguracionPagoPlanes.Add(plan);
        await _context.SaveChangesAsync();

        var categoria = new Categoria { Nombre = $"CatF61-{suffix}" };
        var marca = new Marca { Nombre = $"MarcaF61-{suffix}" };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = $"F61-{suffix}",
            NumeroDocumento = $"F61{suffix}",
            NivelRiesgo = NivelRiesgoCredito.AprobadoTotal,
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PF61-{suffix}",
            Nombre = $"Prod F61 {suffix}",
            PrecioVenta = totalOriginal,
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
            Numero = $"VF61-{suffix}",
            Estado = EstadoVenta.Presupuesto,
            TipoPago = TipoPago.MercadoPago,
            Total = totalOriginal,
            RequiereAutorizacion = false,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioUnitario = totalOriginal,
            Subtotal = totalOriginal,
            IsDeleted = false
        };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();

        return (venta, plan);
    }

    // ── Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Test integral principal de Fase 6.1.
    ///
    /// Flujo:
    ///   1. Crear configuración global de pago + plan con recargo del 10%
    ///   2. Aplicar plan sobre venta (GuardarDatosTarjetaAsync)
    ///   3. Confirmar venta (ConfirmarVentaAsync)
    ///
    /// Verifica:
    ///   - Venta.Total = totalAjustado (no el original)
    ///   - DatosTarjeta persiste snapshot: porcentaje, monto, cuotas, etiqueta
    ///   - ProductoCondicionPagoPlanId == null (flujo global canónico, sin legacy por producto)
    ///   - MovimientoCaja.Monto == totalAjustado (CajaService real persiste en DB)
    ///   - El movimiento NO guarda el total original
    /// </summary>
    [Fact]
    public async Task FlujoCanonico_AjusteGlobal_ConfirmaVenta_MovimientoCajaConTotalAjustado()
    {
        // Arrange
        const decimal totalOriginal = 1_000m;
        const decimal ajustePorcentaje = 10m;
        const int cantidadCuotas = 3;
        // 10% de 1000 = 100 → total ajustado = 1100
        const decimal totalAjustado = 1_100m;
        const decimal montoAjuste = 100m;
        // round(1100/3, 2, AwayFromZero) = 366.67
        const decimal valorCuotaEsperado = 366.67m;
        const string etiquetaPlan = "MP 3 cuotas +10";

        var (venta, plan) = await SeedVentaConPlanGlobal(
            totalOriginal: totalOriginal,
            ajustePorcentaje: ajustePorcentaje,
            cantidadCuotas: cantidadCuotas,
            etiquetaPlan: etiquetaPlan);

        // Act 1: aplicar plan global — ajusta Venta.Total y persiste snapshot en DatosTarjeta
        var guardado = await _ventaService.GuardarDatosTarjetaAsync(venta.Id, new DatosTarjetaViewModel
        {
            NombreTarjeta = "Mercado Pago",
            TipoTarjeta = TipoTarjeta.Credito,
            ConfiguracionPagoPlanId = plan.Id
        });
        Assert.True(guardado);

        // Assert 1: Venta.Total fue ajustado y persistido
        var ventaTrasGuardar = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(totalAjustado, ventaTrasGuardar.Total);

        // Assert 2: snapshot en DatosTarjeta (flujo global canónico)
        var datos = await _context.DatosTarjeta.AsNoTracking().SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(plan.Id, datos.ConfiguracionPagoPlanId);
        Assert.Equal(ajustePorcentaje, datos.PorcentajeAjustePagoAplicado);
        Assert.Equal(montoAjuste, datos.MontoAjustePagoAplicado);
        Assert.Equal(cantidadCuotas, datos.CantidadCuotas);
        Assert.Equal(valorCuotaEsperado, datos.MontoCuota);
        Assert.Equal(etiquetaPlan, datos.NombrePlanPagoSnapshot);
        // Sin legacy por producto
        Assert.Null(datos.ProductoCondicionPagoPlanId);
        Assert.Null(datos.PorcentajeAjustePlanAplicado);
        Assert.Null(datos.MontoAjustePlanAplicado);

        // Act 2: confirmar venta — CajaService real registra MovimientoCaja
        var confirmada = await _ventaService.ConfirmarVentaAsync(venta.Id);
        Assert.True(confirmada);

        // Assert 3: estado de la venta
        var ventaConfirmada = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaConfirmada.Estado);
        Assert.NotNull(ventaConfirmada.FechaConfirmacion);

        // Assert 4: MovimientoCaja persiste el total AJUSTADO, no el original
        var movimiento = await _context.MovimientosCaja
            .AsNoTracking()
            .SingleAsync(m => m.VentaId == venta.Id);
        Assert.Equal(totalAjustado, movimiento.Monto);
        Assert.NotEqual(totalOriginal, movimiento.Monto);
        Assert.Equal(TipoPago.MercadoPago, movimiento.TipoPago);
        Assert.Equal(TipoMovimientoCaja.Ingreso, movimiento.Tipo);
        Assert.Equal(_apertura.Id, movimiento.AperturaCajaId);
    }

    /// <summary>
    /// Test integral Fase 6.1B — variante descuento global.
    ///
    /// Flujo equivalente al test de recargo, con ajuste negativo del -10%.
    ///   1. Crear configuración global + plan con descuento -10%.
    ///   2. Aplicar plan sobre venta (GuardarDatosTarjetaAsync).
    ///   3. Confirmar venta (ConfirmarVentaAsync).
    ///
    /// Verifica:
    ///   - Venta.Total = 900 (1000 - 100)
    ///   - DatosTarjeta persiste snapshot: porcentaje -10, monto -100, cuotas, etiqueta
    ///   - ProductoCondicionPagoPlanId == null
    ///   - MovimientoCaja.Monto == 900 (CajaService real persiste en DB)
    /// </summary>
    [Fact]
    public async Task FlujoCanonico_DescuentoGlobal_ConfirmaVenta_MovimientoCajaConTotalDescontado()
    {
        // Arrange
        const decimal totalOriginal = 1_000m;
        const decimal ajustePorcentaje = -10m;
        const int cantidadCuotas = 3;
        // -10% de 1000 = -100 → total ajustado = 900
        const decimal totalAjustado = 900m;
        const decimal montoAjuste = -100m;
        // round(900/3, 2, AwayFromZero) = 300.00
        const decimal valorCuotaEsperado = 300.00m;
        const string etiquetaPlan = "MP 3 cuotas -10";

        var (venta, plan) = await SeedVentaConPlanGlobal(
            totalOriginal: totalOriginal,
            ajustePorcentaje: ajustePorcentaje,
            cantidadCuotas: cantidadCuotas,
            etiquetaPlan: etiquetaPlan);

        // Act 1: aplicar plan global — descuenta Venta.Total y persiste snapshot en DatosTarjeta
        var guardado = await _ventaService.GuardarDatosTarjetaAsync(venta.Id, new DatosTarjetaViewModel
        {
            NombreTarjeta = "Mercado Pago",
            TipoTarjeta = TipoTarjeta.Credito,
            ConfiguracionPagoPlanId = plan.Id
        });
        Assert.True(guardado);

        // Assert 1: Venta.Total fue descontado y persistido
        var ventaTrasGuardar = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(totalAjustado, ventaTrasGuardar.Total);

        // Assert 2: snapshot en DatosTarjeta con valores negativos del descuento global
        var datos = await _context.DatosTarjeta.AsNoTracking().SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(plan.Id, datos.ConfiguracionPagoPlanId);
        Assert.Equal(ajustePorcentaje, datos.PorcentajeAjustePagoAplicado);
        Assert.Equal(montoAjuste, datos.MontoAjustePagoAplicado);
        Assert.Equal(cantidadCuotas, datos.CantidadCuotas);
        Assert.Equal(valorCuotaEsperado, datos.MontoCuota);
        Assert.Equal(etiquetaPlan, datos.NombrePlanPagoSnapshot);
        // Sin legacy por producto
        Assert.Null(datos.ProductoCondicionPagoPlanId);
        Assert.Null(datos.PorcentajeAjustePlanAplicado);
        Assert.Null(datos.MontoAjustePlanAplicado);

        // Act 2: confirmar venta — CajaService real registra MovimientoCaja
        var confirmada = await _ventaService.ConfirmarVentaAsync(venta.Id);
        Assert.True(confirmada);

        // Assert 3: estado de la venta
        var ventaConfirmada = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(EstadoVenta.Confirmada, ventaConfirmada.Estado);
        Assert.NotNull(ventaConfirmada.FechaConfirmacion);

        // Assert 4: MovimientoCaja persiste el total DESCONTADO, no el original
        var movimiento = await _context.MovimientosCaja
            .AsNoTracking()
            .SingleAsync(m => m.VentaId == venta.Id);
        Assert.Equal(totalAjustado, movimiento.Monto);
        Assert.NotEqual(totalOriginal, movimiento.Monto);
        Assert.Equal(TipoPago.MercadoPago, movimiento.TipoPago);
        Assert.Equal(TipoMovimientoCaja.Ingreso, movimiento.Tipo);
        Assert.Equal(_apertura.Id, movimiento.AperturaCajaId);
    }

    /// <summary>
    /// Verifica que FacturaComprobanteBuilder.Build propaga el snapshot del plan global
    /// cuando la venta tiene DatosTarjeta con PorcentajeAjustePagoAplicado y sin pago por ítem.
    ///
    /// Complementa el test anterior: confirma que el comprobante final refleja
    /// los mismos valores que quedan persistidos en DatosTarjeta.
    /// </summary>
    [Fact]
    public void FacturaComprobanteBuilder_ConSnapshotPlanGlobal_PropagaAjusteYEtiqueta()
    {
        // Arrange: entidades equivalentes a las que persiste el flujo del test anterior
        const decimal totalAjustado = 1_100m;
        const decimal montoAjuste = 100m;
        const decimal ajustePorcentaje = 10m;
        const int cantidadCuotas = 3;
        const string etiquetaPlan = "MP 3 cuotas +10";

        var factura = new Factura
        {
            Numero = "FC-F61-TEST",
            Tipo = TipoFactura.B,
            FechaEmision = DateTime.UtcNow,
            Total = totalAjustado,
            Venta = new Venta
            {
                Numero = "VF61-COMP",
                TipoPago = TipoPago.MercadoPago,
                Total = totalAjustado,
                DatosTarjeta = new DatosTarjeta
                {
                    NombreTarjeta = "Mercado Pago",
                    TipoTarjeta = TipoTarjeta.Credito,
                    ConfiguracionPagoPlanId = 1,
                    CantidadCuotas = cantidadCuotas,
                    PorcentajeAjustePagoAplicado = ajustePorcentaje,
                    MontoAjustePagoAplicado = montoAjuste,
                    NombrePlanPagoSnapshot = etiquetaPlan,
                    // Sin legacy por producto
                    ProductoCondicionPagoPlanId = null,
                    PorcentajeAjustePlanAplicado = null,
                    MontoAjustePlanAplicado = null
                },
                Cliente = new Cliente
                {
                    Nombre = "Test",
                    Apellido = "Integral",
                    NumeroDocumento = "99999999"
                },
                Detalles = new List<VentaDetalle>
                {
                    new()
                    {
                        Producto = new Producto { Codigo = "PF61", Nombre = "Producto F61" },
                        Cantidad = 1,
                        PrecioUnitario = 1_000m,
                        Subtotal = 1_000m,
                        SubtotalFinal = 1_000m
                    }
                }
            }
        };

        // Act
        var comprobante = FacturaComprobanteBuilder.Build(factura);

        // Assert: el builder usa PorcentajeAjustePagoAplicado (global) sobre PorcentajeAjustePlanAplicado (legacy)
        Assert.Equal(ajustePorcentaje, comprobante.Totales.PorcentajeAjustePlanAplicado);
        Assert.Equal(montoAjuste, comprobante.Totales.MontoAjustePlanAplicado);
        Assert.Equal(etiquetaPlan, comprobante.Totales.NombrePlanPagoSnapshot);
        Assert.Equal(cantidadCuotas, comprobante.Totales.CantidadCuotas);
        Assert.Equal(totalAjustado, comprobante.Totales.Total);
        // Sin grupos de pago por ítem (flujo global, no por producto)
        Assert.Empty(comprobante.GruposPagoPorItem);
    }

    /// <summary>
    /// Fase 6.5 — Verifica que ConfirmarVentaAsync no modifica ni resetea el snapshot de
    /// DatosTarjeta ya persistido por GuardarDatosTarjetaAsync.
    /// ConfirmarVentaAsync incluye DatosTarjeta vía CargarVentaCompleta pero no lo escribe.
    /// </summary>
    [Fact]
    public async Task ConfirmarVenta_ConDatosTarjetaGlobal_SnapshotDatosTarjetaInmutablePostConfirmacion()
    {
        const decimal totalOriginal = 2_000m;
        const decimal ajustePorcentaje = 15m;
        // 15% de 2000 = 300 → total ajustado = 2300
        const decimal totalAjustado = 2_300m;
        const string etiqueta = "MP 2 cuotas +15";

        var (venta, plan) = await SeedVentaConPlanGlobal(
            totalOriginal: totalOriginal,
            ajustePorcentaje: ajustePorcentaje,
            cantidadCuotas: 2,
            etiquetaPlan: etiqueta);

        var guardado = await _ventaService.GuardarDatosTarjetaAsync(venta.Id, new DatosTarjetaViewModel
        {
            NombreTarjeta = "Mercado Pago",
            TipoTarjeta = TipoTarjeta.Credito,
            ConfiguracionPagoPlanId = plan.Id
        });
        Assert.True(guardado);

        var confirmada = await _ventaService.ConfirmarVentaAsync(venta.Id);
        Assert.True(confirmada);

        // Snapshot DatosTarjeta intacto después del confirm
        var datos = await _context.DatosTarjeta.AsNoTracking().SingleAsync(d => d.VentaId == venta.Id);
        Assert.Equal(plan.Id, datos.ConfiguracionPagoPlanId);
        Assert.Equal(ajustePorcentaje, datos.PorcentajeAjustePagoAplicado);
        Assert.Equal(300m, datos.MontoAjustePagoAplicado);
        Assert.Equal(2, datos.CantidadCuotas);
        Assert.Equal(etiqueta, datos.NombrePlanPagoSnapshot);
        // Sin legacy por producto — no contaminado por confirm
        Assert.Null(datos.ProductoCondicionPagoPlanId);
        Assert.Null(datos.PorcentajeAjustePlanAplicado);
        Assert.Null(datos.MontoAjustePlanAplicado);

        // Total no recalculado por ConfirmarVentaAsync
        var ventaConfirmada = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(totalAjustado, ventaConfirmada.Total);
        Assert.Equal(EstadoVenta.Confirmada, ventaConfirmada.Estado);
    }

    /// <summary>
    /// Variante Fase 6.1B: verifica que FacturaComprobanteBuilder.Build propaga el snapshot
    /// de descuento global (valores negativos) sin mezclar con campos legacy por producto.
    /// </summary>
    [Fact]
    public void FacturaComprobanteBuilder_ConSnapshotDescuentoGlobal_PropagaAjusteNegativoYEtiqueta()
    {
        // Arrange: descuento -10% sobre base 1000 → total 900, monto ajuste -100
        const decimal totalAjustado = 900m;
        const decimal montoAjuste = -100m;
        const decimal ajustePorcentaje = -10m;
        const int cantidadCuotas = 3;
        const string etiquetaPlan = "MP 3 cuotas -10";

        var factura = new Factura
        {
            Numero = "FC-F61B-TEST",
            Tipo = TipoFactura.B,
            FechaEmision = DateTime.UtcNow,
            Total = totalAjustado,
            Venta = new Venta
            {
                Numero = "VF61B-COMP",
                TipoPago = TipoPago.MercadoPago,
                Total = totalAjustado,
                DatosTarjeta = new DatosTarjeta
                {
                    NombreTarjeta = "Mercado Pago",
                    TipoTarjeta = TipoTarjeta.Credito,
                    ConfiguracionPagoPlanId = 1,
                    CantidadCuotas = cantidadCuotas,
                    PorcentajeAjustePagoAplicado = ajustePorcentaje,
                    MontoAjustePagoAplicado = montoAjuste,
                    NombrePlanPagoSnapshot = etiquetaPlan,
                    // Sin legacy por producto
                    ProductoCondicionPagoPlanId = null,
                    PorcentajeAjustePlanAplicado = null,
                    MontoAjustePlanAplicado = null
                },
                Cliente = new Cliente
                {
                    Nombre = "Test",
                    Apellido = "Descuento",
                    NumeroDocumento = "88888888"
                },
                Detalles = new List<VentaDetalle>
                {
                    new()
                    {
                        Producto = new Producto { Codigo = "PF61B", Nombre = "Producto F61B" },
                        Cantidad = 1,
                        PrecioUnitario = 1_000m,
                        Subtotal = 1_000m,
                        SubtotalFinal = 1_000m
                    }
                }
            }
        };

        // Act
        var comprobante = FacturaComprobanteBuilder.Build(factura);

        // Assert: el builder usa PorcentajeAjustePagoAplicado (global, negativo) sin confundir con legacy
        Assert.Equal(ajustePorcentaje, comprobante.Totales.PorcentajeAjustePlanAplicado);
        Assert.Equal(montoAjuste, comprobante.Totales.MontoAjustePlanAplicado);
        Assert.Equal(etiquetaPlan, comprobante.Totales.NombrePlanPagoSnapshot);
        Assert.Equal(cantidadCuotas, comprobante.Totales.CantidadCuotas);
        Assert.Equal(totalAjustado, comprobante.Totales.Total);
        // Sin grupos de pago por ítem (flujo global, no por producto)
        Assert.Empty(comprobante.GruposPagoPorItem);
    }
}
