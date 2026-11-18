using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// D-03 Tesorería administrativa contra SQL Server REAL: un cheque físico por pago o anticipo, anticipos aplicables a varias facturas del
// mismo proveedor, idempotencia por formulario y reversión. El lock por proveedor y los índices únicos filtrados solo se prueban aquí
// (SQLite serializa todo con un único escritor).
//
// Mismo gate que los demás harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*".
// El fixture crea y elimina su propio catálogo ("<base>_tes") y aplica TODAS las migraciones.
//
// Cada escenario se repite varias rondas y exige: nada de excepciones no controladas (un 500 en la UI), los invariantes de saldo y de
// unicidad del cheque sobre lo PERSISTIDO.
// ---------------------------------------------------------------------------------------------

public sealed class TesoreriaSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_tes";
}

public class TesoreriaSqlServerTests : IClassFixture<TesoreriaSqlHostFixture>
{
    private const int Rondas = 6;

    private readonly TesoreriaSqlHostFixture _fx;
    private int _origenId;

    public TesoreriaSqlServerTests(TesoreriaSqlHostFixture fx) => _fx = fx;

    // ── siembra ─────────────────────────────────────────────────────────

    private static string Sufijo() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private async Task<int> OrigenAsync()
    {
        if (_origenId != 0)
            return _origenId;
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var origen = await ctx.OrigenesFondos.FirstOrDefaultAsync(o => o.Codigo == "TESCONC");
        if (origen is null)
        {
            origen = new OrigenFondos { Codigo = "TESCONC", Nombre = "Origen tesorería conc", Tipo = TipoOrigenFondos.CuentaBancaria };
            ctx.OrigenesFondos.Add(origen);
            await ctx.SaveChangesAsync();
        }
        return _origenId = origen.Id;
    }

    private async Task<int> SembrarProveedorAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Sufijo();
        var proveedor = new Proveedor { Cuit = sufijo + "0", RazonSocial = "Prov-" + sufijo, Activo = true };
        ctx.Proveedores.Add(proveedor);
        await ctx.SaveChangesAsync();
        return proveedor.Id;
    }

    private async Task<int> SembrarFacturaAsync(int proveedorId, decimal total)
    {
        using var scope = _fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
        var r = await svc.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = proveedorId, Tipo = TipoComprobanteProveedor.Factura, NumeroComprobante = "F-" + Sufijo(),
            FechaEmision = DateTime.UtcNow.Date, ImporteNeto = total, ImporteIva = 0m, ImporteTotal = total
        }, "conc");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private async Task<int> SembrarChequeAsync(int proveedorId, decimal monto)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cheque = new Cheque { Numero = Sufijo(), Banco = "Banco conc", Monto = monto, ProveedorId = proveedorId, Estado = EstadoCheque.Emitido };
        ctx.Cheques.Add(cheque);
        await ctx.SaveChangesAsync();
        return cheque.Id;
    }

    private async Task<int> SembrarAnticipoAsync(int proveedorId, decimal monto)
    {
        using var scope = _fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
        var r = await svc.RegistrarAnticipoAsync(AnticipoReq(proveedorId, monto), "conc");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

    private AnticipoProveedorRequest AnticipoReq(int proveedorId, decimal monto, int? chequeId = null, string? clave = null) => new()
    {
        ProveedorId = proveedorId, Fecha = DateTime.UtcNow.Date, Monto = monto, OrigenFondosId = _origenId,
        Medio = chequeId.HasValue ? MedioPagoProveedor.Cheque : MedioPagoProveedor.Transferencia, ChequeId = chequeId, ClaveIdempotencia = clave
    };

    private AnticipoProveedorRequest AnticipoEn(int proveedorId, decimal monto, int origenId)
    {
        var r = AnticipoReq(proveedorId, monto);
        r.OrigenFondosId = origenId;
        return r;
    }

    private PagoProveedorRequest PagoReq(int proveedorId, int facturaId, decimal monto, int? chequeId = null, string? clave = null) => new()
    {
        ProveedorId = proveedorId, Fecha = DateTime.UtcNow.Date, Monto = monto, OrigenFondosId = _origenId,
        Medio = chequeId.HasValue ? MedioPagoProveedor.Cheque : MedioPagoProveedor.Transferencia, ChequeId = chequeId, ClaveIdempotencia = clave,
        Imputaciones = new List<ImputacionRequest> { new() { ComprobanteId = facturaId, Monto = monto } }
    };

    /// <summary>Ejecuta las operaciones a la vez (compuerta común) y devuelve "OK" / "RECHAZADA: motivo" / "EXCEPCION: tipo".</summary>
    private async Task<string[]> EnParaleloAsync(params Func<ICuentaPorPagarService, Task<ResultadoCxp>>[] operaciones)
    {
        var compuerta = new TaskCompletionSource();
        var tareas = operaciones.Select(op => Task.Run(async () =>
        {
            using var scope = _fx.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
            await compuerta.Task;
            try
            {
                var r = await op(svc);
                return r.Ok ? "OK" : "RECHAZADA: " + r.Error;
            }
            catch (Exception ex)
            {
                return "EXCEPCION: " + ex.GetType().Name + " " + ex.Message;
            }
        })).ToArray();

        await Task.Delay(300);
        compuerta.SetResult();
        return await Task.WhenAll(tareas);
    }

    private static void AssertSinExcepciones(IEnumerable<string> resultados, string contexto)
    {
        var malos = resultados.Where(r => r.StartsWith("EXCEPCION")).ToList();
        Assert.True(malos.Count == 0, $"{contexto}: excepción no controlada (sería un 500): {string.Join(" | ", malos)}");
    }

    private async Task<(decimal Saldo, decimal Aplicado)> LeerFacturaAsync(int facturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = await ctx.ComprobantesProveedor.AsNoTracking().SingleAsync(x => x.Id == facturaId);
        var pagos = await ctx.PagoProveedorImputaciones.AsNoTracking()
            .Where(i => i.ComprobanteProveedorId == facturaId && !i.Pago.Anulado).Select(i => i.Monto).ToListAsync();
        var anticipos = await ctx.AnticipoAplicaciones.AsNoTracking()
            .Where(a => a.ComprobanteProveedorId == facturaId && !a.Anulada && !a.Anticipo.Anulado).Select(a => a.Monto).ToListAsync();
        return (c.SaldoPendiente, pagos.Sum() + anticipos.Sum());
    }

    private async Task<int> EgresosVigentesConChequeAsync(int chequeId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pagos = await ctx.PagosProveedor.AsNoTracking().CountAsync(p => p.ChequeId == chequeId && !p.Anulado);
        var anticipos = await ctx.AnticiposProveedor.AsNoTracking().CountAsync(a => a.ChequeId == chequeId && !a.Anulado);
        return pagos + anticipos;
    }

    // ── cheque único ────────────────────────────────────────────────────

    [SqlServerConcFact(DisplayName = "D03-CONC-01 dos pagos simultaneos con el MISMO cheque: solo uno entra")]
    public async Task Dos_Pagos_Con_El_Mismo_Cheque_Solo_Uno_Entra()
    {
        await OrigenAsync();
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var prov = await SembrarProveedorAsync();
            var f1 = await SembrarFacturaAsync(prov, 500m);
            var f2 = await SembrarFacturaAsync(prov, 500m);
            var cheque = await SembrarChequeAsync(prov, 500m);

            var res = await EnParaleloAsync(
                s => s.RegistrarPagoAsync(PagoReq(prov, f1, 500m, cheque), "u1"),
                s => s.RegistrarPagoAsync(PagoReq(prov, f2, 500m, cheque), "u2"));

            AssertSinExcepciones(res, $"ronda {ronda}");
            Assert.Equal(1, res.Count(r => r == "OK"));
            Assert.Equal(1, await EgresosVigentesConChequeAsync(cheque));
            Assert.Equal(1, res.Count(r => r.StartsWith("RECHAZADA") && r.Contains("cheque")));
        }
    }

    [SqlServerConcFact(DisplayName = "D03-CONC-02 un pago y un anticipo simultaneos con el MISMO cheque: solo uno entra")]
    public async Task Un_Pago_Y_Un_Anticipo_Con_El_Mismo_Cheque_Solo_Uno_Entra()
    {
        await OrigenAsync();
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var prov = await SembrarProveedorAsync();
            var f = await SembrarFacturaAsync(prov, 500m);
            var cheque = await SembrarChequeAsync(prov, 500m);

            var res = await EnParaleloAsync(
                s => s.RegistrarPagoAsync(PagoReq(prov, f, 500m, cheque), "u1"),
                s => s.RegistrarAnticipoAsync(AnticipoReq(prov, 500m, cheque), "u2"));

            AssertSinExcepciones(res, $"ronda {ronda}");
            Assert.Equal(1, res.Count(r => r == "OK"));
            Assert.Equal(1, await EgresosVigentesConChequeAsync(cheque));
        }
    }

    [SqlServerConcFact(DisplayName = "D03-CONC-03 anular el pago libera el cheque: se puede usar en otro pago (y el indice unico impide dos vigentes)")]
    public async Task Anular_El_Pago_Libera_El_Cheque_Y_El_Indice_Impide_Dos_Vigentes()
    {
        await OrigenAsync();
        var prov = await SembrarProveedorAsync();
        var f = await SembrarFacturaAsync(prov, 500m);
        var cheque = await SembrarChequeAsync(prov, 500m);

        int pagoId;
        using (var scope = _fx.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
            var r = await svc.RegistrarPagoAsync(PagoReq(prov, f, 500m, cheque), "u1");
            Assert.True(r.Ok, r.Error);
            pagoId = r.Id!.Value;
            Assert.False((await svc.RegistrarPagoAsync(PagoReq(prov, f, 500m, cheque), "u1")).Ok);
            Assert.True((await svc.AnularPagoAsync(pagoId, "error de carga", "u1")).Ok);
            Assert.True((await svc.RegistrarPagoAsync(PagoReq(prov, f, 500m, cheque), "u1")).Ok);
        }

        Assert.Equal(1, await EgresosVigentesConChequeAsync(cheque));

        // Aunque se salte el servicio, el índice único filtrado impide un segundo pago vigente con el mismo cheque.
        using var scope2 = _fx.Services.CreateScope();
        var ctx = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.PagosProveedor.Add(new PagoProveedor
        {
            ProveedorId = prov, Fecha = DateTime.UtcNow.Date, Medio = MedioPagoProveedor.Cheque, Monto = 500m, ChequeId = cheque,
            OrigenFondosId = _origenId, CreatedBy = "idx"
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("IX_PagosProveedor_ChequeId_Vigente", ex.InnerException?.Message ?? ex.Message);
        Assert.IsType<SqlException>(ex.InnerException);
    }

    // ── anticipos ───────────────────────────────────────────────────────

    [SqlServerConcFact(DisplayName = "D03-CONC-04 dos aplicaciones simultaneas de un anticipo de 1000 por 700: solo una entra, sin sobreaplicar")]
    public async Task Dos_Aplicaciones_Simultaneas_No_Sobreaplican_El_Anticipo()
    {
        await OrigenAsync();
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var prov = await SembrarProveedorAsync();
            var f1 = await SembrarFacturaAsync(prov, 800m);
            var f2 = await SembrarFacturaAsync(prov, 800m);
            var anticipo = await SembrarAnticipoAsync(prov, 1000m);

            var res = await EnParaleloAsync(
                s => s.AplicarAnticipoAsync(anticipo, new[] { new ImputacionRequest { ComprobanteId = f1, Monto = 700m } }, null, "u1"),
                s => s.AplicarAnticipoAsync(anticipo, new[] { new ImputacionRequest { ComprobanteId = f2, Monto = 700m } }, null, "u2"));

            AssertSinExcepciones(res, $"ronda {ronda}");
            Assert.Equal(1, res.Count(r => r == "OK"));
            using var scope = _fx.Services.CreateScope();
            var item = (await scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>().ObtenerAnticipoAsync(anticipo))!;
            Assert.Equal(300m, item.SaldoSinAplicar);
            Assert.Equal(1, item.Aplicaciones.Count(a => !a.Anulada));
        }
    }

    [SqlServerConcFact(DisplayName = "D03-CONC-05 un anticipo repartido entre varias facturas y un pago simultaneo sobre la misma factura: nunca se sobreimputa")]
    public async Task Anticipo_Y_Pago_Sobre_La_Misma_Factura_No_Sobreimputan()
    {
        await OrigenAsync();
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var prov = await SembrarProveedorAsync();
            var f = await SembrarFacturaAsync(prov, 1000m);
            var anticipo = await SembrarAnticipoAsync(prov, 700m);

            var res = await EnParaleloAsync(
                s => s.AplicarAnticipoAsync(anticipo, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 700m } }, null, "u1"),
                s => s.RegistrarPagoAsync(PagoReq(prov, f, 700m), "u2"));

            AssertSinExcepciones(res, $"ronda {ronda}");
            Assert.Equal(1, res.Count(r => r == "OK"));
            var (saldo, aplicado) = await LeerFacturaAsync(f);
            Assert.Equal(700m, aplicado);
            Assert.Equal(300m, saldo);
        }
    }

    [SqlServerConcFact(DisplayName = "D03-CONC-06 aplicar el anticipo vs anularlo a la vez: nunca queda anulado con aplicaciones vigentes")]
    public async Task Aplicar_Vs_Anular_Anticipo_Nunca_Deja_Anulado_Con_Aplicaciones()
    {
        await OrigenAsync();
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var prov = await SembrarProveedorAsync();
            var f = await SembrarFacturaAsync(prov, 500m);
            var anticipo = await SembrarAnticipoAsync(prov, 500m);

            var res = await EnParaleloAsync(
                s => s.AplicarAnticipoAsync(anticipo, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 500m } }, null, "u1"),
                s => s.AnularAnticipoAsync(anticipo, "carga duplicada", "u2"));

            AssertSinExcepciones(res, $"ronda {ronda}");
            Assert.Equal(1, res.Count(r => r == "OK"));
            using var scope = _fx.Services.CreateScope();
            var item = (await scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>().ObtenerAnticipoAsync(anticipo))!;
            Assert.False(item.Anulado && item.Aplicaciones.Any(a => !a.Anulada), $"ronda {ronda}: anticipo anulado con aplicaciones vigentes ({string.Join(" / ", res)})");
            var (saldo, aplicado) = await LeerFacturaAsync(f);
            Assert.Equal(item.Anulado ? 0m : 500m, aplicado);
            Assert.Equal(item.Anulado ? 500m : 0m, saldo);
        }
    }

    [SqlServerConcFact(DisplayName = "D03-CONC-07 el mismo formulario de anticipo enviado tres veces a la vez: un solo anticipo")]
    public async Task El_Mismo_Formulario_De_Anticipo_Tres_Veces_Registra_Uno()
    {
        await OrigenAsync();
        var prov = await SembrarProveedorAsync();
        var clave = Guid.NewGuid().ToString("N");

        var res = await EnParaleloAsync(
            s => s.RegistrarAnticipoAsync(AnticipoReq(prov, 400m, clave: clave), "u1"),
            s => s.RegistrarAnticipoAsync(AnticipoReq(prov, 400m, clave: clave), "u1"),
            s => s.RegistrarAnticipoAsync(AnticipoReq(prov, 400m, clave: clave), "u1"));

        AssertSinExcepciones(res, "triple envío");
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await ctx.AnticiposProveedor.AsNoTracking().CountAsync(a => a.ProveedorId == prov));
    }

    [SqlServerConcFact(DisplayName = "D03-CONC-08 anticipo aplicado a varias facturas: se revierte aplicacion por aplicacion y los saldos vuelven")]
    public async Task Anticipo_Repartido_En_Varias_Facturas_Se_Revierte_Y_Restituye_Saldos()
    {
        await OrigenAsync();
        var prov = await SembrarProveedorAsync();
        var f1 = await SembrarFacturaAsync(prov, 300m);
        var f2 = await SembrarFacturaAsync(prov, 400m);
        var anticipo = await SembrarAnticipoAsync(prov, 600m);

        using var scope = _fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
        var aplicar = await svc.AplicarAnticipoAsync(anticipo, new[]
        {
            new ImputacionRequest { ComprobanteId = f1, Monto = 300m }, new ImputacionRequest { ComprobanteId = f2, Monto = 300m }
        }, null, "u1");
        Assert.True(aplicar.Ok, aplicar.Error);
        Assert.Equal(0m, (await LeerFacturaAsync(f1)).Saldo);
        Assert.Equal(100m, (await LeerFacturaAsync(f2)).Saldo);

        var item = (await svc.ObtenerAnticipoAsync(anticipo))!;
        Assert.False((await svc.AnularAnticipoAsync(anticipo, "intento con aplicaciones", "u1")).Ok);
        foreach (var a in item.Aplicaciones)
            Assert.True((await svc.AnularAplicacionAnticipoAsync(a.Id, "reversión", "u1")).Ok);

        Assert.Equal(300m, (await LeerFacturaAsync(f1)).Saldo);
        Assert.Equal(400m, (await LeerFacturaAsync(f2)).Saldo);
        Assert.True((await svc.AnularAnticipoAsync(anticipo, "reversión completa", "u1")).Ok);
    }

    // ── baja de un origen vs egresos ────────────────────────────────────

    [SqlServerConcFact(DisplayName = "D03-CONC-09 eliminar un origen vs registrar un pago o anticipo con ese origen a la vez: nunca queda un egreso sobre un origen eliminado")]
    public async Task Eliminar_Origen_Vs_Registrar_Egreso_Nunca_Deja_Un_Egreso_Sobre_Un_Origen_Eliminado()
    {
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            int origenId;
            using (var scope = _fx.Services.CreateScope())
            {
                var tes = scope.ServiceProvider.GetRequiredService<ITesoreriaService>();
                var r = await tes.GuardarOrigenAsync(new OrigenFondosRequest { Codigo = "R" + Sufijo(), Nombre = "Origen ronda " + ronda }, "conc");
                Assert.True(r.Ok, r.Error);
                origenId = r.Id!.Value;
            }
            var prov = await SembrarProveedorAsync();
            var f = await SembrarFacturaAsync(prov, 300m);

            var compuerta = new TaskCompletionSource();
            async Task<string> Ejecutar(Func<IServiceProvider, Task<ResultadoCxp>> op)
            {
                using var scope = _fx.Services.CreateScope();
                await compuerta.Task;
                try { var r = await op(scope.ServiceProvider); return r.Ok ? "OK" : "RECHAZADA: " + r.Error; }
                catch (Exception ex) { return "EXCEPCION: " + ex.GetType().Name + " " + ex.Message; }
            }

            var esPago = ronda % 2 == 0;
            var tareas = new[]
            {
                Task.Run(() => Ejecutar(sp => esPago
                    ? sp.GetRequiredService<ICuentaPorPagarService>().RegistrarPagoAsync(new PagoProveedorRequest
                    {
                        ProveedorId = prov, Fecha = DateTime.UtcNow.Date, Monto = 300m, OrigenFondosId = origenId, Medio = MedioPagoProveedor.Transferencia,
                        Imputaciones = new List<ImputacionRequest> { new() { ComprobanteId = f, Monto = 300m } }
                    }, "u1")
                    : sp.GetRequiredService<ICuentaPorPagarService>().RegistrarAnticipoAsync(AnticipoEn(prov, 300m, origenId), "u1"))),
                Task.Run(() => Ejecutar(sp => sp.GetRequiredService<ITesoreriaService>().EliminarOrigenAsync(origenId, "u2")))
            };
            await Task.Delay(300);
            compuerta.SetResult();
            var res = await Task.WhenAll(tareas);

            AssertSinExcepciones(res, $"ronda {ronda}");
            using var verificar = _fx.Services.CreateScope();
            var ctx = verificar.ServiceProvider.GetRequiredService<AppDbContext>();
            var origen = await ctx.OrigenesFondos.AsNoTracking().SingleAsync(o => o.Id == origenId);
            var egresos = await ctx.PagosProveedor.AsNoTracking().CountAsync(p => p.OrigenFondosId == origenId)
                          + await ctx.AnticiposProveedor.AsNoTracking().CountAsync(a => a.OrigenFondosId == origenId);
            Assert.False(origen.IsDeleted && egresos > 0, $"ronda {ronda}: origen eliminado con {egresos} egreso(s) ({string.Join(" / ", res)})");
            Assert.False(res[0] == "OK" && res[1] == "OK", $"ronda {ronda}: ambos terminaron OK ({string.Join(" / ", res)})");
        }
    }
}
