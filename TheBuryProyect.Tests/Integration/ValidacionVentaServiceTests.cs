using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ValidacionVentaService.
/// Cubren ValidarVentaCreditoPersonalAsync: cliente Apto puede proceder,
/// NoApto retorna problemas bloqueantes, RequiereAutorizacion retorna razones,
/// configuración incompleta bloquea, cupo insuficiente con/sin cupo asignado,
/// crédito específico no existe/no activo/excede saldo.
/// Cubren PrevalidarAsync: mapeo correcto a PrevalidacionResultViewModel,
/// excepción interna retorna NoViable.
/// Cubren ValidarConfirmacionVentaAsync: venta no encontrada, pago no crédito,
/// pago crédito personal delega a ValidarVentaCreditoPersonalAsync.
/// </summary>
public class ValidacionVentaServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly StubClienteAptitudService _stubAptitud;
    private readonly ValidacionVentaService _service;

    public ValidacionVentaServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _stubAptitud = new StubClienteAptitudService();
        _service = new ValidacionVentaService(
            _context, _stubAptitud, NullLogger<ValidacionVentaService>.Instance);
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
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "ValidVenta",
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoAsync(
        int clienteId,
        EstadoCredito estado = EstadoCredito.Activo,
        decimal saldoPendiente = 5000m)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = estado,
            TotalAPagar = saldoPendiente,
            SaldoPendiente = saldoPendiente,
            CantidadCuotas = 12,
            MontoCuota = saldoPendiente / 12
        };
        _context.Creditos.Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<Venta> SeedVentaAsync(
        int clienteId,
        TipoPago tipoPago = TipoPago.Efectivo,
        int? creditoId = null,
        decimal total = 100m)
    {
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = EstadoVenta.Confirmada,
            TipoPago = tipoPago,
            CreditoId = creditoId,
            FechaVenta = DateTime.Today,
            Subtotal = total,
            Total = total
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    private static AptitudCrediticiaViewModel AptitudApto(decimal cupoDisponible = 10000m) =>
        new()
        {
            Estado = EstadoCrediticioCliente.Apto,
            ConfiguracionCompleta = true,
            Cupo = new AptitudCupoDetalle
            {
                TieneCupoAsignado = true,
                LimiteCredito = cupoDisponible,
                CupoDisponible = cupoDisponible
            }
        };

    private static AptitudCrediticiaViewModel AptitudNoApto(string categoria = "Mora") =>
        new()
        {
            Estado = EstadoCrediticioCliente.NoApto,
            ConfiguracionCompleta = true,
            Motivo = "Mora activa",
            Detalles = new List<AptitudDetalleItem>
            {
                new() { Categoria = categoria, Descripcion = "Bloqueo", EsBloqueo = true }
            }
        };

    private static AptitudCrediticiaViewModel AptitudRequiereAutorizacion() =>
        new()
        {
            Estado = EstadoCrediticioCliente.RequiereAutorizacion,
            ConfiguracionCompleta = true,
            Detalles = new List<AptitudDetalleItem>
            {
                new() { Categoria = "Mora", Descripcion = "Mora leve", EsBloqueo = false }
            }
        };

    // -------------------------------------------------------------------------
    // ValidarVentaCreditoPersonalAsync — cliente Apto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarVenta_ClienteApto_CupoSuficiente_PuedeProceeder()
    {
        _stubAptitud.ResultadoAptitud = AptitudApto(cupoDisponible: 10000m);
        _stubAptitud.CupoDisponible = 10000m;

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 500m);

        Assert.True(result.PuedeProceeder);
        Assert.False(result.NoViable);
        Assert.False(result.RequiereAutorizacion);
    }

    [Fact]
    public async Task ValidarVenta_ClienteApto_MontoExcedeCupo_ConCupoAsignado_EsNoViable()
    {
        _stubAptitud.ResultadoAptitud = AptitudApto(cupoDisponible: 1000m);
        _stubAptitud.CupoDisponible = 1000m;

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 2000m);

        Assert.True(result.NoViable);
        Assert.NotEmpty(result.RequisitosPendientes);
    }

    [Fact]
    public async Task ValidarVenta_ClienteApto_SinCupoAsignado_ExcedeMonto_EsNoViable()
    {
        var aptitud = AptitudApto(cupoDisponible: 0m);
        aptitud.Cupo.TieneCupoAsignado = false;
        aptitud.Cupo.LimiteCredito = null;
        _stubAptitud.ResultadoAptitud = aptitud;
        _stubAptitud.CupoDisponible = 0m;

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 100m);

        Assert.True(result.NoViable);
        var req = result.RequisitosPendientes.FirstOrDefault(r => r.Tipo == TipoRequisitoPendiente.SinLimiteCredito);
        Assert.NotNull(req);
    }

    // -------------------------------------------------------------------------
    // ValidarVentaCreditoPersonalAsync — cliente NoApto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarVenta_ClienteNoApto_RetornaNoViableConProblemasMora()
    {
        _stubAptitud.ResultadoAptitud = AptitudNoApto(categoria: "Mora");

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 100m);

        Assert.True(result.NoViable);
        Assert.NotEmpty(result.RequisitosPendientes);
        var req = result.RequisitosPendientes.First();
        Assert.Equal(TipoRequisitoPendiente.ClienteNoApto, req.Tipo);
    }

    [Fact]
    public async Task ValidarVenta_ClienteNoApto_ProblemaDocumentacion_TipoDocumentacion()
    {
        _stubAptitud.ResultadoAptitud = AptitudNoApto(categoria: "Documentación");

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 100m);

        var req = result.RequisitosPendientes.First();
        Assert.Equal(TipoRequisitoPendiente.DocumentacionFaltante, req.Tipo);
    }

    // -------------------------------------------------------------------------
    // ValidarVentaCreditoPersonalAsync — RequiereAutorizacion
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarVenta_RequiereAutorizacion_NoEsNoViableNiPuedeProceeder()
    {
        _stubAptitud.ResultadoAptitud = AptitudRequiereAutorizacion();
        _stubAptitud.CupoDisponible = 10000m;

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 100m);

        Assert.True(result.RequiereAutorizacion);
        Assert.False(result.NoViable);
        Assert.NotEmpty(result.RazonesAutorizacion);
    }

    // -------------------------------------------------------------------------
    // ValidarVentaCreditoPersonalAsync — configuración incompleta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarVenta_ConfiguracionIncompleta_EsNoViable()
    {
        _stubAptitud.ResultadoAptitud = new AptitudCrediticiaViewModel
        {
            Estado = EstadoCrediticioCliente.NoEvaluado,
            ConfiguracionCompleta = false,
            AdvertenciaConfiguracion = "Sistema no configurado"
        };

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 100m);

        Assert.True(result.NoViable);
        var req = result.RequisitosPendientes.FirstOrDefault(r => r.Tipo == TipoRequisitoPendiente.SinEvaluacionCrediticia);
        Assert.NotNull(req);
    }

    // -------------------------------------------------------------------------
    // ValidarVentaCreditoPersonalAsync — crédito específico
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarVenta_CreditoEspecificoInexistente_EsNoViable()
    {
        _stubAptitud.ResultadoAptitud = AptitudApto();

        var result = await _service.ValidarVentaCreditoPersonalAsync(1, montoVenta: 100m, creditoId: 99999);

        Assert.True(result.NoViable);
        var req = result.RequisitosPendientes.FirstOrDefault(r => r.Tipo == TipoRequisitoPendiente.SinCreditoAprobado);
        Assert.NotNull(req);
    }

    [Fact]
    public async Task ValidarVenta_CreditoEspecificoCancelado_EsNoViable()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, estado: EstadoCredito.Cancelado);
        _stubAptitud.ResultadoAptitud = AptitudApto();

        var result = await _service.ValidarVentaCreditoPersonalAsync(cliente.Id, montoVenta: 100m, creditoId: credito.Id);

        Assert.True(result.NoViable);
    }

    [Fact]
    public async Task ValidarVenta_CreditoEspecificoActivo_MontoOk_PuedeProceeder()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, estado: EstadoCredito.Activo, saldoPendiente: 5000m);
        _stubAptitud.ResultadoAptitud = AptitudApto();

        var result = await _service.ValidarVentaCreditoPersonalAsync(cliente.Id, montoVenta: 500m, creditoId: credito.Id);

        Assert.True(result.PuedeProceeder);
    }

    [Fact]
    public async Task ValidarVenta_CreditoEspecificoActivo_MontoExcedeSaldo_EsNoViable()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, estado: EstadoCredito.Activo, saldoPendiente: 200m);
        _stubAptitud.ResultadoAptitud = AptitudApto();

        var result = await _service.ValidarVentaCreditoPersonalAsync(cliente.Id, montoVenta: 500m, creditoId: credito.Id);

        Assert.True(result.NoViable);
        Assert.NotEmpty(result.RequisitosPendientes);
    }

    // -------------------------------------------------------------------------
    // PrevalidarAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Prevalidar_ClienteApto_RetornaAprobable()
    {
        _stubAptitud.ResultadoAptitud = AptitudApto(cupoDisponible: 10000m);
        _stubAptitud.CupoDisponible = 10000m;

        var result = await _service.PrevalidarAsync(1, monto: 500m);

        Assert.Equal(ResultadoPrevalidacion.Aprobable, result.Resultado);
        Assert.Empty(result.Motivos.Where(m => m.EsBloqueante));
    }

    [Fact]
    public async Task Prevalidar_ClienteNoApto_RetornaNoViableConMotivos()
    {
        _stubAptitud.ResultadoAptitud = AptitudNoApto();

        var result = await _service.PrevalidarAsync(1, monto: 100m);

        Assert.Equal(ResultadoPrevalidacion.NoViable, result.Resultado);
        Assert.NotEmpty(result.Motivos);
    }

    [Fact]
    public async Task Prevalidar_ExcepcionInterna_RetornaNoViable()
    {
        _stubAptitud.LanzarExcepcion = true;

        var result = await _service.PrevalidarAsync(1, monto: 100m);

        Assert.Equal(ResultadoPrevalidacion.NoViable, result.Resultado);
        Assert.Contains(result.Motivos, m => m.EsBloqueante);
    }

    // -------------------------------------------------------------------------
    // ValidarConfirmacionVentaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidarConfirmacion_VentaNoExiste_RetornaNoViable()
    {
        var result = await _service.ValidarConfirmacionVentaAsync(99999);

        Assert.True(result.PendienteRequisitos);
    }

    [Fact]
    public async Task ValidarConfirmacion_PagoEfectivo_PuedeProceeder()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, tipoPago: TipoPago.Efectivo);

        var result = await _service.ValidarConfirmacionVentaAsync(venta.Id);

        // Pago no crediticio → no hay validaciones adicionales
        Assert.True(result.PuedeProceeder);
    }

    [Fact]
    public async Task ValidarConfirmacion_CreditoPersonal_ClienteApto_PuedeProceeder()
    {
        var cliente = await SeedClienteAsync();
        var venta = await SeedVentaAsync(cliente.Id, tipoPago: TipoPago.CreditoPersonal, total: 500m);
        _stubAptitud.ResultadoAptitud = AptitudApto(cupoDisponible: 10000m);
        _stubAptitud.CupoDisponible = 10000m;

        var result = await _service.ValidarConfirmacionVentaAsync(venta.Id);

        Assert.True(result.PuedeProceeder);
    }

    [Fact]
    public async Task ValidarConfirmacion_CreditoConfigurado_CupoInsuficiente_PuedeProceeder()
    {
        // Crédito ya configurado → la re-evaluación de cupo no debe bloquear la confirmación
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, estado: EstadoCredito.Configurado);
        var venta = await SeedVentaAsync(cliente.Id, tipoPago: TipoPago.CreditoPersonal,
            creditoId: credito.Id, total: 500m);
        _stubAptitud.ResultadoAptitud = new AptitudCrediticiaViewModel
        {
            Estado = EstadoCrediticioCliente.NoApto,
            ConfiguracionCompleta = true,
            Motivo = "Cupo insuficiente",
            Detalles = new List<AptitudDetalleItem>
            {
                new() { Categoria = "Cupo", Descripcion = "Cupo insuficiente. Disponible: $0", EsBloqueo = true }
            }
        };

        var result = await _service.ValidarConfirmacionVentaAsync(venta.Id);

        Assert.True(result.PuedeProceeder);
    }

    [Fact]
    public async Task ValidarConfirmacion_CreditoConfigurado_MoraBloqueante_SigueBloquendo()
    {
        // Mora bloqueante sí debe seguir impidiendo la confirmación aunque el crédito esté configurado
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id, estado: EstadoCredito.Configurado);
        var venta = await SeedVentaAsync(cliente.Id, tipoPago: TipoPago.CreditoPersonal,
            creditoId: credito.Id, total: 500m);
        _stubAptitud.ResultadoAptitud = new AptitudCrediticiaViewModel
        {
            Estado = EstadoCrediticioCliente.NoApto,
            ConfiguracionCompleta = true,
            Motivo = "Mora activa",
            Detalles = new List<AptitudDetalleItem>
            {
                new() { Categoria = "Mora", Descripcion = "Mora vencida > 90 días", EsBloqueo = true }
            }
        };

        var result = await _service.ValidarConfirmacionVentaAsync(venta.Id);

        Assert.False(result.PuedeProceeder);
        Assert.Contains(result.RequisitosPendientes, r => r.Tipo == TipoRequisitoPendiente.ClienteNoApto);
    }

    // =========================================================================
    // ClientePuedeRecibirCreditoAsync
    // =========================================================================

    [Fact]
    public async Task ClientePuedeRecibirCredito_ClienteApto_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        _stubAptitud.ResultadoAptitud = AptitudApto(cupoDisponible: 10_000m);
        _stubAptitud.CupoDisponible = 10_000m;

        var resultado = await _service.ClientePuedeRecibirCreditoAsync(cliente.Id, 1_000m);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ClientePuedeRecibirCredito_ClienteNoApto_RetornaFalse()
    {
        var cliente = await SeedClienteAsync();
        _stubAptitud.ResultadoAptitud = AptitudNoApto("Mora");

        var resultado = await _service.ClientePuedeRecibirCreditoAsync(cliente.Id, 1_000m);

        Assert.False(resultado);
    }

    // =========================================================================
    // ObtenerResumenCrediticioAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerResumenCrediticio_ClienteApto_RetornaResumenConDatos()
    {
        var cliente = await SeedClienteAsync();
        _stubAptitud.ResultadoAptitud = AptitudApto(cupoDisponible: 5_000m);

        var resultado = await _service.ObtenerResumenCrediticioAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(5_000m, resultado.CupoDisponible);
    }

    [Fact]
    public async Task ObtenerResumenCrediticio_ConCreditosActivos_IncluijeCreditosEnResumen()
    {
        var cliente = await SeedClienteAsync();
        _stubAptitud.ResultadoAptitud = AptitudApto(cupoDisponible: 10_000m);
        await SeedCreditoAsync(cliente.Id, EstadoCredito.Activo, 3_000m);

        var resultado = await _service.ObtenerResumenCrediticioAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Single(resultado.CreditosActivos);
        Assert.Equal(3_000m, resultado.CreditosActivos[0].SaldoDisponible);
    }

    [Fact]
    public async Task ObtenerResumenCrediticio_ClienteNoApto_RetornaMensajeAdvertencia()
    {
        var cliente = await SeedClienteAsync();
        _stubAptitud.ResultadoAptitud = AptitudNoApto("Mora");

        var resultado = await _service.ObtenerResumenCrediticioAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.NotNull(resultado.MensajeAdvertencia);
    }
}

// ---------------------------------------------------------------------------
// Stub configurable de IClienteAptitudService
// ---------------------------------------------------------------------------

internal sealed class StubClienteAptitudService : IClienteAptitudService
{
    public AptitudCrediticiaViewModel ResultadoAptitud { get; set; } = new()
    {
        Estado = EstadoCrediticioCliente.Apto,
        ConfiguracionCompleta = true
    };
    public decimal CupoDisponible { get; set; } = 10000m;
    public bool LanzarExcepcion { get; set; } = false;

    public Task<AptitudCrediticiaViewModel> EvaluarAptitudSinGuardarAsync(int clienteId)
    {
        if (LanzarExcepcion) throw new InvalidOperationException("Error simulado");
        return Task.FromResult(ResultadoAptitud);
    }

    public Task<decimal> GetCupoDisponibleAsync(int clienteId)
        => Task.FromResult(CupoDisponible);

    // Métodos no usados por ValidacionVentaService — implementación mínima
    public Task<AptitudCrediticiaViewModel> EvaluarAptitudAsync(int clienteId, bool guardarResultado = true)
        => Task.FromResult(ResultadoAptitud);

    public Task<AptitudCrediticiaViewModel?> GetUltimaEvaluacionAsync(int clienteId)
        => Task.FromResult<AptitudCrediticiaViewModel?>(null);

    public Task<(bool EsApto, string? Motivo)> VerificarAptitudParaMontoAsync(int clienteId, decimal monto)
        => Task.FromResult((true, (string?)null));

    public Task<AptitudDocumentacionDetalle> EvaluarDocumentacionAsync(int clienteId)
        => Task.FromResult(new AptitudDocumentacionDetalle { Completa = true });

    public Task<AptitudCupoDetalle> EvaluarCupoAsync(int clienteId)
        => Task.FromResult(new AptitudCupoDetalle { TieneCupoAsignado = true });

    public Task<AptitudMoraDetalle> EvaluarMoraAsync(int clienteId)
        => Task.FromResult(new AptitudMoraDetalle());

    public Task<ConfiguracionCredito> GetConfiguracionAsync()
        => Task.FromResult(new ConfiguracionCredito());

    public Task<ConfiguracionCredito> UpdateConfiguracionAsync(ConfiguracionCreditoViewModel viewModel)
        => Task.FromResult(new ConfiguracionCredito());

    public Task<(bool EstaConfigurando, string? Mensaje)> VerificarConfiguracionAsync()
        => Task.FromResult((false, (string?)null));

    public Task<bool> AsignarLimiteCreditoAsync(int clienteId, decimal limite, string? motivo = null)
        => Task.FromResult(true);

    public Task<decimal> GetCreditoUtilizadoAsync(int clienteId)
        => Task.FromResult(0m);

    public Task<ScoringThresholdsViewModel> GetScoringThresholdsAsync()
        => Task.FromResult(new ScoringThresholdsViewModel());

    public Task UpdateScoringThresholdsAsync(ScoringThresholdsViewModel model)
        => Task.CompletedTask;

    public Task<SemaforoFinancieroViewModel> GetSemaforoFinancieroAsync()
        => Task.FromResult(new SemaforoFinancieroViewModel());

    public Task UpdateSemaforoFinancieroAsync(SemaforoFinancieroViewModel model)
        => Task.CompletedTask;
}
