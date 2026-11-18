using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// D-01 (AC-01-3 / AC-01-5): concurrencia REAL sobre SQL Server de la lista predeterminada y del precio manual por producto y lista.
// SQLite serializa todo con un unico escritor: la carrera solo se reproduce aqui.
//
// Mismo gate que los demas harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catalogo "qa_fin_conc*".
// El fixture crea y elimina su propio catalogo ("<base>_lpre") y aplica TODAS las migraciones.
//
// Cada escenario se repite varias rondas (la carrera no se da siempre) y exige: ninguna excepcion NO controlada (un 500 en
// la UI), el invariante de la base y el historial coherente. "Controlada" = OK, InvalidOperationException (mensaje al
// usuario) o DbUpdateConcurrencyException (el controller la traduce a "recargá e intentá de nuevo").
// ---------------------------------------------------------------------------------------------

public sealed class ListaPrecioPrecioManualSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_lpre";
}

public class ListaPrecioPrecioManualSqlServerTests : IClassFixture<ListaPrecioPrecioManualSqlHostFixture>
{
    private const int Rondas = 12;

    private readonly ListaPrecioPrecioManualSqlHostFixture _fx;

    public ListaPrecioPrecioManualSqlServerTests(ListaPrecioPrecioManualSqlHostFixture fx) => _fx = fx;

    // ── helpers ─────────────────────────────────────────────────────────

    private static string Sufijo() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private async Task LimpiarListasAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Las listas de rondas anteriores salen del invariante (baja logica) para que cada ronda arranque limpia.
        await ctx.ListasPrecios.IgnoreQueryFilters().Where(l => !l.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsDeleted, true).SetProperty(l => l.EsPredeterminada, false));
    }

    private async Task<int> SembrarListaAsync(bool predeterminada)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Sufijo();
        var lista = new ListaPrecio
        {
            Nombre = "Lista " + sufijo, Codigo = "L" + sufijo, Tipo = TipoListaPrecio.Contado,
            Activa = true, EsPredeterminada = predeterminada, Orden = 1
        };
        ctx.ListasPrecios.Add(lista);
        await ctx.SaveChangesAsync();
        return lista.Id;
    }

    private async Task<(ListaPrecio Lista, byte[] RowVersion)> LeerListaAsync(int id)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var l = await ctx.ListasPrecios.AsNoTracking().IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        return (l, l.RowVersion);
    }

    private async Task<int> SembrarProductoAsync(int? listaId = null, decimal precioInicial = 100m)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sufijo = Sufijo();
        var categoria = new Categoria { Codigo = sufijo, Nombre = "Cat-" + sufijo, Activo = true };
        var marca = new Marca { Codigo = sufijo, Nombre = "Marca-" + sufijo, Activo = true };
        ctx.AddRange(categoria, marca);
        await ctx.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = sufijo, Nombre = "Prod-" + sufijo, CategoriaId = categoria.Id, MarcaId = marca.Id,
            PrecioCompra = 50m, PrecioVenta = precioInicial, PorcentajeIVA = 21m, StockActual = 0m, Activo = true
        };
        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        if (listaId.HasValue)
        {
            ctx.ProductosPrecios.Add(new ProductoPrecioLista
            {
                ProductoId = producto.Id, ListaId = listaId.Value, VigenciaDesde = DateTime.UtcNow.AddDays(-1), Costo = 50m,
                Precio = precioInicial, MargenValor = precioInicial - 50m, MargenPorcentaje = 100m, EsVigente = true, EsManual = true,
                CreadoPor = "seed"
            });
            await ctx.SaveChangesAsync();
        }
        return producto.Id;
    }

    /// <summary>Ejecuta las operaciones a la vez (compuerta comun) y clasifica cada resultado.</summary>
    private async Task<string[]> EnParaleloAsync(params Func<IPrecioService, Task>[] operaciones)
    {
        var compuerta = new TaskCompletionSource();
        var tareas = operaciones.Select(op => Task.Run(async () =>
        {
            using var scope = _fx.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPrecioService>();
            await compuerta.Task;
            try
            {
                await op(svc);
                return "OK";
            }
            catch (InvalidOperationException ex)
            {
                return "RECHAZADA: " + ex.Message;
            }
            catch (DbUpdateConcurrencyException)
            {
                return "CONCURRENCIA";
            }
            catch (Exception ex)
            {
                return "EXCEPCION: " + ex.GetType().Name + " " + ex.Message;
            }
        })).ToArray();

        await Task.Delay(200);
        compuerta.SetResult();
        return await Task.WhenAll(tareas);
    }

    private static void AssertSinExcepcionesNoControladas(IEnumerable<string> resultados, string contexto)
    {
        var malos = resultados.Where(r => r.StartsWith("EXCEPCION")).ToList();
        Assert.True(malos.Count == 0, $"{contexto}: excepcion no controlada (seria un 500): {string.Join(" | ", malos)}");
    }

    private async Task<List<int>> PredeterminadasAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.ListasPrecios.AsNoTracking().IgnoreQueryFilters()
            .Where(l => !l.IsDeleted && l.EsPredeterminada).Select(l => l.Id).ToListAsync();
    }

    private static Task EditarComoPredeterminadaAsync(IPrecioService svc, ListaPrecio origen, byte[] rowVersion)
    {
        var propuesta = new ListaPrecio
        {
            Id = origen.Id, Nombre = origen.Nombre, Codigo = origen.Codigo, Tipo = origen.Tipo, Activa = true,
            EsPredeterminada = true, Orden = origen.Orden
        };
        return svc.UpdateListaAsync(propuesta, rowVersion);
    }

    // ── 1. lista predeterminada ─────────────────────────────────────────

    [SqlServerConcFact(DisplayName = "D01-CONC-01 dos «marcar predeterminada» simultaneos sobre listas distintas: siempre exactamente una")]
    public async Task Dos_Marcar_Predeterminada_Simultaneos_Dejan_Exactamente_Una()
    {
        var resumen = new List<string>();
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            await LimpiarListasAsync();
            var vieja = await SembrarListaAsync(predeterminada: true);
            var a = await SembrarListaAsync(false);
            var b = await SembrarListaAsync(false);
            var (listaA, rvA) = await LeerListaAsync(a);
            var (listaB, rvB) = await LeerListaAsync(b);

            var resultados = await EnParaleloAsync(
                svc => EditarComoPredeterminadaAsync(svc, listaA, rvA),
                svc => EditarComoPredeterminadaAsync(svc, listaB, rvB));
            resumen.Add(string.Join(" / ", resultados.Select(r => r.Split(':')[0])));

            AssertSinExcepcionesNoControladas(resultados, $"ronda {ronda}");
            var predeterminadas = await PredeterminadasAsync();
            Assert.True(predeterminadas.Count == 1, $"ronda {ronda}: predeterminadas={predeterminadas.Count} ({string.Join(",", resultados)})");
            Assert.NotEqual(vieja, predeterminadas[0]);
            Assert.Contains("OK", resultados);
        }
        Assert.NotEmpty(resumen);
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-02 creacion simultanea de dos listas predeterminadas: siempre exactamente una")]
    public async Task Creacion_Simultanea_De_Dos_Listas_Predeterminadas_Deja_Exactamente_Una()
    {
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            await LimpiarListasAsync();
            await SembrarListaAsync(predeterminada: true);

            ListaPrecio Nueva()
            {
                var s = Sufijo();
                return new ListaPrecio { Nombre = "N " + s, Codigo = "N" + s, Tipo = TipoListaPrecio.Contado, Activa = true, EsPredeterminada = true, Orden = 1 };
            }

            var resultados = await EnParaleloAsync(
                svc => svc.CreateListaAsync(Nueva()),
                svc => svc.CreateListaAsync(Nueva()));

            AssertSinExcepcionesNoControladas(resultados, $"ronda {ronda}");
            var predeterminadas = await PredeterminadasAsync();
            Assert.True(predeterminadas.Count == 1, $"ronda {ronda}: predeterminadas={predeterminadas.Count} ({string.Join(",", resultados)})");
            Assert.Contains("OK", resultados);
        }
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-03 baja concurrente de la predeterminada vs designar otra: nunca quedan cero ni dos")]
    public async Task Baja_Concurrente_De_La_Predeterminada_Contra_Designar_Otra_No_Corrompe_El_Estado()
    {
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            await LimpiarListasAsync();
            var vieja = await SembrarListaAsync(predeterminada: true);
            var otra = await SembrarListaAsync(false);
            var (_, rvVieja) = await LeerListaAsync(vieja);
            var (listaOtra, rvOtra) = await LeerListaAsync(otra);

            var resultados = await EnParaleloAsync(
                svc => svc.DeleteListaAsync(vieja, rvVieja),
                svc => EditarComoPredeterminadaAsync(svc, listaOtra, rvOtra));

            AssertSinExcepcionesNoControladas(resultados, $"ronda {ronda}");
            var predeterminadas = await PredeterminadasAsync();
            Assert.True(predeterminadas.Count == 1, $"ronda {ronda}: predeterminadas={predeterminadas.Count} ({string.Join(",", resultados)})");
            var (despues, _) = await LeerListaAsync(vieja);
            // La baja solo es valida si ya estaba designada la otra; nunca puede quedar la vieja dada de baja Y predeterminada.
            Assert.False(despues.IsDeleted && despues.EsPredeterminada);
            Assert.Contains("OK", resultados);
        }
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-04 baja concurrente de una lista vs designarla predeterminada: nunca queda dada de baja y predeterminada")]
    public async Task Baja_De_Una_Lista_Contra_Designarla_Predeterminada_No_La_Deja_Baja_Y_Predeterminada()
    {
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            await LimpiarListasAsync();
            var vieja = await SembrarListaAsync(predeterminada: true);
            var objetivo = await SembrarListaAsync(false);
            var (listaObjetivo, rv) = await LeerListaAsync(objetivo);

            var resultados = await EnParaleloAsync(
                svc => svc.DeleteListaAsync(objetivo, rv),
                svc => EditarComoPredeterminadaAsync(svc, listaObjetivo, rv));

            AssertSinExcepcionesNoControladas(resultados, $"ronda {ronda}");
            var predeterminadas = await PredeterminadasAsync();
            Assert.True(predeterminadas.Count == 1, $"ronda {ronda}: predeterminadas={predeterminadas.Count} ({string.Join(",", resultados)})");
            var (despues, _) = await LeerListaAsync(objetivo);
            Assert.False(despues.IsDeleted && despues.EsPredeterminada, $"ronda {ronda}: lista dada de baja y predeterminada");
            Assert.Equal(1, resultados.Count(r => r == "OK"));
            _ = vieja;
        }
    }

    // ── 2. precio manual ────────────────────────────────────────────────

    /// <summary>Vigente unico, y vigencias sin solapamiento ni invertidas: Desde &lt;= Hasta &lt; Desde siguiente.</summary>
    private async Task AssertHistorialCoherenteAsync(int productoId, int listaId, string contexto)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var filas = await ctx.ProductosPrecios.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.ProductoId == productoId && p.ListaId == listaId && !p.IsDeleted)
            .OrderBy(p => p.VigenciaDesde).ToListAsync();

        Assert.True(filas.Count(f => f.EsVigente) == 1, $"{contexto}: vigentes={filas.Count(f => f.EsVigente)}");
        Assert.True(filas.Last().EsVigente, $"{contexto}: el vigente no es el mas reciente");
        Assert.Null(filas.Last().VigenciaHasta);
        for (var i = 0; i < filas.Count - 1; i++)
        {
            var f = filas[i];
            Assert.False(f.EsVigente, $"{contexto}: fila {i} sigue vigente");
            Assert.NotNull(f.VigenciaHasta);
            Assert.True(f.VigenciaHasta >= f.VigenciaDesde, $"{contexto}: vigencia invertida en fila {i} ({f.VigenciaDesde:O} > {f.VigenciaHasta:O})");
            Assert.True(f.VigenciaHasta < filas[i + 1].VigenciaDesde, $"{contexto}: solapamiento entre fila {i} y {i + 1}");
        }
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-05 dos precios manuales simultaneos sobre el mismo producto y lista: un solo vigente, sin perder ni invertir cambios")]
    public async Task Dos_Precios_Manuales_Simultaneos_Dejan_Un_Solo_Vigente_E_Historial_Coherente()
    {
        await LimpiarListasAsync();
        var lista = await SembrarListaAsync(false);
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var producto = await SembrarProductoAsync(lista, 100m);

            var resultados = await EnParaleloAsync(
                svc => svc.SetPrecioManualAsync(producto, lista, 120m, 50m),
                svc => svc.SetPrecioManualAsync(producto, lista, 130m, 50m));

            AssertSinExcepcionesNoControladas(resultados, $"ronda {ronda}");
            await AssertResultadoPrecioAsync(producto, lista, resultados, filasIniciales: 1, $"ronda {ronda}");
        }
    }

    /// <summary>
    /// Sin cambios perdidos ni fantasmas (cada OK dejo exactamente una fila y cada rechazo ninguna), rechazos solo controlados
    /// y el historial coherente. Que ambos terminen OK es valido solo si uno se aplico DESPUES del otro (vigencia posterior).
    /// </summary>
    private async Task AssertResultadoPrecioAsync(int producto, int lista, string[] resultados, int filasIniciales, string contexto)
    {
        var ok = resultados.Count(r => r == "OK");
        Assert.True(ok >= 1, $"{contexto}: ninguna operacion tuvo exito: {string.Join(" / ", resultados)}");
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var filas = await ctx.ProductosPrecios.AsNoTracking().CountAsync(p => p.ProductoId == producto && p.ListaId == lista);
            Assert.True(filas == filasIniciales + ok, $"{contexto}: filas={filas}, esperadas={filasIniciales + ok} ({string.Join(" / ", resultados)})");
        }
        Assert.All(resultados.Where(r => r != "OK"), r =>
            Assert.True(r.StartsWith("RECHAZADA") && (r.Contains("al mismo tiempo") || r.Contains("posterior")), $"{contexto}: rechazo inesperado: {r}"));
        await AssertHistorialCoherenteAsync(producto, lista, $"{contexto} ({string.Join(" / ", resultados)})");
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-06 dos primeros precios manuales simultaneos (producto sin precio en la lista): un solo vigente")]
    public async Task Dos_Primeros_Precios_Simultaneos_Sin_Precio_Previo_Dejan_Un_Solo_Vigente()
    {
        await LimpiarListasAsync();
        var lista = await SembrarListaAsync(false);
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var producto = await SembrarProductoAsync(null);

            var resultados = await EnParaleloAsync(
                svc => svc.SetPrecioManualAsync(producto, lista, 120m, 50m),
                svc => svc.SetPrecioManualAsync(producto, lista, 130m, 50m));

            AssertSinExcepcionesNoControladas(resultados, $"ronda {ronda}");
            await AssertResultadoPrecioAsync(producto, lista, resultados, filasIniciales: 0, $"ronda {ronda}");
        }
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-07 precio manual vs aplicar lote simultaneos: un solo vigente y vigencias sin solapamiento")]
    public async Task Precio_Manual_Contra_Aplicar_Lote_Simultaneos_Dejan_Historial_Coherente()
    {
        await LimpiarListasAsync();
        var lista = await SembrarListaAsync(false);
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var producto = await SembrarProductoAsync(lista, 100m);

            int batchId;
            byte[] rowVersion;
            using (var scope = _fx.Services.CreateScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IPrecioService>();
                var simulado = await svc.SimularCambioMasivoAsync(
                    "D01-conc-" + Sufijo(), TipoCambio.PorcentajeSobrePrecioActual, TipoAplicacion.Aumento, 10m,
                    new List<int> { lista }, productoIds: new List<int> { producto });
                var aprobado = await svc.AprobarBatchAsync(simulado.Id, "conc", simulado.RowVersion);
                batchId = aprobado.Id;
                using var ctx2 = _fx.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
                rowVersion = await ctx2.PriceChangeBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.RowVersion).SingleAsync();
            }

            var resultados = await EnParaleloAsync(
                svc => svc.SetPrecioManualAsync(producto, lista, 140m, 50m),
                svc => svc.AplicarBatchAsync(batchId, "conc", rowVersion));

            AssertSinExcepcionesNoControladas(resultados, $"ronda {ronda}");
            await AssertResultadoPrecioAsync(producto, lista, resultados, filasIniciales: 1, $"ronda {ronda}");
        }
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-09 aplicar lote que pierde la carrera contra un precio manual (leyo «sin vigente»): rechazo controlado, sin duplicado ni 500")]
    public async Task Aplicar_Lote_Que_Pierde_La_Carrera_Contra_Un_Precio_Manual_Falla_De_Forma_Controlada()
    {
        // Reproduccion DETERMINISTA del fallo visto en CI (SQL 2601 sin controlar): el lote lee «sin precio vigente», un precio
        // manual completo toma el lugar vigente antes del alta del lote, y el indice unico rechaza el INSERT del lote.
        await LimpiarListasAsync();
        var lista = await SembrarListaAsync(false);
        var producto = await SembrarProductoAsync(lista, 100m);

        int batchId;
        byte[] rowVersion;
        using (var scope = _fx.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPrecioService>();
            var simulado = await svc.SimularCambioMasivoAsync(
                "D01-conc-" + Sufijo(), TipoCambio.PorcentajeSobrePrecioActual, TipoAplicacion.Aumento, 10m,
                new List<int> { lista }, productoIds: new List<int> { producto });
            var aprobado = await svc.AprobarBatchAsync(simulado.Id, "conc", simulado.RowVersion);
            batchId = aprobado.Id;
            using var ctx2 = _fx.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();
            rowVersion = await ctx2.PriceChangeBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.RowVersion).SingleAsync();
        }

        // El producto queda sin precio vigente en la lista: el lote lo leera como «sin vigente» (no cierra nada).
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE ProductosPrecios SET EsVigente = 0, VigenciaHasta = {0} WHERE ProductoId = {1} AND ListaId = {2}",
                DateTime.UtcNow.AddHours(-1), producto, lista);
        }

        var plan = new GatePlan("lote-antes-del-alta", sql => sql.Contains("INSERT INTO [ProductosPrecios]", StringComparison.Ordinal));
        var lote = Task.Run(async () =>
        {
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                using var scope = _fx.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPrecioService>().AplicarBatchAsync(batchId, "conc", rowVersion);
                return "OK";
            }
            catch (InvalidOperationException ex) { return "RECHAZADA: " + ex.Message; }
            catch (Exception ex) { return "EXCEPCION: " + ex.GetType().Name + " " + ex.Message; }
            finally { ConcGateInterceptor.Plan.Value = null; }
        });

        Assert.True(await Task.WhenAny(plan.Reached.Task, lote, Task.Delay(TimeSpan.FromSeconds(30))) == plan.Reached.Task,
            "el lote no llego a la compuerta");

        // Con el lote detenido justo antes de su alta, un precio manual completo toma el lugar vigente.
        string manual;
        using (var scope = _fx.Services.CreateScope())
        {
            try { await scope.ServiceProvider.GetRequiredService<IPrecioService>().SetPrecioManualAsync(producto, lista, 140m, 50m); manual = "OK"; }
            catch (InvalidOperationException ex) { manual = "RECHAZADA: " + ex.Message; }
        }
        Assert.Equal("OK", manual);

        plan.Release.TrySetResult();
        var resultado = await lote;

        Assert.True(resultado.StartsWith("RECHAZADA") && resultado.Contains("al mismo tiempo"), $"el lote debia fallar de forma controlada: {resultado}");
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vigentes = await ctx.ProductosPrecios.AsNoTracking().Where(p => p.ProductoId == producto && p.ListaId == lista && p.EsVigente && !p.IsDeleted).ToListAsync();
            Assert.Single(vigentes);
            Assert.Equal(140m, vigentes[0].Precio);
            Assert.Equal(0, await ctx.ProductosPrecios.AsNoTracking().CountAsync(p => p.BatchId == batchId));
            // La transaccion del lote se revirtio por completo: el lote sigue sin aplicarse y se puede reintentar.
            Assert.Equal(EstadoBatch.Aprobado, (await ctx.PriceChangeBatches.AsNoTracking().SingleAsync(b => b.Id == batchId)).Estado);
        }
        await AssertHistorialCoherenteAsync(producto, lista, "D01-CONC-09");
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-10 cambio directo de precio que pierde la carrera contra un precio manual: resultado fallido controlado, sin duplicado ni excepcion")]
    public async Task Cambio_Directo_Que_Pierde_La_Carrera_Contra_Un_Precio_Manual_Falla_De_Forma_Controlada()
    {
        await LimpiarListasAsync();
        var lista = await SembrarListaAsync(false);
        var producto = await SembrarProductoAsync(lista, 100m);
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE ProductosPrecios SET EsVigente = 0, VigenciaHasta = {0} WHERE ProductoId = {1} AND ListaId = {2}",
                DateTime.UtcNow.AddHours(-1), producto, lista);
        }

        var plan = new GatePlan("directo-antes-del-alta", sql => sql.Contains("INSERT INTO [ProductosPrecios]", StringComparison.Ordinal));
        var directo = Task.Run(async () =>
        {
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                using var scope = _fx.Services.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<IPrecioService>().AplicarCambioPrecioDirectoAsync(
                    new ViewModels.AplicarCambioPrecioDirectoViewModel
                    {
                        Alcance = "seleccionados", ProductoIdsText = producto.ToString(), ValorPorcentaje = 10m,
                        TipoCambio = "porcentaje", ListaPrecioId = lista, Motivo = "D01 concurrencia"
                    });
                return r.Exitoso ? "OK" : "RECHAZADA: " + r.Mensaje;
            }
            catch (Exception ex) { return "EXCEPCION: " + ex.GetType().Name + " " + ex.Message; }
            finally { ConcGateInterceptor.Plan.Value = null; }
        });

        Assert.True(await Task.WhenAny(plan.Reached.Task, directo, Task.Delay(TimeSpan.FromSeconds(30))) == plan.Reached.Task,
            "el cambio directo no llego a la compuerta");

        using (var scope = _fx.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPrecioService>().SetPrecioManualAsync(producto, lista, 140m, 50m);

        plan.Release.TrySetResult();
        var resultado = await directo;

        Assert.True(resultado.StartsWith("RECHAZADA") && resultado.Contains("al mismo tiempo"), $"debia fallar de forma controlada: {resultado}");
        using (var scope = _fx.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vigentes = await ctx.ProductosPrecios.AsNoTracking().Where(p => p.ProductoId == producto && p.ListaId == lista && p.EsVigente && !p.IsDeleted).ToListAsync();
            Assert.Single(vigentes);
            Assert.Equal(140m, vigentes[0].Precio);
            // El cambio directo se revirtio por completo: no cambio el precio base del producto.
            Assert.Equal(100m, (await ctx.Productos.AsNoTracking().SingleAsync(p => p.Id == producto)).PrecioVenta);
        }
        await AssertHistorialCoherenteAsync(producto, lista, "D01-CONC-10");
    }

    [SqlServerConcFact(DisplayName = "D01-CONC-08 el indice unico del precio vigente impide dos vigentes aunque se salte el servicio")]
    public async Task El_Indice_Unico_De_La_Base_Impide_Dos_Vigentes()
    {
        await LimpiarListasAsync();
        var lista = await SembrarListaAsync(false);
        var producto = await SembrarProductoAsync(lista, 100m);

        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.ProductosPrecios.Add(new ProductoPrecioLista
        {
            ProductoId = producto, ListaId = lista, VigenciaDesde = DateTime.UtcNow, Costo = 50m, Precio = 111m,
            MargenValor = 61m, MargenPorcentaje = 122m, EsVigente = true, EsManual = true, CreadoPor = "idx"
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.IsType<SqlException>(ex.InnerException);
    }
}
