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
using TheBuryProject.Tests.Helpers;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Mora;
using TheBuryProject.ViewModels.Requests;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para MoraService.
/// Cubren ResolverAlertaAsync, MarcarAlertaComoLeidaAsync, RegistrarContactoAsync
/// (con y sin promesa de pago), RegistrarPromesaPagoAsync, MarcarPromesaCumplidaAsync,
/// MarcarPromesaIncumplidaAsync (incluye escalado de prioridad) y CrearAcuerdoPagoAsync
/// (happy path, máximo de cuotas, entrega mínima, condonación no permitida).
/// </summary>
public class MoraServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly MoraService _service;
    private readonly RecordingCreditoService _creditoServiceFake;
    private readonly ClienteScoringService _clienteScoringService;

    public MoraServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        _creditoServiceFake = new RecordingCreditoService();
        _clienteScoringService = new ClienteScoringService(_context, NullLogger<ClienteScoringService>.Instance);
        _service = new MoraService(_context, _mapper, NullLogger<MoraService>.Instance, _creditoServiceFake, _clienteScoringService, RelojComercial.Sistema);
    }

    /// <summary>
    /// PUN-ML7 (corrección): instancia alternativa con un <see cref="IRelojComercial"/> inyectado
    /// explícitamente, para probar que la frontera de vencimiento de <c>ProcesarMoraAsync</c> depende
    /// de él y no de <see cref="DateTime.UtcNow"/>/<see cref="DateTime.Today"/>.
    /// </summary>
    private MoraService CrearServicioConReloj(IRelojComercial reloj) =>
        new(_context, _mapper, NullLogger<MoraService>.Instance, _creditoServiceFake, _clienteScoringService, reloj);

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
            Apellido = "Mora",
            TipoDocumento = "DNI",
            NumeroDocumento = Guid.NewGuid().ToString("N")[..8],
            Email = "mora@test.com"
        };
        _context.Set<Cliente>().Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Credito> SeedCreditoAsync(int clienteId)
    {
        var credito = new Credito
        {
            Numero = Guid.NewGuid().ToString("N")[..10],
            ClienteId = clienteId,
            Estado = EstadoCredito.Activo,
            MontoSolicitado = 10_000m,
            MontoAprobado = 10_000m,
            SaldoPendiente = 5_000m,
            TasaInteres = 3m,
            CantidadCuotas = 12,
            FechaSolicitud = DateTime.UtcNow
        };
        _context.Set<Credito>().Add(credito);
        await _context.SaveChangesAsync();
        return credito;
    }

    private async Task<AlertaCobranza> SeedAlertaAsync(
        int clienteId,
        int creditoId,
        PrioridadAlerta prioridad = PrioridadAlerta.Media,
        EstadoGestionCobranza estado = EstadoGestionCobranza.Pendiente,
        bool resuelta = false)
    {
        var alerta = new AlertaCobranza
        {
            ClienteId = clienteId,
            CreditoId = creditoId,
            Prioridad = prioridad,
            EstadoGestion = estado,
            Resuelta = resuelta,
            MontoVencido = 1_000m,
            FechaAlerta = DateTime.UtcNow,
            Tipo = TipoAlertaCobranza.CuotaVencida
        };
        _context.Set<AlertaCobranza>().Add(alerta);
        await _context.SaveChangesAsync();
        await _context.Entry(alerta).ReloadAsync();
        return alerta;
    }

    // -------------------------------------------------------------------------
    // ResolverAlertaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolverAlerta_AlertaActiva_MarcaResuelta()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var resultado = await _service.ResolverAlertaAsync(
            alerta.Id, "Pago recibido", alerta.RowVersion);

        Assert.True(resultado);
        var alertaBd = await _context.Set<AlertaCobranza>().FirstAsync(a => a.Id == alerta.Id);
        Assert.True(alertaBd.Resuelta);
        Assert.NotNull(alertaBd.FechaResolucion);
        Assert.Equal("Pago recibido", alertaBd.Observaciones);
    }

    [Fact]
    public async Task ResolverAlerta_YaResuelta_RetornaTrueIdempotente()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id, resuelta: true);

        // No requiere rowVersion cuando ya está resuelta (retorna true inmediatamente)
        var resultado = await _service.ResolverAlertaAsync(alerta.Id, null, null);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ResolverAlerta_SinRowVersion_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ResolverAlertaAsync(alerta.Id, null, Array.Empty<byte>()));
    }

    [Fact]
    public async Task ResolverAlerta_NoExiste_RetornaFalse()
    {
        var resultado = await _service.ResolverAlertaAsync(99999, null, new byte[8]);
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // MarcarAlertaComoLeidaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarComoLeida_AlertaActiva_RetornaTrue()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var resultado = await _service.MarcarAlertaComoLeidaAsync(alerta.Id, alerta.RowVersion);

        Assert.True(resultado);
    }

    [Fact]
    public async Task MarcarComoLeida_SinRowVersion_LanzaExcepcion()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.MarcarAlertaComoLeidaAsync(alerta.Id, null));
    }

    // -------------------------------------------------------------------------
    // RegistrarContactoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarContacto_SinPromesa_GuardaHistorial()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var contacto = new RegistrarContactoViewModel
        {
            ClienteId = cliente.Id,
            AlertaId = alerta.Id,
            TipoContacto = TipoContacto.LlamadaTelefonica,
            Resultado = ResultadoContacto.ContactoExitoso,
            Observaciones = "Se habló con el cliente",
            DuracionMinutos = 5
        };

        var resultado = await _service.RegistrarContactoAsync(contacto, "gestor1");

        Assert.True(resultado);
        var historial = await _context.HistorialContactos
            .Where(h => h.ClienteId == cliente.Id)
            .ToListAsync();
        Assert.Single(historial);
        Assert.Equal("gestor1", historial[0].GestorId);
        Assert.Equal(ResultadoContacto.ContactoExitoso, historial[0].Resultado);
    }

    [Fact]
    public async Task RegistrarContacto_ConPromesaPago_ActualizaAlerta()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);
        var fechaPromesa = DateTime.Today.AddDays(7);

        var contacto = new RegistrarContactoViewModel
        {
            ClienteId = cliente.Id,
            AlertaId = alerta.Id,
            TipoContacto = TipoContacto.LlamadaTelefonica,
            Resultado = ResultadoContacto.PromesaPago,
            FechaPromesaPago = fechaPromesa,
            MontoPromesaPago = 500m
        };

        await _service.RegistrarContactoAsync(contacto, "gestor1");

        var alertaBd = await _context.Set<AlertaCobranza>().FirstAsync(a => a.Id == alerta.Id);
        Assert.Equal(EstadoGestionCobranza.PromesaPago, alertaBd.EstadoGestion);
        Assert.Equal(fechaPromesa, alertaBd.FechaPromesaPago);
        Assert.Equal(500m, alertaBd.MontoPromesaPago);
    }

    // -------------------------------------------------------------------------
    // RegistrarPromesaPagoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RegistrarPromesa_AlertaExistente_ActualizaEstadoYGuardaHistorial()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);
        var fechaPromesa = DateTime.Today.AddDays(10);

        var promesa = new RegistrarPromesaViewModel
        {
            AlertaId = alerta.Id,
            ClienteId = cliente.Id,
            FechaPromesa = fechaPromesa,
            MontoPromesa = 800m,
            Observaciones = "Se acordó pago el viernes"
        };

        var resultado = await _service.RegistrarPromesaPagoAsync(promesa, "gestor2");

        Assert.True(resultado);

        var alertaBd = await _context.Set<AlertaCobranza>().FirstAsync(a => a.Id == alerta.Id);
        Assert.Equal(EstadoGestionCobranza.PromesaPago, alertaBd.EstadoGestion);
        Assert.Equal(fechaPromesa, alertaBd.FechaPromesaPago);

        var historial = await _context.HistorialContactos
            .Where(h => h.AlertaCobranzaId == alerta.Id)
            .ToListAsync();
        Assert.Single(historial);
        Assert.Equal(ResultadoContacto.PromesaPago, historial[0].Resultado);
    }

    [Fact]
    public async Task RegistrarPromesa_AlertaNoExiste_RetornaFalse()
    {
        var promesa = new RegistrarPromesaViewModel
        {
            AlertaId = 99999,
            ClienteId = 1,
            FechaPromesa = DateTime.Today.AddDays(5),
            MontoPromesa = 100m
        };

        var resultado = await _service.RegistrarPromesaPagoAsync(promesa, "gestor1");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // MarcarPromesaCumplidaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarPromesaCumplida_AlertaExistente_MarcaRegularizado()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id,
            estado: EstadoGestionCobranza.PromesaPago);

        var resultado = await _service.MarcarPromesaCumplidaAsync(alerta.Id);

        Assert.True(resultado);
        var alertaBd = await _context.Set<AlertaCobranza>().FirstAsync(a => a.Id == alerta.Id);
        Assert.Equal(EstadoGestionCobranza.Regularizado, alertaBd.EstadoGestion);
        Assert.True(alertaBd.Resuelta);
        Assert.NotNull(alertaBd.FechaResolucion);
    }

    [Fact]
    public async Task MarcarPromesaCumplida_NoExiste_RetornaFalse()
    {
        var resultado = await _service.MarcarPromesaCumplidaAsync(99999);
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // MarcarPromesaIncumplidaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarPromesaIncumplida_EscalaPrioridadYLimpiaDatos()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id,
            prioridad: PrioridadAlerta.Media,
            estado: EstadoGestionCobranza.PromesaPago);

        var resultado = await _service.MarcarPromesaIncumplidaAsync(
            alerta.Id, "No pagó en la fecha acordada");

        Assert.True(resultado);
        var alertaBd = await _context.Set<AlertaCobranza>().FirstAsync(a => a.Id == alerta.Id);
        Assert.Equal(EstadoGestionCobranza.EnGestion, alertaBd.EstadoGestion);
        Assert.Null(alertaBd.FechaPromesaPago);
        Assert.Null(alertaBd.MontoPromesaPago);
        // Prioridad debe escalar de Media(2) a Alta(3)
        Assert.Equal(PrioridadAlerta.Alta, alertaBd.Prioridad);
        Assert.Contains("No pagó en la fecha acordada", alertaBd.Observaciones);
    }

    [Fact]
    public async Task MarcarPromesaIncumplida_PrioridadCritica_NoEscalaMas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id,
            prioridad: PrioridadAlerta.Critica); // ya en el máximo

        await _service.MarcarPromesaIncumplidaAsync(alerta.Id, "Segundo incumplimiento");

        var alertaBd = await _context.Set<AlertaCobranza>().FirstAsync(a => a.Id == alerta.Id);
        Assert.Equal(PrioridadAlerta.Critica, alertaBd.Prioridad); // no cambia
    }

    [Fact]
    public async Task MarcarPromesaIncumplida_NoExiste_RetornaFalse()
    {
        var resultado = await _service.MarcarPromesaIncumplidaAsync(99999, "Motivo");
        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // CrearAcuerdoPagoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearAcuerdo_SinConfiguracion_CreaConDefaults()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var acuerdo = new CrearAcuerdoViewModel
        {
            AlertaId = alerta.Id,
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            MontoDeudaOriginal = 5_000m,
            MontoMoraOriginal = 500m,
            MontoCondonar = 0m,
            MontoEntregaInicial = 1_000m,
            CantidadCuotas = 3,
            FechaPrimeraCuota = DateTime.Today.AddMonths(1)
        };

        var acuerdoId = await _service.CrearAcuerdoPagoAsync(acuerdo, "gestor1");

        Assert.True(acuerdoId > 0);
        var acuerdoBd = await _context.AcuerdosPago.FirstAsync(a => a.Id == acuerdoId);
        Assert.Equal(cliente.Id, acuerdoBd.ClienteId);
        Assert.Equal(3, acuerdoBd.CantidadCuotas);
        Assert.Equal(EstadoAcuerdo.Borrador, acuerdoBd.Estado);
        // MontoCuotaAcuerdo = (5000 + 500 - 0 - 1000) / 3 = 1500
        Assert.Equal(1_500m, acuerdoBd.MontoCuotaAcuerdo);
    }

    [Fact]
    public async Task CrearAcuerdo_MaximoCuotasExcedido_LanzaExcepcion()
    {
        // Seed ConfiguracionMora con límite de cuotas
        var config = new ConfiguracionMora
        {
            TasaMoraBase = 0.1m,
            DiasGracia = 3,
            ProcesoAutomaticoActivo = false,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            MaximoCuotasAcuerdo = 6
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var acuerdo = new CrearAcuerdoViewModel
        {
            AlertaId = alerta.Id,
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            MontoDeudaOriginal = 5_000m,
            MontoMoraOriginal = 0m,
            MontoCondonar = 0m,
            MontoEntregaInicial = 500m,
            CantidadCuotas = 12, // excede el máximo de 6
            FechaPrimeraCuota = DateTime.Today.AddMonths(1)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearAcuerdoPagoAsync(acuerdo, "gestor1"));
    }

    [Fact]
    public async Task CrearAcuerdo_CondonacionNoPermitida_LanzaExcepcion()
    {
        var config = new ConfiguracionMora
        {
            TasaMoraBase = 0.1m,
            DiasGracia = 3,
            ProcesoAutomaticoActivo = false,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            PermitirCondonacionMora = false
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var acuerdo = new CrearAcuerdoViewModel
        {
            AlertaId = alerta.Id,
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            MontoDeudaOriginal = 5_000m,
            MontoMoraOriginal = 500m,
            MontoCondonar = 200m, // intentar condonar cuando no está permitido
            MontoEntregaInicial = 500m,
            CantidadCuotas = 3,
            FechaPrimeraCuota = DateTime.Today.AddMonths(1)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearAcuerdoPagoAsync(acuerdo, "gestor1"));
    }

    // =========================================================================
    // ProcesarMoraAsync
    // =========================================================================

    private static int _cuotaCounter = 0;

    private async Task<Cuota> SeedCuotaVencidaAsync(int creditoId, int diasVencido = 10, decimal montoTotal = 1_000m)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = Interlocked.Increment(ref _cuotaCounter),
            MontoCapital = montoTotal * 0.8m,
            MontoInteres = montoTotal * 0.2m,
            MontoTotal = montoTotal,
            MontoPagado = 0m,
            Estado = EstadoCuota.Pendiente,
            FechaVencimiento = DateTime.Today.AddDays(-diasVencido)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    private async Task<Cuota> SeedCuotaPorVencerAsync(int creditoId, int diasHastaVencimiento = 3, decimal montoTotal = 1_000m)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = Interlocked.Increment(ref _cuotaCounter),
            MontoCapital = montoTotal * 0.8m,
            MontoInteres = montoTotal * 0.2m,
            MontoTotal = montoTotal,
            MontoPagado = 0m,
            Estado = EstadoCuota.Pendiente,
            FechaVencimiento = DateTime.Today.AddDays(diasHastaVencimiento)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    /// <summary>
    /// PUN-ML7 (corrección): helper genérico para los escenarios de selección de mora que
    /// <see cref="SeedCuotaVencidaAsync"/>/<see cref="SeedCuotaPorVencerAsync"/> no cubren — Estado y
    /// MontoPagado explícitos, para poblar cuotas Vencida/Parcial/Pagada/Cancelada directamente
    /// (simulando el resultado de ActualizarEstadoCuotasAsync o de un pago parcial previo).
    /// </summary>
    private async Task<Cuota> SeedCuotaConEstadoAsync(
        int creditoId,
        EstadoCuota estado,
        int diasVencido,
        decimal montoTotal = 1_000m,
        decimal montoPagado = 0m)
    {
        var cuota = new Cuota
        {
            CreditoId = creditoId,
            NumeroCuota = Interlocked.Increment(ref _cuotaCounter),
            MontoCapital = montoTotal * 0.8m,
            MontoInteres = montoTotal * 0.2m,
            MontoTotal = montoTotal,
            MontoPagado = montoPagado,
            Estado = estado,
            FechaVencimiento = DateTime.Today.AddDays(-diasVencido)
        };
        _context.Set<Cuota>().Add(cuota);
        await _context.SaveChangesAsync();
        return cuota;
    }

    [Fact]
    public async Task ProcesarMora_SinCuotasVencidas_GeneraLogExitosoSinAlertas()
    {
        await _service.ProcesarMoraAsync();

        var log = await _context.LogsMora.OrderByDescending(l => l.Id).FirstAsync();
        Assert.True(log.Exitoso);
        Assert.Equal(0, log.CuotasProcesadas);
        Assert.Equal(0, log.AlertasGeneradas);
    }

    [Fact]
    public async Task ProcesarMora_ConCuotaVencida_GeneraAlertaCobranza()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        var alertas = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Single(alertas);
        Assert.False(alertas[0].Resuelta);
        Assert.Equal(cliente.Id, alertas[0].ClienteId);
    }

    [Fact]
    public async Task ProcesarMora_ConCuotaVencida_LogRegistraCuotasProcesadas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10, montoTotal: 2_000m);

        await _service.ProcesarMoraAsync();

        var log = await _context.LogsMora.OrderByDescending(l => l.Id).FirstAsync();
        Assert.True(log.Exitoso);
        Assert.Equal(1, log.CuotasProcesadas);
        Assert.Equal(1, log.AlertasGeneradas);
        Assert.Equal(1, log.CuotasConMora);
        Assert.Equal(2_000m, log.TotalMora);
    }

    [Fact]
    public async Task ProcesarMora_CreditoConAlertaActiva_NoGeneraNuevaAlerta()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        // Alerta preexistente activa (no resuelta)
        await SeedAlertaAsync(cliente.Id, credito.Id);

        await _service.ProcesarMoraAsync();

        var alertas = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Single(alertas); // Solo la preexistente, no se creó una nueva
    }

    [Fact]
    public async Task ProcesarMora_DentroDelPeriodoDeGracia_NoGeneraAlerta()
    {
        // Config con diasGracia = 15 → cuota vencida hace 5 días no debe generar alerta
        var config = new ConfiguracionMora
        {
            TasaMoraBase = 1m,
            DiasGracia = 15,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 5); // dentro del período de gracia

        await _service.ProcesarMoraAsync();

        var alertas = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Empty(alertas);
    }

    [Fact]
    public async Task ProcesarMora_CuotaPorVencer_GeneraAlertaProximoVencimiento()
    {
        // Config con DiasAntesAlertaPreventiva = 5 → cuota que vence en 3 días sí genera alerta
        var config = new ConfiguracionMora
        {
            TasaMoraBase = 1m,
            DiasGracia = 0,
            DiasAntesAlertaPreventiva = 5,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaPorVencerAsync(credito.Id, diasHastaVencimiento: 3);

        await _service.ProcesarMoraAsync();

        var alertas = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito.Id && a.Tipo == TipoAlertaCobranza.ProximoVencimiento)
            .ToListAsync();
        Assert.Single(alertas);
        Assert.Equal(PrioridadAlerta.Baja, alertas[0].Prioridad);
    }

    [Fact]
    public async Task ProcesarMora_MultiplesCreditos_GeneraUnaAlertaPorCredito()
    {
        var cliente1 = await SeedClienteAsync();
        var credito1 = await SeedCreditoAsync(cliente1.Id);
        await SeedCuotaVencidaAsync(credito1.Id, diasVencido: 10);
        await SeedCuotaVencidaAsync(credito1.Id, diasVencido: 20); // segunda cuota mismo crédito

        var cliente2 = await SeedClienteAsync();
        var credito2 = await SeedCreditoAsync(cliente2.Id);
        await SeedCuotaVencidaAsync(credito2.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        // Una alerta por crédito, no por cuota
        var alertasCredito1 = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito1.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Single(alertasCredito1);
        Assert.Equal(2, alertasCredito1[0].CuotasVencidas); // agrupa ambas cuotas

        var alertasCredito2 = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito2.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Single(alertasCredito2);
    }

    [Fact]
    public async Task ProcesarMora_SiempreGuardaLogAunConError()
    {
        // El proceso siempre registra el log en el finally block
        // Ejecutamos con DB vacía → no hay error, pero validamos que el log existe
        await _service.ProcesarMoraAsync();

        var count = await _context.LogsMora.CountAsync();
        Assert.True(count >= 1);
    }

    // =========================================================================
    // ProcesarMoraAsync — FASE 10C: días de gracia (opción B)
    // =========================================================================

    [Fact]
    public async Task ProcesarMora_ActualizarMoraAutomaticaYCambiarEstadoActivos_LlamaActualizarEstadoCuotas()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = true,
            CambiarEstadoCuotaAuto = true
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        Assert.Equal(1, _creditoServiceFake.ActualizarEstadoCuotasAsyncCallCount);
    }

    [Fact]
    public async Task ProcesarMora_CambiarEstadoCuotaAutoFalse_NoLlamaActualizarEstadoCuotas()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = true,
            CambiarEstadoCuotaAuto = false
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        Assert.Equal(0, _creditoServiceFake.ActualizarEstadoCuotasAsyncCallCount);
    }

    [Fact]
    public async Task ProcesarMora_ActualizarMoraAutomaticaFalse_NoModificaDatosDeMora()
    {
        // Aunque CambiarEstadoCuotaAuto/ImpactarScorePorMora estén en true, el master switch
        // ActualizarMoraAutomaticamente=false debe impedir cualquier mutación.
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = false,
            CambiarEstadoCuotaAuto = true,
            ImpactarScorePorMora = true
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        Assert.Equal(0, _creditoServiceFake.ActualizarEstadoCuotasAsyncCallCount);
        var historial = await _context.ClientesPuntajeHistorial
            .Where(h => h.ClienteId == cliente.Id && h.Origen == "RecalculoAutomaticoMora")
            .ToListAsync();
        Assert.Empty(historial);
    }

    [Fact]
    public async Task ProcesarMora_ImpactarScoreDentroDeGracia_NoAuditaPuntaje()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 15,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = true,
            ImpactarScorePorMora = true
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        cliente.PuntajeCliente = 3;
        await _context.SaveChangesAsync();

        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 5); // dentro del período de gracia (15)

        await _service.ProcesarMoraAsync();

        var historial = await _context.ClientesPuntajeHistorial
            .Where(h => h.ClienteId == cliente.Id && h.Origen == "RecalculoAutomaticoMora")
            .ToListAsync();
        Assert.Empty(historial);

        _context.ChangeTracker.Clear();
        var clienteNoTocado = await _context.Clientes.FindAsync(cliente.Id);
        Assert.Equal(3, clienteNoTocado!.PuntajeCliente); // dentro de gracia: no se recalcula
    }

    [Fact]
    public async Task ProcesarMora_ImpactarScoreFueraDeGracia_AuditaPuntajeConOrigenMora()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 3,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = true,
            ImpactarScorePorMora = true
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        // Puntaje previo simulado > 0 para que el recalculo por mora (que clampea a 0
        // con la config de scoring por defecto) produzca un cambio observable.
        cliente.PuntajeCliente = 3;
        await _context.SaveChangesAsync();

        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10); // fuera del período de gracia (3)

        await _service.ProcesarMoraAsync();

        var historial = await _context.ClientesPuntajeHistorial
            .Where(h => h.ClienteId == cliente.Id && h.Origen == "RecalculoAutomaticoMora")
            .ToListAsync();
        Assert.Single(historial);
        Assert.Equal(0, historial[0].Puntaje); // base 0 + atraso (-2) clampeado a mínimo 0

        _context.ChangeTracker.Clear();
        var clienteActualizado = await _context.Clientes.FindAsync(cliente.Id);
        Assert.Equal(0, clienteActualizado!.PuntajeCliente);
    }

    [Fact]
    public async Task ProcesarMora_ImpactarScorePorMoraFalse_NoAuditaPuntajeAunFueraDeGracia()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 3,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = true,
            ImpactarScorePorMora = false
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        cliente.PuntajeCliente = 3;
        await _context.SaveChangesAsync();

        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        var historial = await _context.ClientesPuntajeHistorial
            .Where(h => h.ClienteId == cliente.Id && h.Origen == "RecalculoAutomaticoMora")
            .ToListAsync();
        Assert.Empty(historial);

        _context.ChangeTracker.Clear();
        var clienteNoTocado = await _context.Clientes.FindAsync(cliente.Id);
        Assert.Equal(3, clienteNoTocado!.PuntajeCliente); // sin recalculo, queda igual
    }

    [Fact]
    public async Task ProcesarMora_ImpactarScore_CapitalSaldadoSoloConPunitorioPendiente_NoImpacta()
    {
        // PUN-ML7 (auditoría, lote 2): ImpactarScorePorMoraAsync no chequeaba MontoPagado<MontoTotal
        // — una cuota con capital saldado y Estado=Parcial únicamente por un punitorio aplicado
        // pendiente (ver EstadoCuotaResolver.Resolver) impactaba el puntaje como si debiera capital.
        var config = new ConfiguracionMora
        {
            DiasGracia = 3,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = true,
            ImpactarScorePorMora = true
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        cliente.PuntajeCliente = 3;
        await _context.SaveChangesAsync();

        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 1_000m); // capital saldado, solo punitorio pendiente

        await _service.ProcesarMoraAsync();

        var historial = await _context.ClientesPuntajeHistorial
            .Where(h => h.ClienteId == cliente.Id && h.Origen == "RecalculoAutomaticoMora")
            .ToListAsync();
        Assert.Empty(historial);

        _context.ChangeTracker.Clear();
        var clienteNoTocado = await _context.Clientes.FindAsync(cliente.Id);
        Assert.Equal(3, clienteNoTocado!.PuntajeCliente); // sin recalculo, queda igual
    }

    [Fact]
    public async Task ProcesarMora_EjecucionRepetidaSinCambioDePuntaje_NoDuplicaHistorial()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 3,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0),
            ActualizarMoraAutomaticamente = true,
            ImpactarScorePorMora = true
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        cliente.PuntajeCliente = 3;
        await _context.SaveChangesAsync();

        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();
        await _service.ProcesarMoraAsync(); // misma mora, el puntaje ya no cambia (queda en 0)

        var historial = await _context.ClientesPuntajeHistorial
            .Where(h => h.ClienteId == cliente.Id && h.Origen == "RecalculoAutomaticoMora")
            .ToListAsync();
        Assert.Single(historial);
    }

    // =========================================================================
    // ProcesarMoraAsync — corrección PUN-ML7: selección de cuotas en mora
    //
    // Antes filtraba solo Estado==Pendiente. Con ActualizarEstadoCuotasAsync transicionando
    // Pendiente→Vencida (bulk, corrida previa o el mismo config.CambiarEstadoCuotaAuto de esta
    // corrida), esa condición hacía que una cuota vencida se volviera invisible para la generación
    // de alertas apenas cambiaba de estado — aunque siguiera en mora real. Esta sección prueba que
    // la selección ahora depende de la condición de negocio (no terminal, saldo de capital
    // pendiente, vencida) y no del valor puntual del enum.
    // =========================================================================

    private async Task<AlertaCobranza?> AlertaVencidaDelCreditoAsync(int creditoId) =>
        await _context.AlertasCobranza
            .Where(a => a.CreditoId == creditoId && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .FirstOrDefaultAsync();

    [Fact]
    public async Task ProcesarMora_CuotaVencidaEstado_ConSaldo_Entra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Vencida, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        Assert.NotNull(await AlertaVencidaDelCreditoAsync(credito.Id));
    }

    [Fact]
    public async Task ProcesarMora_CuotaParcialVencida_ConSaldoDeCapital_Entra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 400m);

        await _service.ProcesarMoraAsync();

        Assert.NotNull(await AlertaVencidaDelCreditoAsync(credito.Id));
    }

    [Fact]
    public async Task ProcesarMora_CuotaParcialNoVencida_NoEntra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        // diasVencido: -3 → FechaVencimiento en el futuro (todavía no vence).
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: -3,
            montoTotal: 1_000m, montoPagado: 400m);

        await _service.ProcesarMoraAsync();

        Assert.Null(await AlertaVencidaDelCreditoAsync(credito.Id));
    }

    [Fact]
    public async Task ProcesarMora_CuotaPagada_NoEntra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Pagada, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 1_000m);

        await _service.ProcesarMoraAsync();

        Assert.Null(await AlertaVencidaDelCreditoAsync(credito.Id));
    }

    [Fact]
    public async Task ProcesarMora_CuotaCancelada_NoEntra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Cancelada, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        Assert.Null(await AlertaVencidaDelCreditoAsync(credito.Id));
    }

    [Fact]
    public async Task ProcesarMora_CapitalTotalmentePagado_SoloConservaPunitorio_NoEntra()
    {
        // Estado Parcial porque EstadoCuotaResolver la retiene ahí mientras haya un punitorio
        // aplicado pendiente (capital saldado no implica Pagada) — pero sin saldo de CAPITAL, no es
        // candidata a la cola de mora de capital: ese seguimiento es responsabilidad exclusiva de
        // PunitorioService/AlertaVencida no debe duplicar esa cobranza.
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 1_000m);

        await _service.ProcesarMoraAsync();

        Assert.Null(await AlertaVencidaDelCreditoAsync(credito.Id));
    }

    [Fact]
    public async Task ProcesarMora_TransicionPendienteAVencidaEntreCorridas_NoDesaparece()
    {
        // Simula el orden real: una corrida previa (o ActualizarEstadoCuotasAsync disparado por
        // config.CambiarEstadoCuotaAuto en esta misma corrida) ya dejó la cuota en Vencida antes de
        // que ProcesarMoraAsync vuelva a ejecutarse. Con el filtro viejo (Estado==Pendiente) la cuota
        // desaparecía del query y jamás volvía a alertar; con el filtro corregido, sigue entrando
        // mientras conserve saldo de capital y esté vencida.
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);
        cuota.Estado = EstadoCuota.Vencida; // efecto ya aplicado de ActualizarEstadoCuotasAsync
        await _context.SaveChangesAsync();

        await _service.ProcesarMoraAsync();

        var alerta = await AlertaVencidaDelCreditoAsync(credito.Id);
        Assert.NotNull(alerta);
        Assert.Equal(1, alerta!.CuotasVencidas);
    }

    [Fact]
    public async Task ProcesarMora_EjecutadoDosVecesElMismoDia_NoDuplicaAlertas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();
        await _service.ProcesarMoraAsync();

        var alertas = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Single(alertas);
    }

    [Fact]
    public async Task ProcesarMora_DiaDelVencimiento_NoEntra_DiaSiguienteSiEntra()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var clienteHoy = await SeedClienteAsync();
        var creditoHoy = await SeedCreditoAsync(clienteHoy.Id);
        await SeedCuotaVencidaAsync(creditoHoy.Id, diasVencido: 0); // vence hoy, todavía no vencida

        var clienteAyer = await SeedClienteAsync();
        var creditoAyer = await SeedCreditoAsync(clienteAyer.Id);
        await SeedCuotaVencidaAsync(creditoAyer.Id, diasVencido: 1); // venció ayer

        await _service.ProcesarMoraAsync();

        Assert.Null(await AlertaVencidaDelCreditoAsync(creditoHoy.Id));
        var alertaAyer = await AlertaVencidaDelCreditoAsync(creditoAyer.Id);
        Assert.NotNull(alertaAyer);
        Assert.Equal(1, alertaAyer!.DiasAtraso);
    }

    [Fact]
    public async Task ProcesarMora_FronteraUsaRelojComercial_NoUtcNiToday()
    {
        // 2026-06-16 01:00 UTC = 2026-06-15 22:00 ART (UTC-3): HoyComercial es 15/06, un día antes
        // que DateTime.UtcNow.Date/DateTime.Today (16/06). La cuota vence exactamente hoy en
        // Argentina (15/06) — con la fecha comercial correcta NO está vencida (frontera estricta);
        // si el servicio usara UTC/Today en su lugar, la trataría como vencida desde ayer.
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero));
        var servicio = CrearServicioConReloj(reloj);

        // DiasGracia=0 explícito: el default (3) enmascararía la frontera de un día que este test
        // necesita observar.
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaVencidaAsync(credito.Id, diasVencido: 0);
        cuota.FechaVencimiento = reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        await _context.SaveChangesAsync();

        await servicio.ProcesarMoraAsync();

        Assert.Null(await AlertaVencidaDelCreditoAsync(credito.Id));
    }

    // =========================================================================
    // ProcesarMoraAsync — cierre PUN-ML7: AlertaCobranza.DiasAtraso
    //
    // Antes nunca se asignaba (quedaba en 0 pese a documentarse como "días de atraso actuales"),
    // rompiendo GetClientesEnMoraAsync/GetDashboardKPIsAsync (DiasMaxAtraso/DiasPromedioAtraso
    // reales inútiles). Corregido: se calcula con EstadoCuotaResolver.DiasAtrasoDerivado (autoridad
    // canónica, misma que usa GetCreditosEnMoraAsync) tanto al crear la alerta como al reprocesar una
    // ya activa, sin duplicarla ni tocar datos de gestión manual.
    // =========================================================================

    [Fact]
    public async Task ProcesarMora_NuevaAlerta_GuardaDiasAtrasoReales()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        var alerta = await AlertaVencidaDelCreditoAsync(credito.Id);
        Assert.NotNull(alerta);
        Assert.Equal(10, alerta!.DiasAtraso);
    }

    [Fact]
    public async Task ProcesarMora_AlertaActivaExistente_ActualizaDiasAtrasoEnCorridaPosterior()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);

        var relojDia1 = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 20, 15, 0, 0, TimeSpan.Zero)); // 12:00 ART
        var servicioDia1 = CrearServicioConReloj(relojDia1);
        var cuota = await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);
        cuota.FechaVencimiento = relojDia1.HoyComercial.AddDays(-10).ToDateTime(TimeOnly.MinValue);
        await _context.SaveChangesAsync();

        await servicioDia1.ProcesarMoraAsync();

        var alertaDia1 = await AlertaVencidaDelCreditoAsync(credito.Id);
        Assert.NotNull(alertaDia1);
        Assert.Equal(10, alertaDia1!.DiasAtraso);

        // 5 días comerciales después, misma alerta activa se reprocesa (no se crea otra).
        var relojDia2 = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 25, 15, 0, 0, TimeSpan.Zero));
        var servicioDia2 = CrearServicioConReloj(relojDia2);

        await servicioDia2.ProcesarMoraAsync();

        var alertasCredito = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Single(alertasCredito); // no duplicó
        Assert.Equal(alertaDia1.Id, alertasCredito[0].Id); // misma identidad
        Assert.Equal(15, alertasCredito[0].DiasAtraso); // 10 + 5 días transcurridos
    }

    [Fact]
    public async Task ProcesarMora_EjecucionRepetidaMismoDia_NoAlteraDatosManualesYMantieneDiasAtraso()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();

        var alerta = await AlertaVencidaDelCreditoAsync(credito.Id);
        Assert.NotNull(alerta);

        // Simular gestión manual sobre la alerta ya creada.
        alerta!.Observaciones = "Cliente contactado, pidió prórroga";
        alerta.EstadoGestion = EstadoGestionCobranza.EnGestion;
        await _context.SaveChangesAsync();
        var updatedAtAntes = alerta.UpdatedAt;
        var idAntes = alerta.Id;

        await _service.ProcesarMoraAsync(); // misma fecha comercial, segunda corrida

        _context.ChangeTracker.Clear();
        var alertas = await _context.AlertasCobranza
            .Where(a => a.CreditoId == credito.Id && a.Tipo == TipoAlertaCobranza.CuotaVencida)
            .ToListAsync();
        Assert.Single(alertas); // no duplicó
        Assert.Equal(idAntes, alertas[0].Id); // misma identidad
        Assert.Equal(10, alertas[0].DiasAtraso); // mismo valor, idempotente para la misma fecha
        Assert.Equal("Cliente contactado, pidió prórroga", alertas[0].Observaciones); // dato manual intacto
        Assert.Equal(EstadoGestionCobranza.EnGestion, alertas[0].EstadoGestion); // dato manual intacto
        // Sin cambio real en DiasAtraso, el reproceso no debe tocar UpdatedAt (del que depende
        // AlertaCobranza.Leida) — de lo contrario una alerta que nadie leyó aparecería como "leída".
        Assert.Equal(updatedAtAntes, alertas[0].UpdatedAt);
    }

    [Fact]
    public async Task ProcesarMora_TrasCorrida_GetClientesEnMoraAsync_MuestraDiasAtrasoActualizado()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 12);

        await _service.ProcesarMoraAsync();

        var bandeja = await _service.GetClientesEnMoraAsync(new FiltrosBandejaClientes());

        Assert.Single(bandeja.Clientes);
        Assert.Equal(12, bandeja.Clientes[0].DiasMaxAtraso);
    }

    // =========================================================================
    // GetConfiguracionAsync
    // =========================================================================

    [Fact]
    public async Task GetConfiguracion_SinConfiguracionEnBD_CreaConfiguracionPorDefecto()
    {
        var config = await _service.GetConfiguracionAsync();

        Assert.NotNull(config);
        Assert.Equal(3, config.DiasGracia);
        Assert.True(config.ProcesoAutomaticoActivo);
        var persistida = await _context.ConfiguracionesMora.FirstOrDefaultAsync();
        Assert.NotNull(persistida);
    }

    [Fact]
    public async Task GetConfiguracion_ConConfiguracionExistente_LaRetorna()
    {
        var existente = new ConfiguracionMora
        {
            DiasGracia = 5,
            TasaMoraBase = 2m,
            ProcesoAutomaticoActivo = false,
            NotificacionesActivas = true,
            HoraEjecucionDiaria = new TimeSpan(9, 0, 0)
        };
        _context.ConfiguracionesMora.Add(existente);
        await _context.SaveChangesAsync();

        var config = await _service.GetConfiguracionAsync();

        Assert.Equal(existente.Id, config.Id);
        Assert.Equal(5, config.DiasGracia);
    }

    // =========================================================================
    // UpdateConfiguracionAsync
    // =========================================================================

    [Fact]
    public async Task UpdateConfiguracion_HappyPath_ActualizaCampos()
    {
        // Asegurar que existe configuración
        var config = await _service.GetConfiguracionAsync();

        var vm = new ConfiguracionMoraViewModel
        {
            Id = config.Id,
            DiasGracia = 7,
            PorcentajeRecargo = 10m,
            CalculoAutomatico = true,
            NotificacionAutomatica = false,
            HoraEjecucion = new TimeSpan(7, 0, 0)
        };

        var actualizado = await _service.UpdateConfiguracionAsync(vm);

        Assert.Equal(7, actualizado.DiasGracia);
        Assert.Equal(10m, actualizado.TasaMoraBase);
    }

    [Fact]
    public async Task UpdateConfiguracion_IdInexistente_LanzaExcepcion()
    {
        var vm = new ConfiguracionMoraViewModel { Id = 99999 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateConfiguracionAsync(vm));
    }

    // =========================================================================
    // GetAlertasActivasAsync
    // =========================================================================

    [Fact]
    public async Task GetAlertasActivas_SinAlertas_RetornaVacio()
    {
        var resultado = await _service.GetAlertasActivasAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAlertasActivas_ExcluyeResueltas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedAlertaAsync(cliente.Id, credito.Id, resuelta: false);
        await SeedAlertaAsync(cliente.Id, credito.Id, resuelta: true);

        var resultado = await _service.GetAlertasActivasAsync();

        Assert.Single(resultado);
        Assert.All(resultado, a => Assert.False(a.Resuelta));
    }

    // =========================================================================
    // GetTodasAlertasAsync
    // =========================================================================

    [Fact]
    public async Task GetTodasAlertas_InclujeResueltasYActivas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedAlertaAsync(cliente.Id, credito.Id, resuelta: false);
        await SeedAlertaAsync(cliente.Id, credito.Id, resuelta: true);

        var resultado = await _service.GetTodasAlertasAsync();

        Assert.Equal(2, resultado.Count);
    }

    // =========================================================================
    // GetAlertaByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetAlertaById_AlertaExistente_RetornaViewModel()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var resultado = await _service.GetAlertaByIdAsync(alerta.Id);

        Assert.NotNull(resultado);
        Assert.Equal(alerta.Id, resultado!.Id);
    }

    [Fact]
    public async Task GetAlertaById_AlertaInexistente_RetornaNull()
    {
        var resultado = await _service.GetAlertaByIdAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // GetAlertasPorClienteAsync
    // =========================================================================

    [Fact]
    public async Task GetAlertasPorCliente_ClienteSinAlertas_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetAlertasPorClienteAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAlertasPorCliente_RetornaSoloLasDelCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        var cred1 = await SeedCreditoAsync(c1.Id);
        var cred2 = await SeedCreditoAsync(c2.Id);
        await SeedAlertaAsync(c1.Id, cred1.Id);
        await SeedAlertaAsync(c2.Id, cred2.Id);

        var resultado = await _service.GetAlertasPorClienteAsync(c1.Id);

        Assert.Single(resultado);
        Assert.All(resultado, a => Assert.Equal(c1.Id, a.ClienteId));
    }

    // =========================================================================
    // GetLogsAsync
    // =========================================================================

    [Fact]
    public async Task GetLogs_SinLogs_RetornaVacio()
    {
        var resultado = await _service.GetLogsAsync();
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetLogs_ConLogs_RetornaLogs()
    {
        await _service.ProcesarMoraAsync(); // genera un log

        var resultado = await _service.GetLogsAsync();

        Assert.NotEmpty(resultado);
    }

    [Fact]
    public async Task GetLogs_LimitaCantidad()
    {
        // Generar varios logs ejecutando el proceso varias veces
        for (int i = 0; i < 3; i++)
            await _service.ProcesarMoraAsync();

        var resultado = await _service.GetLogsAsync(cantidad: 2);

        Assert.Equal(2, resultado.Count);
    }

    // =========================================================================
    // GetConteoPorPrioridadAsync
    // =========================================================================

    [Fact]
    public async Task GetConteoPorPrioridad_SinAlertas_RetornaCeroEnTodas()
    {
        var resultado = await _service.GetConteoPorPrioridadAsync();

        Assert.Equal(0, resultado["Critica"]);
        Assert.Equal(0, resultado["Alta"]);
        Assert.Equal(0, resultado["Media"]);
        Assert.Equal(0, resultado["Baja"]);
    }

    [Fact]
    public async Task GetConteoPorPrioridad_ConAlertasActivas_ContaClientesUnicos()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedAlertaAsync(cliente.Id, credito.Id, prioridad: PrioridadAlerta.Alta);

        var resultado = await _service.GetConteoPorPrioridadAsync();

        Assert.Equal(1, resultado["Alta"]);
        Assert.Equal(0, resultado["Critica"]);
    }

    // =========================================================================
    // GetCreditosEnMoraAsync
    // =========================================================================

    [Fact]
    public async Task GetCreditosEnMora_ClienteSinCreditosVencidos_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCreditosEnMora_ConCuotasVencidas_RetornaCredito()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 15);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Single(resultado);
        Assert.Equal(credito.Id, resultado[0].CreditoId);
        Assert.Equal(1, resultado[0].CuotasVencidas);
    }

    // -------------------------------------------------------------------------
    // GetCreditosEnMoraAsync — corrección PUN-ML7 (auditoría, lote 2): predicado canónico de
    // mora de capital (antes filtraba solo Estado==Pendiente, perdiendo Vencida/Parcial con saldo).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCreditosEnMora_CuotaVencidaEstado_ConSaldo_Entra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Vencida, diasVencido: 10);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].CuotasVencidas);
    }

    [Fact]
    public async Task GetCreditosEnMora_CuotaParcialVencida_ConSaldoDeCapital_Entra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 400m);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Single(resultado);
        Assert.Equal(600m, resultado[0].MontoCuotasVencidas);
    }

    [Fact]
    public async Task GetCreditosEnMora_CuotaParcialNoVencida_NoEntra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: -3,
            montoTotal: 1_000m, montoPagado: 400m);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCreditosEnMora_CuotaPagada_NoEntra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Pagada, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 1_000m);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCreditosEnMora_CuotaCancelada_NoEntra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Cancelada, diasVencido: 10);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCreditosEnMora_CapitalTotalmentePagado_SoloConservaPunitorio_NoEntra()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 1_000m);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCreditosEnMora_DiaDelVencimiento_NoEntra_DiaSiguienteSiEntra()
    {
        var clienteHoy = await SeedClienteAsync();
        var creditoHoy = await SeedCreditoAsync(clienteHoy.Id);
        await SeedCuotaVencidaAsync(creditoHoy.Id, diasVencido: 0); // vence hoy, todavía no vencida

        var clienteAyer = await SeedClienteAsync();
        var creditoAyer = await SeedCreditoAsync(clienteAyer.Id);
        await SeedCuotaVencidaAsync(creditoAyer.Id, diasVencido: 1); // venció ayer

        Assert.Empty(await _service.GetCreditosEnMoraAsync(clienteHoy.Id));
        Assert.Single(await _service.GetCreditosEnMoraAsync(clienteAyer.Id));
    }

    [Fact]
    public async Task GetCreditosEnMora_FronteraUsaRelojComercial_NoUtcNiToday()
    {
        // Mismo escenario que ProcesarMora_FronteraUsaRelojComercial_NoUtcNiToday: 01:00 UTC del
        // 16/06 es 22:00 ART del 15/06 (UTC-3). La cuota vence exactamente "hoy" en Argentina y no
        // debe verse como vencida si el servicio usa IRelojComercial en vez de UTC/Today.
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero));
        var servicio = CrearServicioConReloj(reloj);

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaVencidaAsync(credito.Id, diasVencido: 0);
        cuota.FechaVencimiento = reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        await _context.SaveChangesAsync();

        var resultado = await servicio.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetCreditosEnMora_CuotaFuturaEnElMismoCredito_NoAfectaElResultado()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10, montoTotal: 1_000m);
        await SeedCuotaPorVencerAsync(credito.Id, diasHastaVencimiento: 15, montoTotal: 1_000m);

        var resultado = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Single(resultado);
        Assert.Equal(1, resultado[0].CuotasVencidas);
        Assert.Equal(1_000m, resultado[0].MontoCuotasVencidas);
    }

    [Fact]
    public async Task GetCreditosEnMora_LecturaRepetida_NoModificaEstadosNiPersiste()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var cuota = await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.GetCreditosEnMoraAsync(cliente.Id);
        await _service.GetCreditosEnMoraAsync(cliente.Id);

        _context.ChangeTracker.Clear();
        var cuotaBd = await _context.Cuotas.FindAsync(cuota.Id);
        Assert.Equal(EstadoCuota.Pendiente, cuotaBd!.Estado);
        Assert.Equal(0m, cuotaBd.MontoPagado);
    }

    // =========================================================================
    // GetHistorialContactosAsync
    // =========================================================================

    [Fact]
    public async Task GetHistorialContactos_ClienteSinContactos_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetHistorialContactosAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetHistorialContactos_ConContactos_RetornaSolosLosDelCliente()
    {
        var c1 = await SeedClienteAsync();
        var c2 = await SeedClienteAsync();
        var cred = await SeedCreditoAsync(c1.Id);
        var alerta = await SeedAlertaAsync(c1.Id, cred.Id);

        var contactoVm = new RegistrarContactoViewModel
        {
            ClienteId = c1.Id,
            AlertaId = alerta.Id,
            TipoContacto = TipoContacto.LlamadaTelefonica,
            Resultado = ResultadoContacto.ContactoExitoso,
            Observaciones = "Llamada realizada"
        };
        await _service.RegistrarContactoAsync(contactoVm, gestorId: "gestor1");

        var resultado = await _service.GetHistorialContactosAsync(c1.Id);
        var resultadoC2 = await _service.GetHistorialContactosAsync(c2.Id);

        Assert.Single(resultado);
        Assert.Empty(resultadoC2);
    }

    // =========================================================================
    // GetPromesasActivasAsync
    // =========================================================================

    [Fact]
    public async Task GetPromesasActivas_ClienteSinPromesas_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetPromesasActivasAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetPromesasActivas_ConPromesa_RetornaPromesas()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var promesaVm = new RegistrarPromesaViewModel
        {
            AlertaId = alerta.Id,
            ClienteId = cliente.Id,
            FechaPromesa = DateTime.Today.AddDays(7),
            MontoPromesa = 1_000m
        };
        await _service.RegistrarPromesaPagoAsync(promesaVm, gestorId: "gestor1");

        var resultado = await _service.GetPromesasActivasAsync(cliente.Id);

        Assert.Single(resultado);
        Assert.Equal(1_000m, resultado[0].MontoPrometido);
    }

    [Fact]
    public async Task GetPromesasActivas_PromesaConFechaYaPasada_NoSeIncluye()
    {
        // PUN-ML7 (auditoría, lote 2): "activa" exige vigencia real (fecha comercial), no solo
        // "no resuelta". Antes el filtro de vigencia estaba declarado pero desconectado del query.
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var promesaVm = new RegistrarPromesaViewModel
        {
            AlertaId = alerta.Id,
            ClienteId = cliente.Id,
            FechaPromesa = DateTime.Today.AddDays(-5), // ya pasó, nadie la marcó cumplida/incumplida
            MontoPromesa = 1_000m
        };
        await _service.RegistrarPromesaPagoAsync(promesaVm, gestorId: "gestor1");

        var resultado = await _service.GetPromesasActivasAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetPromesasActivas_FronteraUsaRelojComercial_NoUtcNiToday()
    {
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero));
        var servicio = CrearServicioConReloj(reloj);

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);

        var promesaVm = new RegistrarPromesaViewModel
        {
            AlertaId = alerta.Id,
            ClienteId = cliente.Id,
            FechaPromesa = reloj.HoyComercial.ToDateTime(TimeOnly.MinValue), // "hoy" en Argentina
            MontoPromesa = 1_000m
        };
        await servicio.RegistrarPromesaPagoAsync(promesaVm, gestorId: "gestor1");

        // Con la fecha comercial correcta la promesa de "hoy" sigue vigente (>=), no vencida.
        var resultado = await servicio.GetPromesasActivasAsync(cliente.Id);

        Assert.Single(resultado);
    }

    // =========================================================================
    // GetAcuerdosPagoAsync
    // =========================================================================

    [Fact]
    public async Task GetAcuerdosPago_ClienteSinAcuerdos_RetornaVacio()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetAcuerdosPagoAsync(cliente.Id);

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // GetAcuerdosPagoAsync — cierre PUN-ML7: la próxima cuota vigente ("ProximaFechaVencimiento")
    // usaba DateTime.Today. Corregido a IRelojComercial.InicioDiaComercial — no cambia la regla de
    // negocio (misma comparación FechaVencimiento >= hoy), solo la fuente de "hoy".
    // -------------------------------------------------------------------------

    private async Task<(int AcuerdoId, CuotaAcuerdo Cuota)> SeedAcuerdoConUnaCuotaAsync(
        int clienteId, int creditoId, int alertaId, DateTime fechaVencimientoCuota)
    {
        var acuerdoId = await _service.CrearAcuerdoPagoAsync(new CrearAcuerdoViewModel
        {
            AlertaId = alertaId,
            ClienteId = clienteId,
            CreditoId = creditoId,
            MontoDeudaOriginal = 3_000m,
            MontoMoraOriginal = 0m,
            MontoCondonar = 0m,
            MontoEntregaInicial = 0m,
            CantidadCuotas = 1,
            FechaPrimeraCuota = fechaVencimientoCuota
        }, "gestor1");

        var cuotaAcuerdo = new CuotaAcuerdo
        {
            AcuerdoPagoId = acuerdoId,
            NumeroCuota = 1,
            MontoCapital = 3_000m,
            MontoMora = 0m,
            MontoTotal = 3_000m,
            FechaVencimiento = fechaVencimientoCuota,
            Estado = EstadoCuotaAcuerdo.Pendiente
        };
        _context.Set<CuotaAcuerdo>().Add(cuotaAcuerdo);
        await _context.SaveChangesAsync();

        return (acuerdoId, cuotaAcuerdo);
    }

    [Fact]
    public async Task GetAcuerdosPago_CuotaVenceHoy_SeConsideraProximaFecha_FronteraUsaRelojComercial()
    {
        // 01:00 UTC del 16/06 = 22:00 ART del 15/06 (UTC-3): "hoy" en Argentina es 15/06, un día antes
        // que DateTime.UtcNow.Date/DateTime.Today (16/06). La cuota vence exactamente "hoy" en
        // Argentina; con la fecha comercial correcta sigue siendo la próxima cuota vigente. Si el
        // servicio usara UTC/Today, la trataría como vencida desde ayer y la excluiría (quedaría null).
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero));
        var servicio = CrearServicioConReloj(reloj);

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);
        var fechaVencimiento = reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
        await SeedAcuerdoConUnaCuotaAsync(cliente.Id, credito.Id, alerta.Id, fechaVencimiento);

        var resumen = await servicio.GetAcuerdosPagoAsync(cliente.Id);

        Assert.Single(resumen);
        Assert.Equal(fechaVencimiento, resumen[0].ProximaFechaVencimiento);
    }

    [Fact]
    public async Task GetAcuerdosPago_CuotaVencioAyer_NoSeConsideraProximaFecha()
    {
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero)); // "hoy" ART = 15/06
        var servicio = CrearServicioConReloj(reloj);

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);
        var fechaVencimiento = reloj.HoyComercial.AddDays(-1).ToDateTime(TimeOnly.MinValue); // venció ayer
        await SeedAcuerdoConUnaCuotaAsync(cliente.Id, credito.Id, alerta.Id, fechaVencimiento);

        var resumen = await servicio.GetAcuerdosPagoAsync(cliente.Id);

        Assert.Single(resumen);
        Assert.Null(resumen[0].ProximaFechaVencimiento); // ninguna cuota pendiente vigente
    }

    // =========================================================================
    // GetAcuerdoPagoDetalleAsync
    // =========================================================================

    [Fact]
    public async Task GetAcuerdoPagoDetalle_AcuerdoInexistente_RetornaNull()
    {
        var resultado = await _service.GetAcuerdoPagoDetalleAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // GetClientesEnMoraAsync
    // =========================================================================

    [Fact]
    public async Task GetClientesEnMora_SinAlertas_RetornaVacio()
    {
        var filtros = new FiltrosBandejaClientes();
        var resultado = await _service.GetClientesEnMoraAsync(filtros);

        Assert.NotNull(resultado);
        Assert.Empty(resultado.Clientes);
    }

    [Fact]
    public async Task GetClientesEnMora_ConAlertasActivas_RetornaCliente()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedAlertaAsync(cliente.Id, credito.Id);

        var filtros = new FiltrosBandejaClientes();
        var resultado = await _service.GetClientesEnMoraAsync(filtros);

        Assert.NotNull(resultado);
        Assert.Single(resultado.Clientes);
        Assert.Equal(cliente.Id, resultado.Clientes[0].ClienteId);
    }

    [Fact]
    public async Task GetClientesEnMora_MismoUniversoQueGetCreditosEnMora()
    {
        // PUN-ML7 (auditoría, lote 2): "mismo cliente + mismos datos + misma fecha comercial debe
        // producir la misma clasificación". GetClientesEnMoraAsync deriva de AlertaCobranza (generada
        // por ProcesarMoraAsync con el predicado canónico); con DiasGracia=0 y sin resolución manual
        // de alertas, su universo coincide con el de GetCreditosEnMoraAsync (lectura directa sobre
        // Cuotas). Un DiasGracia>0 retrasa deliberadamente la alerta de cobranza — no es la misma
        // pregunta que "¿está vencida hoy?" — riesgo documentado en el cierre.
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 400m);

        await _service.ProcesarMoraAsync();

        var bandeja = await _service.GetClientesEnMoraAsync(new FiltrosBandejaClientes());
        var creditosEnMora = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.Single(bandeja.Clientes);
        Assert.Equal(cliente.Id, bandeja.Clientes[0].ClienteId);
        Assert.Single(creditosEnMora);
        Assert.Equal(bandeja.Clientes[0].MontoVencido, creditosEnMora[0].MontoCuotasVencidas);
        // PUN-ML7 (cierre): AlertaCobranza.DiasAtraso ya se asigna en ProcesarMoraAsync (antes
        // quedaba siempre en 0) — ambas lecturas ahora coinciden también en días de atraso.
        Assert.Equal(bandeja.Clientes[0].DiasMaxAtraso, creditosEnMora[0].DiasAtraso);
    }

    // =========================================================================
    // UpdateConfiguracionExpandidaAsync
    // =========================================================================

    [Fact]
    public async Task UpdateConfiguracionExpandida_HappyPath_ActualizaCampos()
    {
        var config = await _service.GetConfiguracionAsync();

        var vm = new ConfiguracionMoraExpandidaViewModel
        {
            Id = config.Id,
            TasaMoraBase = 2.5m,
            DiasGracia = 5,
            DiasMaximosSinGestion = 10,
            MaximoCuotasAcuerdo = 6,
            PorcentajeMinimoEntrega = 20m
        };

        var resultado = await _service.UpdateConfiguracionExpandidaAsync(vm);

        Assert.Equal(2.5m, resultado.TasaMoraBase);
        Assert.Equal(5, resultado.DiasGracia);
        Assert.Equal(10, resultado.DiasMaximosSinGestion);
    }

    [Fact]
    public async Task UpdateConfiguracionExpandida_IdInexistente_LanzaExcepcion()
    {
        var vm = new ConfiguracionMoraExpandidaViewModel { Id = 99999 };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.UpdateConfiguracionExpandidaAsync(vm));
    }

    // FASE 12B: el flag de score por mora se persiste y es independiente de
    // CambiarEstadoCuotaAuto.
    [Fact]
    public async Task UpdateConfiguracionExpandida_ConScorePorMora_PersisteCamposScore()
    {
        var config = await _service.GetConfiguracionAsync();

        var vm = new ConfiguracionMoraExpandidaViewModel
        {
            Id = config.Id,
            ImpactarScorePorMora = true,
            PuntosRestarPorCuotaVencida = 5,
            PuntosRestarPorDiaMora = 0.5m,
            PuntosMaximosARestar = 50,
            RecuperarScoreAlPagar = true,
            PorcentajeRecuperacionScore = 25m,
            CambiarEstadoCuotaAuto = false
        };

        var resultado = await _service.UpdateConfiguracionExpandidaAsync(vm);

        Assert.True(resultado.ImpactarScorePorMora);
        Assert.Equal(5, resultado.PuntosRestarPorCuotaVencida);
        Assert.Equal(0.5m, resultado.PuntosRestarPorDiaMora);
        Assert.Equal(50, resultado.PuntosMaximosARestar);
        Assert.True(resultado.RecuperarScoreAlPagar);
        Assert.Equal(25m, resultado.PorcentajeRecuperacionScore);
        // El flag de score no se mezcla con el cambio de estado de cuota.
        Assert.False(resultado.CambiarEstadoCuotaAuto);
    }

    // =========================================================================
    // GetFichaClienteAsync
    // =========================================================================

    [Fact]
    public async Task GetFichaCliente_ClienteInexistente_RetornaNull()
    {
        var resultado = await _service.GetFichaClienteAsync(99999);
        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetFichaCliente_ClienteExistenteSinAlertas_RetornaFichaVacia()
    {
        var cliente = await SeedClienteAsync();

        var resultado = await _service.GetFichaClienteAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(cliente.Id, resultado!.ClienteId);
        Assert.Equal(0, resultado.Resumen.TotalCreditosConMora);
    }

    [Fact]
    public async Task GetFichaCliente_ClienteConAlerta_RetornaPrioridadCorrecta()
    {
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedAlertaAsync(cliente.Id, credito.Id, prioridad: PrioridadAlerta.Alta);

        var resultado = await _service.GetFichaClienteAsync(cliente.Id);

        Assert.NotNull(resultado);
        Assert.Equal(PrioridadAlerta.Alta, resultado!.Resumen.PrioridadMaxima);
    }

    [Fact]
    public async Task GetFichaCliente_UsaMismoTotalYMismasCuotasQueGetCreditosEnMora()
    {
        // PUN-ML7 (auditoría, lote 2): "no mezclar una lista basada en Vencida/Parcial con totales
        // basados solo en Pendiente". El resumen de la ficha se arma sumando el resultado de
        // GetCreditosEnMoraAsync (ya corregido) — cuota Vencida + cuota Parcial con saldo, ambas
        // deben aparecer y con el mismo dato en ambos lados.
        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Vencida, diasVencido: 20,
            montoTotal: 1_000m, montoPagado: 0m);
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 300m);

        var ficha = await _service.GetFichaClienteAsync(cliente.Id);
        var creditos = await _service.GetCreditosEnMoraAsync(cliente.Id);

        Assert.NotNull(ficha);
        Assert.Equal(creditos.Sum(c => c.CuotasVencidas), ficha!.Resumen.TotalCuotasVencidas);
        Assert.Equal(2, ficha.Resumen.TotalCuotasVencidas); // Vencida + Parcial con saldo, no solo Pendiente
        Assert.Equal(creditos.Sum(c => c.MontoCuotasVencidas), ficha.Resumen.MontoCapitalVencido);
        Assert.Equal(creditos.Max(c => c.DiasAtraso), ficha.Resumen.DiasMaxAtraso);
    }

    // =========================================================================
    // GetDashboardKPIsAsync — cierre PUN-ML7
    //
    // Antes usaba DateTime.Today para "hoy" (inconsistente con el resto de MoraService, ya corregido)
    // y la agregación de cobrosMes usaba SumAsync sobre decimal, que el proveedor Sqlite (usado en
    // estos tests) no traduce a SQL (NotSupportedException) — mismo límite documentado para otros
    // SumAsync de la base. Se corrigió a traer las filas y sumar en cliente (sin cambiar la regla de
    // negocio), lo que habilita cubrir este método con tests de integración por primera vez.
    // =========================================================================

    [Fact]
    public async Task GetDashboardKPIs_SinDatos_RetornaCeros()
    {
        var kpis = await _service.GetDashboardKPIsAsync();

        Assert.Equal(0, kpis.TotalClientesMora);
        Assert.Equal(0, kpis.AlertasActivas);
        Assert.Equal(0m, kpis.DiasPromedioAtraso);
    }

    [Fact]
    public async Task GetDashboardKPIs_TrasProcesarMora_CoincideConBandejaYDiasAtrasoReal()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 20);

        await _service.ProcesarMoraAsync();

        var kpis = await _service.GetDashboardKPIsAsync();
        var bandeja = await _service.GetClientesEnMoraAsync(new FiltrosBandejaClientes());

        Assert.Equal(bandeja.TotalClientes, kpis.TotalClientesMora);
        Assert.Equal(1, kpis.AlertasActivas);
        // Gracias al fix de AlertaCobranza.DiasAtraso (antes siempre 0), el promedio ya es real.
        Assert.Equal(20m, kpis.DiasPromedioAtraso);
    }

    [Fact]
    public async Task GetDashboardKPIs_IncluyeCuotaVencidaYParcialConSaldo()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var clienteVencida = await SeedClienteAsync();
        var creditoVencida = await SeedCreditoAsync(clienteVencida.Id);
        await SeedCuotaConEstadoAsync(creditoVencida.Id, EstadoCuota.Vencida, diasVencido: 10);

        var clienteParcial = await SeedClienteAsync();
        var creditoParcial = await SeedCreditoAsync(clienteParcial.Id);
        await SeedCuotaConEstadoAsync(creditoParcial.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 400m);

        await _service.ProcesarMoraAsync();

        var kpis = await _service.GetDashboardKPIsAsync();

        Assert.Equal(2, kpis.TotalClientesMora);
    }

    [Fact]
    public async Task GetDashboardKPIs_ExcluyeCapitalSaldadoConSoloPunitorioPendiente()
    {
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        // Capital saldado, Estado=Parcial únicamente por un punitorio aplicado pendiente.
        await SeedCuotaConEstadoAsync(credito.Id, EstadoCuota.Parcial, diasVencido: 10,
            montoTotal: 1_000m, montoPagado: 1_000m);

        await _service.ProcesarMoraAsync();

        var kpis = await _service.GetDashboardKPIsAsync();

        Assert.Equal(0, kpis.TotalClientesMora);
    }

    [Fact]
    public async Task GetDashboardKPIs_PromesaVenceHoy_ClasificaConFechaComercial_NoUtcNiToday()
    {
        // 01:00 UTC del 16/06 = 22:00 ART del 15/06 (UTC-3): "hoy" en Argentina es 15/06. Si el
        // servicio usara DateTime.Today/UtcNow.Date (16/06) en vez de IRelojComercial, la promesa
        // de "hoy" en Argentina aparecería vencida un día antes de tiempo.
        var reloj = RelojComercialFijo.EnUtc(new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero));
        var servicio = CrearServicioConReloj(reloj);

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        var alerta = await SeedAlertaAsync(cliente.Id, credito.Id);
        alerta.FechaPromesaPago = reloj.HoyComercial.ToDateTime(TimeOnly.MinValue); // "hoy" en Argentina
        await _context.SaveChangesAsync();

        var kpis = await servicio.GetDashboardKPIsAsync();

        Assert.Equal(1, kpis.PromesasVencenHoy);
        Assert.Equal(1, kpis.PromesasActivas);
        Assert.Equal(0, kpis.PromesasVencidas);
    }

    [Fact]
    public async Task GetDashboardKPIs_AgregarCuotaFutura_NoAlteraKPIsDeHoy()
    {
        // "Agregar eventos futuros no cambia una consulta histórica": una cuota del mismo crédito que
        // recién vence dentro de 30 días no debe sumarse a la mora actual ni mover el promedio de
        // días de atraso ya calculado.
        var config = new ConfiguracionMora
        {
            DiasGracia = 0,
            ProcesoAutomaticoActivo = true,
            HoraEjecucionDiaria = new TimeSpan(8, 0, 0)
        };
        _context.Set<ConfiguracionMora>().Add(config);
        await _context.SaveChangesAsync();

        var cliente = await SeedClienteAsync();
        var credito = await SeedCreditoAsync(cliente.Id);
        await SeedCuotaVencidaAsync(credito.Id, diasVencido: 10);

        await _service.ProcesarMoraAsync();
        var kpisAntes = await _service.GetDashboardKPIsAsync();

        await SeedCuotaPorVencerAsync(credito.Id, diasHastaVencimiento: 30);
        await _service.ProcesarMoraAsync(); // reprocesa; la cuota futura no debería afectar la mora actual

        var kpisDespues = await _service.GetDashboardKPIsAsync();

        Assert.Equal(kpisAntes.TotalClientesMora, kpisDespues.TotalClientesMora);
        Assert.Equal(kpisAntes.DiasPromedioAtraso, kpisDespues.DiasPromedioAtraso);
    }

    /// <summary>
    /// Fake de ICreditoService que solo registra si ActualizarEstadoCuotasAsync fue invocado,
    /// para validar el wiring de MoraService sin depender del grafo completo de CreditoService.
    /// </summary>
    private sealed class RecordingCreditoService : ICreditoService
    {
        public int ActualizarEstadoCuotasAsyncCallCount { get; private set; }

        public Task ActualizarEstadoCuotasAsync()
        {
            ActualizarEstadoCuotasAsyncCallCount++;
            return Task.CompletedTask;
        }

        public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<CreditoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor) => throw new NotImplementedException();
        public Task<bool> RechazarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<bool> CancelarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId) => throw new NotImplementedException();
        public Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoCuotaContextoResultado?> ObtenerContextoPagoCuotaAsync(int cuotaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaPreviewResultado?> PrevisualizarPagoCuotaAsync(PagoCuotaIndividualComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaResultado?> RegistrarPagoCuotaIndividualAsync(PagoCuotaIndividualComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoMultipleCuotasResult> PagarCuotasAsync(PagoMultipleCuotasRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TheBuryProject.Services.Models.CobroPrimeraCuotaResultado> CobrarPrimeraCuotaAlGenerarAsync(int creditoId, string medioPago, string? comprobante = null, string? observaciones = null) => throw new NotImplementedException();
        public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoCuotaContextoResultado?> ObtenerContextoAdelantoAsync(int creditoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaPreviewResultado?> PrevisualizarAdelantoAsync(AdelantoCuotaComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaResultado?> RegistrarAdelantoAsync(AdelantoCuotaComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoMultiplePreviewResultado> PrevisualizarPagoMultipleAsync(int clienteId, List<int> cuotaIds, string medioPago, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
        public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando) => throw new NotImplementedException();
    }
}
