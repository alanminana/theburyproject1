using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// QA-PRD-P1 B4: idempotencia REAL de los ajustes de stock bajo concurrencia. A diferencia de los tests HTTP
/// (una sola conexión SQLite en memoria), acá cada solicitud usa su propio DbContext y su propia conexión
/// sobre un archivo SQLite en modo WAL, de modo que las escrituras compiten de verdad.
/// </summary>
public sealed class QaPrdP1IdempotenciaConcurrenciaTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"qa-prd-p1-b4-{Guid.NewGuid():N}.db");
    private const int ProductoId = 9301;

    public QaPrdP1IdempotenciaConcurrenciaTests()
    {
        using var ctx = NuevoContexto();
        ctx.Database.EnsureCreated();
        ctx.Database.CloseConnection();
        using (var wal = new SqliteConnection($"Data Source={_dbPath}"))
        {
            wal.Open();
            using var cmd = wal.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            var modo = (string?)cmd.ExecuteScalar();
            Assert.Equal("wal", modo);
        }

        ctx.Categorias.Add(new Categoria { Id = 9301, Codigo = "C9301", Nombre = "Cat", Activo = true });
        ctx.Marcas.Add(new Marca { Id = 9301, Codigo = "M9301", Nombre = "Marca", Activo = true });
        ctx.SaveChanges();
        ctx.Productos.Add(new Producto
        {
            Id = ProductoId, Codigo = "B4-1", Nombre = "Producto B4", CategoriaId = 9301, MarcaId = 9301,
            PrecioCompra = 50m, PrecioVenta = 100m, PorcentajeIVA = 21m, StockActual = 10m, Activo = true
        });
        ctx.SaveChanges();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(f); } catch (IOException) { /* best effort */ }
        }
    }

    private AppDbContext NuevoContexto(params IInterceptor[] interceptores)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath};Default Timeout=30");
        if (interceptores.Length > 0)
            builder.AddInterceptors(interceptores);
        return new AppDbContext(builder.Options);
    }

    private static MovimientoStockService Servicio(AppDbContext ctx) =>
        new(ctx, NullLogger<MovimientoStockService>.Instance);

    private async Task<(int movimientos, decimal stock)> EstadoAsync()
    {
        await using var ctx = NuevoContexto();
        return (await ctx.MovimientosStock.CountAsync(m => m.ProductoId == ProductoId),
                (await ctx.Productos.AsNoTracking().FirstAsync(p => p.Id == ProductoId)).StockActual);
    }

    [Fact]
    public async Task B4_Concurrencia_MismoRequestIdEnParalelo_ConContextosIndependientes_RegistraUnSoloMovimiento()
    {
        var requestId = Guid.NewGuid();
        const int solicitudes = 8;
        using var largada = new ManualResetEventSlim(false);

        var tareas = Enumerable.Range(0, solicitudes).Select(_ => Task.Run(async () =>
        {
            await using var ctx = NuevoContexto();
            largada.Wait();
            try
            {
                var mov = await Servicio(ctx).RegistrarAjusteAsync(
                    ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa", requestId: requestId);
                return (Id: (int?)mov.Id, Error: (string?)null);
            }
            catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
            {
                return (null, ex.Message);
            }
        })).ToList();

        largada.Set();
        var resultados = await Task.WhenAll(tareas);

        var (movimientos, stock) = await EstadoAsync();
        Assert.Equal(1, movimientos);
        Assert.Equal(15m, stock);

        // Todas las solicitudes que no fallaron por contención devuelven el MISMO movimiento (no uno propio).
        var ids = resultados.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).Distinct().ToList();
        Assert.Single(ids);
        Assert.NotEmpty(resultados.Where(r => r.Id.HasValue));
    }

    [Fact]
    public async Task B4_Concurrencia_PerdedorDeLaCarreraRecuperaElMovimientoGanador()
    {
        // Determinista: el "ganador" confirma su movimiento justo antes de que el "perdedor" persista el suyo
        // (hook tras su consulta previa por RequestId), forzando el camino de recuperación y no solo la consulta previa.
        var requestId = Guid.NewGuid();
        var interceptor = new GanadorEnCarreraInterceptor(async () =>
        {
            await using var ctxGanador = NuevoContexto();
            await Servicio(ctxGanador).RegistrarAjusteAsync(
                ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa-ganador", requestId: requestId);
        });

        await using var ctxPerdedor = NuevoContexto(interceptor);
        var devuelto = await Servicio(ctxPerdedor).RegistrarAjusteAsync(
            ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa-perdedor", requestId: requestId);

        Assert.True(interceptor.Disparado, "El ganador debía confirmarse entre la consulta previa y la escritura del perdedor.");
        var (movimientos, stock) = await EstadoAsync();
        Assert.Equal(1, movimientos);
        Assert.Equal(15m, stock);
        Assert.Equal("qa-ganador", devuelto.CreatedBy);
    }

    [Fact]
    public async Task B4_Concurrencia_RequestIdDistintosEnParalelo_NoSeDeduplicanPorContenido()
    {
        // Guarda: mismo producto/tipo/cantidad con RequestId distintos son movimientos legítimos. Bajo contención
        // de SQLite alguno puede fallar con conflicto, pero ninguno se descarta como "duplicado" ni queda stock
        // inconsistente: stock final == 10 + 5 × movimientos confirmados.
        const int solicitudes = 4;
        using var largada = new ManualResetEventSlim(false);

        var tareas = Enumerable.Range(0, solicitudes).Select(_ => Task.Run(async () =>
        {
            await using var ctx = NuevoContexto();
            largada.Wait();
            try
            {
                await Servicio(ctx).RegistrarAjusteAsync(
                    ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa", requestId: Guid.NewGuid());
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
            {
                return false;
            }
        })).ToList();

        largada.Set();
        var exitosas = (await Task.WhenAll(tareas)).Count(ok => ok);

        var (movimientos, stock) = await EstadoAsync();
        Assert.True(exitosas >= 1);
        Assert.Equal(exitosas, movimientos);
        Assert.Equal(10m + 5m * movimientos, stock);
    }

    [Fact]
    public async Task B4_RequestIdReutilizadoEnOtroProducto_SeRechazaEnLugarDeDevolverUnMovimientoAjeno()
    {
        var requestId = Guid.NewGuid();
        await using (var ctx = NuevoContexto())
        {
            ctx.Productos.Add(new Producto
            {
                Id = 9302, Codigo = "B4-2", Nombre = "Otro", CategoriaId = 9301, MarcaId = 9301,
                PrecioCompra = 50m, PrecioVenta = 100m, PorcentajeIVA = 21m, StockActual = 10m, Activo = true
            });
            await ctx.SaveChangesAsync();
            await Servicio(ctx).RegistrarAjusteAsync(9302, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa", requestId: requestId);
        }

        await using var otro = NuevoContexto();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(otro).RegistrarAjusteAsync(ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa", requestId: requestId));

        Assert.Equal((0, 10m), await EstadoAsync());
    }

    [Fact]
    public async Task B4_SinRequestId_DosLlamadasIgualesSiguenSiendoDosMovimientos()
    {
        await using var ctx = NuevoContexto();
        var svc = Servicio(ctx);
        await svc.RegistrarAjusteAsync(ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa");
        await svc.RegistrarAjusteAsync(ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4", "qa");

        Assert.Equal((2, 20m), await EstadoAsync());
    }

    /// <summary>
    /// Ejecuta una acción (el "ganador") justo después de la consulta previa por RequestId del perdedor:
    /// éste ya vio "no existe" pero todavía no abrió su transacción de escritura. (SQLite abre las
    /// transacciones de escritura con BEGIN IMMEDIATE, así que la carrera solo es posible en esa ventana.)
    /// </summary>
    private sealed class GanadorEnCarreraInterceptor : DbCommandInterceptor
    {
        private readonly Func<Task> _ganador;
        public bool Disparado { get; private set; }

        public GanadorEnCarreraInterceptor(Func<Task> ganador) => _ganador = ganador;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Disparado && command.CommandText.Contains("MovimientosStock") && command.CommandText.Contains("RequestId"))
            {
                Disparado = true;
                await _ganador();
            }
            return result;
        }
    }
}
