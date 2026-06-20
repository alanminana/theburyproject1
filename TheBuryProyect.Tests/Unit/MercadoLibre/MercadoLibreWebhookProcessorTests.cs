using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase J: el procesador de webhooks consume eventos pendientes en
/// forma idempotente (dedup por topic+resource) con reintentos acotados.
/// </summary>
public class MercadoLibreWebhookProcessorTests : IDisposable
{
    private sealed class FakeAuthService : IMercadoLibreAuthService
    {
        public bool EstaConfigurado => true;
        public string BuildAuthorizationUrl() => throw new NotSupportedException();
        public bool ValidarState(string? state) => throw new NotSupportedException();
        public Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult("token-test");
    }

    private sealed class FakeOrderService : IMercadoLibreOrderService
    {
        public List<(int AccountId, long MeliOrderId)> Importadas { get; } = new();
        public bool LanzarError { get; set; }

        public Task<MercadoLibreOrder> ImportarOrdenAsync(int accountId, long meliOrderId, CancellationToken ct = default)
        {
            if (LanzarError)
                throw new InvalidOperationException("Fallo simulado de importación");

            Importadas.Add((accountId, meliOrderId));
            return Task.FromResult(new MercadoLibreOrder { AccountId = accountId, MeliOrderId = meliOrderId });
        }

        public Task<int> ImportarOrdenesRecientesAsync(int accountId, DateTime? desdeUtc = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MercadoLibreOrderSimulationResult> CrearOrdenSimuladaAsync(
            string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MercadoLibreOrderSimulationResult> CrearOrdenOperativaSimuladaAsync(
            string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MercadoLibreOrderProcessResult> CrearVentaInternaAsync(int orderId, string usuario, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task RegistrarLiquidacionAsync(int orderId, decimal netoReal, string usuario, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DecidirDevolucionAsync(int orderId, MercadoLibreDevolucionEstado decision, string? nota, string usuario, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MercadoLibreOrderSimulationResult> SimularClaimAsync(
            int orderId,
            MercadoLibreClaimTipo tipo,
            string? motivo,
            string usuario,
            bool permitirPorDevelopment = false,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ResolverClaimAsync(
            int claimId,
            MercadoLibreClaimAccionStock accionStock,
            MercadoLibreClaimAccionEconomica accionEconomica,
            string? resolucionManual,
            string? observaciones,
            string usuario,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task AsignarUnidadesAsync(int orderId, int orderItemId, IReadOnlyCollection<int> unidadIds, string usuario, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task MarcarIgnoradaAsync(int orderId, string usuario, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task ActualizarEnvioAsync(int orderId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<MercadoLibreOrderSimulationResult> SimularEnvioAsync(
            int orderId, string escenario, string usuario, bool permitirPorDevelopment = false, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<List<TheBuryProject.ViewModels.MercadoLibreOrderViewModel>> GetOrdenesAsync(string? filtro = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<TheBuryProject.ViewModels.MercadoLibreOrderDetalleViewModel?> GetOrdenAsync(int orderId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePrecioVigenteResolver : IPrecioVigenteResolver
    {
        public Task<PrecioVigenteResultado?> ResolverAsync(int productoId, int? listaId = null, DateTime? fecha = null)
            => Task.FromResult<PrecioVigenteResultado?>(null);

        public Task<IReadOnlyDictionary<int, PrecioVigenteResultado>> ResolverBatchAsync(
            IEnumerable<int> productoIds, int? listaId = null, DateTime? fecha = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, PrecioVigenteResultado>>(
                new Dictionary<int, PrecioVigenteResultado>());
    }

    private readonly TestDbContextFactory _factory;
    private readonly AppDbContext _context;
    private readonly FakeMercadoLibreApiClient _api;
    private readonly FakeOrderService _orderService;
    private readonly MercadoLibreWebhookProcessor _processor;
    private readonly MercadoLibreConfiguracionService _configService;

    public MercadoLibreWebhookProcessorTests()
    {
        (_factory, _) = MercadoLibreTestDb.Create();
        _context = _factory.CreateDbContext();
        _api = new FakeMercadoLibreApiClient();
        _orderService = new FakeOrderService();

        _configService = new MercadoLibreConfiguracionService(
            _factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        var pricing = new MercadoLibrePricingService(_configService, new FakePrecioVigenteResolver());

        var sync = new MercadoLibreSyncService(
            _factory, _api, new FakeAuthService(), _configService, pricing,
            NullLogger<MercadoLibreSyncService>.Instance);

        var questionService = new MercadoLibreQuestionService(
            _context, _configService, new FakeAuthService(), _api,
            NullLogger<MercadoLibreQuestionService>.Instance);

        var messageService = new MercadoLibreMessageService(
            _context, _configService, new FakeAuthService(), _api,
            NullLogger<MercadoLibreMessageService>.Instance);

        _processor = new MercadoLibreWebhookProcessor(
            _context, _orderService, sync, new FakeAuthService(), _api, _configService,
            questionService, messageService,
            NullLogger<MercadoLibreWebhookProcessor>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private async Task<MercadoLibreAccount> SembrarCuentaAsync(long meliUserId)
    {
        var cuenta = new MercadoLibreAccount
        {
            MeliUserId = meliUserId,
            Nickname = "TEST",
            AccessTokenEncrypted = "x",
            RefreshTokenEncrypted = "x",
            Activa = true
        };
        _context.MercadoLibreAccounts.Add(cuenta);
        await _context.SaveChangesAsync();
        return cuenta;
    }

    private async Task<int> SembrarEventoAsync(string topic, string resource, long? meliUserId = null)
    {
        var evento = new MercadoLibreWebhookEvent
        {
            Topic = topic,
            Resource = resource,
            MeliUserId = meliUserId,
            RawBody = "{}",
            RecibidoUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _context.MercadoLibreWebhookEvents.Add(evento);
        await _context.SaveChangesAsync();
        return evento.Id;
    }

    [Fact]
    public async Task Orders_EventoValido_ImportaOrdenYMarcaProcesado()
    {
        var cuenta = await SembrarCuentaAsync(777);
        await SembrarEventoAsync("orders_v2", "/orders/123456789", 777);

        var procesados = await _processor.ProcesarPendientesAsync();

        Assert.Equal(1, procesados);
        Assert.Single(_orderService.Importadas);
        Assert.Equal((cuenta.Id, 123456789L), _orderService.Importadas[0]);

        var evento = await _context.MercadoLibreWebhookEvents.SingleAsync();
        Assert.True(evento.Procesado);
        Assert.NotNull(evento.ProcesadoUtc);
    }

    [Fact]
    public async Task Orders_EventosDuplicados_ImportaUnaVezYMarcaTodos()
    {
        await SembrarCuentaAsync(777);
        await SembrarEventoAsync("orders_v2", "/orders/555", 777);
        await SembrarEventoAsync("orders_v2", "/orders/555", 777);
        await SembrarEventoAsync("orders_v2", "/orders/555", 777);

        var procesados = await _processor.ProcesarPendientesAsync();

        Assert.Equal(3, procesados);
        Assert.Single(_orderService.Importadas); // UNA sola importación

        Assert.Equal(3, await _context.MercadoLibreWebhookEvents.CountAsync(e => e.Procesado));
    }

    [Fact]
    public async Task Orders_ImportacionAutomaticaDesactivada_MarcaProcesadoSinImportar()
    {
        await SembrarCuentaAsync(777);

        var config = await _configService.GetAsync();
        await using (var ctx = _factory.CreateDbContext())
        {
            var c = await ctx.MercadoLibreConfiguraciones.FirstAsync(x => x.Id == config.Id);
            c.ImportacionAutomaticaOrdenes = false;
            await ctx.SaveChangesAsync();
        }

        await SembrarEventoAsync("orders_v2", "/orders/999", 777);

        await _processor.ProcesarPendientesAsync();

        Assert.Empty(_orderService.Importadas);

        var evento = await _context.MercadoLibreWebhookEvents.SingleAsync();
        Assert.True(evento.Procesado);
        Assert.Contains("desactivada", evento.ErrorProcesamiento);
    }

    [Fact]
    public async Task Items_RefrescaListingDesdeMl()
    {
        var cuenta = await SembrarCuentaAsync(777);

        _context.MercadoLibreListings.Add(new MercadoLibreListing
        {
            AccountId = cuenta.Id,
            ItemId = "MLA111",
            Titulo = "Listing webhook",
            Precio = 1000m,
            AvailableQuantity = 5,
            Status = "active"
        });
        await _context.SaveChangesAsync();

        _api.Items.Add(new MeliItemDto
        {
            Id = "MLA111",
            Title = "Listing webhook",
            Price = 1500m,
            AvailableQuantity = 3,
            SoldQuantity = 7,
            Status = "paused"
        });

        await SembrarEventoAsync("items", "/items/MLA111", 777);

        await _processor.ProcesarPendientesAsync();

        var listing = await _context.MercadoLibreListings.SingleAsync();
        Assert.Equal(1500m, listing.Precio);
        Assert.Equal(3, listing.AvailableQuantity);
        Assert.Equal(7, listing.SoldQuantity);
        Assert.Equal("paused", listing.Status);
        Assert.NotNull(listing.LastSyncUtc);
    }

    [Fact]
    public async Task Questions_EventosDuplicados_CreanUnaSolaPreguntaPendiente()
    {
        await SembrarCuentaAsync(777);
        // ModoSimulacion=true por defecto: NO se llama a la API real.
        await SembrarEventoAsync("questions", "/questions/4001", 777);
        await SembrarEventoAsync("questions", "/questions/4001", 777);

        var procesados = await _processor.ProcesarPendientesAsync();

        Assert.Equal(2, procesados);
        var pregunta = await _context.MercadoLibreQuestions.SingleAsync();
        Assert.Equal(4001L, pregunta.QuestionId);
        Assert.Equal(MercadoLibreQuestionEstado.Pendiente, pregunta.Estado);
        Assert.Null(pregunta.ListingId); // sin publicación: queda pendiente controlada
        Assert.Empty(_api.AnswerQuestionCalls); // no se respondió ni se llamó a ML
    }

    [Fact]
    public async Task Messages_EventoValido_CreaMensajeRecibidoSinLlamarMl()
    {
        await SembrarCuentaAsync(777);
        await SembrarEventoAsync("messages", "/messages/m-9001?pack_id=1", 777);

        var procesados = await _processor.ProcesarPendientesAsync();

        Assert.Equal(1, procesados);
        var mensaje = await _context.MercadoLibreMessages.SingleAsync();
        Assert.Equal("m-9001", mensaje.MessageId);
        Assert.Equal(MercadoLibreMessageDireccion.Entrante, mensaje.Direccion);
        Assert.Empty(_api.SendMessageCalls);
    }

    [Fact]
    public async Task TopicoDesconocido_MarcaProcesadoConNota()
    {
        await SembrarEventoAsync("payments", "/payments/1", null);

        var procesados = await _processor.ProcesarPendientesAsync();

        Assert.Equal(1, procesados);

        var evento = await _context.MercadoLibreWebhookEvents.SingleAsync();
        Assert.True(evento.Procesado);
        Assert.Contains("sin handler", evento.ErrorProcesamiento);
    }

    [Fact]
    public async Task Error_IncrementaIntentosYReintenta()
    {
        await SembrarCuentaAsync(777);
        _orderService.LanzarError = true;

        await SembrarEventoAsync("orders_v2", "/orders/321", 777);

        await _processor.ProcesarPendientesAsync();

        var evento = await _context.MercadoLibreWebhookEvents.SingleAsync();
        Assert.False(evento.Procesado); // sigue pendiente para reintento
        Assert.Equal(1, evento.IntentosProcesamiento);
        Assert.Contains("Fallo simulado", evento.ErrorProcesamiento);

        // Tras agotar los reintentos queda procesado con error (no bloquea la cola).
        for (var i = 1; i < MercadoLibreWebhookProcessor.MaxIntentos; i++)
            await _processor.ProcesarPendientesAsync();

        evento = await _context.MercadoLibreWebhookEvents.AsNoTracking().SingleAsync();
        Assert.True(evento.Procesado);
        Assert.Equal(MercadoLibreWebhookProcessor.MaxIntentos, evento.IntentosProcesamiento);
        Assert.Empty(_orderService.Importadas);
    }
}
