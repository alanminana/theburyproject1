using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// H3-Q1: recepcion de ordenes de compra contra SQL Server real (el RowVersion de SQLite no se regenera al actualizar,
// por eso la proteccion contra formularios viejos solo se puede probar aca).
//
// Mismo gate que los demas harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catalogo "qa_fin_conc*".
// ---------------------------------------------------------------------------------------------

public sealed class OrdenCompraRecepcionSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_oc";
}

public class OrdenCompraRecepcionSqlServerTests : IClassFixture<OrdenCompraRecepcionSqlHostFixture>
{
    private readonly OrdenCompraRecepcionSqlHostFixture _fx;

    public OrdenCompraRecepcionSqlServerTests(OrdenCompraRecepcionSqlHostFixture fx) => _fx = fx;

    private sealed record Semilla(int OrdenId, int DetalleId, int ProductoId);

    private async Task<Semilla> SembrarOrdenConfirmadaAsync(int cantidad)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var categoria = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        var proveedor = new Proveedor { Cuit = sufijo + "0", RazonSocial = "Prov-" + sufijo, Activo = true };
        ctx.AddRange(categoria, marca, proveedor);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 15m, PorcentajeIVA = 21m, StockActual = 0m, Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();

        var orden = new OrdenCompra
        {
            Numero = "OC-H3Q1-" + sufijo, ProveedorId = proveedor.Id, Estado = EstadoOrdenCompra.Confirmada,
            FechaEmision = DateTime.UtcNow,
            Detalles = new List<OrdenCompraDetalle>
            {
                new() { ProductoId = producto.Id, Cantidad = cantidad, PrecioUnitario = 10m, Subtotal = cantidad * 10m }
            }
        };
        ctx.OrdenesCompra.Add(orden);
        await ctx.SaveChangesAsync();
        return new Semilla(orden.Id, orden.Detalles.Single().Id, producto.Id);
    }

    private async Task<byte[]> LeerRowVersionAsync(int ordenId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.OrdenesCompra.AsNoTracking().Where(o => o.Id == ordenId).Select(o => o.RowVersion).SingleAsync();
    }

    private async Task<string> RecepcionarAsync(Semilla s, byte[] rowVersion, int cantidad)
    {
        try
        {
            using var scope = _fx.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IOrdenCompraService>();
            await svc.RecepcionarAsync(s.OrdenId, rowVersion, new List<RecepcionDetalleViewModel>
            {
                new() { DetalleId = s.DetalleId, ProductoId = s.ProductoId, CantidadARecepcionar = cantidad }
            });
            return "OK";
        }
        catch (InvalidOperationException ex)
        {
            return "RECHAZADA: " + ex.Message;
        }
    }

    private async Task<(decimal stock, int movimientos, int recibido, EstadoOrdenCompra estado)> EstadoFinalAsync(Semilla s)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stock = await ctx.Productos.AsNoTracking().Where(p => p.Id == s.ProductoId).Select(p => p.StockActual).SingleAsync();
        var movs = await ctx.MovimientosStock.AsNoTracking().CountAsync(m => m.OrdenCompraId == s.OrdenId);
        var recibido = await ctx.OrdenCompraDetalles.AsNoTracking().Where(d => d.Id == s.DetalleId).Select(d => d.CantidadRecibida).SingleAsync();
        var estado = await ctx.OrdenesCompra.AsNoTracking().Where(o => o.Id == s.OrdenId).Select(o => o.Estado).SingleAsync();
        return (stock, movs, recibido, estado);
    }

    [SqlServerConcFact(DisplayName = "H3-Q1 [OC-REC-01] el mismo formulario reenviado sobre una orden ya En transito no ingresa la mercaderia dos veces")]
    public async Task Reenvio_Del_Mismo_Formulario_En_Orden_EnTransito_No_Duplica_El_Ingreso()
    {
        var s = await SembrarOrdenConfirmadaAsync(cantidad: 10);

        // 1ª recepción parcial (Confirmada -> EnTransito): el formulario trae el token de la orden Confirmada.
        var rv0 = await LeerRowVersionAsync(s.OrdenId);
        Assert.Equal("OK", await RecepcionarAsync(s, rv0, 3));

        // 2ª recepción parcial con el token fresco (la orden ya esta En transito): legitima.
        var rv1 = await LeerRowVersionAsync(s.OrdenId);
        Assert.Equal("OK", await RecepcionarAsync(s, rv1, 3));

        // Reenvio del MISMO formulario de la 2ª (doble clic / pestaña vieja): debe rechazarse.
        var reenvio = await RecepcionarAsync(s, rv1, 3);
        Assert.StartsWith("RECHAZADA", reenvio);

        var final = await EstadoFinalAsync(s);
        Assert.Equal(6m, final.stock);
        Assert.Equal(2, final.movimientos);
        Assert.Equal(6, final.recibido);
        Assert.Equal(EstadoOrdenCompra.EnTransito, final.estado);
    }

    [SqlServerConcFact(DisplayName = "H3-Q1 [OC-REC-02] dos recepciones simultaneas de la misma orden: una sola gana y el stock no se excede")]
    public async Task Dos_Recepciones_Simultaneas_Solo_Una_Gana()
    {
        var s = await SembrarOrdenConfirmadaAsync(cantidad: 10);
        // Deja la orden En transito para ejercitar el caso en que solo cambian las lineas.
        Assert.Equal("OK", await RecepcionarAsync(s, await LeerRowVersionAsync(s.OrdenId), 2));

        var rv = await LeerRowVersionAsync(s.OrdenId);
        var resultados = await Task.WhenAll(RecepcionarAsync(s, rv, 6), RecepcionarAsync(s, rv, 6));

        Assert.Equal(1, resultados.Count(r => r == "OK"));
        var final = await EstadoFinalAsync(s);
        Assert.Equal(8m, final.stock);
        Assert.Equal(8, final.recibido);
        Assert.Equal(2, final.movimientos);
    }
}
