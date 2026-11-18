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
// D-03 / AC-03-3: cuentas por pagar contra SQL Server real. El lock por proveedor (UPDLOCK/HOLDLOCK sobre la fila
// de Proveedores) solo se puede probar aquí: SQLite serializa todo con un único escritor.
//
// Mismo gate que los demás harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*".
// El fixture crea y elimina su propio catálogo ("<base>_cxp") y aplica TODAS las migraciones (incluye AddCuentasPorPagar).
// ---------------------------------------------------------------------------------------------

public sealed class CuentaPorPagarSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_cxp";
}

public class CuentaPorPagarSqlServerTests : IClassFixture<CuentaPorPagarSqlHostFixture>
{
    private readonly CuentaPorPagarSqlHostFixture _fx;

    public CuentaPorPagarSqlServerTests(CuentaPorPagarSqlHostFixture fx) => _fx = fx;

    private int _origenId;

    /// <summary>D-03 Tesorería: todo pago nuevo exige un origen de fondos activo (se crea uno la primera vez).</summary>
    private async Task AsegurarOrigenAsync(AppDbContext ctx)
    {
        if (_origenId != 0)
            return;
        var origen = await ctx.OrigenesFondos.FirstOrDefaultAsync(o => o.Codigo == "CXPCONC");
        if (origen is null)
        {
            origen = new OrigenFondos { Codigo = "CXPCONC", Nombre = "Origen concurrencia", Tipo = TipoOrigenFondos.CuentaBancaria };
            ctx.OrigenesFondos.Add(origen);
            await ctx.SaveChangesAsync();
        }
        _origenId = origen.Id;
    }

    private async Task<(int ProveedorId, int FacturaId)> SembrarFacturaAsync(decimal total)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AsegurarOrigenAsync(ctx);
        var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var proveedor = new Proveedor { Cuit = sufijo + "0", RazonSocial = "Prov-" + sufijo, Activo = true };
        ctx.Proveedores.Add(proveedor);
        await ctx.SaveChangesAsync();

        var r = await svc.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = proveedor.Id, Tipo = TipoComprobanteProveedor.Factura, NumeroComprobante = "F-" + sufijo,
            FechaEmision = DateTime.UtcNow.Date, ImporteNeto = total, ImporteIva = 0m, ImporteTotal = total
        }, "conc");
        Assert.True(r.Ok, r.Error);
        return (proveedor.Id, r.Id!.Value);
    }

    private async Task<int> SembrarNotaCreditoAsync(int proveedorId, decimal total)
    {
        using var scope = _fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
        var r = await svc.RegistrarComprobanteAsync(new ComprobanteProveedorRequest
        {
            ProveedorId = proveedorId, Tipo = TipoComprobanteProveedor.NotaCredito,
            NumeroComprobante = "NC-" + Guid.NewGuid().ToString("N")[..10], FechaEmision = DateTime.UtcNow.Date,
            ImporteNeto = total, ImporteIva = 0m, ImporteTotal = total
        }, "conc");
        Assert.True(r.Ok, r.Error);
        return r.Id!.Value;
    }

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

    private PagoProveedorRequest Pago(int proveedorId, int facturaId, decimal monto, string? clave = null) => new()
    {
        ProveedorId = proveedorId, Fecha = DateTime.UtcNow.Date, Medio = MedioPagoProveedor.Transferencia, Monto = monto,
        OrigenFondosId = _origenId, ClaveIdempotencia = clave,
        Imputaciones = new List<ImputacionRequest> { new() { ComprobanteId = facturaId, Monto = monto } }
    };

    private async Task<(decimal Saldo, EstadoComprobanteProveedor Estado, decimal ImputadoVigente, int PagosVigentes)> LeerAsync(int facturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = await ctx.ComprobantesProveedor.AsNoTracking().SingleAsync(x => x.Id == facturaId);
        var pagos = await ctx.PagoProveedorImputaciones.AsNoTracking()
            .Where(i => i.ComprobanteProveedorId == facturaId && !i.Pago.Anulado).Select(i => i.Monto).ToListAsync();
        var ncs = await ctx.ImputacionesNotaCredito.AsNoTracking()
            .Where(i => i.ComprobanteId == facturaId && !i.Anulada).Select(i => i.Monto).ToListAsync();
        return (c.SaldoPendiente, c.Estado, pagos.Sum() + ncs.Sum(), pagos.Count);
    }

    [SqlServerConcFact(DisplayName = "D-03 [AC-03-3] dos pagos simultaneos de 700 sobre una factura de 1000: solo uno entra, sin sobreimputar")]
    public async Task Dos_Pagos_Simultaneos_No_Sobreimputan()
    {
        for (var ronda = 0; ronda < 5; ronda++)
        {
            var (prov, f) = await SembrarFacturaAsync(1000m);

            var resultados = await EnParaleloAsync(
                s => s.RegistrarPagoAsync(Pago(prov, f, 700m), "u1"),
                s => s.RegistrarPagoAsync(Pago(prov, f, 700m), "u2"));

            Assert.Equal(1, resultados.Count(r => r == "OK"));
            Assert.DoesNotContain(resultados, r => r.StartsWith("EXCEPCION"));
            var final = await LeerAsync(f);
            Assert.Equal(300m, final.Saldo);
            Assert.Equal(700m, final.ImputadoVigente);
            Assert.Equal(1, final.PagosVigentes);
            Assert.Equal(EstadoComprobanteProveedor.ParcialmentePagado, final.Estado);
        }
    }

    [SqlServerConcFact(DisplayName = "D-03 [AC-03-3] ocho pagos simultaneos de 200 sobre una factura de 1000: exactamente cinco entran y el saldo queda en 0")]
    public async Task Ocho_Pagos_Simultaneos_Cubren_Exactamente_El_Saldo()
    {
        var (prov, f) = await SembrarFacturaAsync(1000m);

        var resultados = await EnParaleloAsync(Enumerable.Range(0, 8)
            .Select<int, Func<ICuentaPorPagarService, Task<ResultadoCxp>>>(i => s => s.RegistrarPagoAsync(Pago(prov, f, 200m), "u" + i))
            .ToArray());

        Assert.Equal(5, resultados.Count(r => r == "OK"));
        Assert.Equal(3, resultados.Count(r => r.StartsWith("RECHAZADA")));
        Assert.DoesNotContain(resultados, r => r.StartsWith("EXCEPCION"));
        var final = await LeerAsync(f);
        Assert.Equal(0m, final.Saldo);
        Assert.Equal(1000m, final.ImputadoVigente);
        Assert.Equal(EstadoComprobanteProveedor.Pagado, final.Estado);
    }

    [SqlServerConcFact(DisplayName = "D-03 [AC-03-3] un pago y una nota de credito simultaneos sobre la misma factura no la sobreimputan")]
    public async Task Pago_Y_Nota_De_Credito_Simultaneos_No_Sobreimputan()
    {
        var (prov, f) = await SembrarFacturaAsync(1000m);
        var nc = await SembrarNotaCreditoAsync(prov, 600m);

        var resultados = await EnParaleloAsync(
            s => s.RegistrarPagoAsync(Pago(prov, f, 700m), "u1"),
            s => s.ImputarNotaCreditoAsync(nc, new[] { new ImputacionRequest { ComprobanteId = f, Monto = 600m } }, null, "u2"));

        Assert.Equal(1, resultados.Count(r => r == "OK"));
        Assert.DoesNotContain(resultados, r => r.StartsWith("EXCEPCION"));
        var final = await LeerAsync(f);
        Assert.True(final.Saldo >= 0m);
        Assert.Equal(1000m - final.ImputadoVigente, final.Saldo);
    }

    [SqlServerConcFact(DisplayName = "D-03 [AC-03-3] el mismo formulario enviado dos veces a la vez registra un solo pago")]
    public async Task Mismo_Formulario_Simultaneo_Registra_Un_Solo_Pago()
    {
        var (prov, f) = await SembrarFacturaAsync(1000m);
        var clave = Guid.NewGuid().ToString("N");

        var resultados = await EnParaleloAsync(
            s => s.RegistrarPagoAsync(Pago(prov, f, 400m, clave), "u1"),
            s => s.RegistrarPagoAsync(Pago(prov, f, 400m, clave), "u1"),
            s => s.RegistrarPagoAsync(Pago(prov, f, 400m, clave), "u1"));

        Assert.All(resultados, r => Assert.Equal("OK", r));
        var final = await LeerAsync(f);
        Assert.Equal(1, final.PagosVigentes);
        Assert.Equal(600m, final.Saldo);
    }

    [SqlServerConcFact(DisplayName = "D-03 [AC-03-3] anular el mismo pago dos veces a la vez restituye el saldo una sola vez")]
    public async Task Doble_Anulacion_Simultanea_Restituye_Una_Sola_Vez()
    {
        var (prov, f) = await SembrarFacturaAsync(1000m);
        int pagoId;
        using (var scope = _fx.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ICuentaPorPagarService>();
            pagoId = (await svc.RegistrarPagoAsync(Pago(prov, f, 1000m), "u")).Id!.Value;
        }

        var resultados = await EnParaleloAsync(
            s => s.AnularPagoAsync(pagoId, "doble clic", "u1"),
            s => s.AnularPagoAsync(pagoId, "doble clic", "u2"));

        Assert.Equal(1, resultados.Count(r => r == "OK"));
        var final = await LeerAsync(f);
        Assert.Equal(1000m, final.Saldo);
        Assert.Equal(EstadoComprobanteProveedor.Pendiente, final.Estado);
        Assert.Equal(0, final.PagosVigentes);
    }

    [SqlServerConcFact(DisplayName = "D-03 [AC-03-3] anular un comprobante a la vez que se le paga no deja un pago sobre un comprobante anulado")]
    public async Task Anular_Comprobante_Y_Pagarlo_Simultaneos_Son_Consistentes()
    {
        var (prov, f) = await SembrarFacturaAsync(1000m);

        var resultados = await EnParaleloAsync(
            s => s.RegistrarPagoAsync(Pago(prov, f, 300m), "u1"),
            s => s.AnularComprobanteAsync(f, "carga errónea", "u2"));

        Assert.Equal(1, resultados.Count(r => r == "OK"));
        var final = await LeerAsync(f);
        // O quedó pagado en parte y vigente, o quedó anulado sin pagos vigentes: nunca ambas cosas.
        if (final.Estado == EstadoComprobanteProveedor.Anulado)
            Assert.Equal(0, final.PagosVigentes);
        else
            Assert.Equal(1, final.PagosVigentes);
    }
}
