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

// ---------------------------------------------------------------------------
// Stub de ICajaService — devuelve una apertura real
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceCP : ICajaService
{
    private readonly AperturaCaja _apertura;
    public StubCajaServiceCP(AperturaCaja apertura) => _apertura = apertura;

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
        => Task.FromResult<AperturaCaja?>(_apertura);

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
// Stub de IValidacionVentaService — configurable para devolver NoViable
// ---------------------------------------------------------------------------

file sealed class StubValidacionVentaService : IValidacionVentaService
{
    private readonly ValidacionVentaResult _resultado;
    public StubValidacionVentaService(ValidacionVentaResult resultado) => _resultado = resultado;

    public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(
        int clienteId, decimal montoVenta, int? creditoId = null)
        => Task.FromResult(_resultado);

    public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
    public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
    public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
    public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
}

// ---------------------------------------------------------------------------
// Stub de ICurrentUserService
// ---------------------------------------------------------------------------

file sealed class StubCurrentUserServiceCP : ICurrentUserService
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
// Tests de la rama CreditoPersonal en CreateAsync
// ---------------------------------------------------------------------------

/// <summary>
/// Verifica que CreateAsync aborta con InvalidOperationException cuando
/// IValidacionVentaService devuelve NoViable = true y no aplica excepción documental.
///
/// El flujo llega a la validación crediticia (línea ~180) habiendo pasado:
///   - guard de usuario (nombre presente en Claims)
///   - guard de caja (stub devuelve AperturaCaja real)
///   - ResolverVendedorAsync (sin VendedorUserId → devuelve usuario actual sin tocar Roles)
///   - AplicarPrecioVigenteADetallesAsync (Detalles vacío → no llama a IPrecioService)
///   - CapturarSnapshotLimiteCreditoAsync (ClienteId sin match en DB → early return silencioso)
///
/// No necesita seed de Cliente, Producto ni entidades de negocio.
/// Solo Caja + AperturaCaja para superar el guard de caja.
/// </summary>
public class VentaServiceCreditoPersonalTests
{
    private const string TestUser = "testuser";

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

    /// <summary>
    /// Siembra únicamente Caja + AperturaCaja.
    /// El guard de caja requiere una AperturaCaja con Id real,
    /// pero ningún otro flujo necesita entidades de negocio en este test.
    /// </summary>
    private static async Task<AperturaCaja> SeedCajaAsync(AppDbContext ctx)
    {
        var caja = new Caja
        {
            Codigo = "C01",
            Nombre = "Caja Test",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.Cajas.Add(caja);
        await ctx.SaveChangesAsync();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.AperturasCaja.Add(apertura);
        await ctx.SaveChangesAsync();

        return apertura;
    }

    private static VentaService BuildService(
        AppDbContext ctx,
        ICajaService cajaService,
        IValidacionVentaService validacionVentaService)
    {
        var mapper = CreateMapper();
        var logger = NullLogger<VentaService>.Instance;
        var validator = new VentaValidator();
        var numberGenerator = new VentaNumberGenerator(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheBuryProject.Services.VentaNumberGenerator>.Instance);
        var financialService = new FinancialCalculationService();

        return new VentaService(
            ctx,
            mapper,
            logger,
            null!,                   // IAlertaStockService
            null!,                   // IMovimientoStockService
            financialService,
            validator,
            numberGenerator,
            new PrecioVigenteResolver(ctx),
            new StubCurrentUserServiceCP(),
            validacionVentaService,
            cajaService,
            null!,                   // ICreditoDisponibleService
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
    }

    private static VentaViewModel CreditoPersonalViewModel() => new()
    {
        ClienteId = 999,             // ID inexistente → CapturarSnapshot hace early return
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.CreditoPersonal,
        AplicarExcepcionDocumental = false,
        Detalles = new List<VentaDetalleViewModel>()
    };

    // ---------------------------------------------------------------------------
    // Caso — crédito personal no viable, sin excepción documental
    // ---------------------------------------------------------------------------

    /// <summary>
    /// IValidacionVentaService.ValidarVentaCreditoPersonalAsync devuelve NoViable = true.
    /// AplicarExcepcionDocumental = false → PuedeAplicarExcepcionDocumentalCreate retorna false.
    /// El flujo debe lanzar InvalidOperationException antes de cualquier persistencia.
    /// </summary>
    [Fact]
    public async Task CreateAsync_CreditoPersonalNoViable_LanzaInvalidOperationException()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var apertura = await SeedCajaAsync(ctx);

            var resultadoNoViable = new ValidacionVentaResult
            {
                NoViable = true,
                RequisitosPendientes = new List<RequisitoPendiente>
                {
                    new() { Tipo = TipoRequisitoPendiente.ClienteNoApto, Descripcion = "Cliente no apto para crédito" }
                }
            };

            var cajaStub = new StubCajaServiceCP(apertura);
            var validacionStub = new StubValidacionVentaService(resultadoNoViable);

            var svc = BuildService(ctx, cajaStub, validacionStub);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CreateAsync(CreditoPersonalViewModel()));

            Assert.Contains("No es posible crear la venta con crédito personal", ex.Message);
            Assert.Contains("OPERACIÓN NO VIABLE", ex.Message);
        }
    }
}
