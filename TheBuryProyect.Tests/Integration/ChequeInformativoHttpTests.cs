using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Tests.Concurrency;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// V-02 por HTTP real: el cheque recibido se muestra como pago pendiente informativo (sin acciones de acreditar ni rechazar), la
/// conciliación lo separa del cobrado y cancelar la venta conserva el historial sin dejar el cheque como dinero cobrado.
/// </summary>
[Collection("HttpIntegration.2")]
public class ChequeInformativoHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Usuario = "testuser";
    private readonly CustomWebApplicationFactory _factory;

    public ChequeInformativoHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Sembrado(int VentaId, int ChequeId, int Apertura, string Numero);

    private async Task<Sembrado> SembrarAsync()
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var g = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente { Nombre = "V02", Apellido = g, NumeroDocumento = g };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var apertura = await ConcSeed.SembrarAperturaAsync(ctx, Usuario);
        var venta = new Venta
        {
            ClienteId = cliente.Id, AperturaCajaId = apertura, Numero = "V02-" + g,
            Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Cheque, Total = 400m, Subtotal = 400m
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        ctx.DatosCheque.Add(new DatosCheque
        {
            VentaId = venta.Id, NumeroCheque = "0001" + g[..4], Banco = "Banco Demo", Titular = "Titular " + g,
            FechaEmision = DateTime.UtcNow.Date, FechaVencimiento = DateTime.UtcNow.Date.AddDays(30), Monto = 400m
        });
        var cheque = new MovimientoCaja
        {
            AperturaCajaId = apertura, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaCheque,
            EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente, TipoPago = TipoPago.Cheque, Monto = 400m,
            Descripcion = "Venta " + venta.Numero, Referencia = venta.Numero, ReferenciaId = venta.Id, VentaId = venta.Id,
            Usuario = Usuario, FechaMovimiento = DateTime.UtcNow
        };
        ctx.MovimientosCaja.Add(cheque);
        await ctx.SaveChangesAsync();
        return new Sembrado(venta.Id, cheque.Id, apertura, venta.Numero);
    }

    private static async Task<string> LeerAsync(HttpClient client, string url)
    {
        var r = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task VentaDetails_MuestraElChequeComoPagoPendienteInformativo_SinAcciones()
    {
        var s = await SembrarAsync();
        var html = await LeerAsync(_factory.CreateAuthenticatedClient(), $"/Venta/Details/{s.VentaId}");

        Assert.Contains("data-cheque-card", html);
        Assert.Contains("Pago pendiente", html);
        Assert.Contains("data-cheque-informativo", html);
        Assert.DoesNotContain("Pendiente de acreditar", html);
        Assert.DoesNotContain("data-cheque-acreditar", html);
    }

    [Fact]
    public async Task DetalleDeCaja_SeparaElChequeRecibidoDelCobrado_YNoOfreceAcreditarNiRechazar()
    {
        var s = await SembrarAsync();
        var html = await LeerAsync(_factory.CreateAuthenticatedClient(), $"/Caja/DetallesApertura/{s.Apertura}");

        Assert.Contains("data-cc-cheques-recibidos", html);
        Assert.Contains("Cheque recibido", html);
        Assert.DoesNotContain("data-cheque-acreditar", html);
        Assert.DoesNotContain("data-cheque-rechazo", html);
        Assert.DoesNotContain("AcreditarCheque", html);
    }

    [Fact]
    public async Task CancelarLaVenta_ConservaElHistorial_YElChequeAnuladoDejaDeFigurarComoCheque()
    {
        var s = await SembrarAsync();
        var client = _factory.CreateAuthenticatedClient();
        var tokenHtml = await LeerAsync(client, $"/Venta/Cancelar/{s.VentaId}");
        var token = WebUtility.HtmlDecode(Regex.Match(tokenHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

        var r = await client.PostAsync($"/Venta/Cancelar/{s.VentaId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["motivo"] = "V-02 el cliente desistió",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(EstadoVenta.Cancelada, await ctx.Ventas.AsNoTracking().Where(v => v.Id == s.VentaId).Select(v => v.Estado).FirstAsync());
            // Historial: el ingreso original sigue (anulado) y se agrega su contrapartida; el cheque informativo se conserva.
            Assert.Equal(EstadoAcreditacionMovimientoCaja.Anulado,
                await ctx.MovimientosCaja.AsNoTracking().Where(m => m.Id == s.ChequeId).Select(m => m.EstadoAcreditacion).FirstAsync());
            Assert.Equal(1, await ctx.MovimientosCaja.CountAsync(m => m.VentaId == s.VentaId && m.Concepto == ConceptoMovimientoCaja.ReversionVenta));
            Assert.Equal(1, await ctx.DatosCheque.CountAsync(d => d.VentaId == s.VentaId && !d.IsDeleted));
        }

        var html = await LeerAsync(client, $"/Caja/DetallesApertura/{s.Apertura}");
        Assert.DoesNotContain("data-cc-cheques-recibidos", html);
    }
}
