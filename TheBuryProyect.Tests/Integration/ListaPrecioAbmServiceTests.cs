using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// D-01 (listas de precios): AC-01-1 a AC-01-5 y AC-01-8 sobre <see cref="PrecioService"/> con SQLite en memoria.
/// Alta/edición/baja con validaciones, código único, predeterminada única (servicio e índice), baja que conserva
/// el historial, precio manual por producto y lista, y no regresión del precio que resuelven Venta y Cotización.
/// </summary>
public class ListaPrecioAbmServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioService _service;

    public ListaPrecioAbmServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new PrecioService(_context, NullLogger<PrecioService>.Instance,
            new UsuarioPruebaD01(), new ConfigurationBuilder().Build());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static ListaPrecio NuevaLista(string codigo = "CONTADO", string nombre = "Contado") => new()
    {
        Nombre = nombre,
        Codigo = codigo,
        Tipo = TipoListaPrecio.Contado,
        Activa = true,
        Orden = 1
    };

    private async Task<ListaPrecio> CrearAsync(string codigo = "CONTADO", Action<ListaPrecio>? ajustar = null)
    {
        var lista = NuevaLista(codigo, "Lista " + codigo);
        ajustar?.Invoke(lista);
        await _service.CreateListaAsync(lista);
        _context.ChangeTracker.Clear();
        return await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == lista.Id);
    }

    private async Task<Producto> ProductoAsync(decimal costo = 100m, decimal precioVenta = 150m)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code, CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = costo, PrecioVenta = precioVenta, PorcentajeIVA = 21m, StockActual = 5m, Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    private async Task<InvalidOperationException> RechazaAsync(Func<Task> accion)
        => await Assert.ThrowsAsync<InvalidOperationException>(accion);

    // ── AC-01-1: alta, edición y baja con código único ───────────────────

    [Fact]
    public async Task AC011_Alta_NormalizaCodigoANombreYPersisteConAuditoria()
    {
        var lista = await CrearAsync(" cont-1 ", l => l.Nombre = "  Contado  ");

        Assert.Equal("CONT-1", lista.Codigo);
        Assert.Equal("Contado", lista.Nombre);
        Assert.False(string.IsNullOrWhiteSpace(lista.CreatedBy));
        Assert.False(lista.IsDeleted);
    }

    [Fact]
    public async Task AC011_CodigoRepetido_SeRechazaSinImportarMayusculasNiEspacios()
    {
        await CrearAsync("CONTADO");

        var ex = await RechazaAsync(() => _service.CreateListaAsync(NuevaLista("  contado ", "Otra")));

        Assert.Contains("código", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await _context.ListasPrecios.CountAsync());
    }

    [Fact]
    public async Task AC011_EditarConElCodigoDeOtraLista_SeRechaza_PeroConservarElPropioSePermite()
    {
        var a = await CrearAsync("A");
        var b = await CrearAsync("B");

        var conflicto = NuevaLista("a", "B editada");
        conflicto.Id = b.Id;
        await RechazaAsync(() => _service.UpdateListaAsync(conflicto, b.RowVersion));

        _context.ChangeTracker.Clear();
        var propio = NuevaLista("A", "A renombrada");
        propio.Id = a.Id;
        var resultado = await _service.UpdateListaAsync(propio, a.RowVersion);
        Assert.Equal("A renombrada", resultado.Nombre);
    }

    [Fact]
    public async Task AC011_DarDeBajaLiberaElCodigoParaUnaListaNueva()
    {
        var vieja = await CrearAsync("TEMP");
        Assert.True(await _service.DeleteListaAsync(vieja.Id, vieja.RowVersion));

        var nueva = await CrearAsync("TEMP");

        Assert.NotEqual(vieja.Id, nueva.Id);
    }

    [Fact]
    public async Task AC011_Alta_SinNombreOSinCodigo_SeRechaza()
    {
        await RechazaAsync(() => _service.CreateListaAsync(NuevaLista("X", "   ")));
        await RechazaAsync(() => _service.CreateListaAsync(NuevaLista("   ", "Sin código")));
        Assert.Equal(0, await _context.ListasPrecios.CountAsync());
    }

    [Fact]
    public async Task AC011_RowVersionObsoleto_AlEditarOBorrar_LanzaConcurrencia()
    {
        var lista = await CrearAsync("CC");
        var cambio = NuevaLista("CC", "Cambiada");
        cambio.Id = lista.Id;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            _service.UpdateListaAsync(cambio, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }));

        _context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            _service.DeleteListaAsync(lista.Id, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }));

        _context.ChangeTracker.Clear();
        Assert.Equal("Lista CC", (await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == lista.Id)).Nombre);
    }

    // ── AC-01-2: reglas de la lista ──────────────────────────────────────

    [Fact]
    public async Task AC012_Editar_PersisteTodosLosCampos_IncluidosMargenMinimoRedondeoYNotas()
    {
        var lista = await CrearAsync("FULL");
        var cambio = NuevaLista("FULL", "Completa");
        cambio.Id = lista.Id;
        cambio.Tipo = TipoListaPrecio.Tarjeta;
        cambio.MargenPorcentaje = 40m;
        cambio.MargenMinimoPorcentaje = 25m;
        cambio.RecargoPorcentaje = 10m;
        cambio.CantidadCuotas = 6;
        cambio.ReglaRedondeo = "Decena";
        cambio.Notas = "  nota interna ";
        cambio.Descripcion = "descripción";
        cambio.Orden = 7;
        cambio.ReglasJson = "{\"a\":1}";

        await _service.UpdateListaAsync(cambio, lista.RowVersion);

        _context.ChangeTracker.Clear();
        var bd = await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == lista.Id);
        Assert.Equal(TipoListaPrecio.Tarjeta, bd.Tipo);
        Assert.Equal(40m, bd.MargenPorcentaje);
        Assert.Equal(25m, bd.MargenMinimoPorcentaje);
        Assert.Equal(10m, bd.RecargoPorcentaje);
        Assert.Equal(6, bd.CantidadCuotas);
        Assert.Equal("decena", bd.ReglaRedondeo);
        Assert.Equal("nota interna", bd.Notas);
        Assert.Equal("descripción", bd.Descripcion);
        Assert.Equal(7, bd.Orden);
        Assert.Equal("{\"a\":1}", bd.ReglasJson);
        Assert.NotNull(bd.UpdatedAt);
    }

    [Theory]
    [InlineData(20, 10)]
    [InlineData(10.01, 10)]
    public async Task AC012_MargenMinimoMayorQueElMargen_SeRechaza(double minimo, double margen)
    {
        var lista = NuevaLista("M");
        lista.MargenMinimoPorcentaje = (decimal)minimo;
        lista.MargenPorcentaje = (decimal)margen;

        var ex = await RechazaAsync(() => _service.CreateListaAsync(lista));

        Assert.Contains("margen mínimo", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _context.ListasPrecios.CountAsync());
    }

    [Fact]
    public async Task AC012_MargenMinimoIgualAlMargen_SePermite()
    {
        var lista = await CrearAsync("IGUAL", l => { l.MargenMinimoPorcentaje = 15m; l.MargenPorcentaje = 15m; });
        Assert.Equal(15m, lista.MargenMinimoPorcentaje);
    }

    [Theory]
    [InlineData("margen", -1, 0, 0)]
    [InlineData("margen", 1000, 0, 0)]
    [InlineData("margen", 10.123, 0, 0)]
    [InlineData("recargo", 0, -5, 0)]
    [InlineData("recargo", 0, 1000, 0)]
    [InlineData("cuotas", 0, 0, 0)]
    [InlineData("cuotas", 0, 0, 61)]
    public async Task AC012_ValoresFueraDeRango_SeRechazan(string campo, double margen, double recargo, int cuotas)
    {
        var lista = NuevaLista("R");
        if (campo == "margen") lista.MargenPorcentaje = (decimal)margen;
        if (campo == "recargo") lista.RecargoPorcentaje = (decimal)recargo;
        if (campo == "cuotas") lista.CantidadCuotas = cuotas;

        await RechazaAsync(() => _service.CreateListaAsync(lista));
        Assert.Equal(0, await _context.ListasPrecios.CountAsync());
    }

    [Fact]
    public async Task AC012_RedondeoDesconocidoOJsonInvalido_SeRechazan()
    {
        var a = NuevaLista("RED");
        a.ReglaRedondeo = "millar";
        await RechazaAsync(() => _service.CreateListaAsync(a));

        var b = NuevaLista("JSON");
        b.ReglasJson = "{no es json";
        await RechazaAsync(() => _service.CreateListaAsync(b));

        var c = NuevaLista("TIPO");
        c.Tipo = (TipoListaPrecio)99;
        await RechazaAsync(() => _service.CreateListaAsync(c));
    }

    [Fact]
    public async Task AC012_EdicionInvalida_NoDejaLaListaModificada()
    {
        var lista = await CrearAsync("KEEP", l => l.MargenPorcentaje = 30m);
        var cambio = NuevaLista("KEEP", "Nombre nuevo");
        cambio.Id = lista.Id;
        cambio.MargenPorcentaje = 5m;
        cambio.MargenMinimoPorcentaje = 50m;

        await RechazaAsync(() => _service.UpdateListaAsync(cambio, lista.RowVersion));

        Assert.Empty(_context.ChangeTracker.Entries<ListaPrecio>().Where(e => e.State == EntityState.Modified));
        var bd = await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == lista.Id);
        Assert.Equal("Lista KEEP", bd.Nombre);
        Assert.Equal(30m, bd.MargenPorcentaje);
    }

    // ── AC-01-3: predeterminada única ────────────────────────────────────

    [Fact]
    public async Task AC013_MarcarOtraComoPredeterminada_DejaSoloUna()
    {
        var a = await CrearAsync("A", l => l.EsPredeterminada = true);
        var b = await CrearAsync("B");
        var cambio = NuevaLista("B", "B");
        cambio.Id = b.Id;
        cambio.EsPredeterminada = true;

        await _service.UpdateListaAsync(cambio, b.RowVersion);

        _context.ChangeTracker.Clear();
        var predeterminadas = await _context.ListasPrecios.Where(l => l.EsPredeterminada && !l.IsDeleted).ToListAsync();
        Assert.Single(predeterminadas);
        Assert.Equal(b.Id, predeterminadas[0].Id);
        Assert.False((await _context.ListasPrecios.SingleAsync(l => l.Id == a.Id)).EsPredeterminada);
    }

    [Fact]
    public async Task AC013_ElIndiceDeLaBase_ImpideDosPredeterminadasAunSaltandoseElServicio()
    {
        await CrearAsync("A", l => l.EsPredeterminada = true);
        _context.ListasPrecios.Add(new ListaPrecio { Nombre = "Intrusa", Codigo = "I", Tipo = TipoListaPrecio.Contado, Activa = true, EsPredeterminada = true });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task AC013_LaPredeterminadaNoSeDeSmarcaPorEdicion_NiSeDaDeBaja()
    {
        var a = await CrearAsync("DEF", l => l.EsPredeterminada = true);
        var cambio = NuevaLista("DEF", "DEF");
        cambio.Id = a.Id;
        cambio.EsPredeterminada = false;

        var ex = await RechazaAsync(() => _service.UpdateListaAsync(cambio, a.RowVersion));
        Assert.Contains("predeterminada", ex.Message, StringComparison.OrdinalIgnoreCase);

        _context.ChangeTracker.Clear();
        var baja = await RechazaAsync(() => _service.DeleteListaAsync(a.Id, a.RowVersion));
        Assert.Contains("predeterminada", baja.Message, StringComparison.OrdinalIgnoreCase);

        _context.ChangeTracker.Clear();
        var bd = await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == a.Id);
        Assert.True(bd.EsPredeterminada);
        Assert.False(bd.IsDeleted);
    }

    [Fact]
    public async Task AC013_UnaListaInactivaNoPuedeSerPredeterminada()
    {
        var lista = NuevaLista("INA");
        lista.Activa = false;
        lista.EsPredeterminada = true;

        await RechazaAsync(() => _service.CreateListaAsync(lista));

        var activa = await CrearAsync("ACT");
        var cambio = NuevaLista("ACT", "ACT");
        cambio.Id = activa.Id;
        cambio.Activa = false;
        cambio.EsPredeterminada = true;
        await RechazaAsync(() => _service.UpdateListaAsync(cambio, activa.RowVersion));
    }

    [Fact]
    public async Task AC013_DesignarOtraPredeterminada_PermiteDarDeBajaLaVieja()
    {
        var a = await CrearAsync("OLD", l => l.EsPredeterminada = true);
        var b = await CrearAsync("NEW", l => l.EsPredeterminada = true);

        _context.ChangeTracker.Clear();
        var vieja = await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == a.Id);
        Assert.False(vieja.EsPredeterminada);
        Assert.True(await _service.DeleteListaAsync(vieja.Id, vieja.RowVersion));
        Assert.Equal(b.Id, (await _service.GetListaPredeterminadaAsync())!.Id);
    }

    // ── AC-01-4: baja de lista ───────────────────────────────────────────

    [Fact]
    public async Task AC014_BajaDeListaConPrecios_ConservaPreciosEHistorial()
    {
        var lista = await CrearAsync("USADA");
        var prod = await ProductoAsync();
        await _service.SetPrecioManualAsync(prod.Id, lista.Id, 200m, 100m);
        await _service.SetPrecioManualAsync(prod.Id, lista.Id, 220m, 100m);

        _context.ChangeTracker.Clear();
        var actual = await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == lista.Id);
        Assert.True(await _service.DeleteListaAsync(actual.Id, actual.RowVersion));

        _context.ChangeTracker.Clear();
        var bd = await _context.ListasPrecios.IgnoreQueryFilters().SingleAsync(l => l.Id == lista.Id);
        Assert.True(bd.IsDeleted);
        Assert.False(bd.Activa);
        Assert.Equal(2, await _context.ProductosPrecios.CountAsync(p => p.ListaId == lista.Id && !p.IsDeleted));
        Assert.DoesNotContain(await _service.GetAllListasAsync(soloActivas: false), l => l.Id == lista.Id);
    }

    [Fact]
    public async Task AC014_ListaUsadaPorMercadoLibre_NoSePuedeDarDeBaja()
    {
        var lista = await CrearAsync("ML");
        _context.MercadoLibreConfiguraciones.Add(new MercadoLibreConfiguracion { ListaPrecioId = lista.Id });
        await _context.SaveChangesAsync();

        var ex = await RechazaAsync(() => _service.DeleteListaAsync(lista.Id, lista.RowVersion));

        Assert.Contains("Mercado Libre", ex.Message);
        Assert.False((await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == lista.Id)).IsDeleted);
    }

    [Fact]
    public async Task AC014_ResumenDeListas_CuentaSoloPreciosVigentes()
    {
        var lista = await CrearAsync("CNT");
        var p1 = await ProductoAsync();
        var p2 = await ProductoAsync();
        await _service.SetPrecioManualAsync(p1.Id, lista.Id, 200m, 100m);
        await _service.SetPrecioManualAsync(p1.Id, lista.Id, 210m, 100m); // el anterior deja de ser vigente
        await _service.SetPrecioManualAsync(p2.Id, lista.Id, 300m, 100m);

        var resumen = await _service.GetResumenListasAsync();

        Assert.Equal(2, resumen.Single(r => r.Lista.Id == lista.Id).PreciosVigentes);
    }

    // ── AC-01-5: precio manual por producto y lista ──────────────────────

    [Fact]
    public async Task AC015_PrecioManual_GuardaCostoMargenUsuarioYNotasYCierraElAnterior()
    {
        var lista = await CrearAsync("PM");
        var prod = await ProductoAsync();

        var primero = await _service.SetPrecioManualAsync(prod.Id, lista.Id, 150m, 100m, notas: "  alta inicial ");
        await Task.Delay(30);
        var segundo = await _service.SetPrecioManualAsync(prod.Id, lista.Id, 180m, 100m);

        _context.ChangeTracker.Clear();
        var historial = await _service.GetHistorialPreciosAsync(prod.Id, lista.Id);
        Assert.Equal(2, historial.Count);
        Assert.Equal(segundo.Id, historial[0].Id);
        Assert.True(historial[0].EsVigente);
        Assert.Null(historial[0].VigenciaHasta);
        Assert.False(historial[1].EsVigente);
        Assert.NotNull(historial[1].VigenciaHasta);
        Assert.True(historial[1].VigenciaHasta < historial[0].VigenciaDesde);
        Assert.Equal("alta inicial", historial[1].Notas);
        Assert.Equal(50m, historial[1].MargenPorcentaje);
        Assert.Equal("testuser", historial[0].CreadoPor);
        Assert.Equal(primero.Id, historial[1].Id);
        Assert.Equal(180m, (await _service.GetPrecioVigenteAsync(prod.Id, lista.Id))!.Precio);
    }

    [Fact]
    public async Task AC015_PrecioManual_BajoElMargenMinimoDeLaLista_SeRechazaYNoCambiaNada()
    {
        var lista = await CrearAsync("MIN", l => { l.MargenPorcentaje = 50m; l.MargenMinimoPorcentaje = 20m; });
        var prod = await ProductoAsync();
        await _service.SetPrecioManualAsync(prod.Id, lista.Id, 130m, 100m);

        var ex = await RechazaAsync(() => _service.SetPrecioManualAsync(prod.Id, lista.Id, 110m, 100m));

        Assert.Contains("mínimo", ex.Message, StringComparison.OrdinalIgnoreCase);
        _context.ChangeTracker.Clear();
        Assert.Equal(130m, (await _service.GetPrecioVigenteAsync(prod.Id, lista.Id))!.Precio);
        Assert.Equal(1, await _context.ProductosPrecios.CountAsync(p => p.ProductoId == prod.Id));
        // En el borde exacto (20 %) se acepta.
        await Task.Delay(30);
        await _service.SetPrecioManualAsync(prod.Id, lista.Id, 120m, 100m);
    }

    [Fact]
    public async Task AC015_PrecioManual_ListaSinMargenMinimo_NoRestringe()
    {
        var lista = await CrearAsync("SINMIN");
        var prod = await ProductoAsync();

        var precio = await _service.SetPrecioManualAsync(prod.Id, lista.Id, 101m, 100m);

        Assert.Equal(1m, precio.MargenPorcentaje);
    }

    [Fact]
    public async Task AC015_PrecioManual_RechazaVigenciaFuturaAnteriorAlVigenteTopesYDecimales()
    {
        var lista = await CrearAsync("VIG");
        var prod = await ProductoAsync();
        await _service.SetPrecioManualAsync(prod.Id, lista.Id, 150m, 100m);

        await RechazaAsync(() => _service.SetPrecioManualAsync(prod.Id, lista.Id, 160m, 100m, DateTime.UtcNow.AddDays(3)));
        await RechazaAsync(() => _service.SetPrecioManualAsync(prod.Id, lista.Id, 160m, 100m, DateTime.UtcNow.AddDays(-30)));
        await RechazaAsync(() => _service.SetPrecioManualAsync(prod.Id, lista.Id, 20_000_001m, 100m));
        await RechazaAsync(() => _service.SetPrecioManualAsync(prod.Id, lista.Id, 160.555m, 100m));
        await RechazaAsync(() => _service.SetPrecioManualAsync(prod.Id, lista.Id, 160m, -1m));
        await RechazaAsync(() => _service.SetPrecioManualAsync(prod.Id, lista.Id, 160m, 100m, notas: new string('n', 501)));

        _context.ChangeTracker.Clear();
        Assert.Equal(1, await _context.ProductosPrecios.CountAsync(p => p.ProductoId == prod.Id));
        Assert.Equal(150m, (await _service.GetPrecioVigenteAsync(prod.Id, lista.Id))!.Precio);
    }

    [Fact]
    public async Task AC015_DosPreciosVigentesParaElMismoProductoYLista_LosImpideElIndiceUnico()
    {
        var lista = await CrearAsync("UNQ");
        var prod = await ProductoAsync();
        await _service.SetPrecioManualAsync(prod.Id, lista.Id, 150m, 100m);

        _context.ProductosPrecios.Add(new ProductoPrecioLista
        {
            ProductoId = prod.Id, ListaId = lista.Id, Precio = 999m, Costo = 100m,
            VigenciaDesde = DateTime.UtcNow.AddHours(1), EsVigente = true, EsManual = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task AC015_PreciosDeLista_PaginaConPrecioVigenteYSugeridoPorMargen()
    {
        var lista = await CrearAsync("PAG", l => { l.MargenPorcentaje = 50m; l.RecargoPorcentaje = 10m; l.ReglaRedondeo = "ninguno"; });
        var con = await ProductoAsync(costo: 100m);
        var sin = await ProductoAsync(costo: 200m);
        await _service.SetPrecioManualAsync(con.Id, lista.Id, 180m, 100m);

        _context.ChangeTracker.Clear();
        var pagina = await _service.GetPreciosDeListaAsync(lista.Id, null, 1, 25);

        Assert.Equal(2, pagina.Total);
        Assert.Equal(180m, pagina.Items.Single(i => i.ProductoId == con.Id).PrecioEnLista);
        Assert.Null(pagina.Items.Single(i => i.ProductoId == sin.Id).PrecioEnLista);
        // 100 * 1,5 * 1,1 = 165 (misma regla que CalcularPrecioAutomaticoAsync).
        var lst = await _service.GetListaByIdAsync(lista.Id);
        Assert.Equal(165m, _service.CalcularPrecioSugerido(lst!, 100m));
        Assert.Equal(165m, await _service.CalcularPrecioAutomaticoAsync(con.Id, lista.Id, 100m));

        var filtrada = await _service.GetPreciosDeListaAsync(lista.Id, con.Codigo.ToLowerInvariant(), 1, 25);
        Assert.Single(filtrada.Items);
    }

    // ── AC-01-8: no regresión del precio que resuelven Venta y Cotización ─

    [Fact]
    public async Task AC018_ABMDeListasNoPredeterminadas_NoCambiaElPrecioQueResuelvenVentaYCotizacion()
    {
        var resolver = new PrecioVigenteResolver(_context);
        var prod = await ProductoAsync(precioVenta: 150m);
        var defecto = await CrearAsync("DEF", l => l.EsPredeterminada = true);
        await _service.SetPrecioManualAsync(prod.Id, defecto.Id, 170m, 100m);
        var antes = await resolver.ResolverAsync(prod.Id);

        var otra = await CrearAsync("OTRA", l => l.MargenPorcentaje = 90m);
        await _service.SetPrecioManualAsync(prod.Id, otra.Id, 999m, 100m);
        _context.ChangeTracker.Clear();
        var edit = NuevaLista("OTRA", "Otra editada");
        edit.Id = otra.Id;
        await _service.UpdateListaAsync(edit, otra.RowVersion);
        _context.ChangeTracker.Clear();
        var otraBd = await _context.ListasPrecios.AsNoTracking().SingleAsync(l => l.Id == otra.Id);
        await _service.DeleteListaAsync(otraBd.Id, otraBd.RowVersion);
        _context.ChangeTracker.Clear();
        var despues = await resolver.ResolverAsync(prod.Id);

        Assert.Equal(170m, antes!.PrecioFinalConIva);
        Assert.Equal(antes.PrecioFinalConIva, despues!.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioLista, despues.FuentePrecio);
        Assert.Equal(defecto.Id, despues.ListaId);
        Assert.Equal(150m, (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == prod.Id)).PrecioVenta);
    }

    [Fact]
    public async Task AC018_SinListas_VentaYCotizacionUsanElPrecioBaseDelProducto()
    {
        var resolver = new PrecioVigenteResolver(_context);
        var prod = await ProductoAsync(precioVenta: 150m);

        var r = await resolver.ResolverAsync(prod.Id);

        Assert.Equal(150m, r!.PrecioFinalConIva);
        Assert.Equal(FuentePrecioVigente.ProductoPrecioBase, r.FuentePrecio);
    }

    [Fact]
    public async Task AC018_FijarPrecioEnUnaListaNoPredeterminada_NoToleraAlPrecioDeVenta()
    {
        var resolver = new PrecioVigenteResolver(_context);
        var prod = await ProductoAsync(precioVenta: 150m);
        var defecto = await CrearAsync("D", l => l.EsPredeterminada = true);
        var otra = await CrearAsync("O");

        await _service.SetPrecioManualAsync(prod.Id, otra.Id, 888m, 100m);

        var r = await resolver.ResolverAsync(prod.Id);
        Assert.Equal(150m, r!.PrecioFinalConIva); // la predeterminada no tiene precio propio: cae al base
        Assert.Equal(defecto.Id, r.ListaId);
        Assert.True(r.EsFallbackProductoBase);
    }
}

file sealed class UsuarioPruebaD01 : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}
