using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Tests de Fase 5 (parcial): el webhook persiste el evento crudo y responde 200,
/// incluso con bodies no parseables.
/// </summary>
public class MercadoLibreWebhookControllerTests
{
    private static (MercadoLibreWebhookController Controller, TestDbContextFactory Factory) BuildController(string body)
    {
        var (factory, _) = MercadoLibreTestDb.Create();

        var controller = new MercadoLibreWebhookController(
            factory, NullLogger<MercadoLibreWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes(body)) }
                }
            }
        };

        return (controller, factory);
    }

    [Fact]
    public async Task Recibir_NotificacionDeOrden_GuardaEventoCrudoYRespondeOk()
    {
        const string body = """
            {
              "_id": "f9f08571-1f65-4c46-9e0a-123456",
              "topic": "orders_v2",
              "resource": "/orders/2195160686",
              "user_id": 468828,
              "application_id": 5503910054141466,
              "attempts": 1,
              "sent": "2026-06-11T10:00:00.000Z",
              "received": "2026-06-11T10:00:00.000Z"
            }
            """;

        var (controller, factory) = BuildController(body);

        var resultado = await controller.Recibir(default);

        Assert.IsType<OkResult>(resultado);

        await using var ctx = factory.CreateDbContext();
        var evento = await ctx.MercadoLibreWebhookEvents.SingleAsync();

        Assert.Equal("orders_v2", evento.Topic);
        Assert.Equal("/orders/2195160686", evento.Resource);
        Assert.Equal(468828, evento.MeliUserId);
        Assert.Equal(1, evento.Attempts);
        Assert.Contains("f9f08571", evento.RawBody);
        Assert.False(evento.Procesado);
    }

    [Fact]
    public async Task Recibir_BodyNoParseable_IgualGuardaCrudoYRespondeOk()
    {
        var (controller, factory) = BuildController("esto no es json {{{");

        var resultado = await controller.Recibir(default);

        Assert.IsType<OkResult>(resultado);

        await using var ctx = factory.CreateDbContext();
        var evento = await ctx.MercadoLibreWebhookEvents.SingleAsync();

        Assert.Equal("esto no es json {{{", evento.RawBody);
        Assert.Equal(string.Empty, evento.Topic);
    }

    [Fact]
    public async Task Recibir_BodyVacio_RespondeOkSinPersistir()
    {
        var (controller, factory) = BuildController(string.Empty);

        var resultado = await controller.Recibir(default);

        Assert.IsType<OkResult>(resultado);

        await using var ctx = factory.CreateDbContext();
        Assert.False(await ctx.MercadoLibreWebhookEvents.AnyAsync());
    }

    // COM-S-004 (backlog de la auditoria QA): JSON valido pero de otra forma terminaba en 500, que ML reintenta.
    [Theory]
    [InlineData("""{"topic": 123, "resource": "/orders/1"}""")]
    [InlineData("""{"topic": "orders_v2", "resource": {"id": 1}}""")]
    [InlineData("""{"topic": "orders_v2", "user_id": 1.5}""")]
    [InlineData("""{"topic": "orders_v2", "user_id": 99999999999999999999999999}""")]
    [InlineData("""{"topic": "orders_v2", "attempts": 3000000000}""")]
    [InlineData("""["no", "es", "un", "objeto"]""")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task Recibir_JsonValidoDeOtraForma_NoDa500_YGuardaElEventoCrudo(string body)
    {
        var (controller, factory) = BuildController(body);

        var resultado = await controller.Recibir(default);

        Assert.IsType<OkResult>(resultado);
        await using var ctx = factory.CreateDbContext();
        var evento = await ctx.MercadoLibreWebhookEvents.SingleAsync();
        Assert.Equal(body, evento.RawBody);
    }

    [Fact]
    public async Task Recibir_CamposConElTipoEsperado_SeSiguenLeyendoAunqueOtrosVenganMal()
    {
        var (controller, factory) = BuildController("""{"topic": "orders_v2", "resource": "/orders/9", "user_id": 77, "attempts": "uno"}""");

        await controller.Recibir(default);

        await using var ctx = factory.CreateDbContext();
        var evento = await ctx.MercadoLibreWebhookEvents.SingleAsync();
        Assert.Equal("orders_v2", evento.Topic);
        Assert.Equal("/orders/9", evento.Resource);
        Assert.Equal(77, evento.MeliUserId);
    }

    [Fact]
    public async Task Recibir_CuerpoQueSuperaElTope_Responde413_YNoPersiste()
    {
        var (controller, factory) = BuildController(new string('x', MercadoLibreWebhookController.MaxBodyChars + 1));

        var resultado = await controller.Recibir(default);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsType<StatusCodeResult>(resultado).StatusCode);
        await using var ctx = factory.CreateDbContext();
        Assert.False(await ctx.MercadoLibreWebhookEvents.AnyAsync());
    }

    [Fact]
    public async Task Recibir_CuerpoJustoEnElTope_SeGuarda()
    {
        var body = new string('x', MercadoLibreWebhookController.MaxBodyChars);
        var (controller, factory) = BuildController(body);

        var resultado = await controller.Recibir(default);

        Assert.IsType<OkResult>(resultado);
        await using var ctx = factory.CreateDbContext();
        Assert.Equal(MercadoLibreWebhookController.MaxBodyChars, (await ctx.MercadoLibreWebhookEvents.SingleAsync()).RawBody.Length);
    }
}
