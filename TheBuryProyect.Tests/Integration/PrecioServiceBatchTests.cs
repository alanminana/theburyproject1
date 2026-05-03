using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para PrecioService — flujo PriceChangeBatch (componente canónico).
/// Cubren SimularCambioMasivoAsync (persistencia, items creados, cálculo de nuevo precio,
/// advertencia por margen negativo, sin precio en lista omite item),
/// AprobarBatchAsync (happy path, RowVersion vacío, no simulado, no existe),
/// RechazarBatchAsync (happy path, no simulado),
/// CancelarBatchAsync (happy path, ya aplicado bloquea),
/// AplicarBatchAsync (happy path, precios actualizados, precio anterior no vigente,
/// no aprobado lanza, RowVersion vacío lanza),
/// RequiereAutorizacionAsync (bajo umbral, sobre umbral).
/// </summary>
public class PrecioServiceBatchTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioService _service;

    public PrecioServiceBatchTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder().Build();
        _service = new PrecioService(_context, NullLogger<PrecioService>.Instance,
            new StubCurrentUserServiceBatch(), config);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(decimal precioCompra = 60m, decimal precioVenta = 100m)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = precioCompra, PrecioVenta = precioVenta,
            PorcentajeIVA = 21m, StockActual = 5m, Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    private async Task<ListaPrecio> SeedListaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var lista = new ListaPrecio
        {
            Nombre = "Lista-" + code, Codigo = code,
            Tipo = TipoListaPrecio.Contado,
            Activa = true, EsPredeterminada = false, Orden = 1,
            ReglaRedondeo = "ninguno"
        };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        await _context.Entry(lista).ReloadAsync();
        return lista;
    }

    private async Task<ProductoPrecioLista> SeedPrecioVigenteAsync(
        int productoId, int listaId, decimal precio = 100m, decimal costo = 60m)
    {
        var pp = new ProductoPrecioLista
        {
            ProductoId = productoId, ListaId = listaId,
            Precio = precio, Costo = costo,
            MargenValor = precio - costo,
            MargenPorcentaje = costo > 0 ? ((precio - costo) / costo) * 100 : 0,
            VigenciaDesde = DateTime.UtcNow.AddDays(-1),
            EsVigente = true, EsManual = true, CreadoPor = "test"
        };
        _context.ProductosPrecios.Add(pp);
        await _context.SaveChangesAsync();
        return pp;
    }

    private async Task<PriceChangeBatch> SeedBatchSimuladoAsync(
        decimal porcentajePromedio = 5m)
    {
        var batch = new PriceChangeBatch
        {
            Nombre = "Batch-Test",
            TipoCambio = TipoCambio.PorcentajeSobrePrecioActual,
            TipoAplicacion = TipoAplicacion.Aumento,
            ValorCambio = porcentajePromedio,
            AlcanceJson = "{}",
            ListasAfectadasJson = "[]",
            Estado = EstadoBatch.Simulado,
            SolicitadoPor = "testuser",
            FechaSolicitud = DateTime.UtcNow,
            PorcentajePromedioCambio = porcentajePromedio,
            CantidadProductos = 0
        };
        _context.PriceChangeBatches.Add(batch);
        await _context.SaveChangesAsync();
        await _context.Entry(batch).ReloadAsync();
        return batch;
    }

    // -------------------------------------------------------------------------
    // SimularCambioMasivoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Simular_ConProductoYPrecio_CreaItems()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 100m, costo: 60m);

        var batch = await _service.SimularCambioMasivoAsync(
            nombre: "Aumento 10%",
            tipoCambio: TipoCambio.PorcentajeSobrePrecioActual,
            tipoAplicacion: TipoAplicacion.Aumento,
            valorCambio: 10m,
            listasIds: new List<int> { lista.Id },
            productoIds: new List<int> { prod.Id });

        Assert.True(batch.Id > 0);
        Assert.Equal(EstadoBatch.Simulado, batch.Estado);
        Assert.Equal(1, batch.CantidadProductos);

        var items = await _context.PriceChangeItems
            .Where(i => i.BatchId == batch.Id)
            .ToListAsync();
        Assert.Single(items);
        Assert.Equal(prod.Id, items[0].ProductoId);
    }

    [Fact]
    public async Task Simular_AumentoPorcentual_CalculaNuevoPrecioCorrectamente()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 100m, costo: 60m);

        var batch = await _service.SimularCambioMasivoAsync(
            nombre: "Aumento 20%",
            tipoCambio: TipoCambio.PorcentajeSobrePrecioActual,
            tipoAplicacion: TipoAplicacion.Aumento,
            valorCambio: 20m,
            listasIds: new List<int> { lista.Id },
            productoIds: new List<int> { prod.Id });

        var item = await _context.PriceChangeItems.FirstAsync(i => i.BatchId == batch.Id);
        Assert.Equal(100m, item.PrecioAnterior);
        Assert.Equal(120m, item.PrecioNuevo); // 100 * 1.20
        Assert.Equal(20m, item.DiferenciaValor);
    }

    [Fact]
    public async Task Simular_AsignacionDirecta_AsignaPrecioDirectamente()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 100m, costo: 60m);

        var batch = await _service.SimularCambioMasivoAsync(
            nombre: "Fijar precio",
            tipoCambio: TipoCambio.AsignacionDirecta,
            tipoAplicacion: TipoAplicacion.Aumento,
            valorCambio: 150m,
            listasIds: new List<int> { lista.Id },
            productoIds: new List<int> { prod.Id });

        var item = await _context.PriceChangeItems.FirstAsync(i => i.BatchId == batch.Id);
        Assert.Equal(150m, item.PrecioNuevo);
    }

    [Fact]
    public async Task Simular_MargenNegativo_ItemTieneAdvertencia()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        // precio = 100, costo = 60 → si bajamos 50% → precio = 50 < costo → margen negativo
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 100m, costo: 60m);

        var batch = await _service.SimularCambioMasivoAsync(
            nombre: "Baja 50%",
            tipoCambio: TipoCambio.PorcentajeSobrePrecioActual,
            tipoAplicacion: TipoAplicacion.Disminucion,
            valorCambio: 50m,
            listasIds: new List<int> { lista.Id },
            productoIds: new List<int> { prod.Id });

        var item = await _context.PriceChangeItems.FirstAsync(i => i.BatchId == batch.Id);
        Assert.True(item.TieneAdvertencia);
        Assert.NotEmpty(item.MensajeAdvertencia!);
    }

    [Fact]
    public async Task Simular_ProductoSinPrecioEnLista_OmiteItem()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        // No seed de precio → no hay precio vigente

        var batch = await _service.SimularCambioMasivoAsync(
            nombre: "Sin precio",
            tipoCambio: TipoCambio.PorcentajeSobrePrecioActual,
            tipoAplicacion: TipoAplicacion.Aumento,
            valorCambio: 10m,
            listasIds: new List<int> { lista.Id },
            productoIds: new List<int> { prod.Id });

        var items = await _context.PriceChangeItems
            .Where(i => i.BatchId == batch.Id).ToListAsync();
        Assert.Empty(items);
        Assert.Equal(0, batch.CantidadProductos);
    }

    [Fact]
    public async Task Simular_ProductoSinPrecioEnLista_NoActualizaPrecioBaseProducto()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync();

        var batch = await _service.SimularCambioMasivoAsync(
            nombre: "Sin precio lista",
            tipoCambio: TipoCambio.PorcentajeSobrePrecioActual,
            tipoAplicacion: TipoAplicacion.Aumento,
            valorCambio: 10m,
            listasIds: new List<int> { lista.Id },
            productoIds: new List<int> { prod.Id });

        await _context.Entry(batch).ReloadAsync();
        var aprobado = await _service.AprobarBatchAsync(batch.Id, "sup", batch.RowVersion!);
        await _context.Entry(aprobado).ReloadAsync();

        var aplicado = await _service.AplicarBatchAsync(aprobado.Id, "admin", aprobado.RowVersion!);

        _context.ChangeTracker.Clear();
        var prodBd = await _context.Productos.SingleAsync(p => p.Id == prod.Id);
        var preciosLista = await _context.ProductosPrecios
            .Where(p => p.ProductoId == prod.Id && p.ListaId == lista.Id)
            .ToListAsync();

        Assert.Equal(EstadoBatch.Aplicado, aplicado.Estado);
        Assert.Equal(0, aplicado.CantidadProductos);
        Assert.Equal(100m, prodBd.PrecioVenta);
        Assert.Empty(preciosLista);
    }

    // -------------------------------------------------------------------------
    // AprobarBatchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aprobar_HappyPath_CambiaEstadoAprobado()
    {
        var batch = await SeedBatchSimuladoAsync();

        var resultado = await _service.AprobarBatchAsync(
            batch.Id, "supervisor", batch.RowVersion!, notas: "OK");

        Assert.Equal(EstadoBatch.Aprobado, resultado.Estado);
        Assert.Equal("supervisor", resultado.AprobadoPor);
        Assert.Equal("OK", resultado.Notas);
    }

    [Fact]
    public async Task Aprobar_RowVersionVacio_LanzaExcepcion()
    {
        var batch = await SeedBatchSimuladoAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AprobarBatchAsync(batch.Id, "sup", Array.Empty<byte>()));
    }

    [Fact]
    public async Task Aprobar_NoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AprobarBatchAsync(99999, "sup", new byte[8]));
    }

    [Fact]
    public async Task Aprobar_NoSimulado_LanzaExcepcion()
    {
        var batch = await SeedBatchSimuladoAsync();
        // Cambiar estado a Aprobado manualmente
        batch.Estado = EstadoBatch.Aprobado;
        await _context.SaveChangesAsync();
        await _context.Entry(batch).ReloadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AprobarBatchAsync(batch.Id, "sup", batch.RowVersion!));
    }

    // -------------------------------------------------------------------------
    // RechazarBatchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Rechazar_HappyPath_CambiaEstadoRechazado()
    {
        var batch = await SeedBatchSimuladoAsync();

        var resultado = await _service.RechazarBatchAsync(
            batch.Id, "supervisor", batch.RowVersion!, "No aprobado");

        Assert.Equal(EstadoBatch.Rechazado, resultado.Estado);
        Assert.Equal("No aprobado", resultado.MotivoRechazo);
    }

    [Fact]
    public async Task Rechazar_NoSimulado_LanzaExcepcion()
    {
        var batch = await SeedBatchSimuladoAsync();
        batch.Estado = EstadoBatch.Aprobado;
        await _context.SaveChangesAsync();
        await _context.Entry(batch).ReloadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RechazarBatchAsync(batch.Id, "sup", batch.RowVersion!, "motivo"));
    }

    // -------------------------------------------------------------------------
    // CancelarBatchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Cancelar_Simulado_CambiaEstadoCancelado()
    {
        var batch = await SeedBatchSimuladoAsync();

        var resultado = await _service.CancelarBatchAsync(
            batch.Id, "testuser", batch.RowVersion!, "cancelado por test");

        Assert.Equal(EstadoBatch.Cancelado, resultado.Estado);
    }

    [Fact]
    public async Task Cancelar_YaAplicado_LanzaExcepcion()
    {
        var batch = await SeedBatchSimuladoAsync();
        batch.Estado = EstadoBatch.Aplicado;
        await _context.SaveChangesAsync();
        await _context.Entry(batch).ReloadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CancelarBatchAsync(batch.Id, "testuser", batch.RowVersion!, "motivo"));
    }

    // -------------------------------------------------------------------------
    // AplicarBatchAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aplicar_HappyPath_CreaNewPrecios()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 100m, costo: 60m);

        // Simular
        var batch = await _service.SimularCambioMasivoAsync(
            "Aumento 10%", TipoCambio.PorcentajeSobrePrecioActual, TipoAplicacion.Aumento,
            10m, new List<int> { lista.Id }, productoIds: new List<int> { prod.Id });

        // Aprobar
        await _context.Entry(batch).ReloadAsync();
        batch = await _service.AprobarBatchAsync(batch.Id, "sup", batch.RowVersion!);

        // Aplicar
        await _context.Entry(batch).ReloadAsync();
        var resultado = await _service.AplicarBatchAsync(batch.Id, "admin", batch.RowVersion!);

        Assert.Equal(EstadoBatch.Aplicado, resultado.Estado);
        Assert.Equal("admin", resultado.AplicadoPor);

        // Debe existir un nuevo precio vigente con el valor nuevo
        _context.ChangeTracker.Clear();
        var precioVigente = await _context.ProductosPrecios
            .FirstOrDefaultAsync(p => p.ProductoId == prod.Id
                                   && p.ListaId == lista.Id
                                   && p.EsVigente && !p.IsDeleted);
        Assert.NotNull(precioVigente);
        Assert.Equal(110m, precioVigente!.Precio); // 100 * 1.10
    }

    [Fact]
    public async Task Aplicar_NoAprobado_LanzaExcepcion()
    {
        var batch = await SeedBatchSimuladoAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AplicarBatchAsync(batch.Id, "admin", batch.RowVersion!));
    }

    [Fact]
    public async Task Aplicar_RowVersionVacio_LanzaExcepcion()
    {
        var batch = await SeedBatchSimuladoAsync();
        batch.Estado = EstadoBatch.Aprobado;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AplicarBatchAsync(batch.Id, "admin", Array.Empty<byte>()));
    }

    // -------------------------------------------------------------------------
    // RequiereAutorizacionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequiereAutorizacion_BajoUmbral_RetornaFalse()
    {
        // Default umbral = 10%. Porcentaje = 5% → no requiere
        var batch = await SeedBatchSimuladoAsync(porcentajePromedio: 5m);

        var resultado = await _service.RequiereAutorizacionAsync(batch.Id);

        Assert.False(resultado);
    }

    [Fact]
    public async Task RequiereAutorizacion_SobreUmbral_RetornaTrue()
    {
        // Default umbral = 10%. Porcentaje = 15% → requiere
        var batch = await SeedBatchSimuladoAsync(porcentajePromedio: 15m);

        var resultado = await _service.RequiereAutorizacionAsync(batch.Id);

        Assert.True(resultado);
    }

    [Fact]
    public async Task RequiereAutorizacion_NoExiste_RetornaFalse()
    {
        var resultado = await _service.RequiereAutorizacionAsync(99999);

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // RevertirBatchAsync
    // -------------------------------------------------------------------------

    // Helper: crea ciclo completo simulación → aprobación → aplicación y retorna el batch aplicado
    private async Task<PriceChangeBatch> CrearBatchAplicadoAsync(
        int productoId, int listaId, decimal precioOriginal = 100m, decimal costo = 60m)
    {
        await SeedPrecioVigenteAsync(productoId, listaId, precio: precioOriginal, costo: costo);

        var batch = await _service.SimularCambioMasivoAsync(
            "Aumento 10%", TipoCambio.PorcentajeSobrePrecioActual, TipoAplicacion.Aumento,
            10m, new List<int> { listaId }, productoIds: new List<int> { productoId });

        await _context.Entry(batch).ReloadAsync();
        batch = await _service.AprobarBatchAsync(batch.Id, "sup", batch.RowVersion!);

        await _context.Entry(batch).ReloadAsync();
        batch = await _service.AplicarBatchAsync(batch.Id, "admin", batch.RowVersion!);

        await _context.Entry(batch).ReloadAsync();
        return batch;
    }

    [Fact]
    public async Task Revertir_HappyPath_RestauraPrecioAnterior()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        var batchAplicado = await CrearBatchAplicadoAsync(prod.Id, lista.Id, precioOriginal: 100m);

        var batchReversion = await _service.RevertirBatchAsync(
            batchAplicado.Id, "admin", batchAplicado.RowVersion!, "test revertir");

        // Batch original queda Revertido
        _context.ChangeTracker.Clear();
        var original = await _context.PriceChangeBatches.FirstAsync(b => b.Id == batchAplicado.Id);
        Assert.Equal(EstadoBatch.Revertido, original.Estado);
        Assert.Equal("test revertir", original.MotivoReversion);

        // Precio vigente restaurado al valor original
        var precioVigente = await _context.ProductosPrecios
            .FirstOrDefaultAsync(p => p.ProductoId == prod.Id
                                   && p.ListaId == lista.Id
                                   && p.EsVigente && !p.IsDeleted);
        Assert.NotNull(precioVigente);
        Assert.Equal(100m, precioVigente!.Precio);
    }

    [Fact]
    public async Task Revertir_CreasBatchDeReversion()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        var batchAplicado = await CrearBatchAplicadoAsync(prod.Id, lista.Id);

        var batchReversion = await _service.RevertirBatchAsync(
            batchAplicado.Id, "admin", batchAplicado.RowVersion!, "motivo");

        Assert.True(batchReversion.Id > 0);
        Assert.NotEqual(batchAplicado.Id, batchReversion.Id);
        Assert.Equal(EstadoBatch.Aplicado, batchReversion.Estado);
        Assert.Equal(batchAplicado.Id, batchReversion.BatchPadreId);
    }

    [Fact]
    public async Task Revertir_NoAplicado_LanzaExcepcion()
    {
        var batch = await SeedBatchSimuladoAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RevertirBatchAsync(batch.Id, "admin", batch.RowVersion!, "motivo"));
    }

    [Fact]
    public async Task Revertir_RowVersionVacio_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        var batchAplicado = await CrearBatchAplicadoAsync(prod.Id, lista.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RevertirBatchAsync(batchAplicado.Id, "admin", Array.Empty<byte>(), "motivo"));
    }

    [Fact]
    public async Task Revertir_NoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RevertirBatchAsync(99999, "admin", new byte[8], "motivo"));
    }
}

file sealed class StubCurrentUserServiceBatch : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}
