using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stub de ICajaService — configurable para devolver null o una apertura real
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceG : ICajaService
{
    private readonly AperturaCaja? _apertura;
    public StubCajaServiceG(AperturaCaja? apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult(_apertura);

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
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Tests de guards tempranos en CreateAsync
// ---------------------------------------------------------------------------

/// <summary>
/// Verifica que CreateAsync aborta antes del flujo pesado cuando faltan precondiciones básicas.
///
/// Ambos tests necesitan AppDbContext con schema creado (EnsureCreated) porque
/// ObtenerUserIdActualAsync consulta _context.Users antes de llegar al guard de caja.
/// No se necesita ninguna entidad sembrada — solo el schema vacío.
/// </summary>
public class VentaServiceGuardsTests
{
    private static (AppDbContext ctx, SqliteConnection conn) CreateDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    private static IMapper CreateMapper() =>
        new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

    private static VentaService BuildService(
        AppDbContext ctx,
        IHttpContextAccessor httpContextAccessor,
        ICajaService cajaService)
    {
        var mapper = CreateMapper();
        var logger = NullLogger<VentaService>.Instance;
        var validator = new VentaValidator();
        var numberGenerator = new VentaNumberGenerator(ctx);
        var financialService = new FinancialCalculationService();

        return new VentaService(
            ctx,
            mapper,
            logger,
            null!,                  // IConfiguracionPagoService
            null!,                  // IAlertaStockService
            null!,                  // IMovimientoStockService
            financialService,
            validator,
            numberGenerator,
            null!,                  // IPrecioService — no se alcanza en estos tests
            httpContextAccessor,
            null!,                  // IValidacionVentaService
            cajaService,
            null!);                 // ICreditoDisponibleService
    }

    private static VentaViewModel MinimalViewModel() => new()
    {
        ClienteId = 1,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Detalles = new List<VentaDetalleViewModel>()
    };

    // ---------------------------------------------------------------------------
    // Caso 1 — usuario autenticado pero sin caja abierta
    // ---------------------------------------------------------------------------

    /// <summary>
    /// AsegurarCajaAbiertaParaUsuarioActualAsync: currentUserName tiene valor,
    /// pero ICajaService.ObtenerAperturaActivaParaUsuarioAsync devuelve null.
    /// La excepción se lanza en línea ~991, antes de abrir la transacción EF.
    /// </summary>
    [Fact]
    public async Task CreateAsync_SinCajaAbierta_LanzaInvalidOperationException()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "testuser") },
                authenticationType: "Test");
            var httpCtx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
            var httpAccessor = new HttpContextAccessor { HttpContext = httpCtx };

            // Stub devuelve null — sin caja abierta para el usuario
            var cajaStub = new StubCajaServiceG(null);

            var svc = BuildService(ctx, httpAccessor, cajaStub);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CreateAsync(MinimalViewModel()));
        }
    }

    // ---------------------------------------------------------------------------
    // Caso 2 — sin usuario autenticado
    // ---------------------------------------------------------------------------

    /// <summary>
    /// AsegurarCajaAbiertaParaUsuarioActualAsync: _httpContextAccessor.HttpContext es null,
    /// por lo que currentUserName resulta null en línea ~981.
    /// IsNullOrWhiteSpace(null) == true → lanza antes de llamar a ICajaService.
    /// La excepción se lanza en línea ~985, antes de abrir la transacción EF.
    /// </summary>
    [Fact]
    public async Task CreateAsync_SinUsuarioAutenticado_LanzaInvalidOperationException()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            // HttpContext = null simula ausencia total de contexto HTTP
            var httpAccessor = new HttpContextAccessor { HttpContext = null };

            // El stub no debería ser llamado en este caso, pero se pasa de todas formas
            var cajaStub = new StubCajaServiceG(null);

            var svc = BuildService(ctx, httpAccessor, cajaStub);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CreateAsync(MinimalViewModel()));
        }
    }
}
