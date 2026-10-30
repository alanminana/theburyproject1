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
/// B13 por HTTP real (autenticación, permisos, antiforgery, controller, servicios): cancelar una venta pagada con cheque cuya caja
/// ya cerró deja una contrapartida auditada en la caja vigente del usuario y no toca la caja cerrada.
/// </summary>
[Collection("HttpIntegration")]
public class VentaCancelarChequeCajaCerradaHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Usuario = "testuser";
    private readonly CustomWebApplicationFactory _factory;

    public VentaCancelarChequeCajaCerradaHttpTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed record Sembrado(int VentaId, int ChequeId, int AperturaCerrada, int AperturaVigente);

    private async Task<Sembrado> SembrarAsync(EstadoAcreditacionMovimientoCaja estado)
    {
        await _factory.SeedTestUserAsync();
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var g = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente { Nombre = "B13", Apellido = g, NumeroDocumento = g };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var cerrada = await ConcSeed.SembrarAperturaAsync(ctx, Usuario);
        var venta = new Venta
        {
            ClienteId = cliente.Id, AperturaCajaId = cerrada, Numero = "VB13-" + g,
            Estado = EstadoVenta.Confirmada, TipoPago = TipoPago.Cheque, Total = 100m, Subtotal = 100m
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        var cheque = new MovimientoCaja
        {
            AperturaCajaId = cerrada, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaCheque,
            EstadoAcreditacion = estado, TipoPago = TipoPago.Cheque, Monto = 100m, Descripcion = "Venta " + venta.Numero,
            Referencia = venta.Numero, ReferenciaId = venta.Id, VentaId = venta.Id, Usuario = Usuario, FechaMovimiento = DateTime.UtcNow
        };
        ctx.MovimientosCaja.Add(cheque);
        await ctx.SaveChangesAsync();

        // La caja del cheque cierra; el usuario abre otra (la vigente).
        await ctx.AperturasCaja.Where(a => a.Id == cerrada).ExecuteUpdateAsync(s => s.SetProperty(a => a.Cerrada, true));
        var vigente = await ConcSeed.SembrarAperturaAsync(ctx, Usuario);
        return new Sembrado(venta.Id, cheque.Id, cerrada, vigente);
    }

    private static async Task<string> TokenAsync(HttpClient client, int ventaId)
    {
        var response = await client.GetAsync($"/Venta/Cancelar/{ventaId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "No se encontró el token antiforgery.");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> CancelarAsync(HttpClient client, int ventaId, string? token)
    {
        var campos = new Dictionary<string, string> { ["motivo"] = "B13 el cliente desistió" };
        if (token != null)
            campos["__RequestVerificationToken"] = token;
        return client.PostAsync($"/Venta/Cancelar/{ventaId}", new FormUrlEncodedContent(campos));
    }

    private sealed record Lectura(EstadoVenta Venta, EstadoAcreditacionMovimientoCaja? Cheque, List<MovimientoCaja> Reversiones, int MovsCerrada);

    private async Task<Lectura> LeerAsync(Sembrado s)
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estadoVenta = await ctx.Ventas.AsNoTracking().Where(v => v.Id == s.VentaId).Select(v => v.Estado).FirstAsync();
        var estadoCheque = await ctx.MovimientosCaja.AsNoTracking().Where(m => m.Id == s.ChequeId).Select(m => m.EstadoAcreditacion).FirstAsync();
        var rev = await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => m.VentaId == s.VentaId && m.Concepto == ConceptoMovimientoCaja.ReversionVenta).ToListAsync();
        var movsCerrada = await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.AperturaCajaId == s.AperturaCerrada);
        return new Lectura(estadoVenta, estadoCheque, rev, movsCerrada);
    }

    [Fact]
    public async Task ChequeAcreditado_CajaCerrada_PostCancelar_AsientaEgresoEnCajaVigente()
    {
        var s = await SembrarAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client, s.VentaId);

        var r = await CancelarAsync(client, s.VentaId, token);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var f = await LeerAsync(s);
        Assert.Equal(EstadoVenta.Cancelada, f.Venta);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Cheque);
        var egreso = Assert.Single(f.Reversiones);
        Assert.Equal(s.AperturaVigente, egreso.AperturaCajaId);
        Assert.Equal(s.ChequeId, egreso.MovimientoOrigenId);
        Assert.Equal(TipoPago.Cheque, egreso.TipoPago);
        Assert.Equal(1, f.MovsCerrada); // la caja cerrada sigue con su único movimiento

        // Reintento: la venta ya está cancelada; no se duplica nada.
        var again = await CancelarAsync(client, s.VentaId, token);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Single((await LeerAsync(s)).Reversiones);
    }

    [Fact]
    public async Task ChequePendiente_CajaCerrada_PostCancelar_AnulaElIngresoSinEgreso()
    {
        var s = await SembrarAsync(EstadoAcreditacionMovimientoCaja.Pendiente);
        var client = _factory.CreateAuthenticatedClient();
        var token = await TokenAsync(client, s.VentaId);

        var r = await CancelarAsync(client, s.VentaId, token);

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var f = await LeerAsync(s);
        Assert.Equal(EstadoVenta.Cancelada, f.Venta);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Anulado, f.Cheque);
        Assert.Empty(f.Reversiones);
        Assert.Equal(1, f.MovsCerrada);
    }

    [Fact]
    public async Task PostCancelar_SinTokenAntiforgery_SeRechazaYNoCambiaNada()
    {
        var s = await SembrarAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var client = _factory.CreateAuthenticatedClient();

        var r = await CancelarAsync(client, s.VentaId, token: null);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var f = await LeerAsync(s);
        Assert.Equal(EstadoVenta.Confirmada, f.Venta);
        Assert.Empty(f.Reversiones);
    }

    private const string SoloVerUserId = "b13-ventas-solo-ver";

    /// <summary>Usuario con ventas.view y SIN ventas.cancel: aisla el permiso de la acción (el de clase lo cumple).</summary>
    private async Task SembrarUsuarioSoloVerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await ctx.Users.AnyAsync(u => u.Id == SoloVerUserId))
            return;
        const string roleId = "b13-ventas-solo-ver-role";
        const int moduloId = 9961, accionId = 9961;
        ctx.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole { Id = roleId, Name = "B13SoloVer", NormalizedName = "B13SOLOVER" });
        ctx.Users.Add(new ApplicationUser
        {
            Id = SoloVerUserId, UserName = "b13-solo-ver", NormalizedUserName = "B13-SOLO-VER", Email = "b13sv@test.com",
            NormalizedEmail = "B13SV@TEST.COM", Activo = true, RowVersion = new byte[8]
        });
        await ctx.SaveChangesAsync();
        ctx.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = SoloVerUserId, RoleId = roleId });
        ctx.ModulosSistema.Add(new ModuloSistema { Id = moduloId, Nombre = "B13 Ventas", Clave = "ventas-b13-test", Orden = 0, RowVersion = new byte[8] });
        await ctx.SaveChangesAsync();
        ctx.AccionesModulo.Add(new AccionModulo { Id = accionId, ModuloId = moduloId, Nombre = "Ver", Clave = "ver-b13-test", Orden = 0, RowVersion = new byte[8] });
        await ctx.SaveChangesAsync();
        ctx.RolPermisos.Add(new RolPermiso { RoleId = roleId, ModuloId = moduloId, AccionId = accionId, ClaimValue = "ventas.view", IsDeleted = false, RowVersion = new byte[8] });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task UsuarioConVerPeroSinCancelar_ConTokenValidoDeSuSesion_RecibeDenegacionYNoCambiaNada()
    {
        await SembrarUsuarioSoloVerAsync();
        var s = await SembrarAsync(EstadoAcreditacionMovimientoCaja.Acreditado);
        var client = _factory.CreateClientWithUserId(SoloVerUserId);

        // Token válido de ESTA sesión (formulario de una página que sí puede ver): el rechazo no puede ser por antiforgery.
        var detalle = await client.GetAsync($"/Venta/Details/{s.VentaId}");
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        var m = Regex.Match(await detalle.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "La página de detalle no ofrece un token antiforgery.");
        var token = WebUtility.HtmlDecode(m.Groups[1].Value);

        var r = await CancelarAsync(client, s.VentaId, token);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var f = await LeerAsync(s);
        Assert.Equal(EstadoVenta.Confirmada, f.Venta);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, f.Cheque);
        Assert.Empty(f.Reversiones);
        Assert.Equal(1, f.MovsCerrada);
    }
}
