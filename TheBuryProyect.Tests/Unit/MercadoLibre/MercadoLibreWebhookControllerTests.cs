using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Modules.MercadoLibre.Controllers;

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
}
