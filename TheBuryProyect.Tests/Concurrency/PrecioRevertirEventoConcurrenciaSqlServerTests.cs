using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// H11: reversion de cambios de precio del Catalogo contra SQL Server real. Dos reversiones simultaneas del mismo evento
// (doble clic, dos operadores) deben dar un unico ganador: el precio vuelve al original una sola vez, queda un solo evento
// de reversion y la perdedora se rechaza sin efectos parciales.
//
// Mismo gate que los demas harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catalogo "qa_fin_conc*".
// ---------------------------------------------------------------------------------------------

public sealed class PrecioRevertirEventoSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_prec";
}

public class PrecioRevertirEventoConcurrenciaSqlServerTests : IClassFixture<PrecioRevertirEventoSqlHostFixture>
{
    private const decimal PrecioOriginal = 100m;

    private readonly PrecioRevertirEventoSqlHostFixture _fx;

    public PrecioRevertirEventoConcurrenciaSqlServerTests(PrecioRevertirEventoSqlHostFixture fx) => _fx = fx;

    private async Task<int> SembrarProductoAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var categoria = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(categoria, marca);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 50m, PrecioVenta = PrecioOriginal, PorcentajeIVA = 21m, StockActual = 0m, Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto.Id;
    }

    private async Task<int> AplicarAumentoAsync(int productoId, decimal porcentaje)
    {
        using var scope = _fx.Services.CreateScope();
        var precios = scope.ServiceProvider.GetRequiredService<IPrecioService>();
        var resultado = await precios.AplicarCambioPrecioDirectoAsync(new AplicarCambioPrecioDirectoViewModel
        {
            Alcance = "seleccionados",
            ProductoIdsText = productoId.ToString(),
            ValorPorcentaje = porcentaje,
            TipoCambio = "porcentaje",
            Motivo = "H11 concurrencia"
        });
        Assert.True(resultado.Exitoso, resultado.Mensaje);
        Assert.NotNull(resultado.CambioPrecioEventoId);
        return resultado.CambioPrecioEventoId!.Value;
    }

    private async Task<bool> RevertirAsync(int eventoId)
    {
        using var scope = _fx.Services.CreateScope();
        var precios = scope.ServiceProvider.GetRequiredService<IPrecioService>();
        var (exitoso, _, _) = await precios.RevertirCambioPrecioEventoAsync(eventoId);
        return exitoso;
    }

    [SqlServerConcFact(DisplayName = "H11 [PRECIO-REV-01] dos reversiones simultaneas del mismo cambio de precio: una sola gana")]
    public async Task Dos_Reversiones_Simultaneas_Solo_Una_Gana()
    {
        var productoId = await SembrarProductoAsync();
        var eventoId = await AplicarAumentoAsync(productoId, 10m);

        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(110m, await ctx.Productos.AsNoTracking().Where(p => p.Id == productoId).Select(p => p.PrecioVenta).SingleAsync());
        }

        var resultados = await Task.WhenAll(RevertirAsync(eventoId), RevertirAsync(eventoId));

        Assert.Equal(1, resultados.Count(r => r));

        using var scopeFinal = _fx.Services.CreateScope();
        var db = scopeFinal.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PrecioOriginal, await db.Productos.AsNoTracking().Where(p => p.Id == productoId).Select(p => p.PrecioVenta).SingleAsync());
        Assert.NotNull(await db.CambioPrecioEventos.AsNoTracking().Where(e => e.Id == eventoId).Select(e => e.RevertidoEn).SingleAsync());
        Assert.Equal(1, await db.CambioPrecioEventos.AsNoTracking().CountAsync(e => e.Alcance == "reversion" && e.Motivo == "Reversion evento #" + eventoId));
    }

    [SqlServerConcFact(DisplayName = "H11 [PRECIO-REV-02] revertir un cambio cuyo precio fue modificado despues se rechaza y no pisa el precio nuevo")]
    public async Task Revertir_Con_Precio_Modificado_Despues_Se_Rechaza()
    {
        var productoId = await SembrarProductoAsync();
        var eventoId = await AplicarAumentoAsync(productoId, 10m);
        await AplicarAumentoAsync(productoId, 5m); // 110 -> 115,5: el producto cambio despues del evento

        Assert.False(await RevertirAsync(eventoId));

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(115.5m, await ctx.Productos.AsNoTracking().Where(p => p.Id == productoId).Select(p => p.PrecioVenta).SingleAsync());
        Assert.Null(await ctx.CambioPrecioEventos.AsNoTracking().Where(e => e.Id == eventoId).Select(e => e.RevertidoEn).SingleAsync());
    }
}
