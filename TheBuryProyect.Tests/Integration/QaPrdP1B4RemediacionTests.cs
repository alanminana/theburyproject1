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
/// QA-PRD-P1 B4, remediación de la auditoría: la idempotencia compara la INTENCIÓN completa (no solo producto/tipo),
/// el resultado incierto de un commit no se repite a ciegas y la recuperación no desvincula todo el ChangeTracker.
/// Cada solicitud usa su propio DbContext/conexión sobre un SQLite en archivo (WAL), como en
/// <see cref="QaPrdP1IdempotenciaConcurrenciaTests"/>.
/// </summary>
public sealed class QaPrdP1B4RemediacionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"qa-prd-p1-b4r-{Guid.NewGuid():N}.db");
    private const int ProductoId = 9401;

    public QaPrdP1B4RemediacionTests()
    {
        using var ctx = NuevoContexto();
        ctx.Database.EnsureCreated();
        ctx.Database.CloseConnection();
        using (var wal = new SqliteConnection($"Data Source={_dbPath}"))
        {
            wal.Open();
            using var cmd = wal.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            Assert.Equal("wal", (string?)cmd.ExecuteScalar());
        }

        ctx.Categorias.Add(new Categoria { Id = 9401, Codigo = "C9401", Nombre = "Cat", Activo = true });
        ctx.Marcas.Add(new Marca { Id = 9401, Codigo = "M9401", Nombre = "Marca", Activo = true });
        ctx.SaveChanges();
        ctx.Productos.Add(new Producto
        {
            Id = ProductoId, Codigo = "B4R-1", Nombre = "Producto B4R", CategoriaId = 9401, MarcaId = 9401,
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

    private Task<MovimientoStock> EntradaAsync(
        Guid? requestId, decimal cantidad = 5m, string motivo = "Reposición B4R", string? referencia = null,
        int? ordenCompraId = null, TipoMovimiento tipo = TipoMovimiento.Entrada, params IInterceptor[] interceptores)
    {
        return Interna();
        async Task<MovimientoStock> Interna()
        {
            await using var ctx = NuevoContexto(interceptores);
            return await Servicio(ctx).RegistrarAjusteAsync(
                ProductoId, tipo, cantidad, referencia, motivo, "qa", ordenCompraId, requestId);
        }
    }

    // ── Reutilización del RequestId con otra intención ────────────────────

    [Fact]
    public async Task B4R_MismoRequestIdConOtraCantidad_SeRechazaYNoCambiaNada()
    {
        var id = Guid.NewGuid();
        await EntradaAsync(id, cantidad: 5m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => EntradaAsync(id, cantidad: 7m));

        Assert.Contains("otra operación", ex.Message);
        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_MismoRequestIdConOtroMotivo_SeRechaza()
    {
        var id = Guid.NewGuid();
        await EntradaAsync(id, motivo: "Reposición B4R");

        await Assert.ThrowsAsync<InvalidOperationException>(() => EntradaAsync(id, motivo: "Otro motivo"));

        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_MismoRequestIdConOtraReferenciaOOrdenDeCompra_SeRechaza()
    {
        var id = Guid.NewGuid();
        await EntradaAsync(id, referencia: "REF-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => EntradaAsync(id, referencia: "REF-2"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => EntradaAsync(id, referencia: "REF-1", ordenCompraId: 99));

        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_MismoRequestIdConOtroTipo_SeRechaza()
    {
        var id = Guid.NewGuid();
        await EntradaAsync(id, cantidad: 5m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EntradaAsync(id, cantidad: 5m, tipo: TipoMovimiento.Salida));

        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_MismaIntencion_DevuelveElMismoMovimiento_ToleraEspaciosYNulos()
    {
        var id = Guid.NewGuid();
        var primero = await EntradaAsync(id, motivo: "Reposición B4R", referencia: null);
        var repetido = await EntradaAsync(id, motivo: "  Reposición B4R ", referencia: "");

        Assert.Equal(primero.Id, repetido.Id);
        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_AjusteAbsoluto_MismaIntencionEsIdempotenteYOtroValorSeRechaza()
    {
        // Para Ajuste el movimiento guarda el delta; la huella compara contra el stock absoluto solicitado.
        var id = Guid.NewGuid();
        var primero = await EntradaAsync(id, cantidad: 20m, tipo: TipoMovimiento.Ajuste);
        var repetido = await EntradaAsync(id, cantidad: 20m, tipo: TipoMovimiento.Ajuste);

        Assert.Equal(primero.Id, repetido.Id);
        Assert.Equal((1, 20m), await EstadoAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EntradaAsync(id, cantidad: 25m, tipo: TipoMovimiento.Ajuste));
        Assert.Equal((1, 20m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_SolicitudesLegitimasDistintas_ConRequestIdDistinto_NoSeConfundenConDuplicados()
    {
        await EntradaAsync(Guid.NewGuid(), cantidad: 5m);
        await EntradaAsync(Guid.NewGuid(), cantidad: 5m);
        await EntradaAsync(Guid.NewGuid(), cantidad: 3m, motivo: "Otro");

        Assert.Equal((3, 23m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_MovimientosHistoricosSinRequestId_NoInterfierenConLaIdempotencia()
    {
        await EntradaAsync(requestId: null);
        await EntradaAsync(requestId: null);
        var id = Guid.NewGuid();
        await EntradaAsync(id);
        await EntradaAsync(id);

        Assert.Equal((3, 25m), await EstadoAsync());
    }

    // ── Carrera: el perdedor con otros datos no recibe éxito ajeno ────────

    [Fact]
    public async Task B4R_Carrera_PerdedorConOtrosDatos_SeRechazaEnLugarDeDevolverElGanador()
    {
        var id = Guid.NewGuid();
        var interceptor = new GanadorEnCarreraInterceptor(() => EntradaAsync(id, cantidad: 5m));

        await using var ctxPerdedor = NuevoContexto(interceptor);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(ctxPerdedor).RegistrarAjusteAsync(
                ProductoId, TipoMovimiento.Entrada, 9m, null, "Reposición B4R", "qa-perdedor", requestId: id));

        Assert.True(interceptor.Disparado);
        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_Carrera_LaRecuperacionNoDesvinculaEntidadesAjenasDelContexto()
    {
        var id = Guid.NewGuid();
        var interceptor = new GanadorEnCarreraInterceptor(() => EntradaAsync(id, cantidad: 5m));

        await using var ctx = NuevoContexto(interceptor);
        var marcaAjena = await ctx.Marcas.FirstAsync(m => m.Id == 9401); // entidad rastreada, no relacionada con el intento
        Assert.Equal(EntityState.Unchanged, ctx.Entry(marcaAjena).State);

        var devuelto = await Servicio(ctx).RegistrarAjusteAsync(
            ProductoId, TipoMovimiento.Entrada, 5m, null, "Reposición B4R", "qa-perdedor", requestId: id);

        Assert.True(interceptor.Disparado);
        Assert.NotNull(devuelto);
        Assert.Equal(EntityState.Unchanged, ctx.Entry(marcaAjena).State);
        Assert.Equal((1, 15m), await EstadoAsync());
    }

    // ── Resultado incierto (timeout / corte durante el commit) ────────────

    [Fact]
    public async Task B4R_TimeoutDespuesDelCommit_ConfirmaElMovimientoSinDuplicarlo()
    {
        var id = Guid.NewGuid();

        // El commit SÍ se aplicó pero el cliente recibe un timeout: la verificación lo encuentra y lo devuelve.
        var devuelto = await EntradaAsync(id, interceptores: new FalloEnCommitInterceptor(despuesDeCommit: true));

        Assert.NotNull(devuelto);
        Assert.Equal((1, 15m), await EstadoAsync());

        // Un reintento del mismo formulario sigue sin duplicar.
        await EntradaAsync(id);
        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_TimeoutAntesDelCommit_NoRegistraNadaYElReintentoConElMismoRequestIdSeAplicaUnaVez()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EntradaAsync(id, interceptores: new FalloEnCommitInterceptor(despuesDeCommit: false)));

        Assert.Contains("no se registró", ex.Message);
        Assert.Equal((0, 10m), await EstadoAsync());

        await EntradaAsync(id);
        await EntradaAsync(id);
        Assert.Equal((1, 15m), await EstadoAsync());
    }

    [Fact]
    public async Task B4R_ErrorDeNegocio_NoDejaVariacionParcial()
    {
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EntradaAsync(id, cantidad: 999m, tipo: TipoMovimiento.Salida)); // stock insuficiente

        Assert.Equal((0, 10m), await EstadoAsync());
        await EntradaAsync(id, cantidad: 5m); // el mismo id es reutilizable: el intento fallido no dejó rastro
        Assert.Equal((1, 15m), await EstadoAsync());
    }

    // ── Interceptores de prueba ───────────────────────────────────────────

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

    /// <summary>Simula un TimeoutException en el commit, antes o después de que la base lo aplique.</summary>
    private sealed class FalloEnCommitInterceptor : DbTransactionInterceptor
    {
        private readonly bool _despuesDeCommit;
        public FalloEnCommitInterceptor(bool despuesDeCommit) => _despuesDeCommit = despuesDeCommit;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => _despuesDeCommit ? new(result) : throw new TimeoutException("Timeout simulado antes del commit");

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            => _despuesDeCommit ? throw new TimeoutException("Timeout simulado después del commit") : Task.CompletedTask;
    }
}
