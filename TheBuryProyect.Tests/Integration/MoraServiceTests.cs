using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels.Mora;

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
    private readonly MoraService _service;

    public MoraServiceTests()
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

        _service = new MoraService(_context, mapper, NullLogger<MoraService>.Instance);
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
}
