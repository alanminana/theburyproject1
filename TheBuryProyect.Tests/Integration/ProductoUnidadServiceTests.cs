using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ProductoUnidadService.
/// Usan SQLite in-memory (proveedor relacional) para validar constraints e índices.
/// </summary>
public class ProductoUnidadServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoUnidadService _service;

    public ProductoUnidadServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ProductoUnidadService(_context, NullLogger<ProductoUnidadService>.Instance);
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
        string? codigo = null,
        decimal stockActual = 0m,
        bool requiereNumeroSerie = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var marca = new Marca { Codigo = "M" + suffix, Nombre = "Marca-" + suffix, Activo = true };
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cat = new Categoria { Codigo = "C" + suffix, Nombre = "Cat-" + suffix, Activo = true };
        _context.Categorias.Add(cat);
        await _context.SaveChangesAsync();

        var prod = new Producto
        {
            Codigo = codigo ?? "P" + suffix,
            Nombre = "Prod-" + suffix,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 150m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            RequiereNumeroSerie = requiereNumeroSerie
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    // -------------------------------------------------------------------------
    // 1. Crear unidad sin número de serie
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_SinSerie_Persiste()
    {
        var prod = await SeedProductoAsync("TV55");

        var unidad = await _service.CrearUnidadAsync(prod.Id);

        Assert.NotEqual(0, unidad.Id);
        Assert.Equal(prod.Id, unidad.ProductoId);
        Assert.Null(unidad.NumeroSerie);
        Assert.Equal(EstadoUnidad.EnStock, unidad.Estado);
        Assert.False(unidad.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // 2. Crear unidad con número de serie
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_ConSerie_Persiste()
    {
        var prod = await SeedProductoAsync("TV55");

        var unidad = await _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-001");

        Assert.Equal("SN-001", unidad.NumeroSerie);
        Assert.Equal(EstadoUnidad.EnStock, unidad.Estado);
    }

    // -------------------------------------------------------------------------
    // 3. Genera CodigoInternoUnidad automático con base en Producto.Codigo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_GeneraCodigoInterno_UsandoProductoCodigo()
    {
        var prod = await SeedProductoAsync("TV55");

        var unidad = await _service.CrearUnidadAsync(prod.Id);

        Assert.StartsWith("TV55-U-", unidad.CodigoInternoUnidad);
    }

    // -------------------------------------------------------------------------
    // 4. Genera correlativo por producto (primer = 0001, segundo = 0002)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_GeneraCorrelativoSecuencial()
    {
        var prod = await SeedProductoAsync("TV55");

        var u1 = await _service.CrearUnidadAsync(prod.Id);
        var u2 = await _service.CrearUnidadAsync(prod.Id);

        Assert.Equal("TV55-U-0001", u1.CodigoInternoUnidad);
        Assert.Equal("TV55-U-0002", u2.CodigoInternoUnidad);
    }

    // -------------------------------------------------------------------------
    // 5. Correlativos por producto son independientes entre productos distintos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_CorrelativosIndependientesPorProducto()
    {
        var prod1 = await SeedProductoAsync("TV55");
        var prod2 = await SeedProductoAsync("HELADERA");

        var u1 = await _service.CrearUnidadAsync(prod1.Id);
        var u2 = await _service.CrearUnidadAsync(prod2.Id);
        var u3 = await _service.CrearUnidadAsync(prod1.Id);

        Assert.Equal("TV55-U-0001", u1.CodigoInternoUnidad);
        Assert.Equal("HELADERA-U-0001", u2.CodigoInternoUnidad);
        Assert.Equal("TV55-U-0002", u3.CodigoInternoUnidad);
    }

    // -------------------------------------------------------------------------
    // 6. Rechaza producto inexistente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_ProductoInexistente_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CrearUnidadAsync(999999));
    }

    // -------------------------------------------------------------------------
    // 7. Rechaza producto eliminado (soft-deleted)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_ProductoEliminado_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        prod.IsDeleted = true;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CrearUnidadAsync(prod.Id));
    }

    // -------------------------------------------------------------------------
    // 8. Rechaza NumeroSerie duplicado en mismo producto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_NumeroSerieDuplicadoMismoProducto_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();

        await _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-DUP");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-DUP"));
    }

    // -------------------------------------------------------------------------
    // 9. Permite misma serie en producto distinto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_MismaSerieProductoDistinto_Permitida()
    {
        var prod1 = await SeedProductoAsync();
        var prod2 = await SeedProductoAsync();

        await _service.CrearUnidadAsync(prod1.Id, numeroSerie: "SN-SHARED");
        var ex = await Record.ExceptionAsync(
            () => _service.CrearUnidadAsync(prod2.Id, numeroSerie: "SN-SHARED"));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // 10. Crea movimiento inicial en historial
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_CreaMovimientoInicial()
    {
        var prod = await SeedProductoAsync();

        var unidad = await _service.CrearUnidadAsync(prod.Id, usuario: "admin@test.com");

        var historial = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .ToListAsync();

        Assert.Single(historial);
        var mov = historial[0];
        Assert.Equal(EstadoUnidad.EnStock, mov.EstadoAnterior);
        Assert.Equal(EstadoUnidad.EnStock, mov.EstadoNuevo);
        Assert.Equal("Ingreso inicial de unidad", mov.Motivo);
        Assert.Equal("AltaUnidad", mov.OrigenReferencia);
        Assert.Equal("admin@test.com", mov.UsuarioResponsable);
    }

    [Fact]
    public async Task CrearUnidades_CreaVariasEnUnaOperacionConHistorial()
    {
        var prod = await SeedProductoAsync("BULK");

        var unidades = await _service.CrearUnidadesAsync(
            prod.Id,
            new string?[] { null, "SN-BULK-001", "SN-BULK-002" },
            ubicacionActual: "Deposito",
            observaciones: "Alta masiva",
            usuario: "admin@test.com");

        var persistidas = await _context.ProductoUnidades
            .AsNoTracking()
            .Where(u => u.ProductoId == prod.Id)
            .OrderBy(u => u.CodigoInternoUnidad)
            .ToListAsync();
        var movimientos = await _context.ProductoUnidadMovimientos.AsNoTracking().ToListAsync();

        Assert.Equal(3, unidades.Count);
        Assert.Equal(3, persistidas.Count);
        Assert.Equal("BULK-U-0001", persistidas[0].CodigoInternoUnidad);
        Assert.Equal("BULK-U-0002", persistidas[1].CodigoInternoUnidad);
        Assert.Equal("BULK-U-0003", persistidas[2].CodigoInternoUnidad);
        Assert.Single(persistidas.Where(u => u.NumeroSerie == null));
        Assert.Contains(persistidas, u => u.NumeroSerie == "SN-BULK-001");
        Assert.Contains(persistidas, u => u.NumeroSerie == "SN-BULK-002");
        Assert.All(persistidas, u =>
        {
            Assert.Equal(EstadoUnidad.EnStock, u.Estado);
            Assert.Equal("Deposito", u.UbicacionActual);
            Assert.Equal("Alta masiva", u.Observaciones);
        });
        Assert.Equal(3, movimientos.Count);
        Assert.All(movimientos, m => Assert.Equal("admin@test.com", m.UsuarioResponsable));
    }

    [Fact]
    public async Task CrearUnidades_SiUnaSerieDuplica_RollbackCompleto()
    {
        var prod = await SeedProductoAsync("BULK");
        await _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-EXISTENTE");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CrearUnidadesAsync(
                prod.Id,
                new string?[] { "SN-NUEVA", "SN-EXISTENTE" }));

        var unidades = await _context.ProductoUnidades
            .AsNoTracking()
            .Where(u => u.ProductoId == prod.Id)
            .ToListAsync();

        var unidad = Assert.Single(unidades);
        Assert.Equal("SN-EXISTENTE", unidad.NumeroSerie);
    }

    // -------------------------------------------------------------------------
    // 11. ObtenerDisponiblesPorProductoAsync devuelve solo EnStock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDisponibles_DevuelveSoloEnStock()
    {
        var prod = await SeedProductoAsync();

        var u1 = await _service.CrearUnidadAsync(prod.Id);
        var u2 = await _service.CrearUnidadAsync(prod.Id);

        // Cambiamos estado de u2 a Vendida directamente
        var u2Entity = await _context.ProductoUnidades.FindAsync(u2.Id);
        u2Entity!.Estado = EstadoUnidad.Vendida;
        await _context.SaveChangesAsync();

        var disponibles = (await _service.ObtenerDisponiblesPorProductoAsync(prod.Id)).ToList();

        Assert.Single(disponibles);
        Assert.Equal(u1.Id, disponibles[0].Id);
    }

    // -------------------------------------------------------------------------
    // 12. ObtenerHistorialAsync ordena por FechaCambio ascendente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerHistorial_OrdenaPorFechaCambioAscendente()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        // Agrego movimientos adicionales con fechas controladas
        var mov2 = new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidad.Id,
            EstadoAnterior = EstadoUnidad.EnStock,
            EstadoNuevo = EstadoUnidad.Reservada,
            Motivo = "Reserva",
            FechaCambio = DateTime.UtcNow.AddSeconds(10)
        };
        var mov3 = new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidad.Id,
            EstadoAnterior = EstadoUnidad.Reservada,
            EstadoNuevo = EstadoUnidad.Vendida,
            Motivo = "Venta",
            FechaCambio = DateTime.UtcNow.AddSeconds(20)
        };
        _context.ProductoUnidadMovimientos.AddRange(mov2, mov3);
        await _context.SaveChangesAsync();

        var historial = (await _service.ObtenerHistorialAsync(unidad.Id)).ToList();

        Assert.Equal(3, historial.Count);
        Assert.True(historial[0].FechaCambio <= historial[1].FechaCambio);
        Assert.True(historial[1].FechaCambio <= historial[2].FechaCambio);
        Assert.Equal("Ingreso inicial de unidad", historial[0].Motivo);
        Assert.Equal("Reserva", historial[1].Motivo);
        Assert.Equal("Venta", historial[2].Motivo);
    }

    // -------------------------------------------------------------------------
    // 13. ObtenerPorProductoAsync excluye eliminados
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerPorProducto_ExcluyeEliminados()
    {
        var prod = await SeedProductoAsync();

        var u1 = await _service.CrearUnidadAsync(prod.Id);
        var u2 = await _service.CrearUnidadAsync(prod.Id);

        var u2Entity = await _context.ProductoUnidades.FindAsync(u2.Id);
        u2Entity!.IsDeleted = true;
        await _context.SaveChangesAsync();

        var resultado = (await _service.ObtenerPorProductoAsync(prod.Id)).ToList();

        Assert.Single(resultado);
        Assert.Equal(u1.Id, resultado[0].Id);
    }

    // -------------------------------------------------------------------------
    // 14. Fallback a ProductoId cuando Codigo está vacío
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_CodigoProductoVacio_UsaProductoIdComoBase()
    {
        var prod = await SeedProductoAsync();

        // Forzar Codigo vacío directamente en DB para simular edge case
        var entity = await _context.Productos.FindAsync(prod.Id);
        entity!.Codigo = "";
        await _context.SaveChangesAsync();

        // El service recarga con AsNoTracking; necesitamos que la BD tenga el valor vacío
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        Assert.StartsWith($"{prod.Id}-U-", unidad.CodigoInternoUnidad);
    }

    [Fact]
    public async Task ObtenerConciliacion_StockIgualAUnidadesEnStock_DevuelveConciliado()
    {
        var prod = await SeedProductoAsync("CONC", stockActual: 2m, requiereNumeroSerie: true);
        await _service.CrearUnidadAsync(prod.Id);
        await _service.CrearUnidadAsync(prod.Id);

        var conciliacion = await _service.ObtenerConciliacionPorProductoAsync(prod.Id);

        Assert.Equal(prod.Id, conciliacion.ProductoId);
        Assert.True(conciliacion.RequiereNumeroSerie);
        Assert.Equal(2m, conciliacion.StockActual);
        Assert.Equal(2, conciliacion.UnidadesEnStock);
        Assert.Equal(0m, conciliacion.DiferenciaStockVsUnidadesEnStock);
        Assert.False(conciliacion.HayDiferencia);
    }

    [Fact]
    public async Task ObtenerConciliacion_StockMayorAUnidadesEnStock_DevuelveDiferenciaPositiva()
    {
        var prod = await SeedProductoAsync("CONC", stockActual: 5m, requiereNumeroSerie: true);
        await _service.CrearUnidadAsync(prod.Id);
        await _service.CrearUnidadAsync(prod.Id);

        var conciliacion = await _service.ObtenerConciliacionPorProductoAsync(prod.Id);

        Assert.Equal(2, conciliacion.UnidadesEnStock);
        Assert.Equal(3m, conciliacion.DiferenciaStockVsUnidadesEnStock);
        Assert.True(conciliacion.HayDiferencia);
    }

    [Fact]
    public async Task ObtenerConciliacion_StockMenorAUnidadesEnStock_DevuelveDiferenciaNegativa()
    {
        var prod = await SeedProductoAsync("CONC", stockActual: 1m, requiereNumeroSerie: true);
        await _service.CrearUnidadAsync(prod.Id);
        await _service.CrearUnidadAsync(prod.Id);
        await _service.CrearUnidadAsync(prod.Id);

        var conciliacion = await _service.ObtenerConciliacionPorProductoAsync(prod.Id);

        Assert.Equal(3, conciliacion.UnidadesEnStock);
        Assert.Equal(-2m, conciliacion.DiferenciaStockVsUnidadesEnStock);
        Assert.True(conciliacion.HayDiferencia);
    }

    [Fact]
    public async Task ObtenerConciliacion_CuentaBucketsPorEstado()
    {
        var prod = await SeedProductoAsync("CONC", stockActual: 1m, requiereNumeroSerie: true);
        await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnStock);
        await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Vendida);
        await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Faltante);
        await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Baja);
        await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Devuelta);
        await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Reservada);
        await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);

        var conciliacion = await _service.ObtenerConciliacionPorProductoAsync(prod.Id);

        Assert.Equal(1, conciliacion.UnidadesEnStock);
        Assert.Equal(1, conciliacion.UnidadesVendidas);
        Assert.Equal(1, conciliacion.UnidadesFaltantes);
        Assert.Equal(1, conciliacion.UnidadesBaja);
        Assert.Equal(1, conciliacion.UnidadesDevueltas);
        Assert.Equal(1, conciliacion.UnidadesReservadas);
        Assert.Equal(1, conciliacion.UnidadesEnReparacion);
        Assert.Equal(7, conciliacion.TotalUnidadesActivas);
    }

    [Fact]
    public async Task ObtenerConciliacion_ExcluyeUnidadesSoftDeleted()
    {
        var prod = await SeedProductoAsync("CONC", stockActual: 1m, requiereNumeroSerie: true);
        var activa = await _service.CrearUnidadAsync(prod.Id);
        var eliminada = await _service.CrearUnidadAsync(prod.Id);
        var eliminadaEntity = await _context.ProductoUnidades.FindAsync(eliminada.Id);
        eliminadaEntity!.IsDeleted = true;
        await _context.SaveChangesAsync();

        var conciliacion = await _service.ObtenerConciliacionPorProductoAsync(prod.Id);

        Assert.Equal(1, conciliacion.UnidadesEnStock);
        Assert.Equal(1, conciliacion.TotalUnidadesActivas);
        Assert.Equal(activa.Id, (await _service.ObtenerPorProductoAsync(prod.Id)).Single().Id);
    }

    [Fact]
    public async Task ObtenerConciliacion_InformaUltimosMovimientos()
    {
        var prod = await SeedProductoAsync("CONC", stockActual: 1m, requiereNumeroSerie: true);
        var unidad = await _service.CrearUnidadAsync(prod.Id);
        var fechaStock = DateTime.UtcNow.AddHours(-3);
        var fechaUnidad = DateTime.UtcNow.AddHours(1);

        _context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId = prod.Id,
            Tipo = TipoMovimiento.Ajuste,
            Cantidad = 1m,
            StockAnterior = 0m,
            StockNuevo = 1m,
            Motivo = "Ajuste test",
            CreatedAt = fechaStock
        });
        _context.ProductoUnidadMovimientos.Add(new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidad.Id,
            EstadoAnterior = EstadoUnidad.EnStock,
            EstadoNuevo = EstadoUnidad.Faltante,
            Motivo = "Movimiento test",
            FechaCambio = fechaUnidad
        });
        await _context.SaveChangesAsync();

        var conciliacion = await _service.ObtenerConciliacionPorProductoAsync(prod.Id);

        AssertDateClose(fechaStock, conciliacion.UltimoMovimientoStockFecha);
        AssertDateClose(fechaUnidad, conciliacion.UltimoMovimientoUnidadFecha);
    }

    [Fact]
    public async Task ObtenerConciliacion_ProductoInexistente_LanzaExcepcionControlada()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ObtenerConciliacionPorProductoAsync(999999));

        Assert.Contains("No existe el producto", ex.Message);
    }

    // =========================================================================
    // TRANSICIONES DE ESTADO
    // =========================================================================

    // Helper: deshabilita FK enforcement en SQLite para tests que usan IDs arbitrarios de VentaDetalle/Cliente.
    // Seguro porque cada instancia de test usa su propia base de datos in-memory (GUID único).
    private void DisableForeignKeys()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = OFF";
        cmd.ExecuteNonQuery();
    }

    private static void AssertDateClose(DateTime expected, DateTime? actual)
    {
        Assert.NotNull(actual);
        Assert.True(
            (actual.Value - expected).Duration() < TimeSpan.FromSeconds(1),
            $"Expected {expected:O}, actual {actual.Value:O}");
    }

    // Helper: crea una unidad con estado forzado directamente en DB.
    private async Task<ProductoUnidad> SeedUnidadConEstadoAsync(int productoId, EstadoUnidad estado)
    {
        var unidad = await _service.CrearUnidadAsync(productoId);
        if (estado != EstadoUnidad.EnStock)
        {
            var entity = await _context.ProductoUnidades.FindAsync(unidad.Id);
            entity!.Estado = estado;
            await _context.SaveChangesAsync();
            _context.Entry(entity).State = EntityState.Detached;
        }
        return unidad;
    }

    // -------------------------------------------------------------------------
    // 15. MarcarVendida: EnStock → Vendida correcto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarVendida_EnStock_TrasicionaYPersiste()
    {
        DisableForeignKeys();
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        var resultado = await _service.MarcarVendidaAsync(unidad.Id, ventaDetalleId: 42, clienteId: 7, usuario: "op@test.com");

        var saved = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Vendida, saved.Estado);
        Assert.Equal(42, saved.VentaDetalleId);
        Assert.Equal(7, saved.ClienteId);
        Assert.NotNull(saved.FechaVenta);
        Assert.Equal(EstadoUnidad.Vendida, resultado.Estado);
    }

    // -------------------------------------------------------------------------
    // 16. MarcarVendida: estado != EnStock rechazado (ej: Vendida → Vendida)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarVendida_EstadoNoEnStock_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Vendida);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.MarcarVendidaAsync(unidad.Id, ventaDetalleId: 99));
    }

    // -------------------------------------------------------------------------
    // 17. MarcarFaltante: EnStock → Faltante correcto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarFaltante_EnStock_TrasicionaYPersiste()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        var resultado = await _service.MarcarFaltanteAsync(unidad.Id, "Unidad no encontrada en depósito", usuario: "op@test.com");

        var saved = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Faltante, saved.Estado);
        Assert.Equal(EstadoUnidad.Faltante, resultado.Estado);
    }

    // -------------------------------------------------------------------------
    // 18. MarcarFaltante: estado Vendida rechazado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarFaltante_Vendida_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Vendida);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.MarcarFaltanteAsync(unidad.Id, "motivo"));
    }

    // -------------------------------------------------------------------------
    // 19. MarcarFaltante: motivo vacío rechazado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarFaltante_MotivoVacio_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.MarcarFaltanteAsync(unidad.Id, "  "));
    }

    // -------------------------------------------------------------------------
    // 20. MarcarBaja: EnStock → Baja correcto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarBaja_EnStock_TrasicionaYPersiste()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        var resultado = await _service.MarcarBajaAsync(unidad.Id, "Rotura irreparable", usuario: "op@test.com");

        var saved = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.Baja, saved.Estado);
        Assert.Equal(EstadoUnidad.Baja, resultado.Estado);
    }

    // -------------------------------------------------------------------------
    // 21. MarcarBaja: Faltante → Baja permitido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarBaja_Faltante_Permitido()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Faltante);

        var resultado = await _service.MarcarBajaAsync(unidad.Id, "Confirmado extraviado");

        Assert.Equal(EstadoUnidad.Baja, resultado.Estado);
    }

    // -------------------------------------------------------------------------
    // 22. MarcarBaja: Devuelta → Baja permitido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarBaja_Devuelta_Permitido()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Devuelta);

        var resultado = await _service.MarcarBajaAsync(unidad.Id, "Devuelta con daño total");

        Assert.Equal(EstadoUnidad.Baja, resultado.Estado);
    }

    // -------------------------------------------------------------------------
    // 23. MarcarBaja: motivo vacío rechazado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarBaja_MotivoVacio_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.MarcarBajaAsync(unidad.Id, ""));
    }

    // -------------------------------------------------------------------------
    // 24. ReintegrarAStock: Faltante → EnStock correcto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReintegrarAStock_Faltante_TrasicionaYPersiste()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Faltante);

        var resultado = await _service.ReintegrarAStockAsync(unidad.Id, "Unidad recuperada en depósito", usuario: "op@test.com");

        var saved = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, saved.Estado);
        Assert.Equal(EstadoUnidad.EnStock, resultado.Estado);
    }

    // -------------------------------------------------------------------------
    // 25. ReintegrarAStock: Devuelta → EnStock permitido y limpia campos de venta
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReintegrarAStock_Devuelta_LimpiaVentaYTrasiciona()
    {
        DisableForeignKeys();
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Devuelta);

        // Simular campos de venta previos
        var entity = await _context.ProductoUnidades.FindAsync(unidad.Id);
        entity!.VentaDetalleId = 55;
        entity.ClienteId = 10;
        entity.FechaVenta = DateTime.UtcNow.AddDays(-1);
        await _context.SaveChangesAsync();
        _context.Entry(entity).State = EntityState.Detached;

        await _service.ReintegrarAStockAsync(unidad.Id, "Devolución aceptada");

        var saved = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, saved.Estado);
        Assert.Null(saved.VentaDetalleId);
        Assert.Null(saved.ClienteId);
        Assert.Null(saved.FechaVenta);
    }

    // -------------------------------------------------------------------------
    // 26. ReintegrarAStock: motivo vacío rechazado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReintegrarAStock_MotivoVacio_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Faltante);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ReintegrarAStockAsync(unidad.Id, ""));
    }

    // -------------------------------------------------------------------------
    // 27. Transición inválida rechazada (ej: Baja → EnStock)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Transicion_EstadoInvalido_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Baja);

        // Baja no puede reintegrarse a stock (solo Faltante/Devuelta pueden)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ReintegrarAStockAsync(unidad.Id, "intento inválido"));
    }

    // -------------------------------------------------------------------------
    // 28. Cada transición crea movimiento en historial
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarVendida_CreaMovimientoEnHistorial()
    {
        DisableForeignKeys();
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);
        var movimientosAntes = await _context.ProductoUnidadMovimientos
            .CountAsync(m => m.ProductoUnidadId == unidad.Id);

        await _service.MarcarVendidaAsync(unidad.Id, ventaDetalleId: 10);

        var movimientosDespues = await _context.ProductoUnidadMovimientos
            .CountAsync(m => m.ProductoUnidadId == unidad.Id);
        Assert.Equal(movimientosAntes + 1, movimientosDespues);
    }

    // -------------------------------------------------------------------------
    // 29. Historial guarda usuario, motivo y OrigenReferencia en MarcarVendida
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarVendida_Historial_GuardaUsuarioMotivoYOrigen()
    {
        DisableForeignKeys();
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        await _service.MarcarVendidaAsync(unidad.Id, ventaDetalleId: 77, clienteId: 3, usuario: "vendedor@test.com");

        var mov = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id && m.EstadoNuevo == EstadoUnidad.Vendida)
            .SingleAsync();

        Assert.Equal(EstadoUnidad.EnStock, mov.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Vendida, mov.EstadoNuevo);
        Assert.Equal("Venta de unidad", mov.Motivo);
        Assert.Equal("VentaDetalle:77", mov.OrigenReferencia);
        Assert.Equal("vendedor@test.com", mov.UsuarioResponsable);
    }

    // -------------------------------------------------------------------------
    // 30. Historial guarda usuario y motivo en MarcarFaltante
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarFaltante_Historial_GuardaUsuarioYMotivo()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        await _service.MarcarFaltanteAsync(unidad.Id, "Extraviada en traslado", usuario: "bodega@test.com");

        var mov = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id && m.EstadoNuevo == EstadoUnidad.Faltante)
            .SingleAsync();

        Assert.Equal(EstadoUnidad.EnStock, mov.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Faltante, mov.EstadoNuevo);
        Assert.Equal("Extraviada en traslado", mov.Motivo);
        Assert.Equal("AjusteUnidad:Faltante", mov.OrigenReferencia);
        Assert.Equal("bodega@test.com", mov.UsuarioResponsable);
    }

    [Fact]
    public async Task AjustesUnidad_Historial_GuardaOrigenReferencia()
    {
        var prod = await SeedProductoAsync();
        var unidadBaja = await _service.CrearUnidadAsync(prod.Id);
        var unidadReintegro = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Faltante);

        await _service.MarcarBajaAsync(unidadBaja.Id, "Baja operativa");
        await _service.ReintegrarAStockAsync(unidadReintegro.Id, "Reintegro operativo");

        var baja = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .SingleAsync(m => m.ProductoUnidadId == unidadBaja.Id && m.EstadoNuevo == EstadoUnidad.Baja);
        var reintegro = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .SingleAsync(m => m.ProductoUnidadId == unidadReintegro.Id && m.OrigenReferencia == "AjusteUnidad:Reintegro");

        Assert.Equal("AjusteUnidad:Baja", baja.OrigenReferencia);
        Assert.Equal("AjusteUnidad:Reintegro", reintegro.OrigenReferencia);
    }

    // -------------------------------------------------------------------------
    // 31. ObtenerDisponibles no devuelve Vendida, Faltante ni Baja
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDisponibles_ExcluyeVendidaFaltanteBaja()
    {
        var prod = await SeedProductoAsync();

        var uStock   = await _service.CrearUnidadAsync(prod.Id);
        var uVendida = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Vendida);
        var uFaltante= await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Faltante);
        var uBaja    = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.Baja);

        var disponibles = (await _service.ObtenerDisponiblesPorProductoAsync(prod.Id)).ToList();

        Assert.Single(disponibles);
        Assert.Equal(uStock.Id, disponibles[0].Id);
        Assert.DoesNotContain(disponibles, u => u.Id == uVendida.Id);
        Assert.DoesNotContain(disponibles, u => u.Id == uFaltante.Id);
        Assert.DoesNotContain(disponibles, u => u.Id == uBaja.Id);
    }

    // -------------------------------------------------------------------------
    // 32. MarcarVendida no modifica Producto.StockActual
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarcarVendida_NoModificaStockActualProducto()
    {
        DisableForeignKeys();
        var prod = await SeedProductoAsync();
        var stockOriginal = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == prod.Id)).StockActual;

        var unidad = await _service.CrearUnidadAsync(prod.Id);
        await _service.MarcarVendidaAsync(unidad.Id, ventaDetalleId: 1);

        var stockFinal = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == prod.Id)).StockActual;
        Assert.Equal(stockOriginal, stockFinal);
    }

    // -------------------------------------------------------------------------
    // 33–41. FinalizarReparacionAsync — Fase 10.7
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FinalizarReparacion_UnidadEnReparacion_AEnStock_CambiaEstado()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);

        var resultado = await _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada OK");

        Assert.Equal(EstadoUnidad.EnStock, resultado.Estado);
    }

    [Fact]
    public async Task FinalizarReparacion_UnidadEnReparacion_ABaja_CambiaEstado()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);

        var resultado = await _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.Baja, "No reparable");

        Assert.Equal(EstadoUnidad.Baja, resultado.Estado);
    }

    [Fact]
    public async Task FinalizarReparacion_UnidadEnReparacion_ADevuelta_CambiaEstado()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);

        var resultado = await _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.Devuelta, "Pendiente revision");

        Assert.Equal(EstadoUnidad.Devuelta, resultado.Estado);
    }

    [Fact]
    public async Task FinalizarReparacion_RegistraProductoUnidadMovimiento()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);

        await _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada y OK", "operador1");

        var movimiento = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProductoUnidadId == unidad.Id
                                   && m.EstadoAnterior == EstadoUnidad.EnReparacion
                                   && m.EstadoNuevo == EstadoUnidad.EnStock);

        Assert.NotNull(movimiento);
        Assert.Equal("Reparada y OK", movimiento.Motivo);
        Assert.Equal("operador1", movimiento.UsuarioResponsable);
        Assert.StartsWith("FinalizacionReparacion:", movimiento.OrigenReferencia);
    }

    [Fact]
    public async Task FinalizarReparacion_UnidadNoEnReparacion_Falla()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id); // EnStock

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Motivo"));
    }

    [Fact]
    public async Task FinalizarReparacion_DestinoNoPermitido_Falla()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.Vendida, "Motivo"));

        Assert.Contains("Vendida", ex.Message);
    }

    [Fact]
    public async Task FinalizarReparacion_MotivoVacio_Falla()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, ""));
    }

    [Fact]
    public async Task FinalizarReparacion_AEnStock_NoModificaStockAgregado()
    {
        var prod = await SeedProductoAsync(stockActual: 5m);
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);
        var stockAntes = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == prod.Id)).StockActual;

        await _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada");

        var stockDespues = (await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == prod.Id)).StockActual;
        Assert.Equal(stockAntes, stockDespues);
    }

    [Fact]
    public async Task FinalizarReparacion_NoGeneraMovimientoStock()
    {
        var prod = await SeedProductoAsync();
        var unidad = await SeedUnidadConEstadoAsync(prod.Id, EstadoUnidad.EnReparacion);
        var movStockAntes = await _context.MovimientosStock.CountAsync(m => m.ProductoId == prod.Id);

        await _service.FinalizarReparacionAsync(unidad.Id, EstadoUnidad.EnStock, "Reparada");

        var movStockDespues = await _context.MovimientosStock.CountAsync(m => m.ProductoId == prod.Id);
        Assert.Equal(movStockAntes, movStockDespues);
    }
}
