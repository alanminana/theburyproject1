using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para PrecioHistoricoService.
/// Cubren RegistrarCambioAsync (persistencia, campos), GetHistorialByProductoIdAsync,
/// GetUltimoCambioAsync, RevertirCambioAsync (happy path, no existe, no reversible,
/// no es último cambio, hay ventas posteriores), SimularCambioAsync (producto no existe,
/// margen negativo, margen bajo, margen aceptable, costo mayor que venta).
/// </summary>
public class PrecioHistoricoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioHistoricoService _service;

    public PrecioHistoricoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new PrecioHistoricoService(_context, NullLogger<PrecioHistoricoService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(decimal precioCompra = 10m, decimal precioVenta = 50m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo, Nombre = "Prod-" + codigo,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = precioCompra, PrecioVenta = precioVenta,
            PorcentajeIVA = 21m, StockActual = 10m, Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<PrecioHistorico> SeedHistorialAsync(
        int productoId,
        decimal precioCompraAnterior = 8m,
        decimal precioCompraNuevo = 10m,
        decimal precioVentaAnterior = 40m,
        decimal precioVentaNuevo = 50m,
        bool puedeRevertirse = true,
        DateTime? fecha = null)
    {
        var historial = new PrecioHistorico
        {
            ProductoId = productoId,
            PrecioCompraAnterior = precioCompraAnterior,
            PrecioCompraNuevo = precioCompraNuevo,
            PrecioVentaAnterior = precioVentaAnterior,
            PrecioVentaNuevo = precioVentaNuevo,
            MotivoCambio = "Ajuste",
            FechaCambio = fecha ?? DateTime.UtcNow,
            UsuarioModificacion = "testuser",
            PuedeRevertirse = puedeRevertirse
        };
        _context.PreciosHistoricos.Add(historial);
        await _context.SaveChangesAsync();
        return historial;
    }

    private async Task<Cliente> SeedClienteAsync()
    {
        var doc = Guid.NewGuid().ToString("N")[..8];
        var cliente = new Cliente
        {
            Nombre = "Test", Apellido = "Cliente",
            TipoDocumento = "DNI", NumeroDocumento = doc,
            Email = $"{doc}@test.com", Activo = true
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        return cliente;
    }

    private async Task<Venta> SeedVentaConDetalleAsync(int clienteId, int productoId, DateTime fecha)
    {
        var venta = new Venta
        {
            Numero = Guid.NewGuid().ToString("N")[..8],
            ClienteId = clienteId,
            Estado = Models.Enums.EstadoVenta.Confirmada,
            TipoPago = Models.Enums.TipoPago.Efectivo,
            FechaVenta = fecha,
            Subtotal = 50m, Total = 50m,
            Detalles = new List<VentaDetalle>
            {
                new() { ProductoId = productoId, Cantidad = 1, PrecioUnitario = 50m, Descuento = 0m, Subtotal = 50m }
            }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();
        return venta;
    }

    // -------------------------------------------------------------------------
    // RegistrarCambioAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Registrar_DatosValidos_Persiste()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.RegistrarCambioAsync(
            producto.Id,
            precioCompraAnterior: 8m, precioCompraNuevo: 10m,
            precioVentaAnterior: 40m, precioVentaNuevo: 50m,
            motivoCambio: "Ajuste proveedor", usuarioModificacion: "admin");

        Assert.True(resultado.Id > 0);
        var bd = await _context.PreciosHistoricos.FirstOrDefaultAsync(h => h.Id == resultado.Id);
        Assert.NotNull(bd);
        Assert.Equal(producto.Id, bd!.ProductoId);
        Assert.Equal(8m, bd.PrecioCompraAnterior);
        Assert.Equal(10m, bd.PrecioCompraNuevo);
        Assert.Equal(40m, bd.PrecioVentaAnterior);
        Assert.Equal(50m, bd.PrecioVentaNuevo);
        Assert.True(bd.PuedeRevertirse);
    }

    [Fact]
    public async Task Registrar_GuardaMotivoyUsuario()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.RegistrarCambioAsync(
            producto.Id, 8m, 10m, 40m, 50m,
            motivoCambio: "Motivo test", usuarioModificacion: "usuario-test");

        Assert.Equal("Motivo test", resultado.MotivoCambio);
        Assert.Equal("usuario-test", resultado.UsuarioModificacion);
    }

    // -------------------------------------------------------------------------
    // GetHistorialByProductoIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetHistorial_SinCambios_RetornaVacio()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GetHistorialByProductoIdAsync(producto.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetHistorial_ConCambios_RetornaOrdenadoPorFechaDesc()
    {
        var producto = await SeedProductoAsync();
        var older = DateTime.UtcNow.AddDays(-2);
        var newer = DateTime.UtcNow.AddDays(-1);

        await SeedHistorialAsync(producto.Id, fecha: older);
        await SeedHistorialAsync(producto.Id, fecha: newer);

        var resultado = await _service.GetHistorialByProductoIdAsync(producto.Id);

        Assert.Equal(2, resultado.Count);
        Assert.True(resultado[0].FechaCambio >= resultado[1].FechaCambio);
    }

    [Fact]
    public async Task GetHistorial_SoloDelProductoSolicitado()
    {
        var prod1 = await SeedProductoAsync();
        var prod2 = await SeedProductoAsync();
        await SeedHistorialAsync(prod1.Id);
        await SeedHistorialAsync(prod2.Id);

        var resultado = await _service.GetHistorialByProductoIdAsync(prod1.Id);

        Assert.Single(resultado);
        Assert.Equal(prod1.Id, resultado[0].ProductoId);
    }

    // -------------------------------------------------------------------------
    // GetUltimoCambioAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUltimoCambio_SinHistorial_RetornaNull()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GetUltimoCambioAsync(producto.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetUltimoCambio_ConVariosCambios_RetornaElMasReciente()
    {
        var producto = await SeedProductoAsync();
        var older = DateTime.UtcNow.AddDays(-3);
        var newer = DateTime.UtcNow.AddDays(-1);

        await SeedHistorialAsync(producto.Id, fecha: older);
        var masReciente = await SeedHistorialAsync(producto.Id, fecha: newer);

        var resultado = await _service.GetUltimoCambioAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(masReciente.Id, resultado!.Id);
    }

    // -------------------------------------------------------------------------
    // RevertirCambioAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Revertir_HappyPath_RestauraPreciosYMarcaEliminado()
    {
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);
        var historial = await SeedHistorialAsync(
            producto.Id,
            precioCompraAnterior: 8m, precioCompraNuevo: 10m,
            precioVentaAnterior: 40m, precioVentaNuevo: 50m);

        var resultado = await _service.RevertirCambioAsync(historial.Id);

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var prodBd = await _context.Productos.FirstAsync(p => p.Id == producto.Id);
        Assert.Equal(8m, prodBd.PrecioCompra);
        Assert.Equal(40m, prodBd.PrecioVenta);
        var histBd = await _context.PreciosHistoricos.IgnoreQueryFilters().FirstAsync(h => h.Id == historial.Id);
        Assert.True(histBd.IsDeleted);
    }

    [Fact]
    public async Task Revertir_HistorialNoExiste_RetornaFalse()
    {
        var resultado = await _service.RevertirCambioAsync(99999);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Revertir_NoReversible_RetornaFalse()
    {
        var producto = await SeedProductoAsync();
        var historial = await SeedHistorialAsync(producto.Id, puedeRevertirse: false);

        var resultado = await _service.RevertirCambioAsync(historial.Id);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Revertir_NoEsUltimoCambio_RetornaFalse()
    {
        var producto = await SeedProductoAsync();
        var older = DateTime.UtcNow.AddDays(-2);
        var newer = DateTime.UtcNow.AddDays(-1);

        var primerCambio = await SeedHistorialAsync(producto.Id, fecha: older);
        await SeedHistorialAsync(producto.Id, fecha: newer); // hay uno más nuevo

        var resultado = await _service.RevertirCambioAsync(primerCambio.Id);

        Assert.False(resultado);
    }

    [Fact]
    public async Task Revertir_ConVentasPosteriores_RetornaFalseYMarcaNoReversible()
    {
        var producto = await SeedProductoAsync();
        var fechaCambio = DateTime.UtcNow.AddDays(-1);
        var historial = await SeedHistorialAsync(producto.Id, fecha: fechaCambio);

        // Venta posterior al cambio
        var cliente = await SeedClienteAsync();
        await SeedVentaConDetalleAsync(cliente.Id, producto.Id, fechaCambio.AddHours(1));

        var resultado = await _service.RevertirCambioAsync(historial.Id);

        Assert.False(resultado);
        _context.ChangeTracker.Clear();
        var histBd = await _context.PreciosHistoricos.FirstAsync(h => h.Id == historial.Id);
        Assert.False(histBd.PuedeRevertirse);
    }

    // -------------------------------------------------------------------------
    // SimularCambioAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Simular_ProductoNoExiste_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SimularCambioAsync(99999, 10m, 50m));
    }

    [Fact]
    public async Task Simular_MargenNegativo_AggregaAlertaNoRecomendable()
    {
        // PrecioCompra > PrecioVenta → margen negativo
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        var simulacion = await _service.SimularCambioAsync(producto.Id,
            precioCompraNuevo: 60m, precioVentaNuevo: 50m);

        Assert.False(simulacion.EsRecomendable);
        Assert.Contains(simulacion.Alertas, a => a.Contains("NEGATIVO") || a.Contains("costo"));
    }

    [Fact]
    public async Task Simular_MargenBajoMenosDe10_AggregaAdvertencia()
    {
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        // Margen = (10.5 - 10) / 10 * 100 = 5% < 10%
        var simulacion = await _service.SimularCambioAsync(producto.Id,
            precioCompraNuevo: 10m, precioVentaNuevo: 10.5m);

        Assert.False(simulacion.EsRecomendable);
        Assert.NotEmpty(simulacion.Alertas);
    }

    [Fact]
    public async Task Simular_CambioAceptable_CalculaCamposCorrectamente()
    {
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        // Margen actual = (50-10)/10*100 = 400%
        // Propuesto = (60-12)/12*100 = 400%
        var simulacion = await _service.SimularCambioAsync(producto.Id,
            precioCompraNuevo: 12m, precioVentaNuevo: 60m);

        Assert.Equal(10m, simulacion.PrecioCompraActual);
        Assert.Equal(50m, simulacion.PrecioVentaActual);
        Assert.Equal(12m, simulacion.PrecioCompraPropuesto);
        Assert.Equal(60m, simulacion.PrecioVentaPropuesto);
        Assert.Equal(2m, simulacion.DiferenciaCompra);   // 12 - 10
        Assert.Equal(10m, simulacion.DiferenciaVenta);   // 60 - 50
    }

    [Fact]
    public async Task Simular_CosteMayorQueVenta_AggregaAlertaError()
    {
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);

        var simulacion = await _service.SimularCambioAsync(producto.Id,
            precioCompraNuevo: 80m, precioVentaNuevo: 50m);

        Assert.Contains(simulacion.Alertas, a => a.Contains("costo") || a.Contains("ERROR") || a.Contains("mayor"));
        Assert.False(simulacion.EsRecomendable);
    }

    // =========================================================================
    // GetEstadisticasAsync
    // =========================================================================

    [Fact]
    public async Task GetEstadisticas_SinCambios_RetornaTotalesEnCero()
    {
        var estadisticas = await _service.GetEstadisticasAsync(null, null);

        Assert.Equal(0, estadisticas.TotalCambios);
        Assert.Equal(0, estadisticas.CambiosConAumento);
        Assert.Equal(0, estadisticas.CambiosConDisminucion);
    }

    [Fact]
    public async Task GetEstadisticas_ConAumentosYDisminuciones_ContaCorrectamente()
    {
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 50m);
        // Aumento
        await SeedHistorialAsync(producto.Id,
            precioVentaAnterior: 40m, precioVentaNuevo: 50m);
        // Disminución
        await SeedHistorialAsync(producto.Id,
            precioVentaAnterior: 50m, precioVentaNuevo: 45m);
        // Sin cambio en venta
        await SeedHistorialAsync(producto.Id,
            precioVentaAnterior: 45m, precioVentaNuevo: 45m);

        var estadisticas = await _service.GetEstadisticasAsync(null, null);

        Assert.Equal(3, estadisticas.TotalCambios);
        Assert.Equal(1, estadisticas.CambiosConAumento);
        Assert.Equal(1, estadisticas.CambiosConDisminucion);
    }

    [Fact]
    public async Task GetEstadisticas_FiltroFechas_RetornaSoloDentroDelRango()
    {
        var producto = await SeedProductoAsync();
        var antiguo = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var reciente = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedHistorialAsync(producto.Id, fecha: antiguo);
        await SeedHistorialAsync(producto.Id, fecha: reciente);

        var estadisticas = await _service.GetEstadisticasAsync(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, estadisticas.TotalCambios);
    }

    [Fact]
    public async Task GetEstadisticas_ConAumento_CalculaMayorAumentoVenta()
    {
        var producto = await SeedProductoAsync(precioCompra: 10m, precioVenta: 100m);
        await SeedHistorialAsync(producto.Id,
            precioVentaAnterior: 50m, precioVentaNuevo: 100m); // 100% aumento

        var estadisticas = await _service.GetEstadisticasAsync(null, null);

        Assert.True(estadisticas.MayorAumentoVenta > 0);
        Assert.NotNull(estadisticas.ProductoMayorAumentoVenta);
    }

    // =========================================================================
    // BuscarAsync
    // =========================================================================

    [Fact]
    public async Task Buscar_SinFiltros_RetornaTodos()
    {
        var producto = await SeedProductoAsync();
        await SeedHistorialAsync(producto.Id);
        await SeedHistorialAsync(producto.Id);

        var resultado = await _service.BuscarAsync(
            new PrecioHistoricoFiltroViewModel { PageNumber = 1, PageSize = 10 });

        Assert.True(resultado.TotalRecords >= 2);
    }

    [Fact]
    public async Task Buscar_FiltroPorProductoId_RetornaSoloEseProducto()
    {
        var prod1 = await SeedProductoAsync();
        var prod2 = await SeedProductoAsync();
        await SeedHistorialAsync(prod1.Id);
        await SeedHistorialAsync(prod2.Id);

        var resultado = await _service.BuscarAsync(
            new PrecioHistoricoFiltroViewModel
            {
                ProductoId = prod1.Id,
                PageNumber = 1,
                PageSize = 10
            });

        Assert.Equal(1, resultado.TotalRecords);
        Assert.All(resultado.Items, i => Assert.Equal(prod1.Id, i.ProductoId));
    }

    [Fact]
    public async Task Buscar_FiltroPorFechaDesde_ExcluyeAnteriores()
    {
        var producto = await SeedProductoAsync();
        await SeedHistorialAsync(producto.Id,
            fecha: new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedHistorialAsync(producto.Id,
            fecha: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var resultado = await _service.BuscarAsync(
            new PrecioHistoricoFiltroViewModel
            {
                FechaDesde = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                PageNumber = 1,
                PageSize = 10
            });

        Assert.Equal(1, resultado.TotalRecords);
    }

    [Fact]
    public async Task Buscar_FiltroPorSoloPuedeRevertirse_RetornaSoloReversibles()
    {
        var producto = await SeedProductoAsync();
        await SeedHistorialAsync(producto.Id, puedeRevertirse: true);
        await SeedHistorialAsync(producto.Id, puedeRevertirse: false);

        var resultado = await _service.BuscarAsync(
            new PrecioHistoricoFiltroViewModel
            {
                SoloPuedeRevertirse = true,
                PageNumber = 1,
                PageSize = 10
            });

        Assert.Equal(1, resultado.TotalRecords);
        Assert.All(resultado.Items, i => Assert.True(i.PuedeRevertirse));
    }

    [Fact]
    public async Task Buscar_Paginacion_RetornaPageSize()
    {
        var producto = await SeedProductoAsync();
        for (int i = 0; i < 5; i++)
            await SeedHistorialAsync(producto.Id);

        var resultado = await _service.BuscarAsync(
            new PrecioHistoricoFiltroViewModel { PageNumber = 1, PageSize = 3 });

        Assert.True(resultado.TotalRecords >= 5);
        Assert.Equal(3, resultado.Items.Count);
    }

    // =========================================================================
    // MarcarComoNoReversibleAsync
    // =========================================================================

    [Fact]
    public async Task MarcarComoNoReversible_HistorialExistente_CambiaPuedeRevertirseFalse()
    {
        var producto = await SeedProductoAsync();
        var historial = await SeedHistorialAsync(producto.Id, puedeRevertirse: true);

        await _service.MarcarComoNoReversibleAsync(historial.Id);

        _context.ChangeTracker.Clear();
        var actualizado = await _context.PreciosHistoricos.FindAsync(historial.Id);
        Assert.False(actualizado!.PuedeRevertirse);
    }

    [Fact]
    public async Task MarcarComoNoReversible_HistorialInexistente_NoLanzaExcepcion()
    {
        // Should silently do nothing when historial not found
        var ex = await Record.ExceptionAsync(
            () => _service.MarcarComoNoReversibleAsync(99999));

        Assert.Null(ex);
    }
}
