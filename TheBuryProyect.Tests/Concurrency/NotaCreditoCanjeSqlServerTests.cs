using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// H4-E2 — canje de nota de crédito bajo concurrencia, contra SQL Server real (efímero, mismo gate que el resto del
// harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*"). VentaService y CajaService reales, cada
// operación en su propio scope (= su propio DbContext/conexión/transacción) y lanzadas a la vez.
// El invariante: el saldo de una nota se consume UNA sola vez por venta y nunca más allá de su disponible, y la
// restitución por cancelación ocurre UNA sola vez. Se verifica el ESTADO FINAL en la base.
// ---------------------------------------------------------------------------------------------

public sealed class NotaCreditoCanjeSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_nc";
}

public class NotaCreditoCanjeSqlServerTests : IClassFixture<NotaCreditoCanjeSqlHostFixture>
{
    private readonly NotaCreditoCanjeSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public NotaCreditoCanjeSqlServerTests(NotaCreditoCanjeSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private sealed record Esc(int ClienteId, int ProductoId, int NotaId);

    private async Task<Esc> SembrarAsync(decimal saldoNota)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var cliente = new Cliente
        {
            TipoDocumento = "DNI", NumeroDocumento = sufijo, Nombre = "NC", Apellido = "Conc" + sufijo,
            Telefono = "0000000000", Domicilio = "QA 123", Email = $"{sufijo}@test.com", Activo = true
        };
        var cat = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(cliente, cat, marca);
        await ctx.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 50m, PorcentajeIVA = 21m, StockActual = 100m, Activo = true
        };
        ctx.Productos.Add(producto);
        var origen = new Venta
        {
            Numero = "VO" + sufijo, ClienteId = cliente.Id, Estado = EstadoVenta.Facturada, TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow, Subtotal = saldoNota, Total = saldoNota, AperturaCajaId = _fx.AperturaCajaId
        };
        ctx.Ventas.Add(origen);
        await ctx.SaveChangesAsync();
        var dev = new Devolucion
        {
            NumeroDevolucion = "DV" + sufijo, VentaId = origen.Id, ClienteId = cliente.Id, Motivo = MotivoDevolucion.DefectoFabrica,
            Descripcion = "origen NC", Estado = EstadoDevolucion.Completada, TipoResolucion = TipoResolucionDevolucion.NotaCredito,
            FechaDevolucion = DateTime.UtcNow, TotalDevolucion = saldoNota
        };
        ctx.Devoluciones.Add(dev);
        await ctx.SaveChangesAsync();
        var nc = new NotaCredito
        {
            NumeroNotaCredito = "NC" + sufijo, ClienteId = cliente.Id, DevolucionId = dev.Id, FechaEmision = DateTime.UtcNow,
            MontoTotal = saldoNota, Estado = EstadoNotaCredito.Vigente, FechaVencimiento = DateTime.UtcNow.AddYears(1)
        };
        ctx.NotasCredito.Add(nc);
        await ctx.SaveChangesAsync();
        return new Esc(cliente.Id, producto.Id, nc.Id);
    }

    private async Task<int> SembrarVentaAsync(Esc e, decimal total = 100m)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var venta = new Venta
        {
            Numero = "VNC" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), ClienteId = e.ClienteId,
            Estado = EstadoVenta.Cotizacion, TipoPago = TipoPago.NotaCredito, NotaCreditoId = e.NotaId,
            FechaVenta = DateTime.UtcNow, Subtotal = total, Total = total,
            Detalles = new List<VentaDetalle>
            {
                new() { ProductoId = e.ProductoId, Cantidad = 2, PrecioUnitario = total / 2, Subtotal = total,
                        CostoUnitarioAlMomento = 17.5m, CostoTotalAlMomento = 35m }
            }
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();
        return venta.Id;
    }

    private async Task<string?> ConfirmarAsync(int ventaId)
    {
        try
        {
            using var scope = _fx.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IVentaService>().ConfirmarVentaAsync(ventaId);
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    private async Task<string?> CancelarAsync(int ventaId)
    {
        try
        {
            using var scope = _fx.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IVentaService>().CancelarVentaAsync(ventaId, "conc");
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    private async Task<(NotaCredito Nota, int Usos, int UsosRevertidos)> EstadoNotaAsync(int notaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nota = await ctx.NotasCredito.AsNoTracking().SingleAsync(n => n.Id == notaId);
        var usos = await ctx.NotasCreditoUso.AsNoTracking().Where(u => u.NotaCreditoId == notaId).ToListAsync();
        return (nota, usos.Count, usos.Count(u => u.FechaReversion != null));
    }

    private async Task<(int Estado, int Movimientos)> EstadoVentaAsync(int ventaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var estado = await ctx.Ventas.AsNoTracking().Where(v => v.Id == ventaId).Select(v => (int)v.Estado).SingleAsync();
        var movs = await ctx.MovimientosCaja.AsNoTracking().CountAsync(m => m.VentaId == ventaId);
        return (estado, movs);
    }

    private static async Task<string?[]> EnParaleloAsync(params Func<Task<string?>>[] operaciones)
    {
        using var arranque = new ManualResetEventSlim(false);
        var tareas = operaciones.Select(op => Task.Run(async () => { arranque.Wait(); return await op(); })).ToArray();
        arranque.Set();
        return await Task.WhenAll(tareas);
    }

    [SqlServerConcFact(DisplayName = "H4-E2 [NC-C1] dos ventas confirman a la vez con una nota que cubre solo una: aplica una sola")]
    public async Task C1_DosVentasMismaNota_AplicaUna()
    {
        var e = await SembrarAsync(saldoNota: 100m);
        var v1 = await SembrarVentaAsync(e);
        var v2 = await SembrarVentaAsync(e);

        var r = await EnParaleloAsync(() => ConfirmarAsync(v1), () => ConfirmarAsync(v2));
        _out.WriteLine(string.Join(" | ", r.Select(x => x ?? "OK")));

        Assert.Equal(1, r.Count(x => x == null));
        Assert.Equal(1, r.Count(x => x != null));
        var (nota, usos, _) = await EstadoNotaAsync(e.NotaId);
        Assert.Equal(100m, nota.MontoUtilizado);
        Assert.Equal(EstadoNotaCredito.UtilizadaTotalmente, nota.Estado);
        Assert.Equal(1, usos);

        var s1 = await EstadoVentaAsync(v1);
        var s2 = await EstadoVentaAsync(v2);
        // La ganadora quedó Confirmada con su movimiento; la perdedora quedó intacta (Cotización, sin movimiento).
        Assert.Equal(1, new[] { s1, s2 }.Count(s => s.Estado == (int)EstadoVenta.Confirmada && s.Movimientos == 1));
        Assert.Equal(1, new[] { s1, s2 }.Count(s => s.Estado == (int)EstadoVenta.Cotizacion && s.Movimientos == 0));
    }

    [SqlServerConcFact(DisplayName = "H4-E2 [NC-C2] cuatro ventas a la vez con saldo para dos: confirman exactamente dos y nunca se excede el saldo")]
    public async Task C2_CuatroVentasSaldoParaDos_ExactamenteDos()
    {
        var e = await SembrarAsync(saldoNota: 250m);       // alcanza para 2 ventas de 100 (queda 50)
        var ids = new List<int>();
        for (var i = 0; i < 4; i++) ids.Add(await SembrarVentaAsync(e));

        var r = await EnParaleloAsync(ids.Select<int, Func<Task<string?>>>(id => () => ConfirmarAsync(id)).ToArray());
        _out.WriteLine(string.Join(" | ", r.Select(x => x ?? "OK")));

        Assert.Equal(2, r.Count(x => x == null));
        var (nota, usos, _) = await EstadoNotaAsync(e.NotaId);
        Assert.Equal(200m, nota.MontoUtilizado);
        Assert.Equal(50m, nota.MontoDisponible);
        Assert.Equal(EstadoNotaCredito.UtilizadaParcialmente, nota.Estado);
        Assert.Equal(2, usos);
    }

    [SqlServerConcFact(DisplayName = "H4-E2 [NC-C3] confirmar la misma venta dos veces a la vez: una sola vez descuenta")]
    public async Task C3_MismaVentaDosConfirmaciones_UnSoloDescuento()
    {
        var e = await SembrarAsync(saldoNota: 500m);
        var v = await SembrarVentaAsync(e);

        var r = await EnParaleloAsync(() => ConfirmarAsync(v), () => ConfirmarAsync(v));
        _out.WriteLine(string.Join(" | ", r.Select(x => x ?? "OK")));

        Assert.Equal(1, r.Count(x => x == null));
        var (nota, usos, _) = await EstadoNotaAsync(e.NotaId);
        Assert.Equal(100m, nota.MontoUtilizado);
        Assert.Equal(1, usos);
        Assert.Equal(1, (await EstadoVentaAsync(v)).Movimientos);
    }

    [SqlServerConcFact(DisplayName = "H4-E2 [NC-C4] cancelación simultánea (o repetida) de la venta: el saldo se restituye una sola vez")]
    public async Task C4_CancelarEnParalelo_RestituyeUnaVez()
    {
        var e = await SembrarAsync(saldoNota: 300m);
        var v = await SembrarVentaAsync(e);
        Assert.Null(await ConfirmarAsync(v));
        Assert.Equal(100m, (await EstadoNotaAsync(e.NotaId)).Nota.MontoUtilizado);

        var r = await EnParaleloAsync(() => CancelarAsync(v), () => CancelarAsync(v));
        _out.WriteLine(string.Join(" | ", r.Select(x => x ?? "OK")));

        Assert.Equal(1, r.Count(x => x == null));
        var (nota, usos, revertidos) = await EstadoNotaAsync(e.NotaId);
        Assert.Equal(0m, nota.MontoUtilizado);              // ni 0 por doble resta a negativo, ni 100 sin restituir
        Assert.Equal(EstadoNotaCredito.Vigente, nota.Estado);
        Assert.Equal(1, usos);
        Assert.Equal(1, revertidos);
    }

    [SqlServerConcFact(DisplayName = "H4-E2 [NC-C5] cancelar la ganadora mientras otra venta espera la nota: el saldo vuelve y la otra puede usarlo")]
    public async Task C5_CancelarLiberaSaldoParaOtraVenta()
    {
        var e = await SembrarAsync(saldoNota: 100m);
        var v1 = await SembrarVentaAsync(e);
        var v2 = await SembrarVentaAsync(e);
        Assert.Null(await ConfirmarAsync(v1));
        Assert.NotNull(await ConfirmarAsync(v2));            // sin saldo

        Assert.Null(await CancelarAsync(v1));
        Assert.Null(await ConfirmarAsync(v2));               // ahora sí

        var (nota, usos, revertidos) = await EstadoNotaAsync(e.NotaId);
        Assert.Equal(100m, nota.MontoUtilizado);
        Assert.Equal(EstadoNotaCredito.UtilizadaTotalmente, nota.Estado);
        Assert.Equal(2, usos);
        Assert.Equal(1, revertidos);
    }
}
