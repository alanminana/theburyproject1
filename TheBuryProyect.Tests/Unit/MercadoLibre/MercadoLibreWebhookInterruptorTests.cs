using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit.MercadoLibre;

/// <summary>
/// Preproduccion: con Mercado Libre fuera de alcance, MercadoLibre:Webhook:Enabled=false cierra el endpoint anonimo
/// (404, no persiste nada). Ausente o ilegible conserva el comportamiento existente (habilitado).
/// </summary>
public class MercadoLibreWebhookInterruptorTests
{
    private static IConfiguration Config(string? valor) => new ConfigurationBuilder()
        .AddInMemoryCollection(valor is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { [MercadoLibreWebhookSwitch.Clave] = valor })
        .Build();

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("basura", true)]   // ilegible: no cambia el comportamiento
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void Habilitado_segun_configuracion(string? valor, bool esperado)
        => Assert.Equal(esperado, MercadoLibreWebhookSwitch.Habilitado(Config(valor)));

    [Fact]
    public void Habilitado_sin_configuracion_es_true() => Assert.True(MercadoLibreWebhookSwitch.Habilitado(null));

    [Fact]
    public async Task Recibir_deshabilitado_devuelve404_y_no_persiste()
    {
        var (factory, _) = MercadoLibreTestDb.Create();
        var controller = new MercadoLibreWebhookController(
            factory, NullLogger<MercadoLibreWebhookController>.Instance, Config("false"))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"topic\":\"orders_v2\",\"resource\":\"/orders/1\"}")) }
                }
            }
        };

        var resultado = await controller.Recibir(default);

        Assert.IsType<NotFoundResult>(resultado);
        await using var ctx = factory.CreateDbContext();
        Assert.Equal(0, await ctx.MercadoLibreWebhookEvents.CountAsync());
    }
}
