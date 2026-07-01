using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Validators;
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs locales para el enforcement "vendedor vende en su caja".
// El usuario actual tiene un id real ("vend1") para que el vendedor resuelto
// NO sea null y el enforcement de CreateAsync se ejerza efectivamente.
// ---------------------------------------------------------------------------

file sealed class StubCurrentUserCajaEnf : ICurrentUserService
{
    private readonly bool _esAdmin;
    public StubCurrentUserCajaEnf(bool esAdmin = false) => _esAdmin = esAdmin;
    public string GetUsername() => "vend1";
    public string GetUserId() => "vend1";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "vend1@test.com";
    public bool IsInRole(string role) => _esAdmin && (role == Roles.Administrador || role == Roles.SuperAdmin);
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

file sealed class StubCajaServiceCajaEnf : ICajaService
{
    public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);
    private readonly AperturaCaja? _apertura;
    public StubCajaServiceCajaEnf(AperturaCaja? apertura) => _apertura = apertura;

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

/// <summary>
/// Regresión del enforcement "vendedor vende en su caja" (VentaService.CreateAsync):
/// un vendedor NO asignado al padrón de la caja de la apertura no puede registrar venta.
/// El caso de bloqueo lanza antes de tocar dependencias pesadas, así que es estable.
/// El allow/bypass se valida en QA en vivo.
/// </summary>
public class VentaServiceCajaEnforcementTests
{
    private static (AppDbContext ctx, SqliteConnection conn) CreateDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    private static AperturaCaja SeedCaja(AppDbContext ctx)
    {
        var caja = new Caja { Codigo = "ENF1", Nombre = "Caja Enforcement", Activa = true, IsDeleted = false, RowVersion = new byte[8] };
        ctx.Cajas.Add(caja);
        ctx.SaveChanges();

        var apertura = new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = "vend1",
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        ctx.AperturasCaja.Add(apertura);
        ctx.SaveChanges();
        return apertura;
    }

    private static VentaService BuildService(AppDbContext ctx, ICurrentUserService currentUser, ICajaService cajaService)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
        return new VentaService(
            ctx,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            new VentaNumberGenerator(ctx, NullLogger<VentaNumberGenerator>.Instance),
            new PrecioVigenteResolver(ctx),
            currentUser,
            null!,
            cajaService,
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
    }

    private static VentaViewModel MinimalViewModel() => new()
    {
        ClienteId = 1,
        FechaVenta = DateTime.UtcNow,
        Estado = EstadoVenta.Cotizacion,
        TipoPago = TipoPago.Efectivo,
        Detalles = new List<VentaDetalleViewModel>()
    };

    [Fact]
    public async Task CreateAsync_VendedorNoEsMiembroDeLaCaja_Lanza()
    {
        var (ctx, conn) = CreateDb();
        await using (ctx) using (conn)
        {
            var apertura = SeedCaja(ctx); // caja SIN padrón → vendedor "vend1" no es miembro
            var svc = BuildService(ctx, new StubCurrentUserCajaEnf(esAdmin: false), new StubCajaServiceCajaEnf(apertura));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => svc.CreateAsync(MinimalViewModel()));

            Assert.Contains("habilitado para vender", ex.Message);
        }
    }
}
