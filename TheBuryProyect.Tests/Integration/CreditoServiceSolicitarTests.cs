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
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

// ---------------------------------------------------------------------------
// Stubs mínimos
// ---------------------------------------------------------------------------

file sealed class StubCajaServiceSolicitar : ICajaService
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
    public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
    public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
    public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
    public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
    public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
    public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
    public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
    public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
}

file sealed class StubDisponibleSolicitar : ICreditoDisponibleService
{
    private readonly decimal _disponible;
    public StubDisponibleSolicitar(decimal disponible = 1_000_000m) => _disponible = disponible;

    public Task<CreditoDisponibleResultado> CalcularDisponibleAsync(int clienteId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CreditoDisponibleResultado { Limite = _disponible, Disponible = _disponible });

    public Task<decimal> ObtenerLimitePorPuntajeAsync(NivelRiesgoCredito puntaje, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<decimal> CalcularSaldoVigenteAsync(int clienteId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<(bool Ok, List<string> Errores)> GuardarLimitesPorPuntajeAsync(IReadOnlyList<(NivelRiesgoCredito Puntaje, decimal LimiteMonto, bool Activo)> items, string usuario) => throw new NotImplementedException();
    public Task<List<PuntajeCreditoLimite>> GetAllLimitesPorPuntajeAsync() => throw new NotImplementedException();
}

file sealed class StubCurrentUserSolicitar : ICurrentUserService
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
/// Tests de integración para CreditoService.SolicitarCreditoAsync.
///
/// Guards (retornan (false, null, ErrorMessage)):
/// - solicitud null → "Solicitud inválida"
/// - MontoSolicitado ≤ 0 → "Monto inválido"
/// - CantidadCuotas fuera de rango (0 o 121) → "Cantidad de cuotas inválida"
/// - ClienteId inexistente → "Cliente no encontrado"
/// - Monto excede disponible → retorna false con mensaje
///
/// Happy path:
/// - Retorna (true, NumeroCredito, null)
/// - Número no nulo ni vacío
/// - Crédito persiste en DB en estado Aprobado
/// - Genera exactamente CantidadCuotas cuotas en estado Pendiente
/// - Suma de MontoCapital de cuotas ≈ MontoSolicitado
/// - Con GaranteDocumento → crea Garante y lo vincula al crédito
/// - Con GaranteId existente → vincula sin crear uno nuevo
/// - SaldoPendiente = MontoAprobado
/// </summary>
public class CreditoServiceSolicitarTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CreditoService _service;

    public CreditoServiceSolicitarTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _service = new CreditoService(
            _context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new FinancialCalculationService(),
            new StubCajaServiceSolicitar(),
            new StubDisponibleSolicitar(),
            new StubCurrentUserSolicitar());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var cliente = new Cliente
        {
            Nombre = "Test",
            Apellido = "Solicitar",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8]
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private SolicitudCreditoViewModel BuildSolicitud(int clienteId, decimal monto = 12_000m, int cuotas = 3, decimal tasa = 5m)
        => new()
        {
            ClienteId = clienteId,
            MontoSolicitado = monto,
            CantidadCuotas = cuotas,
            TasaInteres = tasa
        };

    // =========================================================================
    // Guards — retornan error sin llegar a DB
    // =========================================================================

    [Fact]
    public async Task Solicitar_Null_RetornaFalseConMensaje()
    {
        var (success, numero, error) = await _service.SolicitarCreditoAsync(null!, "usuario");

        Assert.False(success);
        Assert.Null(numero);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task Solicitar_MontoNegativo_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();

        var (success, _, error) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, monto: -1m), "usuario");

        Assert.False(success);
        Assert.Contains("Monto", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Solicitar_CuotasCero_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();

        var (success, _, error) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, cuotas: 0), "usuario");

        Assert.False(success);
        Assert.Contains("cuota", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Solicitar_CuotasExcedeMaximo_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();

        var (success, _, error) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, cuotas: 121), "usuario");

        Assert.False(success);
        Assert.Contains("cuota", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Solicitar_ClienteInexistente_RetornaFalse()
    {
        var (success, _, error) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(clienteId: 99_999), "usuario");

        Assert.False(success);
        Assert.Contains("Cliente", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Solicitar_MontoExcedeDisponible_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();

        // Servicio con límite muy bajo
        var serviceConLimiteBajo = new CreditoService(
            _context,
            new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance).CreateMapper(),
            NullLogger<CreditoService>.Instance,
            new FinancialCalculationService(),
            new StubCajaServiceSolicitar(),
            new StubDisponibleSolicitar(disponible: 100m), // límite = 100
            new StubCurrentUserSolicitar());

        var (success, _, error) = await serviceConLimiteBajo.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, monto: 50_000m), "usuario");

        Assert.False(success);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // =========================================================================
    // Happy path
    // =========================================================================

    [Fact]
    public async Task Solicitar_HappyPath_RetornaTrueYNumero()
    {
        var cliente = await SeedClienteAsync();

        var (success, numero, error) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id), "operador");

        Assert.True(success);
        Assert.False(string.IsNullOrWhiteSpace(numero));
        Assert.Null(error);
    }

    [Fact]
    public async Task Solicitar_HappyPath_CreditoPersistidoEnAprobado()
    {
        var cliente = await SeedClienteAsync();

        var (_, numero, _) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, monto: 10_000m, cuotas: 6), "operador");

        var credito = await _context.Creditos.FirstOrDefaultAsync(c => c.Numero == numero);
        Assert.NotNull(credito);
        Assert.Equal(EstadoCredito.Aprobado, credito!.Estado);
        Assert.Equal(10_000m, credito.MontoAprobado);
    }

    [Fact]
    public async Task Solicitar_HappyPath_GeneraExactamenteCuotasPendientes()
    {
        var cliente = await SeedClienteAsync();

        var (_, numero, _) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, monto: 12_000m, cuotas: 3), "operador");

        var creditoId = (await _context.Creditos.FirstAsync(c => c.Numero == numero)).Id;
        var cuotas = await _context.Cuotas.Where(c => c.CreditoId == creditoId).ToListAsync();

        Assert.Equal(3, cuotas.Count);
        Assert.All(cuotas, c => Assert.Equal(EstadoCuota.Pendiente, c.Estado));
    }

    [Fact]
    public async Task Solicitar_HappyPath_SumaCapitalCuotasIgualMonto()
    {
        var cliente = await SeedClienteAsync();

        var (_, numero, _) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, monto: 9_000m, cuotas: 3, tasa: 3m), "operador");

        var creditoId = (await _context.Creditos.FirstAsync(c => c.Numero == numero)).Id;
        var cuotas = await _context.Cuotas.Where(c => c.CreditoId == creditoId).ToListAsync();
        var sumaCapital = cuotas.Sum(c => c.MontoCapital);

        Assert.Equal(9_000m, sumaCapital, precision: 1); // tolerancia centavo por redondeo
    }

    [Fact]
    public async Task Solicitar_HappyPath_NumeroCuotasOrdenados()
    {
        var cliente = await SeedClienteAsync();

        var (_, numero, _) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, cuotas: 4), "operador");

        var creditoId = (await _context.Creditos.FirstAsync(c => c.Numero == numero)).Id;
        var numeros = await _context.Cuotas
            .Where(c => c.CreditoId == creditoId)
            .OrderBy(c => c.NumeroCuota)
            .Select(c => c.NumeroCuota)
            .ToListAsync();

        Assert.Equal(new[] { 1, 2, 3, 4 }, numeros);
    }

    [Fact]
    public async Task Solicitar_HappyPath_SaldoPendienteIgualMontoAprobado()
    {
        var cliente = await SeedClienteAsync();

        var (_, numero, _) = await _service.SolicitarCreditoAsync(
            BuildSolicitud(cliente.Id, monto: 15_000m, cuotas: 6), "operador");

        var credito = await _context.Creditos.FirstAsync(c => c.Numero == numero);
        Assert.Equal(credito.MontoAprobado, credito.SaldoPendiente);
    }

    // =========================================================================
    // Garante
    // =========================================================================

    [Fact]
    public async Task Solicitar_ConGaranteDocumento_CreaGaranteYVincula()
    {
        var cliente = await SeedClienteAsync();
        var solicitud = BuildSolicitud(cliente.Id);
        solicitud.GaranteDocumento = "12345678";
        solicitud.GaranteNombre = "Juan Garante";

        var (success, numero, _) = await _service.SolicitarCreditoAsync(solicitud, "operador");

        Assert.True(success);
        var credito = await _context.Creditos.FirstAsync(c => c.Numero == numero);
        Assert.True(credito.GaranteId.HasValue);
        var garante = await _context.Garantes.FindAsync(credito.GaranteId!.Value);
        Assert.NotNull(garante);
        Assert.Equal("12345678", garante!.NumeroDocumento);
    }

    [Fact]
    public async Task Solicitar_ConGaranteId_VinculaSinCrearNuevo()
    {
        var cliente = await SeedClienteAsync();

        // Seed garante existente
        var garanteExistente = new Garante
        {
            ClienteId = cliente.Id,
            TipoDocumento = "DNI",
            NumeroDocumento = "87654321",
            Nombre = "Garante Existente",
            Relacion = "Familiar"
        };
        _context.Garantes.Add(garanteExistente);
        await _context.SaveChangesAsync();

        var solicitud = BuildSolicitud(cliente.Id);
        solicitud.GaranteId = garanteExistente.Id;

        var (success, numero, _) = await _service.SolicitarCreditoAsync(solicitud, "operador");

        Assert.True(success);
        var credito = await _context.Creditos.FirstAsync(c => c.Numero == numero);
        Assert.Equal(garanteExistente.Id, credito.GaranteId);
        // No se crearon garantes adicionales
        var totalGarantes = await _context.Garantes.CountAsync(g => g.ClienteId == cliente.Id);
        Assert.Equal(1, totalGarantes);
    }
}
