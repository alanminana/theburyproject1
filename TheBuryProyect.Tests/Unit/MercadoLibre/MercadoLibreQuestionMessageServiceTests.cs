using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Modules.MercadoLibre.Entities;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase 16 (preguntas y mensajes). Validan el gating de simulación:
/// en ModoSimulacion=true nunca se llama a la API real, las respuestas reales
/// exigen confirmación explícita, y los registros son idempotentes.
/// </summary>
public class MercadoLibreQuestionMessageServiceTests : IDisposable
{
    private sealed class FakeAuthService : IMercadoLibreAuthService
    {
        public bool EstaConfigurado => true;
        public string BuildAuthorizationUrl() => throw new NotSupportedException();
        public bool ValidarState(string? state) => throw new NotSupportedException();
        public Task<MercadoLibreAccount> HandleOAuthCallbackAsync(string code, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> GetValidAccessTokenAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult("token-secreto-no-debe-loguearse");
    }

    private readonly TestDbContextFactory _factory;
    private readonly AppDbContext _context;
    private readonly FakeMercadoLibreApiClient _api;
    private readonly MercadoLibreConfiguracionService _configService;
    private readonly MercadoLibreQuestionService _questionService;
    private readonly MercadoLibreMessageService _messageService;

    public MercadoLibreQuestionMessageServiceTests()
    {
        (_factory, _) = MercadoLibreTestDb.Create();
        _context = _factory.CreateDbContext();
        _api = new FakeMercadoLibreApiClient();

        _configService = new MercadoLibreConfiguracionService(
            _factory, NullLogger<MercadoLibreConfiguracionService>.Instance);

        _questionService = new MercadoLibreQuestionService(
            _context, _configService, new FakeAuthService(), _api,
            NullLogger<MercadoLibreQuestionService>.Instance);

        _messageService = new MercadoLibreMessageService(
            _context, _configService, new FakeAuthService(), _api,
            NullLogger<MercadoLibreMessageService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<MercadoLibreAccount> SembrarCuentaAsync(long meliUserId = 777)
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

    private async Task<MercadoLibreListing> SembrarListingAsync(int accountId, string itemId = "MLA111", int? productoId = null)
    {
        var listing = new MercadoLibreListing
        {
            AccountId = accountId,
            ItemId = itemId,
            Titulo = "Listing test",
            Precio = 1000m,
            AvailableQuantity = 5,
            Status = "active",
            ProductoId = productoId
        };
        _context.MercadoLibreListings.Add(listing);
        await _context.SaveChangesAsync();
        return listing;
    }

    private async Task<MercadoLibreOrder> SembrarOrdenAsync(int accountId, long meliOrderId = 5001, long? buyerId = 888)
    {
        var orden = new MercadoLibreOrder
        {
            AccountId = accountId,
            MeliOrderId = meliOrderId,
            Status = "paid",
            TotalAmount = 1000m,
            FechaCreacionUtc = DateTime.UtcNow,
            BuyerId = buyerId
        };
        _context.MercadoLibreOrders.Add(orden);
        await _context.SaveChangesAsync();
        return orden;
    }

    private async Task DesactivarSimulacionAsync()
    {
        var config = await _configService.GetAsync();
        await using var ctx = _factory.CreateDbContext();
        var c = await ctx.MercadoLibreConfiguraciones.FirstAsync(x => x.Id == config.Id);
        c.ModoSimulacion = false;
        await ctx.SaveChangesAsync();
    }

    // ==================================================================
    // Preguntas
    // ==================================================================

    [Fact]
    public async Task SimularPregunta_CreaPreguntaPendienteSimulada()
    {
        var cuenta = await SembrarCuentaAsync();
        var listing = await SembrarListingAsync(cuenta.Id);

        var r = await _questionService.SimularPreguntaAsync(listing.Id, "¿Tiene stock?", "tester");

        Assert.True(r.Ok);
        var pregunta = await _context.MercadoLibreQuestions.SingleAsync();
        Assert.Equal(MercadoLibreQuestionEstado.Pendiente, pregunta.Estado);
        Assert.True(pregunta.EsSimulada);
        Assert.Equal(listing.Id, pregunta.ListingId);
        Assert.Equal("MLA111", pregunta.ItemId);
    }

    [Fact]
    public async Task ResponderPreguntaSimulada_NoLlamaMl_GuardaLocal()
    {
        var cuenta = await SembrarCuentaAsync();
        var listing = await SembrarListingAsync(cuenta.Id);
        var sim = await _questionService.SimularPreguntaAsync(listing.Id, "¿Color?", "tester");

        var r = await _questionService.ResponderPreguntaAsync(sim.Id!.Value, "Negro", confirmarReal: true, "tester");

        Assert.True(r.Ok);
        Assert.Empty(_api.AnswerQuestionCalls); // nunca llamó a Mercado Libre
        var pregunta = await _context.MercadoLibreQuestions.SingleAsync();
        Assert.Equal(MercadoLibreQuestionEstado.Respondida, pregunta.Estado);
        Assert.True(pregunta.EsSimulada);
        Assert.Equal("Negro", pregunta.RespuestaTexto);
    }

    [Fact]
    public async Task ResponderPreguntaReal_BloqueadaConModoSimulacion_NoLlamaMl()
    {
        var cuenta = await SembrarCuentaAsync();
        var listing = await SembrarListingAsync(cuenta.Id);
        // Pregunta "real" (no simulada) que llegó por webhook, con ModoSimulacion=true.
        _context.MercadoLibreQuestions.Add(new MercadoLibreQuestion
        {
            AccountId = cuenta.Id,
            QuestionId = 6001,
            ListingId = listing.Id,
            ItemId = listing.ItemId,
            TextoPregunta = "Pregunta real",
            Estado = MercadoLibreQuestionEstado.Pendiente,
            EsSimulada = false
        });
        await _context.SaveChangesAsync();
        var id = await _context.MercadoLibreQuestions.Select(q => q.Id).SingleAsync();

        var r = await _questionService.ResponderPreguntaAsync(id, "respuesta", confirmarReal: true, "tester");

        Assert.True(r.Ok);
        Assert.Empty(_api.AnswerQuestionCalls); // simulación: NO se envió a ML
        var pregunta = await _context.MercadoLibreQuestions.SingleAsync();
        Assert.True(pregunta.EsSimulada); // quedó marcada como respuesta local
    }

    [Fact]
    public async Task ResponderPreguntaReal_SinConfirmacion_SeBloquea()
    {
        var cuenta = await SembrarCuentaAsync();
        var listing = await SembrarListingAsync(cuenta.Id);
        await DesactivarSimulacionAsync();

        _context.MercadoLibreQuestions.Add(new MercadoLibreQuestion
        {
            AccountId = cuenta.Id,
            QuestionId = 6002,
            ListingId = listing.Id,
            ItemId = listing.ItemId,
            TextoPregunta = "Pregunta real",
            Estado = MercadoLibreQuestionEstado.Pendiente,
            EsSimulada = false
        });
        await _context.SaveChangesAsync();
        var id = await _context.MercadoLibreQuestions.Select(q => q.Id).SingleAsync();

        var r = await _questionService.ResponderPreguntaAsync(id, "respuesta", confirmarReal: false, "tester");

        Assert.False(r.Ok);
        Assert.Contains("confirmación", r.Mensaje);
        Assert.Empty(_api.AnswerQuestionCalls);
    }

    [Fact]
    public async Task ResponderPreguntaReal_ConfirmadaYModoReal_LlamaMl()
    {
        var cuenta = await SembrarCuentaAsync();
        var listing = await SembrarListingAsync(cuenta.Id);
        await DesactivarSimulacionAsync();

        _context.MercadoLibreQuestions.Add(new MercadoLibreQuestion
        {
            AccountId = cuenta.Id,
            QuestionId = 6003,
            ListingId = listing.Id,
            ItemId = listing.ItemId,
            TextoPregunta = "Pregunta real",
            Estado = MercadoLibreQuestionEstado.Pendiente,
            EsSimulada = false
        });
        await _context.SaveChangesAsync();
        var id = await _context.MercadoLibreQuestions.Select(q => q.Id).SingleAsync();

        var r = await _questionService.ResponderPreguntaAsync(id, "Sí, hay stock", confirmarReal: true, "tester");

        Assert.True(r.Ok);
        Assert.Single(_api.AnswerQuestionCalls);
        Assert.Equal((6003L, "Sí, hay stock"), _api.AnswerQuestionCalls[0]);
        var pregunta = await _context.MercadoLibreQuestions.SingleAsync();
        Assert.Equal(MercadoLibreQuestionEstado.Respondida, pregunta.Estado);
        Assert.False(pregunta.EsSimulada);
    }

    [Fact]
    public async Task RegistrarDesdeWebhook_MismoQuestionId_NoDuplica()
    {
        var cuenta = await SembrarCuentaAsync();
        var config = await _configService.GetAsync();

        await _questionService.RegistrarDesdeWebhookAsync(7001, cuenta.Id, "/questions/7001", config);
        await _questionService.RegistrarDesdeWebhookAsync(7001, cuenta.Id, "/questions/7001", config);

        Assert.Equal(1, await _context.MercadoLibreQuestions.CountAsync());
    }

    [Fact]
    public async Task RegistrarDesdeWebhook_SinPublicacion_QuedaPendienteSinLlamarMl()
    {
        var cuenta = await SembrarCuentaAsync();
        var config = await _configService.GetAsync(); // ModoSimulacion=true

        await _questionService.RegistrarDesdeWebhookAsync(7002, cuenta.Id, "/questions/7002", config);

        var pregunta = await _context.MercadoLibreQuestions.SingleAsync();
        Assert.Equal(MercadoLibreQuestionEstado.Pendiente, pregunta.Estado);
        Assert.Null(pregunta.ListingId);
        Assert.False(pregunta.EsSimulada);
    }

    [Fact]
    public async Task PreguntaSimularYResponder_NoEscribeTokenEnLogs()
    {
        var cuenta = await SembrarCuentaAsync();
        var listing = await SembrarListingAsync(cuenta.Id);
        var sim = await _questionService.SimularPreguntaAsync(listing.Id, "¿stock?", "tester");
        await _questionService.ResponderPreguntaAsync(sim.Id!.Value, "sí", confirmarReal: false, "tester");

        var logs = await _context.MercadoLibreSyncLogs.AsNoTracking().ToListAsync();
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, l => (l.Detalle ?? "").Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    // ==================================================================
    // Mensajes
    // ==================================================================

    [Fact]
    public async Task SimularMensaje_CreaMensajeEntranteSimulado()
    {
        var cuenta = await SembrarCuentaAsync();
        var orden = await SembrarOrdenAsync(cuenta.Id);

        var r = await _messageService.SimularMensajeAsync(orden.Id, "Hola, ¿cuándo llega?", "tester");

        Assert.True(r.Ok);
        var mensaje = await _context.MercadoLibreMessages.SingleAsync();
        Assert.Equal(MercadoLibreMessageDireccion.Entrante, mensaje.Direccion);
        Assert.True(mensaje.EsSimulado);
        Assert.Equal(orden.Id, mensaje.OrderId);
    }

    [Fact]
    public async Task ResponderMensajeSimulado_NoLlamaMl_GuardaLocal()
    {
        var cuenta = await SembrarCuentaAsync();
        var orden = await SembrarOrdenAsync(cuenta.Id);

        var r = await _messageService.ResponderMensajeAsync(orden.Id, "Llega mañana", confirmarReal: true, "tester");

        Assert.True(r.Ok);
        Assert.Empty(_api.SendMessageCalls);
        var mensaje = await _context.MercadoLibreMessages.SingleAsync();
        Assert.Equal(MercadoLibreMessageDireccion.Saliente, mensaje.Direccion);
        Assert.True(mensaje.EsSimulado);
    }

    [Fact]
    public async Task ResponderMensajeReal_SinConfirmacion_SeBloquea()
    {
        var cuenta = await SembrarCuentaAsync();
        var orden = await SembrarOrdenAsync(cuenta.Id);
        await DesactivarSimulacionAsync();

        var r = await _messageService.ResponderMensajeAsync(orden.Id, "hola", confirmarReal: false, "tester");

        Assert.False(r.Ok);
        Assert.Contains("confirmación", r.Mensaje);
        Assert.Empty(_api.SendMessageCalls);
    }

    [Fact]
    public async Task ResponderMensajeReal_ConfirmadaYModoReal_LlamaMl()
    {
        var cuenta = await SembrarCuentaAsync();
        var orden = await SembrarOrdenAsync(cuenta.Id);
        await DesactivarSimulacionAsync();

        var r = await _messageService.ResponderMensajeAsync(orden.Id, "Llega mañana", confirmarReal: true, "tester");

        Assert.True(r.Ok);
        Assert.Single(_api.SendMessageCalls);
        var call = _api.SendMessageCalls[0];
        Assert.Equal(orden.MeliOrderId, call.PackId);
        Assert.Equal(cuenta.MeliUserId, call.SellerId);
        Assert.Equal(orden.BuyerId!.Value, call.BuyerUserId);
        var mensaje = await _context.MercadoLibreMessages.SingleAsync();
        Assert.False(mensaje.EsSimulado);
        Assert.Equal(MercadoLibreMessageEstado.Enviado, mensaje.Estado);
    }

    [Fact]
    public async Task ResponderMensajeReal_OrdenConMensajeSimulado_NoLlamaMl()
    {
        var cuenta = await SembrarCuentaAsync();
        var orden = await SembrarOrdenAsync(cuenta.Id);
        await _messageService.SimularMensajeAsync(orden.Id, "consulta QA", "tester");
        await DesactivarSimulacionAsync();

        var r = await _messageService.ResponderMensajeAsync(orden.Id, "respuesta", confirmarReal: true, "tester");

        Assert.True(r.Ok);
        Assert.Empty(_api.SendMessageCalls); // conversación simulada local: nunca toca ML
    }

    [Fact]
    public async Task RegistrarMensajeDesdeWebhook_MismoMessageId_NoDuplica()
    {
        var cuenta = await SembrarCuentaAsync();
        var config = await _configService.GetAsync();

        await _messageService.RegistrarDesdeWebhookAsync("m-1", cuenta.Id, "/messages/m-1", null, config);
        await _messageService.RegistrarDesdeWebhookAsync("m-1", cuenta.Id, "/messages/m-1", null, config);

        Assert.Equal(1, await _context.MercadoLibreMessages.CountAsync());
    }

    [Fact]
    public async Task RegistrarMensajeDesdeWebhook_SinOrden_QuedaPendiente()
    {
        var cuenta = await SembrarCuentaAsync();
        var config = await _configService.GetAsync();

        await _messageService.RegistrarDesdeWebhookAsync("m-2", cuenta.Id, "/messages/m-2", 999999, config);

        var mensaje = await _context.MercadoLibreMessages.SingleAsync();
        Assert.Null(mensaje.OrderId);
        Assert.Equal(MercadoLibreMessageEstado.Recibido, mensaje.Estado);
    }
}
