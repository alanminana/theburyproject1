using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Validators;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

file sealed class StubCurrentUserDescuentoGuard : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "system";
    public bool IsAuthenticated() => true;
    public string? GetEmail() => null;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => false;
    public string? GetIpAddress() => null;
}

/// <summary>
/// VENTA-DESCUENTO-LINEA-LEGACY-EDIT-GUARD: cubre la clasificación de semántica de
/// "Descuento" de línea (SinDescuento/LegacyAbsoluto/Porcentaje/Ambigua) a través del
/// comportamiento observable de <see cref="VentaService.UpdateAsync"/> — el clasificador
/// es privado a propósito (VENTA-DESCUENTO-LINEA-LEGACY-EDIT-GUARD, punto 27: no
/// exponerlo públicamente solo para testearlo).
///
/// Cada test siembra directamente en el DbContext una línea "ya persistida" con los
/// campos exactos (PrecioUnitario, Cantidad, Descuento, Subtotal) que definen el caso —
/// nunca a través del ViewModel del POST, igual que el guard real.
/// </summary>
public class VentaServiceDescuentoLegacyGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly VentaService _service;
    private static int _counter = 5000;

    public VentaServiceDescuentoLegacyGuardTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var mapper = new MapperConfiguration(
                cfg => cfg.AddProfile<MappingProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();

        var numberGenerator = new VentaNumberGenerator(_context, NullLogger<VentaNumberGenerator>.Instance);

        _service = new VentaService(
            _context,
            mapper,
            NullLogger<VentaService>.Instance,
            null!,
            null!,
            new FinancialCalculationService(),
            new VentaValidator(),
            numberGenerator,
            new PrecioVigenteResolver(_context),
            new StubCurrentUserDescuentoGuard(),
            null!,
            null!,
            null!,
            new StubContratoVentaCreditoService(),
            new StubConfiguracionPagoServiceVenta());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Cliente> SeedClienteAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        var c = new Cliente
        {
            Nombre = "Test", Apellido = "DescGuard",
            TipoDocumento = "DNI", NumeroDocumento = n.ToString("D8"),
            Email = $"dg{n}@test.com"
        };
        _context.Set<Cliente>().Add(c);
        await _context.SaveChangesAsync();
        return c;
    }

    private async Task<Producto> SeedProductoAsync(decimal precioVenta = 1000m)
    {
        var n = Interlocked.Increment(ref _counter);
        var categoria = new Categoria
        {
            Codigo = $"CAT-DG-{n}", Nombre = $"Categoria DG {n}",
            IsDeleted = false, RowVersion = new byte[8]
        };
        var marca = new Marca
        {
            Codigo = $"MAR-DG-{n}", Nombre = $"Marca DG {n}",
            IsDeleted = false, RowVersion = new byte[8]
        };
        _context.Categorias.Add(categoria);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = $"PROD-DG-{n}",
            Nombre = $"Producto DG {n}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = precioVenta / 2m,
            PrecioVenta = precioVenta,
            PorcentajeIVA = 0m,
            StockActual = 100,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    /// <summary>
    /// Siembra una Venta editable (Cotización) con una única línea activa persistida con
    /// los valores exactos provistos — simula el estado de la base ANTES del Edit, tal
    /// como lo consume el guard. Devuelve la venta releída AsNoTracking (con RowVersion
    /// real) y el producto usado, para armar el ViewModel del POST.
    /// </summary>
    private async Task<(Venta venta, Producto producto, VentaDetalle detalle)> SeedVentaConLineaAsync(
        decimal precioUnitario, int cantidad, decimal descuento, decimal subtotal,
        EstadoVenta estado = EstadoVenta.Cotizacion)
    {
        var n = Interlocked.Increment(ref _counter);
        var cliente = await SeedClienteAsync();
        var producto = await SeedProductoAsync(precioUnitario);

        var venta = new Venta
        {
            Numero = $"VTA-DG-{n:D6}",
            ClienteId = cliente.Id,
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            FechaVenta = DateTime.UtcNow,
            Total = subtotal,
            RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        };
        _context.Ventas.Add(venta);
        await _context.SaveChangesAsync();

        var detalle = new VentaDetalle
        {
            VentaId = venta.Id,
            ProductoId = producto.Id,
            Cantidad = cantidad,
            PrecioUnitario = precioUnitario,
            Descuento = descuento,
            Subtotal = subtotal,
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        _context.VentaDetalles.Add(detalle);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var ventaReleida = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        return (ventaReleida, producto, detalle);
    }

    /// <summary>
    /// ViewModel de Update mínimo, viable, que edita algún otro campo (Observaciones) sin
    /// tocar conscientemente la línea. El guard no debe leer nada de acá para clasificar —
    /// estos valores son deliberadamente distintos de los persistidos, para demostrar que
    /// la clasificación ignora el POST.
    /// </summary>
    private static VentaViewModel CrearVmEdicionSimple(Venta venta, int clienteId, int productoId)
    {
        return new VentaViewModel
        {
            Id = venta.Id,
            ClienteId = clienteId,
            FechaVenta = venta.FechaVenta,
            Estado = venta.Estado,
            TipoPago = TipoPago.Efectivo,
            RowVersion = venta.RowVersion,
            Observaciones = "Editado en test",
            Detalles = new List<VentaDetalleViewModel>
            {
                new()
                {
                    ProductoId = productoId,
                    Cantidad = 1,
                    PrecioUnitario = 1_000m,
                    Descuento = 0m
                }
            }
        };
    }

    // =========================================================================
    // Clasificador — SinDescuento
    // =========================================================================

    [Fact]
    public async Task SinDescuento_Descuento0_PermiteGuardar()
    {
        var (venta, producto, _) = await SeedVentaConLineaAsync(
            precioUnitario: 1_000m, cantidad: 1, descuento: 0m, subtotal: 1_000m);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);

        var resultado = await _service.UpdateAsync(venta.Id, vm);

        Assert.NotNull(resultado);
    }

    // =========================================================================
    // Clasificador — Porcentaje (línea nueva, contrato vigente)
    // =========================================================================

    [Fact]
    public async Task Porcentaje_LineaNuevaValida_PermiteGuardar()
    {
        // bruto=100000, Descuento=20, Subtotal=80000 (100000 - 100000*20/100)
        var (venta, producto, _) = await SeedVentaConLineaAsync(
            precioUnitario: 100_000m, cantidad: 1, descuento: 20m, subtotal: 80_000m);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);

        var resultado = await _service.UpdateAsync(venta.Id, vm);

        Assert.NotNull(resultado);
    }

    // =========================================================================
    // Clasificador — LegacyAbsoluto (bloquea + atomicidad)
    // =========================================================================

    [Fact]
    public async Task LegacyAbsoluto_Bloquea_YNoEscribeNadaEnLaBase()
    {
        // bruto=240000, Descuento=10, Subtotal=239990 (240000 - 10, importe absoluto legacy).
        // Modelo porcentual daría 240000 - 240000*10/100 = 216000 ≠ 239990.
        var (venta, producto, detalleOriginal) = await SeedVentaConLineaAsync(
            precioUnitario: 240_000m, cantidad: 1, descuento: 10m, subtotal: 239_990m);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));

        // Mensaje accionable, no técnico.
        Assert.DoesNotContain("LegacyAbsoluto", ex.Message);
        Assert.DoesNotContain("decimal", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("versión anterior del sistema", ex.Message);

        // Atomicidad: la línea original sigue activa, sin cambios, y no se creó ninguna otra.
        var detallesDb = await _context.VentaDetalles
            .AsNoTracking()
            .Where(d => d.VentaId == venta.Id)
            .ToListAsync();
        Assert.Single(detallesDb);
        Assert.False(detallesDb[0].IsDeleted);
        Assert.Equal(detalleOriginal.Id, detallesDb[0].Id);
        Assert.Equal(239_990m, detallesDb[0].Subtotal);
        Assert.Equal(10m, detallesDb[0].Descuento);

        // Venta.Total intacto (no recalculado por CalcularTotales, que nunca debió correr).
        var ventaDb = await _context.Ventas.AsNoTracking().SingleAsync(v => v.Id == venta.Id);
        Assert.Equal(239_990m, ventaDb.Total);
        Assert.Equal(venta.Estado, ventaDb.Estado);
    }

    // =========================================================================
    // Clasificador — Ambigua: bruto == 100 (ambas fórmulas coinciden algebraicamente)
    // =========================================================================

    [Fact]
    public async Task Ambigua_Bruto100_Bloquea()
    {
        // bruto=100, Descuento=20 → absoluto=80, porcentual=80. Coinciden → Ambigua.
        var (venta, producto, _) = await SeedVentaConLineaAsync(
            precioUnitario: 100m, cantidad: 1, descuento: 20m, subtotal: 80m);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));
        Assert.Contains("versión anterior del sistema", ex.Message);
    }

    // =========================================================================
    // Clasificador — Ambigua: Subtotal == 0 con Descuento > 0 (fail closed pese a que el
    // modelo absoluto reconciliaría exactamente)
    // =========================================================================

    [Fact]
    public async Task Ambigua_SubtotalCero_Bloquea_AunqueElModeloAbsolutoReconciliaria()
    {
        // bruto=50, Descuento=60 → absoluto = Max(0, 50-60) = 0 (reconcilia exacto).
        // Pese a eso, Subtotal=0 con Descuento>0 es estructuralmente ambiguo: también
        // podría venir de un 100% porcentual actual. Debe bloquear igual (fail closed).
        var (venta, producto, _) = await SeedVentaConLineaAsync(
            precioUnitario: 50m, cantidad: 1, descuento: 60m, subtotal: 0m);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));
        Assert.Contains("versión anterior del sistema", ex.Message);
    }

    // =========================================================================
    // Clasificador — Ambigua: no reconcilia con ningún modelo
    // =========================================================================

    [Fact]
    public async Task Ambigua_NoReconciliaConNingunModelo_Bloquea()
    {
        // bruto=1000, Descuento=30 → absoluto=970, porcentual=700. Subtotal=850 no matchea
        // ninguno de los dos.
        var (venta, producto, _) = await SeedVentaConLineaAsync(
            precioUnitario: 1_000m, cantidad: 1, descuento: 30m, subtotal: 850m);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));
        Assert.Contains("versión anterior del sistema", ex.Message);
    }

    // =========================================================================
    // Defensivo: Descuento > 100 persistido (fuera del [Range(0,100)] del ViewModel, sólo
    // alcanzable si la fila viene de datos legacy/import). El clamp interno de
    // CalcularSubtotalLineaConDescuento no debe convertirlo silenciosamente en un 100%
    // porcentual "válido" — acá reconcilia con el modelo absoluto exacto y debe bloquear.
    // =========================================================================

    [Fact]
    public async Task DescuentoMayorA100Persistido_NoSePermiteComoPorcentaje100_Bloquea()
    {
        // bruto=10000, Descuento=150 → absoluto = Max(0, 10000-150) = 9850 (exacto).
        // porcentual clampeado a 100% daría 0 ≠ 9850 → LegacyAbsoluto, no Porcentaje.
        var (venta, producto, _) = await SeedVentaConLineaAsync(
            precioUnitario: 10_000m, cantidad: 1, descuento: 150m, subtotal: 9_850m);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));
        Assert.Contains("versión anterior del sistema", ex.Message);
    }

    // =========================================================================
    // No debe correr en estados no editables — ValidarEstadoParaEdicion sigue teniendo
    // autoridad y corre antes que el guard (no lo duplica).
    // =========================================================================

    [Fact]
    public async Task EstadoNoEditable_BloqueaPorEstado_NoPorElGuardDeDescuento()
    {
        var (venta, producto, _) = await SeedVentaConLineaAsync(
            precioUnitario: 240_000m, cantidad: 1, descuento: 10m, subtotal: 239_990m,
            estado: EstadoVenta.Confirmada);
        var vm = CrearVmEdicionSimple(venta, venta.ClienteId, producto.Id);
        vm.Estado = EstadoVenta.Confirmada;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(venta.Id, vm));

        // Mensaje del validador de estado, no el del guard de descuentos.
        Assert.Contains("Cotización, Presupuesto", ex.Message);
    }
}
