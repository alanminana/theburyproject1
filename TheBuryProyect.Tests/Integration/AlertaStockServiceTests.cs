using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para AlertaStockService.
/// Cubren GenerarAlertasStockBajoAsync, VerificarYGenerarAlertaAsync,
/// ResolverAlertaAsync, IgnorarAlertaAsync, LimpiarAlertasAntiguasAsync,
/// GetAlertasPendientesAsync y GetAlertasByProductoIdAsync.
/// </summary>
public class AlertaStockServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AlertaStockService _service;

    public AlertaStockServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new AlertaStockService(_context, NullLogger<AlertaStockService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(
        decimal stockActual = 5m,
        decimal stockMinimo = 10m,
        bool activo = true)
    {
        var codigo = Guid.NewGuid().ToString("N")[..8];
        var categoria = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        _context.Set<Categoria>().Add(categoria);
        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Set<Marca>().Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = "Prod-" + codigo,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 10m,
            PrecioVenta = 50m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            StockMinimo = stockMinimo,
            Activo = activo
        };
        _context.Set<Producto>().Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<AlertaStock> SeedAlertaAsync(
        int productoId,
        EstadoAlerta estado = EstadoAlerta.Pendiente,
        DateTime? fechaResolucion = null,
        bool urgente = false,
        TipoAlertaStock tipo = TipoAlertaStock.StockBajo)
    {
        var alerta = new AlertaStock
        {
            ProductoId = productoId,
            Tipo = tipo,
            Prioridad = PrioridadAlerta.Media,
            Estado = estado,
            Mensaje = "Alerta de test",
            StockActual = 5m,
            StockMinimo = 10m,
            FechaAlerta = DateTime.UtcNow,
            FechaResolucion = fechaResolucion,
            NotificacionUrgente = urgente
        };
        _context.Set<AlertaStock>().Add(alerta);
        await _context.SaveChangesAsync();
        await _context.Entry(alerta).ReloadAsync();
        return alerta;
    }

    // -------------------------------------------------------------------------
    // GenerarAlertasStockBajoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GenerarAlertas_SinProductosConStockBajo_RetornaCero()
    {
        // Producto con stock suficiente
        await SeedProductoAsync(stockActual: 20m, stockMinimo: 10m);

        var resultado = await _service.GenerarAlertasStockBajoAsync();

        Assert.Equal(0, resultado);
    }

    [Fact]
    public async Task GenerarAlertas_ConProductoStockBajo_CreaAlerta()
    {
        await SeedProductoAsync(stockActual: 5m, stockMinimo: 10m);

        var resultado = await _service.GenerarAlertasStockBajoAsync();

        Assert.Equal(1, resultado);
        var alertas = await _context.Set<AlertaStock>().ToListAsync();
        Assert.Single(alertas);
        Assert.Equal(EstadoAlerta.Pendiente, alertas[0].Estado);
    }

    [Fact]
    public async Task GenerarAlertas_ProductoConAlertaPendienteExistente_NoCreaDuplicado()
    {
        var producto = await SeedProductoAsync(stockActual: 5m, stockMinimo: 10m);
        await SeedAlertaAsync(producto.Id); // ya tiene alerta pendiente

        var resultado = await _service.GenerarAlertasStockBajoAsync();

        Assert.Equal(0, resultado);
        var count = await _context.Set<AlertaStock>().CountAsync();
        Assert.Equal(1, count); // solo la original
    }

    [Fact]
    public async Task GenerarAlertas_ProductoInactivo_NoGeneraAlerta()
    {
        await SeedProductoAsync(stockActual: 0m, stockMinimo: 10m, activo: false);

        var resultado = await _service.GenerarAlertasStockBajoAsync();

        Assert.Equal(0, resultado);
    }

    [Fact]
    public async Task GenerarAlertas_StockAgotado_TipoAgotadoPrioridadCritica()
    {
        await SeedProductoAsync(stockActual: 0m, stockMinimo: 10m);

        await _service.GenerarAlertasStockBajoAsync();

        var alerta = await _context.Set<AlertaStock>().FirstAsync();
        Assert.Equal(TipoAlertaStock.StockAgotado, alerta.Tipo);
        Assert.Equal(PrioridadAlerta.Critica, alerta.Prioridad);
        Assert.True(alerta.NotificacionUrgente);
    }

    [Fact]
    public async Task GenerarAlertas_StockCritico_TipoCriticoPrioridadAlta()
    {
        // StockActual <= StockMinimo * 0.3 → crítico
        // StockMinimo = 10, StockActual = 2 → 2 <= 3 → crítico
        await SeedProductoAsync(stockActual: 2m, stockMinimo: 10m);

        await _service.GenerarAlertasStockBajoAsync();

        var alerta = await _context.Set<AlertaStock>().FirstAsync();
        Assert.Equal(TipoAlertaStock.StockCritico, alerta.Tipo);
        Assert.Equal(PrioridadAlerta.Alta, alerta.Prioridad);
        Assert.True(alerta.NotificacionUrgente);
    }

    [Fact]
    public async Task GenerarAlertas_StockBajo_TipoBajoPrioridadMedia()
    {
        // StockActual > StockMinimo * 0.3 pero <= StockMinimo → bajo
        // StockMinimo = 10, StockActual = 7 → 7 > 3 y 7 <= 10 → bajo
        await SeedProductoAsync(stockActual: 7m, stockMinimo: 10m);

        await _service.GenerarAlertasStockBajoAsync();

        var alerta = await _context.Set<AlertaStock>().FirstAsync();
        Assert.Equal(TipoAlertaStock.StockBajo, alerta.Tipo);
        Assert.Equal(PrioridadAlerta.Media, alerta.Prioridad);
        Assert.False(alerta.NotificacionUrgente);
    }

    // -------------------------------------------------------------------------
    // VerificarYGenerarAlertaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerificarYGenerarAlerta_StockSuficiente_RetornaNull()
    {
        var producto = await SeedProductoAsync(stockActual: 20m, stockMinimo: 10m);

        var resultado = await _service.VerificarYGenerarAlertaAsync(producto.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task VerificarYGenerarAlerta_ProductoNoExiste_RetornaNull()
    {
        var resultado = await _service.VerificarYGenerarAlertaAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task VerificarYGenerarAlerta_StockBajoSinAlerta_CreaAlerta()
    {
        var producto = await SeedProductoAsync(stockActual: 5m, stockMinimo: 10m);

        var resultado = await _service.VerificarYGenerarAlertaAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(producto.Id, resultado!.ProductoId);
        Assert.Equal(EstadoAlerta.Pendiente, resultado.Estado);
    }

    [Fact]
    public async Task VerificarYGenerarAlerta_AlertaPendienteExistente_DevuelveExistente()
    {
        var producto = await SeedProductoAsync(stockActual: 5m, stockMinimo: 10m);
        var alertaOriginal = await SeedAlertaAsync(producto.Id);

        var resultado = await _service.VerificarYGenerarAlertaAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(alertaOriginal.Id, resultado!.Id);
        // No crea duplicado
        var count = await _context.Set<AlertaStock>().CountAsync();
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // ResolverAlertaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolverAlerta_AlertaPendiente_MarcaResuelta()
    {
        var producto = await SeedProductoAsync();
        var alerta = await SeedAlertaAsync(producto.Id);

        var resultado = await _service.ResolverAlertaAsync(
            alerta.Id, "usuario1", "Reabastecido", alerta.RowVersion);

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var alertaBd = await _context.Set<AlertaStock>().FirstAsync(a => a.Id == alerta.Id);
        Assert.Equal(EstadoAlerta.Resuelta, alertaBd.Estado);
        Assert.NotNull(alertaBd.FechaResolucion);
        Assert.Equal("usuario1", alertaBd.UsuarioResolucion);
        Assert.Equal("Reabastecido", alertaBd.Observaciones);
    }

    [Fact]
    public async Task ResolverAlerta_YaResuelta_RetornaTrueIdempotente()
    {
        var producto = await SeedProductoAsync();
        var alerta = await SeedAlertaAsync(producto.Id, estado: EstadoAlerta.Resuelta,
            fechaResolucion: DateTime.UtcNow.AddHours(-1));

        // Sin RowVersion — no lo necesita cuando ya está resuelta
        var resultado = await _service.ResolverAlertaAsync(alerta.Id, "usuario1", null, null);

        Assert.True(resultado);
    }

    [Fact]
    public async Task ResolverAlerta_SinRowVersion_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync();
        var alerta = await SeedAlertaAsync(producto.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ResolverAlertaAsync(alerta.Id, "usuario1", null, null));
    }

    [Fact]
    public async Task ResolverAlerta_AlertaNoExiste_RetornaFalse()
    {
        var resultado = await _service.ResolverAlertaAsync(99999, "usuario1", null, new byte[] { 1 });

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // IgnorarAlertaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IgnorarAlerta_AlertaPendiente_MarcaIgnorada()
    {
        var producto = await SeedProductoAsync();
        var alerta = await SeedAlertaAsync(producto.Id);

        var resultado = await _service.IgnorarAlertaAsync(
            alerta.Id, "usuario1", "Producto descontinuado", alerta.RowVersion);

        Assert.True(resultado);
        _context.ChangeTracker.Clear();
        var alertaBd = await _context.Set<AlertaStock>().FirstAsync(a => a.Id == alerta.Id);
        Assert.Equal(EstadoAlerta.Ignorada, alertaBd.Estado);
        Assert.Equal("usuario1", alertaBd.UsuarioResolucion);
    }

    [Fact]
    public async Task IgnorarAlerta_SinRowVersion_LanzaExcepcion()
    {
        var producto = await SeedProductoAsync();
        var alerta = await SeedAlertaAsync(producto.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.IgnorarAlertaAsync(alerta.Id, "usuario1", null, null));
    }

    [Fact]
    public async Task IgnorarAlerta_AlertaNoExiste_RetornaFalse()
    {
        var resultado = await _service.IgnorarAlertaAsync(99999, "usuario1", null, new byte[] { 1 });

        Assert.False(resultado);
    }

    // -------------------------------------------------------------------------
    // GetAlertasPendientesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAlertasPendientes_SinAlertas_RetornaVacio()
    {
        var resultado = await _service.GetAlertasPendientesAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAlertasPendientes_ConAlertasPendientes_RetornaOrdenadas()
    {
        var producto1 = await SeedProductoAsync(stockActual: 0m); // crítico
        var producto2 = await SeedProductoAsync(stockActual: 5m); // bajo
        await SeedAlertaAsync(producto1.Id, urgente: true);
        await SeedAlertaAsync(producto2.Id);

        var resultado = await _service.GetAlertasPendientesAsync();

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, a => Assert.Equal(EstadoAlerta.Pendiente, a.Estado));
    }

    [Fact]
    public async Task GetAlertasPendientes_AlertasResueltas_NoIncluidas()
    {
        var producto = await SeedProductoAsync();
        await SeedAlertaAsync(producto.Id, estado: EstadoAlerta.Resuelta,
            fechaResolucion: DateTime.UtcNow.AddHours(-1));

        var resultado = await _service.GetAlertasPendientesAsync();

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // GetAlertasByProductoIdAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAlertasByProducto_SinAlertas_RetornaVacio()
    {
        var producto = await SeedProductoAsync();

        var resultado = await _service.GetAlertasByProductoIdAsync(producto.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetAlertasByProducto_ConVariasAlertas_RetornaTodasOrdenadas()
    {
        var producto = await SeedProductoAsync();
        await SeedAlertaAsync(producto.Id);
        await SeedAlertaAsync(producto.Id, estado: EstadoAlerta.Resuelta,
            fechaResolucion: DateTime.UtcNow.AddHours(-1));

        var resultado = await _service.GetAlertasByProductoIdAsync(producto.Id);

        Assert.Equal(2, resultado.Count);
    }

    // -------------------------------------------------------------------------
    // LimpiarAlertasAntiguasAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LimpiarAlertas_AlertasAntiguasResueltas_EliminaCorrectamente()
    {
        var producto = await SeedProductoAsync();

        // Alerta resuelta hace 40 días → debe limpiarse (por defecto diasAntiguedad=30)
        var alerta = await SeedAlertaAsync(producto.Id, estado: EstadoAlerta.Resuelta,
            fechaResolucion: DateTime.UtcNow.AddDays(-40));

        var resultado = await _service.LimpiarAlertasAntiguasAsync(diasAntiguedad: 30);

        Assert.Equal(1, resultado);
        _context.ChangeTracker.Clear();
        var alertaBd = await _context.Set<AlertaStock>()
            .IgnoreQueryFilters()
            .FirstAsync(a => a.Id == alerta.Id);
        Assert.True(alertaBd.IsDeleted);
    }

    [Fact]
    public async Task LimpiarAlertas_AlertasPendientes_NoEliminadas()
    {
        var producto = await SeedProductoAsync();
        // Pendiente antigua — no debe borrarse (solo resuelta/ignorada)
        await SeedAlertaAsync(producto.Id, estado: EstadoAlerta.Pendiente);

        var resultado = await _service.LimpiarAlertasAntiguasAsync(diasAntiguedad: 0);

        Assert.Equal(0, resultado);
    }

    [Fact]
    public async Task LimpiarAlertas_AlertasIgnoradas_EliminaCorrectamente()
    {
        var producto = await SeedProductoAsync();

        var alerta = await SeedAlertaAsync(producto.Id, estado: EstadoAlerta.Ignorada,
            fechaResolucion: DateTime.UtcNow.AddDays(-10));

        var resultado = await _service.LimpiarAlertasAntiguasAsync(diasAntiguedad: 5);

        Assert.Equal(1, resultado);
        _context.ChangeTracker.Clear();
        var alertaBd = await _context.Set<AlertaStock>()
            .IgnoreQueryFilters()
            .FirstAsync(a => a.Id == alerta.Id);
        Assert.True(alertaBd.IsDeleted);
    }

    // =========================================================================
    // VerificarYGenerarAlertasAsync (batch)
    // =========================================================================

    [Fact]
    public async Task VerificarYGenerarAlertas_ListaVacia_RetornaCero()
    {
        var resultado = await _service.VerificarYGenerarAlertasAsync(Array.Empty<int>());
        Assert.Equal(0, resultado);
    }

    [Fact]
    public async Task VerificarYGenerarAlertas_ProductoConStockBajo_CreaAlerta()
    {
        var producto = await SeedProductoAsync(stockActual: 2m, stockMinimo: 10m);

        var resultado = await _service.VerificarYGenerarAlertasAsync(new[] { producto.Id });

        Assert.Equal(1, resultado);
        var alertas = await _context.AlertasStock
            .Where(a => a.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Single(alertas);
    }

    [Fact]
    public async Task VerificarYGenerarAlertas_ProductoConStockSuficiente_NoCreaAlerta()
    {
        var producto = await SeedProductoAsync(stockActual: 20m, stockMinimo: 10m);

        var resultado = await _service.VerificarYGenerarAlertasAsync(new[] { producto.Id });

        Assert.Equal(0, resultado);
    }

    [Fact]
    public async Task VerificarYGenerarAlertas_ConAlertaActivaPreexistente_NoDuplica()
    {
        var producto = await SeedProductoAsync(stockActual: 2m, stockMinimo: 10m);
        await SeedAlertaAsync(producto.Id, estado: EstadoAlerta.Pendiente);

        var resultado = await _service.VerificarYGenerarAlertasAsync(new[] { producto.Id });

        Assert.Equal(0, resultado);
    }

    // =========================================================================
    // GetByIdAsync
    // =========================================================================

    [Fact]
    public async Task GetById_AlertaExistente_RetornaViewModel()
    {
        var producto = await SeedProductoAsync();
        var alerta = await SeedAlertaAsync(producto.Id);

        var resultado = await _service.GetByIdAsync(alerta.Id);

        Assert.NotNull(resultado);
        Assert.Equal(alerta.Id, resultado!.Id);
        Assert.Equal(producto.Id, resultado.ProductoId);
    }

    [Fact]
    public async Task GetById_AlertaInexistente_RetornaNull()
    {
        var resultado = await _service.GetByIdAsync(99999);
        Assert.Null(resultado);
    }

    // =========================================================================
    // BuscarAsync
    // =========================================================================

    [Fact]
    public async Task Buscar_SinFiltros_DevuelveTodasLasAlertas()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await SeedAlertaAsync(p1.Id);
        await SeedAlertaAsync(p2.Id);

        var resultado = await _service.BuscarAsync(new AlertaStockFiltroViewModel { PageNumber = 1, PageSize = 20 });

        Assert.Equal(2, resultado.TotalRecords);
        Assert.Equal(2, resultado.Items.Count);
    }

    [Fact]
    public async Task Buscar_FiltradoPorProductoId_DevuelveSoloEse()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await SeedAlertaAsync(p1.Id);
        await SeedAlertaAsync(p2.Id);

        var resultado = await _service.BuscarAsync(new AlertaStockFiltroViewModel
        {
            ProductoId = p1.Id,
            PageNumber = 1,
            PageSize = 20
        });

        Assert.Equal(1, resultado.TotalRecords);
        Assert.All(resultado.Items, a => Assert.Equal(p1.Id, a.ProductoId));
    }

    [Fact]
    public async Task Buscar_FiltradoPorEstado_DevuelveSoloPendientes()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await SeedAlertaAsync(p1.Id, estado: EstadoAlerta.Pendiente);
        // Resuelta en p2 — diferente producto para evitar unique constraint activa
        await SeedAlertaAsync(p2.Id, estado: EstadoAlerta.Resuelta, fechaResolucion: DateTime.UtcNow.AddDays(-1));

        var resultado = await _service.BuscarAsync(new AlertaStockFiltroViewModel
        {
            Estado = EstadoAlerta.Pendiente,
            PageNumber = 1,
            PageSize = 20
        });

        Assert.Equal(1, resultado.TotalRecords);
        Assert.All(resultado.Items, a => Assert.Equal(EstadoAlerta.Pendiente, a.Estado));
    }

    [Fact]
    public async Task Buscar_Paginacion_DevuelveSubset()
    {
        // Crear 5 productos distintos para evitar violación del unique index activo por producto
        for (int i = 0; i < 5; i++)
        {
            var p = await SeedProductoAsync();
            await SeedAlertaAsync(p.Id);
        }

        var resultado = await _service.BuscarAsync(new AlertaStockFiltroViewModel
        {
            PageNumber = 1,
            PageSize = 2
        });

        Assert.Equal(5, resultado.TotalRecords);
        Assert.Equal(2, resultado.Items.Count);
    }

    // =========================================================================
    // GetEstadisticasAsync
    // =========================================================================

    [Fact]
    public async Task GetEstadisticas_SinAlertas_RetornaContadoresCero()
    {
        var resultado = await _service.GetEstadisticasAsync();

        Assert.Equal(0, resultado.TotalAlertas);
        Assert.Equal(0, resultado.AlertasPendientes);
        Assert.Equal(0, resultado.ProductosAfectados);
    }

    [Fact]
    public async Task GetEstadisticas_ConAlertas_ContaCorrectamente()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        var p3 = await SeedProductoAsync();
        await SeedAlertaAsync(p1.Id, estado: EstadoAlerta.Pendiente);
        // Resuelta: diferente producto y con fechaResolucion para no violar unique activo
        await SeedAlertaAsync(p2.Id, estado: EstadoAlerta.Resuelta, fechaResolucion: DateTime.UtcNow.AddDays(-1));
        await SeedAlertaAsync(p3.Id, estado: EstadoAlerta.Ignorada, fechaResolucion: DateTime.UtcNow.AddDays(-1));

        var resultado = await _service.GetEstadisticasAsync();

        Assert.Equal(3, resultado.TotalAlertas);
        Assert.Equal(1, resultado.AlertasPendientes);
        Assert.Equal(1, resultado.AlertasResueltas);
        Assert.Equal(1, resultado.AlertasIgnoradas);
        Assert.Equal(3, resultado.ProductosAfectados);
    }

    // =========================================================================
    // GetProductosCriticosAsync
    // =========================================================================

    [Fact]
    public async Task GetProductosCriticos_SinAlertasCriticas_RetornaVacio()
    {
        var producto = await SeedProductoAsync();
        await SeedAlertaAsync(producto.Id, tipo: TipoAlertaStock.StockBajo);

        var resultado = await _service.GetProductosCriticosAsync();

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetProductosCriticos_ConAlertaStockCritico_RetornaProducto()
    {
        var producto = await SeedProductoAsync(stockActual: 0m, stockMinimo: 5m);
        await SeedAlertaAsync(producto.Id, tipo: TipoAlertaStock.StockCritico, estado: EstadoAlerta.Pendiente);

        var resultado = await _service.GetProductosCriticosAsync();

        Assert.Single(resultado);
        Assert.Equal(producto.Id, resultado[0].Id);
    }

    [Fact]
    public async Task GetProductosCriticos_ConAlertaStockAgotado_RetornaProducto()
    {
        var producto = await SeedProductoAsync(stockActual: 0m, stockMinimo: 5m);
        await SeedAlertaAsync(producto.Id, tipo: TipoAlertaStock.StockAgotado, estado: EstadoAlerta.Pendiente);

        var resultado = await _service.GetProductosCriticosAsync();

        Assert.Single(resultado);
        Assert.Equal(producto.Id, resultado[0].Id);
    }
}
