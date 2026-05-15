using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs mínimos — sólo lo que ConfigurarCreditoAsync necesita
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceConfiguracion : ICajaService
{
    public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(
        int cuotaId, string creditoNumero, int numeroCuota,
        decimal monto, string medioPago, string usuario)
        => Task.FromResult<MovimientoCaja?>(new MovimientoCaja());

    public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario) => throw new NotImplementedException();
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

file sealed class StubFinancialServiceConfig : IFinancialCalculationService
{
    public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas) => throw new NotImplementedException();
    public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
    public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto) => throw new NotImplementedException();
    public decimal ComputeFinancedAmount(decimal total, decimal anticipo) => throw new NotImplementedException();
    public decimal CalcularCFTEADesdeTasa(decimal tasaMensual) => throw new NotImplementedException();
    public SimulacionPlanCreditoDto SimularPlanCredito(
        decimal totalVenta, decimal anticipo, int cuotas, decimal tasaMensual,
        decimal gastosAdministrativos, DateTime fechaPrimeraCuota,
        decimal semaforoRatioVerdeMax = 0.08m,
        decimal semaforoRatioAmarilloMax = 0.15m) => throw new NotImplementedException();
}

file sealed class StubCreditoDisponibleServiceConfig : ICreditoDisponibleService
{
    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserServiceConfig : ICurrentUserService
{
    public string GetUsername() => "TestUser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => "test@test.com";
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => "127.0.0.1";
}

// ---------------------------------------------------------------------------

/// <summary>
/// Tests de integración para CreditoService.ConfigurarCreditoAsync.
///
/// Verifica el contrato completo del método:
/// - Crédito no encontrado → InvalidOperationException
/// - Estado del crédito transiciona a Configurado
/// - Campos financieros (MontoAprobado, SaldoPendiente, TasaInteres, etc.) quedan correctos
/// - VentaId null → la venta no se toca
/// - Venta en PendienteFinanciacion → transiciona a Presupuesto
/// - Venta en otro estado → no cambia
/// - Perfil aplicado: ID y nombre se persisten cuando se proveen
/// - Observaciones: se construyen correctamente según fuente/método
///
/// No requiere IFinancialCalculationService ni ICajaService con lógica real.
/// </summary>
public class CreditoServiceConfiguracionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;

    public CreditoServiceConfiguracionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => { cfg.AddProfile<MappingProfile>(); },
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialServiceConfig(),
            new StubCajaServiceConfiguracion(),
            new StubCreditoDisponibleServiceConfig(),
            new StubCurrentUserServiceConfig());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers de seed
    // -------------------------------------------------------------------------

    private async Task<Credito> SeedCredito(
        int id = 1,
        EstadoCredito estado = EstadoCredito.Solicitado,
        string numero = "CRED0001")
    {
        var cliente = new Cliente
        {
            Id = id,
            Nombre = "Test",
            Apellido = "Cliente",
            NumeroDocumento = $"3000{id:D4}",
            IsDeleted = false
        };
        _context.Clientes.Add(cliente);

        var credito = new Credito
        {
            Id = id,
            ClienteId = id,
            Numero = numero,
            Estado = estado,
            TasaInteres = 0m,
            MontoSolicitado = 10_000m,
            MontoAprobado = 0m,
            SaldoPendiente = 0m,
            CantidadCuotas = 0,
            MontoCuota = 0m,
            TotalAPagar = 0m,
            IsDeleted = false
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Venta> SeedVenta(
        int id = 1,
        EstadoVenta estado = EstadoVenta.PendienteFinanciacion,
        int clienteId = 1)
    {
        var venta = new Venta
        {
            Id = id,
            ClienteId = clienteId,
            Numero = $"VTA{id:D4}",
            Estado = estado,
            Total = 10_000m,
            IsDeleted = false
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private static ConfiguracionCreditoComando BuildComando(
        int creditoId = 1,
        int? ventaId = null,
        decimal monto = 10_000m,
        decimal anticipo = 2_000m,
        int cuotas = 12,
        decimal tasaMensual = 3m,
        decimal gastos = 0m,
        MetodoCalculoCredito metodo = MetodoCalculoCredito.Global,
        FuenteConfiguracionCredito fuente = FuenteConfiguracionCredito.Global,
        int? perfilId = null,
        string? perfilNombre = null,
        int cuotasMin = 1,
        int cuotasMax = 24) => new()
    {
        CreditoId = creditoId,
        VentaId = ventaId,
        Monto = monto,
        Anticipo = anticipo,
        CantidadCuotas = cuotas,
        TasaMensual = tasaMensual,
        GastosAdministrativos = gastos,
        FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
        MetodoCalculo = metodo,
        FuenteConfiguracion = fuente,
        PerfilCreditoAplicadoId = perfilId,
        PerfilCreditoAplicadoNombre = perfilNombre,
        CuotasMinPermitidas = cuotasMin,
        CuotasMaxPermitidas = cuotasMax
    };

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfigurarCredito_CreditoNoEncontrado_LanzaInvalidOperationException()
    {
        // Arrange: no hay crédito en la DB
        var cmd = BuildComando(creditoId: 999);

        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ConfigurarCreditoAsync(cmd));
    }

    [Fact]
    public async Task ConfigurarCredito_HappyPath_EstadoTransicionaAConfigurado()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = BuildComando(creditoId: 1);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal(EstadoCredito.Configurado, credito!.Estado);
    }

    [Fact]
    public async Task ConfigurarCredito_HappyPath_CamposFinancierosCorrectos()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = BuildComando(
            creditoId: 1,
            monto: 10_000m,
            anticipo: 2_000m,
            cuotas: 12,
            tasaMensual: 3.5m,
            gastos: 150m,
            cuotasMin: 3,
            cuotasMax: 24);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal(8_000m, credito!.MontoAprobado);       // monto - anticipo
        Assert.Equal(8_000m, credito.SaldoPendiente);
        Assert.Equal(3.5m, credito.TasaInteres);
        Assert.Equal(3.5m, credito.TasaInteresAplicada);
        Assert.Equal(12, credito.CantidadCuotas);
        Assert.Equal(150m, credito.GastosAdministrativos);
        Assert.Equal(3, credito.CuotasMinimasPermitidas);
        Assert.Equal(24, credito.CuotasMaximasPermitidas);
    }

    [Fact]
    public async Task ConfigurarCredito_SinVentaId_NoTocaVentas()
    {
        // Arrange
        await SeedCredito(id: 1);
        await SeedVenta(id: 1, estado: EstadoVenta.PendienteFinanciacion);
        var cmd = BuildComando(creditoId: 1, ventaId: null); // VentaId null

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert: la venta no cambió de estado
        var venta = await _context.Ventas.FindAsync(1);
        Assert.NotNull(venta);
        Assert.Equal(EstadoVenta.PendienteFinanciacion, venta!.Estado);
    }

    [Fact]
    public async Task ConfigurarCredito_VentaPendienteFinanciacion_TransicionaAPresupuesto()
    {
        // Arrange
        await SeedCredito(id: 1);
        await SeedVenta(id: 1, estado: EstadoVenta.PendienteFinanciacion);
        var cmd = BuildComando(creditoId: 1, ventaId: 1);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var venta = await _context.Ventas.FindAsync(1);
        Assert.NotNull(venta);
        Assert.Equal(EstadoVenta.Presupuesto, venta!.Estado);
    }

    [Fact]
    public async Task ConfigurarCredito_VentaEnOtroEstado_NoModificaEstadoVenta()
    {
        // Arrange
        await SeedCredito(id: 1);
        await SeedVenta(id: 1, estado: EstadoVenta.Presupuesto); // ya no es PendienteFinanciacion
        var cmd = BuildComando(creditoId: 1, ventaId: 1);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert: estado no cambia (la condición en el service solo actúa sobre PendienteFinanciacion)
        var venta = await _context.Ventas.FindAsync(1);
        Assert.NotNull(venta);
        Assert.Equal(EstadoVenta.Presupuesto, venta!.Estado);
    }

    [Fact]
    public async Task ConfigurarCredito_ConPerfil_PersistePerlilIdYNombre()
    {
        // Arrange
        await SeedCredito(id: 1);
        _context.PerfilesCredito.Add(new PerfilCredito { Id = 7, Nombre = "Estándar", TasaMensual = 3m });
        await _context.SaveChangesAsync();

        var cmd = BuildComando(
            creditoId: 1,
            metodo: MetodoCalculoCredito.UsarPerfil,
            perfilId: 7,
            perfilNombre: "Estándar");

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal(7, credito!.PerfilCreditoAplicadoId);
        Assert.Equal("Estándar", credito.PerfilCreditoAplicadoNombre);
    }

    [Fact]
    public async Task ConfigurarCredito_SinPerfil_NoSobreescribePerfilExistente()
    {
        // Arrange: crédito con perfil ya asignado — seedear el perfil para que la FK sea válida
        _context.PerfilesCredito.Add(new PerfilCredito { Id = 5, Nombre = "Anterior", TasaMensual = 2m });
        await _context.SaveChangesAsync();

        await SeedCredito(id: 1);
        var credito = await _context.Creditos.FindAsync(1);
        credito!.PerfilCreditoAplicadoId = 5;
        credito.PerfilCreditoAplicadoNombre = "Anterior";
        await _context.SaveChangesAsync();

        // Comando sin perfil
        var cmd = BuildComando(creditoId: 1, perfilId: null, perfilNombre: null);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert: el bloque if (cmd.PerfilCreditoAplicadoId.HasValue) no ejecuta → valores sin cambio
        var creditoActualizado = await _context.Creditos.FindAsync(1);
        Assert.NotNull(creditoActualizado);
        Assert.Equal(5, creditoActualizado!.PerfilCreditoAplicadoId);
        Assert.Equal("Anterior", creditoActualizado.PerfilCreditoAplicadoNombre);
    }

    [Fact]
    public async Task ConfigurarCredito_FuenteGlobal_ObservacionesContieneTextoGlobal()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = BuildComando(
            creditoId: 1,
            fuente: FuenteConfiguracionCredito.Global,
            metodo: MetodoCalculoCredito.Global);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Contains("Configuración Global", credito!.Observaciones);
    }

    [Fact]
    public async Task ConfigurarCredito_FuenteManual_ObservacionesContieneTextoManual()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = BuildComando(
            creditoId: 1,
            fuente: FuenteConfiguracionCredito.Manual,
            metodo: MetodoCalculoCredito.Manual);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Contains("Configuración Manual", credito!.Observaciones);
    }

    [Fact]
    public async Task ConfigurarCredito_ConGastos_ObservacionesIncluyanGastos()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = BuildComando(creditoId: 1, gastos: 250m);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Contains("Gastos administrativos declarados", credito!.Observaciones);
        Assert.Contains("250", credito.Observaciones);
    }

    [Fact]
    public async Task ConfigurarCredito_ObservacionesPreexistentes_SePreservan()
    {
        // Arrange: crédito con observación existente
        await SeedCredito(id: 1);
        var credito = await _context.Creditos.FindAsync(1);
        credito!.Observaciones = "Nota previa del evaluador";
        await _context.SaveChangesAsync();

        var cmd = BuildComando(creditoId: 1);

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var creditoActualizado = await _context.Creditos.FindAsync(1);
        Assert.NotNull(creditoActualizado);
        Assert.Contains("Nota previa del evaluador", creditoActualizado!.Observaciones);
    }

    // -------------------------------------------------------------------------
    // Tests de trazabilidad de restricción de cuotas (Fase 9.5b)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConfigurarCredito_ConProductoRestrictivo_PersisteFuenteProducto()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = new ConfiguracionCreditoComando
        {
            CreditoId                   = 1,
            Monto                       = 10_000m,
            Anticipo                    = 0m,
            CantidadCuotas              = 12,
            TasaMensual                 = 3m,
            FechaPrimeraCuota           = DateTime.UtcNow.AddMonths(1),
            MetodoCalculo               = MetodoCalculoCredito.Global,
            FuenteConfiguracion         = FuenteConfiguracionCredito.Global,
            CuotasMinPermitidas         = 1,
            CuotasMaxPermitidas         = 6,
            FuenteRestriccionCuotasSnap = "Producto",
            ProductoIdRestrictivoSnap   = 42,
            MaxCuotasBaseSnap           = 24
        };

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal("Producto", credito!.FuenteRestriccionCuotasSnap);
    }

    [Fact]
    public async Task ConfigurarCredito_ConProductoRestrictivo_PersisteProductoIdRestrictivo()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = new ConfiguracionCreditoComando
        {
            CreditoId                   = 1,
            Monto                       = 10_000m,
            Anticipo                    = 0m,
            CantidadCuotas              = 6,
            TasaMensual                 = 3m,
            FechaPrimeraCuota           = DateTime.UtcNow.AddMonths(1),
            MetodoCalculo               = MetodoCalculoCredito.Global,
            FuenteConfiguracion         = FuenteConfiguracionCredito.Global,
            CuotasMinPermitidas         = 1,
            CuotasMaxPermitidas         = 6,
            FuenteRestriccionCuotasSnap = "Producto",
            ProductoIdRestrictivoSnap   = 99,
            MaxCuotasBaseSnap           = 24
        };

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal(99, credito!.ProductoIdRestrictivoSnap);
    }

    [Fact]
    public async Task ConfigurarCredito_ConProductoRestrictivo_PersisteMaxCuotasBaseSnap()
    {
        // Arrange
        await SeedCredito(id: 1);
        var cmd = new ConfiguracionCreditoComando
        {
            CreditoId                   = 1,
            Monto                       = 10_000m,
            Anticipo                    = 0m,
            CantidadCuotas              = 6,
            TasaMensual                 = 3m,
            FechaPrimeraCuota           = DateTime.UtcNow.AddMonths(1),
            MetodoCalculo               = MetodoCalculoCredito.Global,
            FuenteConfiguracion         = FuenteConfiguracionCredito.Global,
            CuotasMinPermitidas         = 1,
            CuotasMaxPermitidas         = 6,
            FuenteRestriccionCuotasSnap = "Producto",
            ProductoIdRestrictivoSnap   = 7,
            MaxCuotasBaseSnap           = 36
        };

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal(36, credito!.MaxCuotasBaseSnap);
    }

    [Fact]
    public async Task ConfigurarCredito_SinProductoRestrictivo_PersisteFuenteGlobal()
    {
        // Arrange: sin producto restrictivo → fuente Global
        await SeedCredito(id: 1);
        var cmd = new ConfiguracionCreditoComando
        {
            CreditoId                   = 1,
            Monto                       = 10_000m,
            Anticipo                    = 0m,
            CantidadCuotas              = 12,
            TasaMensual                 = 3m,
            FechaPrimeraCuota           = DateTime.UtcNow.AddMonths(1),
            MetodoCalculo               = MetodoCalculoCredito.Global,
            FuenteConfiguracion         = FuenteConfiguracionCredito.Global,
            CuotasMinPermitidas         = 1,
            CuotasMaxPermitidas         = 24,
            FuenteRestriccionCuotasSnap = "Global",
            ProductoIdRestrictivoSnap   = null,
            MaxCuotasBaseSnap           = 24
        };

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal("Global", credito!.FuenteRestriccionCuotasSnap);
        Assert.Null(credito.ProductoIdRestrictivoSnap);
    }

    [Fact]
    public async Task ConfigurarCredito_VentaValida_TotalesNoModificados()
    {
        // Arrange: flujo normal completo no altera MontoAprobado ni SaldoPendiente
        await SeedCredito(id: 1);
        await SeedVenta(id: 1, estado: EstadoVenta.PendienteFinanciacion);
        var cmd = new ConfiguracionCreditoComando
        {
            CreditoId                   = 1,
            VentaId                     = 1,
            Monto                       = 10_000m,
            Anticipo                    = 1_000m,
            CantidadCuotas              = 12,
            TasaMensual                 = 2.5m,
            FechaPrimeraCuota           = DateTime.UtcNow.AddMonths(1),
            MetodoCalculo               = MetodoCalculoCredito.Global,
            FuenteConfiguracion         = FuenteConfiguracionCredito.Global,
            CuotasMinPermitidas         = 1,
            CuotasMaxPermitidas         = 24,
            FuenteRestriccionCuotasSnap = "Global",
            ProductoIdRestrictivoSnap   = null,
            MaxCuotasBaseSnap           = 24
        };

        // Act
        await _service.ConfigurarCreditoAsync(cmd);

        // Assert: el monto aprobado es monto - anticipo (9_000); la lógica financiera no cambió
        var credito = await _context.Creditos.FindAsync(1);
        Assert.NotNull(credito);
        Assert.Equal(9_000m, credito!.MontoAprobado);
        Assert.Equal(9_000m, credito.SaldoPendiente);
    }
}
