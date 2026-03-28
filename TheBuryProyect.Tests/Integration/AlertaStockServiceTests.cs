using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

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
        bool urgente = false)
    {
        var alerta = new AlertaStock
        {
            ProductoId = productoId,
            Tipo = TipoAlertaStock.StockBajo,
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
}
