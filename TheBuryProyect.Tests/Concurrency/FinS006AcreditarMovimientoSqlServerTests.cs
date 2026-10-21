using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// FIN-S-006 â€” POST /Caja/AcreditarMovimiento autorizaba por el aperturaId del request (IDOR) y el servicio acreditaba el
// movimientoId sin cruzarlo. Contra SQL Server real (efimero, aislado): controller real + servicios reales + padron
// CajaVendedores real. Mismo gate de seguridad que los demas harness (QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL "qa_fin_conc*").
// ---------------------------------------------------------------------------------------------

public sealed class FinS006SqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_s006";
}

public class FinS006AcreditarMovimientoSqlServerTests : IClassFixture<FinS006SqlHostFixture>
{
    private readonly FinS006SqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public FinS006AcreditarMovimientoSqlServerTests(FinS006SqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private sealed class UsuarioStub(string id, params string[] roles) : ICurrentUserService
    {
        public string GetUsername() => id;
        public string GetUserId() => id;
        public bool IsAuthenticated() => true;
        public string? GetEmail() => null;
        public bool IsInRole(string role) => roles.Contains(role);
        public bool HasPermission(string modulo, string accion) => true;
        public string? GetIpAddress() => null;
    }

    private sealed class TempDataStub : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed record Caja2(string MiembroA, int AperturaA, int MovA, int AperturaB, int MovB);

    private static CajaController Controller(IServiceProvider sp, string userId, params string[] roles)
    {
        var c = new CajaController(
            sp.GetRequiredService<ICajaService>(), sp.GetRequiredService<ICajaVendedorService>(),
            new UsuarioStub(userId, roles), NullLogger<CajaController>.Instance,
            sp.GetRequiredService<IMapper>(), sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IRelojComercial>());
        var http = new DefaultHttpContext();
        c.ControllerContext = new ControllerContext { HttpContext = http };
        c.TempData = new TempDataDictionary(http, new TempDataStub());
        return c;
    }

    private async Task<Caja2> SembrarAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10];
        var miembro = "s6a" + sufijo;
        ctx.Users.Add(new ApplicationUser
        {
            Id = miembro, UserName = miembro, NormalizedUserName = miembro.ToUpperInvariant(),
            Email = $"{miembro}@example.invalid", NormalizedEmail = $"{miembro}@example.invalid".ToUpperInvariant(),
            Activo = true, RowVersion = new byte[8]
        });
        await ctx.SaveChangesAsync();

        var aperturaA = await ConcSeed.SembrarAperturaAsync(ctx, miembro);
        var aperturaB = await ConcSeed.SembrarAperturaAsync(ctx, "s6b" + sufijo);
        var cajaA = (await ctx.AperturasCaja.AsNoTracking().FirstAsync(a => a.Id == aperturaA)).CajaId;
        ctx.CajaVendedores.Add(new CajaVendedor { CajaId = cajaA, VendedorUserId = miembro });

        MovimientoCaja Pendiente(int apertura) => new()
        {
            AperturaCajaId = apertura, Tipo = TipoMovimientoCaja.Ingreso, Concepto = ConceptoMovimientoCaja.VentaTransferencia,
            TipoPago = TipoPago.Transferencia, EstadoAcreditacion = EstadoAcreditacionMovimientoCaja.Pendiente, Monto = 40m,
            Descripcion = "FIN-S-006", Usuario = "seed", FechaMovimiento = DateTime.UtcNow
        };
        var a = Pendiente(aperturaA);
        var b = Pendiente(aperturaB);
        ctx.MovimientosCaja.AddRange(a, b);
        await ctx.SaveChangesAsync();
        return new Caja2(miembro, aperturaA, a.Id, aperturaB, b.Id);
    }

    private sealed record Foto(EstadoAcreditacionMovimientoCaja? Estado, DateTime? UpdatedAt, string? UpdatedBy);

    private async Task<Foto> FotoAsync(int movimientoId)
    {
        using var scope = _fx.Services.CreateScope();
        var m = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MovimientosCaja.AsNoTracking()
            .FirstAsync(x => x.Id == movimientoId);
        return new Foto(m.EstadoAcreditacion, m.UpdatedAt, m.UpdatedBy);
    }

    private async Task<(string? Success, string? Error)> AcreditarAsync(string user, string[] roles, int movimientoId, int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var c = Controller(scope.ServiceProvider, user, roles);
        await c.AcreditarMovimiento(movimientoId, aperturaId);
        return (c.TempData["Success"]?.ToString(), c.TempData["Error"]?.ToString());
    }

    [SqlServerConcFact(DisplayName = "FIN-S-006 [S006-01] PoC original sobre SQL Server: aperturaId propio + movimientoId ajeno no escribe en la caja ajena")]
    public async Task S006_01_PoCOriginal_NoEscribeEnCajaAjena()
    {
        var e = await SembrarAsync();
        var antesB = await FotoAsync(e.MovB);
        var antesA = await FotoAsync(e.MovA);

        var r = await AcreditarAsync(e.MiembroA, [], e.MovB, e.AperturaA);
        _out.WriteLine($"resultado: success={r.Success} error={r.Error}");

        Assert.Null(r.Success);
        Assert.NotNull(r.Error);
        Assert.Equal(antesB, await FotoAsync(e.MovB));      // estado, UpdatedAt y UpdatedBy intactos
        Assert.Equal(antesA, await FotoAsync(e.MovA));
    }

    [SqlServerConcFact(DisplayName = "FIN-S-006 [S006-02] mismatch aperturaId/movimiento y movimiento inexistente: sin escrituras")]
    public async Task S006_02_MismatchYMovimientoInexistente_SinEscrituras()
    {
        var e = await SembrarAsync();
        var antesB = await FotoAsync(e.MovB);

        var ajena = await AcreditarAsync(e.MiembroA, [], e.MovB, e.AperturaB);
        var inexistente = await AcreditarAsync(e.MiembroA, [], 987_654_321, e.AperturaA);

        Assert.NotNull(ajena.Error);
        Assert.NotNull(inexistente.Error);
        Assert.DoesNotContain("no encontrado", inexistente.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(antesB, await FotoAsync(e.MovB));
    }

    [SqlServerConcFact(DisplayName = "FIN-S-006 [S006-03] flujos legitimos: miembro acredita lo suyo; Administrador y SuperAdmin acreditan cualquier caja")]
    public async Task S006_03_FlujosLegitimos()
    {
        var e = await SembrarAsync();
        var e2 = await SembrarAsync();
        var e3 = await SembrarAsync();

        var propio = await AcreditarAsync(e.MiembroA, [], e.MovA, e.AperturaA);
        var admin = await AcreditarAsync("admin-s6", [Roles.Administrador], e2.MovB, e2.AperturaB);
        var superAdmin = await AcreditarAsync("sa-s6", [Roles.SuperAdmin], e3.MovB, e3.AperturaB);

        Assert.NotNull(propio.Success);
        Assert.NotNull(admin.Success);
        Assert.NotNull(superAdmin.Success);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, (await FotoAsync(e.MovA)).Estado);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, (await FotoAsync(e2.MovB)).Estado);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, (await FotoAsync(e3.MovB)).Estado);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, (await FotoAsync(e.MovB)).Estado);   // la caja ajena no se tocÃ³
    }

    [SqlServerConcFact(DisplayName = "FIN-S-006 [S006-04] concurrencia: dos acreditaciones legitimas simultaneas del mismo movimiento, una sola gana")]
    public async Task S006_04_DosAcreditacionesLegitimasSimultaneas_UnaGana()
    {
        var e = await SembrarAsync();
        using var arranque = new ManualResetEventSlim(false);

        Task<(string? Success, string? Error)> Disparo() => Task.Run(async () =>
        {
            arranque.Wait();
            return await AcreditarAsync(e.MiembroA, [], e.MovA, e.AperturaA);
        });
        var t1 = Disparo();
        var t2 = Disparo();
        arranque.Set();
        var r = await Task.WhenAll(t1, t2);
        _out.WriteLine(string.Join(" | ", r.Select(x => $"S={x.Success} E={x.Error}")));

        Assert.Equal(1, r.Count(x => x.Success != null));
        Assert.Equal(1, r.Count(x => x.Error != null));
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, (await FotoAsync(e.MovA)).Estado);
    }

    [SqlServerConcFact(DisplayName = "FIN-S-006 [S006-05] concurrencia: intruso y supervisor sobre el mismo movimiento ajeno: solo el supervisor escribe")]
    public async Task S006_05_IntrusoVsSupervisor_SoloEscribeElSupervisor()
    {
        var e = await SembrarAsync();
        using var arranque = new ManualResetEventSlim(false);

        var intruso = Task.Run(async () => { arranque.Wait(); return await AcreditarAsync(e.MiembroA, [], e.MovB, e.AperturaA); });
        var supervisor = Task.Run(async () => { arranque.Wait(); return await AcreditarAsync("sup-s6", [Roles.Administrador], e.MovB, e.AperturaB); });
        arranque.Set();
        var (ri, rs) = (await intruso, await supervisor);
        _out.WriteLine($"intruso S={ri.Success} E={ri.Error} | supervisor S={rs.Success} E={rs.Error}");

        Assert.Null(ri.Success);
        Assert.NotNull(rs.Success);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Acreditado, (await FotoAsync(e.MovB)).Estado);
        Assert.Equal(EstadoAcreditacionMovimientoCaja.Pendiente, (await FotoAsync(e.MovA)).Estado);
    }
}
