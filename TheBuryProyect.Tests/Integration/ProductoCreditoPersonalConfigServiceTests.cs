using AutoMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

public class ProductoCreditoPersonalConfigServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoCreditoPersonalConfigService _service;

    public ProductoCreditoPersonalConfigServiceTests()
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

        var configuracionPagoService = new ConfiguracionPagoService(
            _context,
            mapper,
            NullLogger<ConfiguracionPagoService>.Instance);

        _service = new ProductoCreditoPersonalConfigService(_context, configuracionPagoService);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Producto> SeedProducto()
    {
        var producto = new Producto
        {
            Codigo = Guid.NewGuid().ToString("N")[..10],
            Nombre = "Producto test",
            Categoria = new Categoria { Nombre = $"Cat {Guid.NewGuid():N}", Codigo = Guid.NewGuid().ToString("N")[..10] },
            Marca = new Marca { Nombre = $"Marca {Guid.NewGuid():N}", Codigo = Guid.NewGuid().ToString("N")[..10] },
            PrecioCompra = 0m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    [Fact]
    public async Task Guardar_Obtener_RoundTripDePlanesYRestriccion()
    {
        var producto = await SeedProducto();

        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            AdmiteCreditoPersonal = true,
            MaxCuotasCredito = 12,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 1, TasaMensual = 0m, Activo = true, Orden = 1 },
                new() { CantidadCuotas = 3, TasaMensual = 10m, Activo = true, Orden = 3 },
                new() { CantidadCuotas = 6, TasaMensual = 25m, Activo = false, Orden = 6 }
            }
        }, "tester");

        var config = await _service.ObtenerAsync(producto.Id);

        Assert.True(config.AdmiteCreditoPersonal);
        Assert.Equal(12, config.MaxCuotasCredito);
        Assert.Equal(0m, config.Cuotas.First(c => c.CantidadCuotas == 1).TasaMensual);
        Assert.True(config.Cuotas.First(c => c.CantidadCuotas == 1).Activo);
        Assert.Equal(10m, config.Cuotas.First(c => c.CantidadCuotas == 3).TasaMensual);

        // La fila inactiva (Id = 0) no se persiste: vuelve como plantilla inactiva.
        var persistidas = await _context.ProductoCreditoPersonalCuotas
            .Where(c => c.ProductoId == producto.Id)
            .ToListAsync();
        Assert.Equal(2, persistidas.Count);
    }

    [Fact]
    public async Task Guardar_BloquearCreditoPersonal_PersisteRestriccion()
    {
        var producto = await SeedProducto();

        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            AdmiteCreditoPersonal = false
        }, "tester");

        var restriccion = await _context.ProductoCreditoRestricciones
            .FirstOrDefaultAsync(r => r.ProductoId == producto.Id && r.Activo);

        Assert.NotNull(restriccion);
        Assert.False(restriccion!.Permitido);

        var config = await _service.ObtenerAsync(producto.Id);
        Assert.False(config.AdmiteCreditoPersonal);
    }

    [Fact]
    public async Task Guardar_DesactivarPlanExistente_ConservaFilaInactiva()
    {
        var producto = await SeedProducto();

        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 3, TasaMensual = 10m, Activo = true, Orden = 3 }
            }
        }, "tester");

        var guardada = await _context.ProductoCreditoPersonalCuotas
            .FirstAsync(c => c.ProductoId == producto.Id && c.CantidadCuotas == 3);

        await _service.GuardarAsync(producto.Id, new ProductoCreditoPersonalConfigViewModel
        {
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { Id = guardada.Id, CantidadCuotas = 3, TasaMensual = 10m, Activo = false, Orden = 3 }
            }
        }, "tester");

        var fila = await _context.ProductoCreditoPersonalCuotas
            .FirstAsync(c => c.ProductoId == producto.Id && c.CantidadCuotas == 3);
        Assert.False(fila.Activo);
        Assert.Equal(10m, fila.TasaMensual);
    }
}
