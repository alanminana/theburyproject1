using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Tests.Concurrency;
using TheBuryProject.Tests.Infrastructure;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// SEG-BAJA: la auditoría de seguridad registra login fallido, bloqueo y cierre de caja, sin datos sensibles y con un
/// tope por usuario+IP+ventana para el tráfico anónimo.
/// </summary>
[Collection("HttpIntegration")]
public sealed class SegBajaAuditoriaAccesoYCajaTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SegBajaAuditoriaAccesoYCajaTests(CustomWebApplicationFactory factory) => _factory = factory;

    private async Task<List<SeguridadEventoAuditoria>> EventosAsync(string accion)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SeguridadEventosAuditoria
            .AsNoTracking().Where(x => x.Accion == accion).OrderBy(x => x.Id).ToListAsync();
    }

    private HttpClient ClienteAnonimo()
    {
        var c = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add(TestAuthHandler.AnonimoHeader, "1");
        return c;
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string usuario, string clave)
    {
        var html = await (await client.GetAsync("/Identity/Account/Login")).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "sin token antiforgery en el login");
        return await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(m.Groups[1].Value),
            ["Input.UserName"] = usuario,
            ["Input.Password"] = clave
        }));
    }

    [Fact]
    public async Task LoginFallido_RegistraUnEventoAgrupado_SinNombreTipeadoNiClave()
    {
        var antes = (await EventosAsync("LoginFallido")).Count;
        using var client = ClienteAnonimo();
        const string tipeado = "usuario-inexistente-xyz";
        const string clave = "Clave-Secreta-123!";

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, tipeado, clave)).StatusCode);

        var eventos = (await EventosAsync("LoginFallido")).Skip(antes).ToList();
        var evento = Assert.Single(eventos);   // 3 intentos misma IP/entidad dentro de la ventana -> 1 evento
        Assert.Equal("Seguridad", evento.Modulo);
        Assert.Equal("Usuario:desconocido", evento.Entidad);
        Assert.DoesNotContain(tipeado, evento.Detalle ?? "");
        Assert.DoesNotContain(clave, evento.Detalle ?? "");
        Assert.DoesNotContain(tipeado, evento.Entidad);
    }

    [Fact]
    public async Task Bloqueo_TrasIntentosFallidos_RegistraEventoConIdInterno()
    {
        const string nombre = "usr-lockout-segbaja";
        string userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = nombre, Email = "lock@segbaja.test", Activo = true, EmailConfirmed = true, LockoutEnabled = true };
            var r = await um.CreateAsync(user, "Correcta-123!aA");
            Assert.True(r.Succeeded, string.Join(";", r.Errors.Select(e => e.Description)));
            userId = user.Id;
        }

        using var client = ClienteAnonimo();
        HttpResponseMessage? ultimo = null;
        for (var i = 0; i < 6; i++)
            ultimo = await LoginAsync(client, nombre, "mala-clave");

        Assert.Equal(HttpStatusCode.Redirect, ultimo!.StatusCode);
        Assert.Contains("Lockout", ultimo.Headers.Location!.OriginalString);

        var bloqueo = Assert.Single(await EventosAsync("Bloqueo"), e => e.Entidad == $"Usuario:{userId}");
        Assert.DoesNotContain(nombre, bloqueo.Detalle ?? "");
        Assert.Contains(await EventosAsync("LoginFallido"), e => e.Entidad == $"Usuario:{userId}");
    }

    [Fact]
    public async Task CerrarCaja_RegistraEventoEnLaMismaOperacion()
    {
        int aperturaId;
        using (var scope = _factory.Services.CreateScope())
            aperturaId = await ConcSeed.SembrarAperturaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "cajero-segbaja");

        using (var scope = _factory.Services.CreateScope())
        {
            var caja = scope.ServiceProvider.GetRequiredService<ICajaService>();
            await caja.CerrarCajaAsync(new CerrarCajaViewModel { AperturaCajaId = aperturaId, EfectivoContado = 0m }, "cajero-segbaja");
        }

        using var check = _factory.Services.CreateScope();
        var evento = await check.ServiceProvider.GetRequiredService<AppDbContext>().SeguridadEventosAuditoria
            .AsNoTracking().SingleAsync(e => e.Accion == "CerrarCaja" && e.Entidad == $"AperturaCaja:{aperturaId}");
        Assert.Equal("Caja", evento.Modulo);
        Assert.Contains("Usuario=cajero-segbaja", evento.Detalle);
    }
}
